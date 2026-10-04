using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AlternateStart;

/// <summary>
/// Turns a start into campaign state on the map: clan standing, purse, kingdom, fiefs, party and position.
/// Each step stands alone, so one that cannot be done (no castle free, no wanderer to hire) is logged and
/// skipped rather than abandoning the rest of the start.
/// </summary>
internal static class StartApplier
{
    private const int CaravanFallbackGold = 15000;

    public static void Apply(StartDef start, string? realmId)
    {
        StartSettings settings = StartSettings.Current;
        Clan clan = Clan.PlayerClan;
        Hero hero = Hero.MainHero;
        MobileParty party = MobileParty.MainParty;
        var report = new List<string>();

        Kingdom? realm = realmId == null ? null : Kingdom.All.FirstOrDefault(k => k.StringId == realmId && !k.IsEliminated);
        if (realm == null && start.NeedsRealm) realm = HomelandRealm(start);
        if (start.NeedsRealm && realm == null)
        {
            ErrorLog.Write($"No realm could host start '{start.Id}'; falling back to the adventurer start.");
            return;
        }

        Vec2 center = realm != null ? RealmCenter(realm) : party.GetPosition2D;
        CultureObject homeCulture = clan.Culture ?? hero.Culture;
        CultureObject localCulture = realm?.Culture ?? homeCulture;
        ErrorLog.Debug($"Applying '{start.Id}' in {realm?.StringId ?? "homeland"}.");

        if (start.IsVanilla)
        {
            if (realm != null) MovePartyTo(NearestTown(realm, center));
            return;
        }

        string bannerCode = clan.Banner.BannerCode;
        uint color1 = clan.Color, color2 = clan.Color2;

        // Standing and purse first: mercenary and vassal contracts check the clan tier.
        if (start.Noble) clan.IsNoble = true;
        RaiseClanTier(start.ClanTier);
        if (start.SetGold) hero.ChangeHeroGold(start.Gold - hero.Gold);
        else if (start.Gold > 0) hero.ChangeHeroGold(start.Gold);

        Settlement? location = null;
        Settlement? seat = null;
        var fiefs = new List<Settlement>();

        switch (start.Politics)
        {
            case Politics.Mercenary when realm != null:
                int award = Campaign.Current.Models.MinorFactionsModel.GetMercenaryAwardFactorToJoinKingdom(clan, realm, false);
                ChangeKingdomAction.ApplyByJoinFactionAsMercenary(clan, realm, default, award, false);
                report.Add($"Your company is under contract to {realm.Name}.");
                break;

            case Politics.Independent when realm != null:
                fiefs.AddRange(TakeFiefs(realm, start, center, report));
                break;

            case Politics.Vassal when realm != null:
                ChangeKingdomAction.ApplyByJoinToKingdom(clan, realm, default, false);
                fiefs.AddRange(TakeFiefs(realm, start, center, report));
                report.Add($"You are a sworn vassal of {realm.Leader?.Name} and {realm.Name}.");
                break;

            case Politics.Rebel when realm != null:
                fiefs.AddRange(TakeFiefs(realm, start, center, report, farFromCapital: true));
                FoundKingdom(realm, homeCulture, report);
                break;

            case Politics.Ruler when realm != null:
                seat = TakeThrone(realm, report);
                if (seat != null) fiefs.Add(seat);
                break;
        }

        seat ??= fiefs.FirstOrDefault(f => f.IsTown) ?? fiefs.FirstOrDefault();
        if (fiefs.Count > 0) clan.ConsiderAndUpdateHomeSettlement();

        // Where the party starts, and the start-specific extras that depend on it.
        switch (start.Id)
        {
            case "captive":
            case "outlaw":
                location = NearestVillage(null, (NearestHideout(center) ?? NearestTown(realm, center))?.GetPosition2D ?? center);
                break;
            case "militia":
                location = NearestVillage(realm, center);
                break;
            default:
                location = seat ?? NearestTown(realm, center);
                break;
        }

        AddTroops(start, start.Id is "noble" or "veteran" or "captive" ? homeCulture : localCulture, location, report);

        switch (start.Id)
        {
            case "captive":
                hero.HitPoints = Math.Max(1, hero.MaxHitPoints * settings.CaptiveHealthPercent / 100);
                break;
            case "militia" when location != null:
                foreach (Hero notable in location.Notables.ToList())
                {
                    ChangeRelationAction.ApplyPlayerRelation(notable, settings.MilitiaRelation, false, false);
                }
                report.Add($"The headmen of {location.Name} owe you their village.");
                break;
            case "outlaw":
                IFaction? lawful = (IFaction?)realm ?? NearestTown(null, center)?.MapFaction;
                if (lawful != null && settings.OutlawCrime > 0)
                {
                    ChangeCrimeRatingAction.Apply(lawful, settings.OutlawCrime, false);
                    report.Add($"{lawful.Name} has a price on your head.");
                }
                break;
            case "merchant":
                SetUpTradingHouse(location, center, report);
                break;
        }

        HireCompanions(start.Companions, fiefs, location?.GetPosition2D ?? center, report);

        if (start.Influence > 0) ChangeClanInfluenceAction.Apply(clan, start.Influence);
        if (start.RulerRelation > 0 && realm?.Leader != null && realm.Leader != hero)
        {
            ChangeRelationAction.ApplyPlayerRelation(realm.Leader, start.RulerRelation, true, false);
        }

        // Vassals and mercenaries take the kingdom's colours, as in vanilla (the game repaints them on every load
        // anyway). A ruling clan shows the kingdom's banner, so a new ruler's realm adopts the banner made in creation.
        if (start.Politics == Politics.Ruler && settings.RulerKingdomTakesYourBanner && clan.Kingdom != null)
        {
            clan.Color = color1;
            clan.Color2 = color2;
            clan.Banner = new Banner(bannerCode);
            clan.Kingdom.Banner = new Banner(bannerCode);
        }

        if (location != null) MovePartyTo(location);
        ShowStory(start, realm, report);
    }

