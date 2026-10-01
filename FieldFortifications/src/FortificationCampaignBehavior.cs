using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;
using Work = FieldFortifications.FortificationState.Work;

namespace FieldFortifications;

/// <summary>
/// Adds a "Fortify your position" entry to the pre-battle encounter menu (field battles only) that opens a submenu
/// where each work can be bought several times at a rising price, with a running total and a full refund.
/// </summary>
public sealed class FortificationCampaignBehavior : CampaignBehaviorBase
{
    private const string EncounterMenu = "encounter";
    private const string FortifyMenu = "ff_fortify";

    private sealed class WorkInfo
    {
        public Work Work;
        public string Id = "", Name = "", Plural = "", Verb = "", Tooltip = "";
    }

    private static readonly WorkInfo[] Works =
    {
        new() { Work = Work.Barricades, Id = "barricades", Name = "barricade line", Plural = "barricade lines", Verb = "{=ff_opt_barricades}Raise barricades",
                Tooltip = "{=ff_tip_barricades}A line of four spiked barricades you place during deployment. Foot soldiers must go around or hack through; horses that charge it are impaled." },
        new() { Work = Work.Ballista, Id = "ballista", Name = "ballista", Plural = "ballistas", Verb = "{=ff_opt_ballista}Set up a ballista",
                Tooltip = "{=ff_tip_ballista}A ballista with {AMMO} bolts, placed during deployment. Your archers crew it." },
        new() { Work = Work.Mangonel, Id = "catapult", Name = "catapult", Plural = "catapults", Verb = "{=ff_opt_mangonel}Set up a catapult",
                Tooltip = "{=ff_tip_mangonel}A mangonel with {AMMO} stones, placed during deployment. Your archers crew it." },
        new() { Work = Work.Arrows, Id = "arrows", Name = "arrow stockpile", Plural = "arrow stockpiles", Verb = "{=ff_opt_arrows}Stock arrows",
                Tooltip = "{=ff_tip_arrows}Two barrels of arrows placed during deployment. Archers running low walk over and refill; you can too." },
        new() { Work = Work.Tower, Id = "platform", Name = "archer platform", Plural = "archer platforms", Verb = "{=ff_opt_tower}Build an archer platform",
                Tooltip = "{=ff_tip_tower}A raised timber deck with a ramp at the back, placed during deployment. Order archers onto it once the battle starts." },
    };

