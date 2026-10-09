using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace StrategicCampaignAI;

/// <summary>
/// Encourages AI lords to leave more troops behind in a fief that is genuinely
/// in danger.
///
/// This replaces the old behaviour, which moved troops by mutating rosters
/// directly (RemoveNumberOfNonHeroTroopsRandomly followed by MemberRoster.Add)
/// because the campaign system exposes no action for transferring troops to a
/// garrison. That was traced as the cause of a hard crash roughly 15-21 days
/// into a campaign, and it is also what produced the old "troops disappear when
/// entering a city" reports.
///
/// The engine already asks how many troops a party should leave when it visits a
/// settlement, and performs the transfer itself. Answering that question is the
/// supported way to do this: no roster is touched by the mod at all.
/// </summary>
public sealed class StrategicGarrisonModel : SettlementGarrisonModel, IWrappingModel
{
    public string WrappedModelName => BaseModel?.GetType().Name ?? "none";

    // Wraps the model registered before ours (War Sails' garrison rules, when present) instead of replacing it.
    // Everything not adjusted below passes straight through.
    private SettlementGarrisonModel? _fallback;
    private SettlementGarrisonModel Base => BaseModel ?? (_fallback ??= new DefaultSettlementGarrisonModel());

    public override int GetMaximumDailyAutoRecruitmentCount(Town town) => Base.GetMaximumDailyAutoRecruitmentCount(town);
    public override ExplainedNumber CalculateBaseGarrisonChange(Settlement settlement, bool includeDescriptions = false) =>
        Base.CalculateBaseGarrisonChange(settlement, includeDescriptions);
    public override int FindNumberOfTroopsToTakeFromGarrison(MobileParty mobileParty, Settlement settlement, float idealGarrisonStrengthPerWalledCenter = 0f) =>
        Base.FindNumberOfTroopsToTakeFromGarrison(mobileParty, settlement, idealGarrisonStrengthPerWalledCenter);
    public override float GetMaximumDailyRepairAmount(Settlement settlement) => Base.GetMaximumDailyRepairAmount(settlement);

    public override int FindNumberOfTroopsToLeaveToGarrison(MobileParty mobileParty, Settlement settlement)
    {
        int baseCount = Base.FindNumberOfTroopsToLeaveToGarrison(mobileParty, settlement);

        if (!StrategicAiTuning.EnableGarrisonReinforcement)
        {
            return baseCount;
        }

        try
        {
            // The player decides what to do with their own troops.
            if (mobileParty == null ||
                settlement == null ||
                StrategicAiHelpers.IsPlayerControlled(mobileParty) ||
                settlement.OwnerClan == Clan.PlayerClan)
            {
                return baseCount;
            }

            if (!StrategicAiHelpers.IsFortification(settlement) ||
                !StrategicAiHelpers.IsOwnTerritory(mobileParty.MapFaction, settlement) ||
                !StrategicAiHelpers.IsLowGarrisonFortification(settlement))
            {
                return baseCount;
            }

            // Only reinforce somewhere actually under threat, not every weak fief.
            float threat = StrategicAiHelpers.NearbyMajorEnemyLordStrength(
                settlement,
                mobileParty.MapFaction!,
                StrategicAiTuning.ThreatenedFortificationRadius);

            if (threat <= 1f && !settlement.IsUnderSiege)
            {
                return baseCount;
            }

            int available = mobileParty.MemberRoster?.TotalHealthyCount ?? 0;
            int floor = StrategicAiTuning.MinimumLeaderPartyTroopsAfterDonation;
            int desired = baseCount + StrategicAiTuning.GarrisonReinforcementBonus;

            // Never strip the lord below a fighting minimum.
            int maximum = available - floor;
            if (maximum <= baseCount)
            {
                return baseCount;
            }

            return Math.Min(desired, maximum);
        }
        catch (Exception)
        {
            // Any doubt, defer to vanilla.
            return baseCount;
        }
    }
}