    // ---- Realm and place ---------------------------------------------------------------------------------------

    /// <summary>For starts that need a realm but were given "Your Homeland": the realm sharing your culture.</summary>
    private static Kingdom? HomelandRealm(StartDef start)
    {
        CultureObject culture = Clan.PlayerClan.Culture;
        Vec2 here = MobileParty.MainParty.GetPosition2D;
        var candidates = Kingdom.All.Where(k => !k.IsEliminated && !k.IsMinorFaction && k.Settlements.Any(s => s.IsTown)).ToList();
        return candidates.Where(k => k.Culture == culture).OrderBy(k => RealmCenter(k).DistanceSquared(here)).FirstOrDefault()
               ?? candidates.OrderBy(k => RealmCenter(k).DistanceSquared(here)).FirstOrDefault();
    }

    private static Vec2 RealmCenter(Kingdom realm)
    {
        Settlement? mid = realm.FactionMidSettlement;
        if (mid != null) return mid.GetPosition2D;
        var fiefs = realm.Settlements.Where(s => s.IsFortification).ToList();
        if (fiefs.Count == 0) return MobileParty.MainParty.GetPosition2D;
        return new Vec2(fiefs.Average(s => s.GetPosition2D.X), fiefs.Average(s => s.GetPosition2D.Y));
    }

    private static IEnumerable<Settlement> SettlementsOf(Kingdom? realm) => realm != null ? realm.Settlements : Settlement.All;

    private static Settlement? NearestTown(Kingdom? realm, Vec2 to) =>
        SettlementsOf(realm).Where(s => s.IsTown).OrderBy(s => s.GetPosition2D.DistanceSquared(to)).FirstOrDefault()
        ?? Settlement.All.Where(s => s.IsTown).OrderBy(s => s.GetPosition2D.DistanceSquared(to)).FirstOrDefault();

    private static Settlement? NearestVillage(Kingdom? realm, Vec2 to) =>
        SettlementsOf(realm).Where(s => s.IsVillage).OrderBy(s => s.GetPosition2D.DistanceSquared(to)).FirstOrDefault()
        ?? NearestTown(realm, to);

    private static Settlement? NearestHideout(Vec2 to) =>
        Settlement.All.Where(s => s.IsHideout).OrderBy(s => s.GetPosition2D.DistanceSquared(to)).FirstOrDefault();