    private FortificationSettings _settings = new();

    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
        // Backstop: the defender still digs in when the player attacks without opening the fortify menu.
        CampaignEvents.MapEventStarted.AddNonSerializedListener(this, (mapEvent, attacker, defender) =>
        {
            if (mapEvent.IsPlayerMapEvent) EnsureEnemyDecided();
        });
    }

    public override void SyncData(IDataStore store)
    {
        int[] bought = (int[])FortificationState.Bought.Clone();
        int[] enemy = (int[])FortificationState.Enemy.Clone();
        int spent = FortificationState.Spent;
        string decided = FortificationState.EnemyDecidedFor;
        for (int i = 0; i < bought.Length; i++) store.SyncData("ff_bought_" + i, ref bought[i]);
        for (int i = 0; i < enemy.Length; i++) store.SyncData("ff_enemy_" + i, ref enemy[i]);
        store.SyncData("ff_spent", ref spent);
        store.SyncData("ff_enemy_decided_for", ref decided);
        for (int i = 0; i < bought.Length; i++) FortificationState.Bought[i] = bought[i];
        for (int i = 0; i < enemy.Length; i++) FortificationState.Enemy[i] = enemy[i];
        FortificationState.Spent = spent;
        FortificationState.EnemyDecidedFor = decided ?? "";
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        _settings = FortificationSettings.Load();

        starter.AddGameMenuOption(EncounterMenu, "ff_fortify_open", "{=ff_opt_open}Fortify your position",
            args =>
            {
                if (CurrentFieldBattle() == null) return false;
                EnsureEnemyDecided();
                args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                args.Tooltip = new TextObject("{=ff_tip_open}Pay your men to raise barricades, set up engines, stock arrows and build a platform before the battle. {SUMMARY} {ENEMY}")
                    .SetTextVariable("SUMMARY", Summary())
                    .SetTextVariable("ENEMY", EnemySummary());
                return true;
            },
            args => GameMenu.SwitchToMenu(FortifyMenu),
            false, 1, false, null);

        starter.AddGameMenu(FortifyMenu,
            "{=ff_menu_body}Your men can throw up works before the battle. Each one is placed by you during deployment, and every extra copy of a work costs more and needs a better engineer than the last.{newline} {newline}{FF_ENGINEER}{newline}{FF_SUMMARY}{newline}{FF_ENEMY}",
            args =>
            {
                if (CurrentFieldBattle() == null)
                {
                    GameMenu.SwitchToMenu(EncounterMenu);
                    return;
                }
                SetVariables();
            },
            GameMenu.MenuOverlayType.Encounter, GameMenu.MenuFlags.None, null);

        int index = 0;
        foreach (WorkInfo info in Works)
        {
            WorkInfo captured = info;
            starter.AddGameMenuOption(FortifyMenu, "ff_buy_" + info.Id,
                info.Verb + " ({FF_" + info.Id + "_HAVE}/{FF_" + info.Id + "_MAX}) {FF_" + info.Id + "_TAIL}",
                args => BuyCondition(captured, args), args => Buy(captured), false, index++, true, null);
        }
        starter.AddGameMenuOption(FortifyMenu, "ff_refund", "{=ff_opt_refund}Take the works down ({FF_SPENT}{GOLD_ICON} back)",
            args =>
            {
                if (FortificationState.Spent <= 0 && !FortificationState.AnyPending) return false;
                args.optionLeaveType = GameMenuOption.LeaveType.Escape;
                args.Tooltip = new TextObject("{=ff_tip_refund}Cancel everything bought for this battle and get the full price back.");
                return true;
            },
            args =>
            {
                if (FortificationState.Spent > 0) GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, FortificationState.Spent, false);
                FortificationState.Clear();
                GameMenu.SwitchToMenu(FortifyMenu);
            },
            false, index++, false, null);
        starter.AddGameMenuOption(FortifyMenu, "ff_back", "{=ff_opt_back}Back",
            args => { args.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
            args => GameMenu.SwitchToMenu(EncounterMenu),
            true, index, false, null);
    }

    private bool BuyCondition(WorkInfo info, MenuCallbackArgs args)
    {
        SetVariables();
        args.optionLeaveType = GameMenuOption.LeaveType.DefendAction;
        int have = FortificationState.Count(info.Work), max = _settings.Max(info.Work);
        int price = _settings.Price(info.Work, have);
        int required = _settings.Required(info.Work, have);
        (Hero engineer, int skill) = Engineer();
        if (have >= max)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("{=ff_tip_full}Your men can build no more of these for one battle.");
        }
        else if (skill < required)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("{=ff_tip_skill}Needs an engineer with Engineering {REQ}. Your best is {NAME} with {SKILL}.")
                .SetTextVariable("REQ", required).SetTextVariable("NAME", engineer.Name).SetTextVariable("SKILL", skill);
        }
        else if (Hero.MainHero.Gold < price)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("{=ff_tip_gold}You need {AMOUNT} denars.").SetTextVariable("AMOUNT", price);
        }
        else
        {
            args.Tooltip = new TextObject(info.Tooltip).SetTextVariable("AMMO", _settings.EngineAmmo);
        }
        return true;
    }

    private void Buy(WorkInfo info)
    {
        int have = FortificationState.Count(info.Work), max = _settings.Max(info.Work);
        int price = _settings.Price(info.Work, have);
        (Hero engineer, int skill) = Engineer();
        if (have >= max || Hero.MainHero.Gold < price || skill < _settings.Required(info.Work, have)) return;
        GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, price, false);
        FortificationState.Bought[(int)info.Work] = have + 1;
        FortificationState.Spent += price;
        // The engineer learns from every work ordered.
        float xp = price / 1000f * _settings.EngineeringXpPer1000;
        if (xp > 0f) engineer.AddSkillXp(DefaultSkills.Engineering, xp);
        GameMenu.SwitchToMenu(FortifyMenu);
    }

    /// <summary>Text variables for the menu body and every option: counts, next prices, and the running total.</summary>
    private void SetVariables()
    {
        foreach (WorkInfo info in Works)
        {
            int have = FortificationState.Count(info.Work), max = _settings.Max(info.Work);
            MBTextManager.SetTextVariable("FF_" + info.Id + "_HAVE", have);
            MBTextManager.SetTextVariable("FF_" + info.Id + "_MAX", max);
            TextObject tail = have >= max
                ? new TextObject("{=ff_full}full")
                : new TextObject("{=ff_tail}{AMOUNT}{GOLD_ICON}  Eng {REQ}")
                    .SetTextVariable("AMOUNT", Denars(_settings.Price(info.Work, have)))
                    .SetTextVariable("REQ", _settings.Required(info.Work, have));
            MBTextManager.SetTextVariable("FF_" + info.Id + "_TAIL", tail, false);
        }
        (Hero best, int bestSkill) = Engineer();
        MBTextManager.SetTextVariable("FF_ENGINEER", new TextObject("{=ff_engineer}Your engineer: {NAME}, Engineering {SKILL}.")
            .SetTextVariable("NAME", best.Name).SetTextVariable("SKILL", bestSkill), false);
        MBTextManager.SetTextVariable("FF_SPENT", Denars(FortificationState.Spent));
        MBTextManager.SetTextVariable("FF_SUMMARY", Summary(), false);
        MBTextManager.SetTextVariable("FF_ENEMY", EnemySummary(), false);
    }

    private static TextObject Summary()
    {
        var parts = new List<string>();
        foreach (WorkInfo info in Works)
        {
            int have = FortificationState.Count(info.Work);
            if (have == 1) parts.Add("1 " + info.Name);
            else if (have > 1) parts.Add(have + " " + info.Plural);
        }
        if (parts.Count == 0)
            return new TextObject("{=ff_sum_none}Nothing bought yet. You have {GOLD}{GOLD_ICON}.").SetTextVariable("GOLD", Denars(Hero.MainHero.Gold));
        return new TextObject("{=ff_sum_some}Bought for this battle: {LIST}. Paid {SPENT}{GOLD_ICON}, {GOLD}{GOLD_ICON} left.")
            .SetTextVariable("LIST", string.Join(", ", parts))
            .SetTextVariable("SPENT", Denars(FortificationState.Spent))
            .SetTextVariable("GOLD", Denars(Hero.MainHero.Gold));
    }

    /// <summary>
    /// Works out once per encounter what the defending lord has dug in. A lord only fortifies when he is holding
    /// ground against the player: his Engineering decides what his men know how to build, and his purse decides how
    /// much of it he can pay for, obstacles first. Nothing is deducted from him; the purse is only a limit.
    /// </summary>
    /// <summary>
    /// Asks the campaign to work out the defender's works if it has not already. The mission calls this because the
    /// battle-start event can fire after the mission's behaviours are created.
    /// </summary>
    public static void EnsureDecided()
    {
        try { Campaign.Current?.GetCampaignBehavior<FortificationCampaignBehavior>()?.EnsureEnemyDecided(); }
        catch (Exception ex) { ErrorLog.Write("Deciding the defender works failed: " + ex); }
    }

    private void EnsureEnemyDecided()
    {
        MapEvent? battle = CurrentFieldBattle();
        if (battle == null) return;
        _settings = FortificationSettings.Load();

        bool test = _settings.AiTest;
        BattleSideEnum enemySide = battle.PlayerSide == BattleSideEnum.Attacker ? BattleSideEnum.Defender : BattleSideEnum.Attacker;
        PartyBase? enemyParty = battle.GetMapEventSide(enemySide)?.LeaderParty;
        string key = enemySide + ":" + (enemyParty?.Name?.ToString() ?? "unknown");
        if (FortificationState.EnemyDecidedFor == key)
        {
            if (_settings.Debug) ErrorLog.Debug("Defender works: already decided for " + key + ".");
            return;
        }
        FortificationState.EnemyDecidedFor = key;
        Array.Clear(FortificationState.Enemy, 0, FortificationState.Enemy.Length);
        if (!_settings.AiFortifications || _settings.AiScale <= 0f)
        {
            if (_settings.Debug) ErrorLog.Debug("Defender works: turned off in settings.");
            return;
        }
        // Normally only a lord holding ground against the player digs in; the test switch lets anyone build.
        if (!test && enemySide != BattleSideEnum.Defender)
        {
            if (_settings.Debug) ErrorLog.Debug("Defender works: the player is not attacking, so nobody digs in.");
            return;
        }
        Hero? defender = enemyParty?.LeaderHero;
        if (defender == Hero.MainHero) return;
        if (!test && (defender == null || defender.Clan == Clan.PlayerClan)) return;

        int skill = test ? int.MaxValue : defender!.GetSkillValue(DefaultSkills.Engineering);
        int purse = test ? int.MaxValue : defender!.Gold;
        foreach (WorkInfo info in Works)
        {
            // The platform is the player's own trick; a lord in the field does not raise one.
            if (info.Work == Work.Tower) continue;
            int max = (int)Math.Round(_settings.Max(info.Work) * _settings.AiScale);
            int count = 0;
            while (count < max && skill >= _settings.Required(info.Work, count))
            {
                int price = _settings.Price(info.Work, count);
                if (purse < price) break;
                purse -= price;
                count++;
            }
            FortificationState.Enemy[(int)info.Work] = count;
        }
        if (_settings.Debug)
            ErrorLog.Debug($"Defender works decided: test={test} side={enemySide} leader={(defender != null ? defender.Name.ToString() : "none")} " +
                           $"skill={(test ? -1 : skill)} purse={(test ? -1 : purse)} -> [{string.Join(",", FortificationState.Enemy)}]");
    }

    /// <summary>What the player's scouts can tell him about the enemy's works.</summary>
    private static TextObject EnemySummary()
    {
        if (!FortificationState.EnemyAnyPending) return new TextObject("");
        var parts = new List<string>();
        foreach (WorkInfo info in Works)
        {
            int have = FortificationState.EnemyCount(info.Work);
            if (have == 1) parts.Add("1 " + info.Name);
            else if (have > 1) parts.Add(have + " " + info.Plural);
        }
        return new TextObject("{=ff_enemy_dug_in}Your scouts report the enemy has dug in: {LIST}.")
            .SetTextVariable("LIST", string.Join(", ", parts));
    }

    private static string Denars(int amount) => amount.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>The hero in the player's party with the highest Engineering: you, or a companion who is better at it.</summary>
    private static (Hero hero, int skill) Engineer()
    {
        Hero best = Hero.MainHero;
        int bestSkill = best.GetSkillValue(DefaultSkills.Engineering);
        MobileParty? party = MobileParty.MainParty;
        if (party != null)
        {
            foreach (TroopRosterElement element in party.MemberRoster.GetTroopRoster())
            {
                Hero? hero = element.Character?.HeroObject;
                if (hero == null || hero == best || element.Number <= 0) continue;
                int skill = hero.GetSkillValue(DefaultSkills.Engineering);
                if (skill > bestSkill) { best = hero; bestSkill = skill; }
            }
        }
        return (best, bestSkill);
    }

    private static MapEvent? CurrentFieldBattle()
    {
        MapEvent? battle = PlayerEncounter.Battle ?? MobileParty.MainParty?.MapEvent;
        return battle != null && battle.IsFieldBattle ? battle : null;
    }

    private static void OnMapEventEnded(MapEvent mapEvent)
    {
        if (mapEvent.IsPlayerMapEvent) FortificationState.Clear();
    }
}
