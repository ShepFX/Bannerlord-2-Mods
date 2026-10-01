using System;
using System.Collections.Generic;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ManorLord;

/// <summary>
/// Stationed troops: real soldiers from the player's party left at the manor. They train on the training field,
/// heal (faster with a physician), draw wages and supplies, defend the estate, and can be collected again at will.
/// Hired guards remain a separate, simpler pool.
/// </summary>
public sealed partial class ManorLordBehavior
{
    private TroopRoster? _stationed;

    private TroopRoster Stationed => _stationed ??= TroopRoster.CreateDummyTroopRoster();

    /// <summary>How many stationed troops the guard quarters can house; grows with estate rank.</summary>
    private int StationedCapacity() => !_guardQuarters ? 0 : _estateTier >= 3 ? 40 : _estateTier >= 2 ? 30 : 20;

    private int StationedCount => _stationed?.TotalManCount ?? 0;
    private int StationedHealthy => _stationed?.TotalHealthyCount ?? 0;

    private void SyncStationed(IDataStore store)
    {
        store.SyncData("ml_stationed_troops", ref _stationed);
    }

    private void AddStationedMenus(CampaignGameStarter starter)
    {
        AddMenuOption(starter, HouseholdMenuId, "ml_station_troops", "{=ml_opt_station}Station or collect troops ({COUNT}/{CAP} stationed)", StationCondition, OpenStationScreen);
    }

    private bool StationCondition(MenuCallbackArgs args)
    {
        if (!AtOwnedManor(args)) return false;
        MBTextManager.SetTextVariable("COUNT", StationedCount);
        MBTextManager.SetTextVariable("CAP", StationedCapacity());
        args.optionLeaveType = GameMenuOption.LeaveType.ManageGarrison;
        args.IsEnabled = _guardQuarters;
        if (!args.IsEnabled) args.Tooltip = new TextObject("{=ml_tip_station_quarters}Requires guard quarters.");
        return true;
    }

    /// <summary>The game's own party screen: the manor's troops on the left, the player's party on the right.</summary>
    private void OpenStationScreen(MenuCallbackArgs args)
    {
        int capacity = StationedCapacity();
        PartyScreenHelper.OpenScreenWithDummyRosterWithMainParty(
            Stationed,
            TroopRoster.CreateDummyTroopRoster(),
            ManorDisplayName(),
            capacity,
            (leftMembers, leftPrisoners, rightMembers, rightPrisoners, leftLimit, rightLimit) =>
                leftMembers.TotalManCount <= capacity
                    ? new Tuple<bool, TextObject>(true, TextObject.GetEmpty())
                    : new Tuple<bool, TextObject>(false, new TextObject("{=ml_station_over_cap}The manor can house only {CAP} stationed troops.").SetTextVariable("CAP", capacity)),
            (leftOwner, leftMembers, leftPrisoners, rightOwner, rightMembers, rightPrisoners, fromCancel) =>
            {
                if (!fromCancel) _stationed = leftMembers;
                Campaign.Current?.CurrentMenuContext?.Refresh();
            },
            // Ordinary soldiers only: heroes belong with the clan, prisoners have no place in the guard quarters.
            (character, type, side, leftOwner) => !character.IsHero && type == PartyScreenLogic.TroopType.Member);
    }

    /// <summary>Extra lines for the household menu describing the stationed troops.</summary>
    private TextObject StationedSummary()
    {
        if (StationedCount == 0)
            return new TextObject("{=ml_household_stationed_none}No troops are stationed here. Leave soldiers at the manor to train them and let the wounded recover.");
        return new TextObject("{=ml_household_stationed}Stationed troops: {COUNT}/{CAP} ({WOUNDED} wounded), {WAGES} denars a day. They train on the training field and heal faster with a physician.")
            .SetTextVariable("COUNT", StationedCount)
            .SetTextVariable("CAP", StationedCapacity())
            .SetTextVariable("WOUNDED", _stationed?.TotalWounded ?? 0)
            .SetTextVariable("WAGES", StationedDailyWages());
    }

    /// <summary>The troops' normal party wages, as the game's own wage model prices them.</summary>
    private int StationedDailyWages()
    {
        if (_stationed == null || _stationed.Count == 0) return 0;
        PartyWageModel? model = Campaign.Current?.Models?.PartyWageModel;
        int total = 0;
        for (int i = 0; i < _stationed.Count; i++)
        {
            TroopRosterElement element = _stationed.GetElementCopyAtIndex(i);
            total += (model?.GetCharacterWage(element.Character) ?? 2) * element.Number;
        }
        return total;
    }

    private int StationedSupplyUse() => StationedCount <= 0 ? 0 : (StationedCount + 1) / 2;