    private static void MovePartyTo(Settlement? settlement)
    {
        if (settlement == null) return;
        MobileParty party = MobileParty.MainParty;
        party.Position = settlement.GatePosition;
        party.SetMoveModeHold();
        if (GameStateManager.Current?.ActiveState is MapState map && map.Handler != null)
        {
            map.Handler.ResetCamera(true, true);
            map.Handler.TeleportCameraToMainParty();
        }
    }

    // ---- Clan ------------------------------------------------------------------------------------------------------

    private static void RaiseClanTier(int tier)
    {
        Clan clan = Clan.PlayerClan;
        if (tier <= clan.Tier) return;
        int needed = Campaign.Current.Models.ClanTierModel.GetRequiredRenownForTier(tier);
        if (needed > clan.Renown) clan.AddRenown(needed - clan.Renown + 1f, false);
    }

    // ---- Fiefs, kingdoms, crowns -----------------------------------------------------------------------------------

    /// <summary>
    /// Hands the start's towns and castles to the player, taking them from clans that hold more than one fief
    /// (never the ruling clan) where possible, so no house is left landless. Castles are taken near the town.
    /// </summary>
    private static List<Settlement> TakeFiefs(Kingdom realm, StartDef start, Vec2 center, List<string> report, bool farFromCapital = false)
    {
        var taken = new List<Settlement>();
        Vec2 anchor = center;

        for (int i = 0; i < start.Towns; i++)
        {
            Settlement? town = PickFief(realm, s => s.IsTown, anchor, taken, farFromCapital);
            if (town == null) { ErrorLog.Write($"No town in {realm.StringId} could be granted for '{start.Id}'."); break; }
            Grant(town, taken);
            anchor = town.GetPosition2D;
        }
        for (int i = 0; i < start.Castles; i++)
        {
            Settlement? castle = PickFief(realm, s => s.IsCastle, anchor, taken, farFromCapital && taken.Count == 0);
            if (castle == null) { ErrorLog.Write($"No castle in {realm.StringId} could be granted for '{start.Id}'."); break; }
            Grant(castle, taken);
        }

        if (taken.Count > 0)
        {
            report.Add("Your lands: " + string.Join(", ", taken.Select(s => s.Name.ToString())) + ".");
        }
        return taken;
    }

    private static Settlement? PickFief(Kingdom realm, Func<Settlement, bool> kind, Vec2 anchor, List<Settlement> exclude, bool farthest)
    {
        Clan? ruling = realm.RulingClan;
        var candidates = realm.Settlements
            .Where(s => kind(s) && !exclude.Contains(s) && !s.IsUnderSiege && s.OwnerClan != null && s.OwnerClan != Clan.PlayerClan)
            .ToList();
        if (candidates.Count == 0) return null;

        int Preference(Settlement s) =>
            s.OwnerClan == ruling ? 2 : s.OwnerClan.Fiefs.Count >= 2 ? 0 : 1;

        var ordered = candidates.OrderBy(Preference);
        return (farthest
                ? ordered.ThenByDescending(s => s.GetPosition2D.DistanceSquared(anchor))
                : ordered.ThenBy(s => s.GetPosition2D.DistanceSquared(anchor)))
            .First();
    }

    private static void Grant(Settlement settlement, List<Settlement> taken)
    {
        ChangeOwnerOfSettlementAction.ApplyByDefault(Hero.MainHero, settlement);
        taken.Add(settlement);
    }

    /// <summary>Proclaims the player's kingdom from the seized fiefs and sets it at war with the realm it left.</summary>
    private static void FoundKingdom(Kingdom realm, CultureObject culture, List<string> report)
    {
        Clan clan = Clan.PlayerClan;
        var name = new TextObject("{=as_kingdom_name}Kingdom of {CLAN}");
        name.SetTextVariable("CLAN", clan.Name);
        Campaign.Current.KingdomManager.CreateKingdom(name, clan.Name, culture, clan, culture.DefaultPolicyList, null, null, null);

        Kingdom? mine = clan.Kingdom;
        if (mine == null)
        {
            ErrorLog.Write("Creating the rebel kingdom failed; the clan stays independent.");
            return;
        }
        if (!mine.IsAtWarWith(realm)) DeclareWarAction.ApplyByRebellion(mine, realm);
        report.Add($"You have proclaimed the {mine.Name}, and {realm.Name} has declared war on you.");

        if (StartSettings.Current.RebelNamePrompt) AskKingdomName(mine);
    }

