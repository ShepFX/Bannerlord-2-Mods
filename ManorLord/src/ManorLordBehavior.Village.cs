using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ManorLord;

/// <summary>
/// Ties to the village. A thriving estate grows the village and wins its notables' regard over time, and defending it
/// from raiders wins more. In return the village's lord expects customary dues every four weeks; paying keeps their
/// favour, refusing costs it. No dues are owed on a village the player's own clan holds.
/// </summary>
public sealed partial class ManorLordBehavior
{
    private const int DuesIntervalDays = 28;
    private const int NotableRegardCap = 30;

    private int _daysSinceDues;
    private int _incomeSinceDues;

    private void SyncVillage(IDataStore store)
    {
        store.SyncData("ml_days_since_dues", ref _daysSinceDues);
        store.SyncData("ml_income_since_dues", ref _incomeSinceDues);
    }

    /// <summary>Hearths the estate adds to the village each day while it is intact: more for a grander estate.</summary>
    private float HearthGrowthPerDay() => _damaged ? 0f : 0.1f * _estateTier;

    private void VillageDailyTick(int incomeToday)
    {
        Settlement? settlement = ManorSettlement;
        Village? village = settlement?.Village;
        if (settlement == null || village == null) return;

        village.Hearth += HearthGrowthPerDay();
        _incomeSinceDues += Math.Max(0, incomeToday);

        // Weekly, an intact estate earns a little regard from each notable, up to a modest cap.
        if (!_damaged && _daysOwned > 0 && _daysOwned % 7 == 0)
        {
            foreach (Hero notable in settlement.Notables.Where(n => n.IsAlive && n.GetRelationWithPlayer() < NotableRegardCap))
                ChangeRelationAction.ApplyPlayerRelation(notable, 1, false, false);
        }

        if (++_daysSinceDues >= DuesIntervalDays)
        {
            _daysSinceDues = 0;
            AskForDues(settlement);
        }
    }

    /// <summary>The lord owed dues, or null when there is none to pay (the player's own clan holds the village).</summary>
    private static Hero? DuesLord(Settlement settlement)
    {
        Clan? owner = settlement.OwnerClan;
        if (owner == null || owner == Clan.PlayerClan) return null;
        Hero? lord = owner.Leader;
        return lord != null && lord.IsAlive && lord != Hero.MainHero ? lord : null;
    }

    /// <summary>A tenth of the estate's income since the last dues, and never less than a token sum.</summary>
    private int DuesAmount() => Math.Max(100, _incomeSinceDues / 10);

    private void AskForDues(Settlement settlement)
    {
        Hero? lord = DuesLord(settlement);
        if (lord == null) { _incomeSinceDues = 0; return; }

        int amount = DuesAmount();
        bool canPay = _treasury + Hero.MainHero.Gold >= amount;
        TextObject body = new TextObject("{=ml_dues_body}A reeve from {LORD} arrives at {MANOR_NAME} to collect the customary dues on your land: {AMOUNT} denars.\n\nPaying keeps the peace with the village's lord. Refusing will not be forgotten.")
            .SetTextVariable("LORD", lord.Name)
            .SetTextVariable("MANOR_NAME", ManorDisplayName())
            .SetTextVariable("AMOUNT", amount);
        if (!canPay)
            body = new TextObject("{=ml_dues_cannot_pay}{BODY}\n\nNeither the manor treasury nor your purse can cover it.").SetTextVariable("BODY", body);

        InformationManager.ShowInquiry(new InquiryData(
            new TextObject("{=ml_dues_title}The Lord's Dues").ToString(),
            body.ToString(),
            true,
            canPay,
            canPay ? new TextObject("{=ml_btn_pay}Pay").ToString() : new TextObject("{=ml_btn_cannot_pay}Explain you cannot pay").ToString(),
            new TextObject("{=ml_btn_refuse}Refuse").ToString(),
            () => { if (canPay) PayDues(lord, amount); else RefuseDues(lord, -2); },
            () => RefuseDues(lord, -4)), true);
    }

    private void PayDues(Hero lord, int amount)
    {
        // The treasury pays first, the player's purse makes up the rest.
        int fromTreasury = Math.Min(_treasury, amount);
        _treasury -= fromTreasury;
        if (amount > fromTreasury) Hero.MainHero.ChangeHeroGold(-(amount - fromTreasury));
        _recordedExpenses += amount;
        _incomeSinceDues = 0;
        ChangeRelationAction.ApplyPlayerRelation(lord, 2, false, true);
        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_dues_paid}You paid {AMOUNT} denars in dues to {LORD}.")
            .SetTextVariable("AMOUNT", amount).SetTextVariable("LORD", lord.Name).ToString()));
    }

    private void RefuseDues(Hero lord, int relation)
    {
        _incomeSinceDues = 0;
        ChangeRelationAction.ApplyPlayerRelation(lord, relation, false, true);
        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_dues_refused}{LORD} received no dues from you. Your relation with them suffers.")
            .SetTextVariable("LORD", lord.Name).ToString(), Colors.Red));
    }

    /// <summary>The village is grateful when the player beats off a raid on the estate.</summary>
    private void RewardDefenseWithVillageGratitude()
    {
        Settlement? settlement = ManorSettlement;
        if (settlement == null) return;
        foreach (Hero notable in settlement.Notables.Where(n => n.IsAlive))
            ChangeRelationAction.ApplyPlayerRelation(notable, 3, false, false);
        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_village_grateful}The people of {VILLAGE} are grateful that you defended them.")
            .SetTextVariable("VILLAGE", settlement.Name).ToString(), Colors.Green));
    }

    /// <summary>The village line for the stewardship menu: growth, the notables' regard, and the lord's dues.</summary>
    private TextObject VillageStandingSummary()
    {
        Settlement? settlement = ManorSettlement;
        if (settlement == null) return TextObject.GetEmpty();

        var notables = settlement.Notables.Where(n => n.IsAlive).ToList();
        int regard = notables.Count == 0 ? 0 : (int)Math.Round(notables.Average(n => n.GetRelationWithPlayer()));
        Hero? lord = DuesLord(settlement);
        TextObject dues = lord == null
            ? new TextObject("{=ml_village_no_dues}Your clan holds the village, so no dues are owed.")
            : new TextObject("{=ml_village_dues}{LORD} expects about {AMOUNT} denars in dues in {DAYS} days (relation {RELATION}).")
                .SetTextVariable("LORD", lord.Name)
                .SetTextVariable("AMOUNT", DuesAmount())
                .SetTextVariable("DAYS", Math.Max(0, DuesIntervalDays - _daysSinceDues))
                .SetTextVariable("RELATION", FormatSigned((int)lord.GetRelationWithPlayer()));
        return new TextObject("{=ml_village_standing}The estate adds {HEARTHS} hearths a day to {VILLAGE}. Its notables' regard for you: {REGARD}.\n{DUES}")
            .SetTextVariable("HEARTHS", HearthGrowthPerDay().ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
            .SetTextVariable("VILLAGE", settlement.Name)
            .SetTextVariable("REGARD", FormatSigned(regard))
            .SetTextVariable("DUES", dues);
    }
}
