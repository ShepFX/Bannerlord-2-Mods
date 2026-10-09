using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace StrategicCampaignAI;

public sealed class StrategicArmyManagementModel : ArmyManagementCalculationModel, IWrappingModel
{
    public string WrappedModelName => BaseModel?.GetType().Name ?? "none";

    // Wraps the model registered before ours (War Sails' naval army rules, when present) instead of replacing it.
    // Everything not adjusted below passes straight through.
    private ArmyManagementCalculationModel? _fallback;
    private ArmyManagementCalculationModel Base => BaseModel ?? (_fallback ??= new DefaultArmyManagementCalculationModel());

    public override float AIMobilePartySizeRatioToCallToArmy => Base.AIMobilePartySizeRatioToCallToArmy;
    public override float PlayerMobilePartySizeRatioToCallToArmy => Base.PlayerMobilePartySizeRatioToCallToArmy;
    public override float MinimumNeededFoodInDaysToCallToArmy => Base.MinimumNeededFoodInDaysToCallToArmy;
    public override float MaximumDistanceToCallToArmy => Base.MaximumDistanceToCallToArmy;
    public override int InfluenceValuePerGold => Base.InfluenceValuePerGold;
    public override int AverageCallToArmyCost => Base.AverageCallToArmyCost;
    public override int CohesionThresholdForDispersion => Base.CohesionThresholdForDispersion;
    public override float MaximumWaitTime => Base.MaximumWaitTime;
    public override bool CanPlayerCreateArmy(out TextObject disabledReason) => Base.CanPlayerCreateArmy(out disabledReason);
    public override int CalculatePartyInfluenceCost(MobileParty armyLeaderParty, MobileParty party) => Base.CalculatePartyInfluenceCost(armyLeaderParty, party);
    public override float DailyBeingAtArmyInfluenceAward(MobileParty armyMemberParty) => Base.DailyBeingAtArmyInfluenceAward(armyMemberParty);
    public override int CalculateTotalInfluenceCost(Army army, float percentage) => Base.CalculateTotalInfluenceCost(army, percentage);
    public override float GetPartySizeScore(MobileParty party) => Base.GetPartySizeScore(party);
    public override int GetPartyRelation(Hero hero) => Base.GetPartyRelation(hero);
    public override int CalculateNewCohesion(Army army, PartyBase newParty, int calculatedCohesion, int sign) =>
        Base.CalculateNewCohesion(army, newParty, calculatedCohesion, sign);
    public override int GetCohesionBoostInfluenceCost(Army army, int percentageToBoost) => Base.GetCohesionBoostInfluenceCost(army, percentageToBoost);

    private static readonly TextObject CooldownText = new("{=SCAI_COOLDOWN}Recovering from a recent defeat or army dispersal.");
    private static readonly TextObject StrengthText = new("{=SCAI_STRENGTH}Party must recover before joining another army.");
    private static readonly TextObject SupplyLineText = new("{=SCAI_SUPPLY_LINES}Overextended supply lines");

    public override bool CanLordCreateArmy(MobileParty mobileParty, out MBList<MobileParty> possibleArmyMembers)
    {
        if (!Base.CanLordCreateArmy(mobileParty, out possibleArmyMembers))
        {
            return false;
        }

        // The player decides for themselves whether to raise an army.
        if (StrategicAiHelpers.IsPlayerControlled(mobileParty))
        {
            return true;
        }

        try
        {
            if (mobileParty.LeaderHero == null ||
                StrategicAiState.IsArmyCreationOnCooldown(mobileParty.LeaderHero) ||
                !StrategicAiHelpers.IsRecoveredEnoughToLeadArmy(mobileParty))
            {
                return false;
            }

            if (mobileParty.MapFaction is Kingdom kingdom &&
                kingdom.Armies != null &&
                kingdom.Armies.Count >= StrategicAiHelpers.GetAllowedArmyCount(kingdom))
            {
                return false;
            }

            if (possibleArmyMembers == null)
            {
                return false;
            }

            for (int i = possibleArmyMembers.Count - 1; i >= 0; i--)
            {
                MobileParty member = possibleArmyMembers[i];
                if (member == null ||
                    member.LeaderHero == null ||
                    StrategicAiState.IsArmyCreationOnCooldown(member.LeaderHero) ||
                    !StrategicAiHelpers.IsRecoveredEnoughToJoinArmy(member))
                {
                    possibleArmyMembers.RemoveAt(i);
                }
            }

            return possibleArmyMembers.Count >= StrategicAiTuning.MinimumArmyMemberParties &&
                   GetProspectiveArmyStrength(mobileParty, possibleArmyMembers) >= StrategicAiTuning.MinimumProspectiveArmyStrength;
        }
        catch (Exception)
        {
            // If our gating throws, defer to vanilla rather than blocking armies.
            return true;
        }
    }

    public override bool CheckPartyEligibility(MobileParty party, out TextObject explanation)
    {
        if (!Base.CheckPartyEligibility(party, out explanation))
        {
            return false;
        }

        if (StrategicAiHelpers.IsPlayerControlled(party))
        {
            return true;
        }

        try
        {
            if (party.LeaderHero != null && StrategicAiState.IsArmyCreationOnCooldown(party.LeaderHero))
            {
                explanation = CooldownText;
                return false;
            }

            if (!StrategicAiHelpers.IsRecoveredEnoughToJoinArmy(party))
            {
                explanation = StrengthText;
                return false;
            }
        }
        catch (Exception)
        {
            return true;
        }

        return true;
    }

    private static float GetProspectiveArmyStrength(MobileParty leader, MBList<MobileParty> members)
    {
        float strength = leader.Party.EstimatedStrength;
        for (int i = 0; i < members.Count; i++)
        {
            strength += members[i].Party.EstimatedStrength;
        }

        return strength;
    }

    public override ExplainedNumber CalculateDailyCohesionChange(Army army, bool includeDescriptions = false)
    {
        ExplainedNumber result = Base.CalculateDailyCohesionChange(army, includeDescriptions);

        try
        {
            if (StrategicAiState.GetEnemyTerritoryDays(army) > StrategicAiTuning.SupplyGraceDays)
            {
                result.Add(StrategicAiTuning.DeepTerritoryCohesionPenalty, includeDescriptions ? SupplyLineText : null);
            }
        }
        catch (Exception)
        {
            // Keep the vanilla cohesion result.
        }

        return result;
    }
}