    private static void AskKingdomName(Kingdom kingdom)
    {
        InformationManager.ShowTextInquiry(new TextInquiryData(
            new TextObject("{=as_name_kingdom_t}Name Your Kingdom").ToString(),
            new TextObject("{=as_name_kingdom_d}What will your realm be called?").ToString(),
            true, true,
            GameTexts.FindText("str_done").ToString(),
            GameTexts.FindText("str_cancel").ToString(),
            input =>
            {
                string trimmed = (input ?? "").Trim();
                if (trimmed.Length > 0) kingdom.ChangeKingdomName(new TextObject("{=!}" + trimmed), new TextObject("{=!}" + trimmed));
            },
            null,
            false,
            text => new Tuple<bool, string>(!string.IsNullOrWhiteSpace(text) && text.Trim().Length <= 40, "1 to 40 characters"),
            "",
            kingdom.Name.ToString()), true, false);
    }

    /// <summary>
    /// Makes the player's clan the realm's ruling clan and grants the old ruler's best town as the royal seat.
    /// The old ruler dies (or, with ruler_old_ruler_dies=0, steps aside and lives on as a vassal).
    /// </summary>
    private static Settlement? TakeThrone(Kingdom realm, List<string> report)
    {
        Clan clan = Clan.PlayerClan;
        Hero? oldRuler = realm.Leader;
        Clan? oldClan = realm.RulingClan;

        if (clan.Kingdom != realm) ChangeKingdomAction.ApplyByJoinToKingdom(clan, realm, default, false);
        ChangeRulingClanAction.Apply(realm, clan);

        Settlement? seat = realm.Settlements
            .Where(s => s.IsTown && s.OwnerClan == oldClan)
            .OrderByDescending(s => s.Town.Prosperity)
            .FirstOrDefault()
            ?? realm.Settlements.Where(s => s.IsTown).OrderByDescending(s => s.Town.Prosperity).FirstOrDefault();
        if (seat != null && seat.OwnerClan != clan) ChangeOwnerOfSettlementAction.ApplyByDefault(Hero.MainHero, seat);

        if (oldRuler != null && oldRuler != Hero.MainHero && oldRuler.IsAlive)
        {
            if (StartSettings.Current.RulerOldRulerDies)
            {
                KillCharacterAction.ApplyByOldAge(oldRuler, false);
                report.Add($"{oldRuler.Name} is dead, and the crown of {realm.Name} has passed to you.");
            }
            else
            {
                report.Add($"{oldRuler.Name} has stepped aside, and the crown of {realm.Name} has passed to you.");
            }
        }
        else
        {
            report.Add($"You rule {realm.Name}.");
        }
        if (seat != null) report.Add($"Your royal seat is {seat.Name}.");
        return seat;
    }

    // ---- Party ---------------------------------------------------------------------------------------------------

    private static void AddTroops(StartDef start, CultureObject culture, Settlement? location, List<string> report)
    {
        if (start.Troops <= 0 || start.Pool == TroopPool.None) return;
        MobileParty party = MobileParty.MainParty;

        List<CharacterObject> pool = start.Pool == TroopPool.Bandit
            ? BanditPool(location?.Culture ?? culture)
            : LineTroops(start.Pool, culture, start.MinTier, start.MaxTier);
        if (pool.Count == 0)
        {
            ErrorLog.Write($"No troops found for '{start.Id}' ({culture.StringId}).");
            return;
        }

        var counts = new Dictionary<CharacterObject, int>();
        for (int i = 0; i < start.Troops; i++)
        {
            CharacterObject troop = pool[MBRandom.RandomInt(pool.Count)];
            counts[troop] = counts.TryGetValue(troop, out int n) ? n + 1 : 1;
        }

        bool wounded = start.Id == "captive";
        foreach (var entry in counts)
        {
            party.MemberRoster.AddToCounts(entry.Key, entry.Value, false, wounded ? (entry.Value + 1) / 2 : 0, 0, true, -1);
        }

        int men = party.MemberRoster.TotalManCount;
        party.ItemRoster.AddToCounts(DefaultItems.Grain, Math.Max(3, men / 4));
        if (men >= 20) party.ItemRoster.AddToCounts(DefaultItems.Meat, men / 10);
    }