    /// <summary>Daily training and healing. Training needs the field, an intact estate and enough supplies.</summary>
    private void StationedDailyTick(bool suppliesShort)
    {
        if (_stationed == null || _stationed.Count == 0) return;
        bool training = _trainingField && !_damaged && !suppliesShort;
        int xpPerTroop = 6 + (_hasCaptain ? 3 : 0) + ((_guardOrder ?? "watch") == "drill" ? 6 : 0);
        float healShare = _hasPhysician ? 0.5f : 0.25f;
        for (int i = 0; i < _stationed.Count; i++)
        {
            TroopRosterElement element = _stationed.GetElementCopyAtIndex(i);
            if (training && element.Number > element.WoundedNumber)
                _stationed.AddXpToTroop(element.Character, xpPerTroop * (element.Number - element.WoundedNumber));
            if (element.WoundedNumber > 0)
            {
                int healed = Math.Max(1, (int)Math.Ceiling(element.WoundedNumber * healShare));
                _stationed.AddToCountsAtIndex(i, 0, -Math.Min(healed, element.WoundedNumber));
            }
        }
    }

    /// <summary>When wages go unpaid and no hired guard is left to desert, a stationed soldier leaves instead.</summary>
    private bool TryStationedDesertion()
    {
        if (_stationed == null || _stationed.TotalRegulars <= 0) return false;
        int index = MBRandom.RandomInt(_stationed.Count);
        TroopRosterElement element = _stationed.GetElementCopyAtIndex(index);
        _stationed.AddToCountsAtIndex(index, -1, element.WoundedNumber > 0 ? -1 : 0);
        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_stationed_deserted}An unpaid stationed {TROOP} has deserted.")
            .SetTextVariable("TROOP", element.Character.Name).ToString(), Colors.Red));
        return true;
    }

    /// <summary>Healthy stationed troops join the defenders of a playable manor defense.</summary>
    private void AddStationedDefenders(TroopRoster defenders)
    {
        if (_stationed == null) return;
        for (int i = 0; i < _stationed.Count; i++)
        {
            TroopRosterElement element = _stationed.GetElementCopyAtIndex(i);
            int healthy = element.Number - element.WoundedNumber;
            if (healthy > 0) defenders.AddToCounts(element.Character, healthy);
        }
    }

    /// <summary>Defense score contributed by healthy stationed troops: better soldiers count for more.</summary>
    private int StationedDefenseScore()
    {
        if (_stationed == null) return 0;
        int score = 0;
        for (int i = 0; i < _stationed.Count; i++)
        {
            TroopRosterElement element = _stationed.GetElementCopyAtIndex(i);
            score += (element.Number - element.WoundedNumber) * (3 + element.Character.Tier);
        }
        return score;
    }

    /// <summary>
    /// Casualties among the stationed troops after a raid or a lost defense: a share of the healthy are killed and a
    /// further share wounded. Returns a line for the outcome report, or empty when nobody was stationed.
    /// </summary>
    private TextObject ApplyStationedCasualties(float killShare, float woundShare)
    {
        if (_stationed == null || _stationed.TotalHealthyCount <= 0) return TextObject.GetEmpty();
        int killed = 0, wounded = 0;
        for (int i = _stationed.Count - 1; i >= 0; i--)
        {
            TroopRosterElement element = _stationed.GetElementCopyAtIndex(i);
            int healthy = element.Number - element.WoundedNumber;
            if (healthy <= 0) continue;
            int dead = Math.Min(healthy, (int)Math.Round(healthy * killShare));
            int hurt = Math.Min(healthy - dead, (int)Math.Round(healthy * woundShare));
            _stationed.AddToCountsAtIndex(i, -dead, hurt);
            killed += dead;
            wounded += hurt;
        }
        _stationed.RemoveZeroCounts();
        if (killed == 0 && wounded == 0) return TextObject.GetEmpty();
        return new TextObject("{=ml_msg_stationed_lost}{KILLED} stationed troops were killed and {WOUNDED} wounded.")
            .SetTextVariable("KILLED", killed)
            .SetTextVariable("WOUNDED", wounded);
    }

    /// <summary>On sale the stationed troops march back to the player's party.</summary>
    private void ReturnStationedToParty()
    {
        if (_stationed == null || _stationed.TotalManCount <= 0) return;
        MobileParty.MainParty.MemberRoster.Add(_stationed);
        _stationed = TroopRoster.CreateDummyTroopRoster();
        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_stationed_returned}Your stationed troops rejoin your party.").ToString()));
    }

    /// <summary>Up to <paramref name="max"/> healthy stationed troops, for the estate scene to stand in the yard.</summary>
    private List<CharacterObject> StationedForScene(int max)
    {
        var troops = new List<CharacterObject>();
        if (_stationed == null) return troops;
        for (int i = 0; i < _stationed.Count && troops.Count < max; i++)
        {
            TroopRosterElement element = _stationed.GetElementCopyAtIndex(i);
            for (int n = element.Number - element.WoundedNumber; n > 0 && troops.Count < max; n--)
                troops.Add(element.Character);
        }
        return troops;
    }

    /// <summary>Appends a non-empty outcome line to an inquiry body.</summary>
    private static string WithLine(TextObject body, TextObject line)
    {
        string extra = line.ToString();
        return string.IsNullOrEmpty(extra) ? body.ToString() : body + "\n\n" + extra;
    }
}