    /// <summary>Every troop in a culture's recruit or noble line between two tiers, the line's start included.</summary>
    private static List<CharacterObject> LineTroops(TroopPool pool, CultureObject culture, int minTier, int maxTier)
    {
        var roots = new List<CharacterObject?>();
        switch (pool)
        {
            case TroopPool.Basic: roots.Add(culture.BasicTroop); break;
            case TroopPool.Elite: roots.Add(culture.EliteBasicTroop); break;
            case TroopPool.BasicAndElite: roots.Add(culture.BasicTroop); roots.Add(culture.EliteBasicTroop); break;
            case TroopPool.Mercenary:
                roots.AddRange(culture.BasicMercenaryTroops);
                if (roots.Count == 0) { roots.Add(culture.BasicTroop); roots.Add(culture.EliteBasicTroop); }
                break;
        }

        var all = new List<CharacterObject>();
        var queue = new Queue<CharacterObject>(roots.Where(r => r != null).Cast<CharacterObject>());
        while (queue.Count > 0)
        {
            CharacterObject troop = queue.Dequeue();
            if (all.Contains(troop)) continue;
            all.Add(troop);
            foreach (CharacterObject next in troop.UpgradeTargets ?? new CharacterObject[0]) queue.Enqueue(next);
        }

        var inRange = all.Where(t => t.Tier >= minTier && t.Tier <= maxTier).ToList();
        if (inRange.Count > 0) return inRange;
        if (all.Count > 0) return all;
        return culture.BasicTroop != null ? new List<CharacterObject> { culture.BasicTroop } : new List<CharacterObject>();
    }

    /// <summary>Looters plus the bandits of the region: forest bandits in Battania, steppe bandits on the steppe and so on.</summary>
    private static List<CharacterObject> BanditPool(CultureObject region)
    {
        string local = region.StringId switch
        {
            "aserai" => "desert_bandit",
            "battania" => "forest_bandit",
            "khuzait" => "steppe_bandit",
            "sturgia" => "sea_raiders",
            _ => "mountain_bandit",
        };
        var pool = new List<CharacterObject>();
        CharacterObject? looter = MBObjectManager.Instance.GetObject<CharacterObject>("looter");
        CharacterObject? bandit = MBObjectManager.Instance.GetObject<CharacterObject>(local);
        if (looter != null) pool.Add(looter);
        if (bandit != null) { pool.Add(bandit); pool.Add(bandit); }
        return pool;
    }

    /// <summary>
    /// Hires the nearest unattached wanderers. With fiefs, the first ones go to govern them; the rest ride with you.
    /// </summary>
    private static void HireCompanions(int count, List<Settlement> fiefs, Vec2 near, List<string> report)
    {
        if (count <= 0) return;
        List<Hero> hired = TakeWanderers(count, near);
        if (hired.Count < count) ErrorLog.Debug($"Only {hired.Count} of {count} companions could be hired.");

        var governed = new Queue<Settlement>(fiefs.Where(f => f.IsFortification && f.Town != null && f.Town.Governor == null));
        foreach (Hero companion in hired)
        {
            AddCompanionAction.Apply(Clan.PlayerClan, companion);
            companion.SetHasMet();
            if (governed.Count > 0 && hired.Count > 1)
            {
                Settlement fief = governed.Dequeue();
                TeleportHeroAction.ApplyDelayedTeleportToSettlementAsGovernor(companion, fief);
                report.Add($"{companion.Name} rides to govern {fief.Name}.");
            }
            else
            {
                AddHeroToPartyAction.Apply(companion, MobileParty.MainParty, false);
            }
        }
        if (hired.Count > 0) report.Add("Companions: " + string.Join(", ", hired.Select(h => h.Name.ToString())) + ".");
    }

    private static List<Hero> TakeWanderers(int count, Vec2 near) =>
        Hero.AllAliveHeroes
            .Where(h => h.IsWanderer && h.CompanionOf == null && h.Clan == null && h.IsActive && !h.IsPrisoner
                        && h.PartyBelongedTo == null && h.CurrentSettlement != null && !h.IsChild)
            .OrderBy(h => h.CurrentSettlement.GetPosition2D.DistanceSquared(near))
            .Take(count)
            .ToList();

    // ---- Merchant -------------------------------------------------------------------------------------------------

    /// <summary>Buys workshops (paid for by the start, on top of its gold), sends out a caravan and loads the mules.</summary>
    private static void SetUpTradingHouse(Settlement? home, Vec2 center, List<string> report)
    {
        StartSettings settings = StartSettings.Current;
        Hero hero = Hero.MainHero;
        Vec2 from = home?.GetPosition2D ?? center;

        var model = Campaign.Current.Models.WorkshopModel;
        int wanted = Math.Min(settings.MerchantWorkshops, model.GetMaxWorkshopCountForClanTier(Clan.PlayerClan.Tier) - hero.OwnedWorkshops.Count);
        var bought = new List<Workshop>();
        foreach (Settlement town in Settlement.All.Where(s => s.IsTown).OrderBy(s => s.GetPosition2D.DistanceSquared(from)).Take(6))
        {
            foreach (Workshop workshop in town.Town.Workshops
                         .Where(w => w.Owner != null && w.Owner != hero && w.WorkshopType != null && !w.WorkshopType.IsHidden)
                         .OrderBy(_ => MBRandom.RandomFloat)
                         .ToList())
            {
                if (bought.Count >= wanted) break;
                hero.ChangeHeroGold(model.GetCostForPlayer(workshop));
                ChangeOwnerOfWorkshopAction.ApplyByPlayerBuying(workshop);
                bought.Add(workshop);
            }
            if (bought.Count >= wanted) break;
        }
        if (bought.Count > 0)
        {
            report.Add("Your workshops: " + string.Join(", ", bought.Select(w => $"{w.WorkshopType.Name} in {w.Settlement.Name}")) + ".");
        }

        if (settings.MerchantCaravan && home != null)
        {
            Hero? leader = TakeWanderers(1, from).FirstOrDefault();
            bool sent = false;
            if (leader != null)
            {
                try
                {
                    AddCompanionAction.Apply(Clan.PlayerClan, leader);
                    leader.SetHasMet();
                    AddHeroToPartyAction.Apply(leader, MobileParty.MainParty, false);
                    CaravanPartyComponent.CreateCaravanParty(hero, home, null, false, leader, null, false);
                    report.Add($"{leader.Name} leads your caravan out of {home.Name}.");
                    sent = true;
                }
                catch (Exception ex)
                {
                    ErrorLog.Write("Creating the merchant caravan failed: " + ex);
                }
            }
            if (!sent)
            {
                hero.ChangeHeroGold(CaravanFallbackGold);
                report.Add($"No caravan master could be found, so its {CaravanFallbackGold:N0} denars are in your strongbox instead.");
            }
        }

        ItemRosterAdd("mule", 4);
        ItemRosterAdd("velvet", 6);
        ItemRosterAdd("jewelry", 3);
        ItemRosterAdd("wine", 8);
        ItemRosterAdd("oil", 8);
        ItemRosterAdd("linen", 8);
        MobileParty.MainParty.ItemRoster.AddToCounts(DefaultItems.Grain, 5);
    }

    private static void ItemRosterAdd(string itemId, int count)
    {
        ItemObject? item = MBObjectManager.Instance.GetObject<ItemObject>(itemId);
        if (item != null) MobileParty.MainParty.ItemRoster.AddToCounts(item, count);
    }

    // ---- Report ---------------------------------------------------------------------------------------------------

    private static void ShowStory(StartDef start, Kingdom? realm, List<string> report)
    {
        MobileParty party = MobileParty.MainParty;
        int troops = party.MemberRoster.TotalManCount - party.MemberRoster.TotalHeroes;
        if (troops > 0) report.Add($"{troops} men follow your banner.");
        report.Add($"Your purse holds {Hero.MainHero.Gold:N0} denars.");

        InformationManager.ShowInquiry(new InquiryData(
            start.Title.ToString(),
            string.Join("\n\n", report),
            true, false,
            GameTexts.FindText("str_ok").ToString(), "",
            null, null), true, false);
    }
}
