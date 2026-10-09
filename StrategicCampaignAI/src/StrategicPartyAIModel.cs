using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace StrategicCampaignAI;

/// <summary>
/// Shapes whether a party is willing to engage or avoid another.
///
/// This is how interception and shadowing are expressed now. The old versions
/// issued SetMoveEngageParty and SetMoveGoAroundParty orders, which the vanilla
/// AI cleared on its own schedule, so the mod re-imposed them and the army never
/// arrived. Answering the engine's own initiative questions influences the same
/// decisions without ever taking the wheel.
///
/// Both overrides are on a very hot path, so they do nothing but compare two
/// strengths -- no map sweeps, no allocation.
/// </summary>
public sealed class StrategicPartyAIModel : MobilePartyAIModel, IWrappingModel
{
    public string WrappedModelName => BaseModel?.GetType().Name ?? "none";

    // Wraps the model registered before ours (War Sails' naval party AI, when present) instead of replacing it.
    // Patrol radii, flee radii and initiative behaviour all pass straight through.
    private MobilePartyAIModel? _fallback;
    private MobilePartyAIModel Base => BaseModel ?? (_fallback ??= new DefaultMobilePartyAIModel());

    public override float AiCheckInterval => Base.AiCheckInterval;
    public override float FleeToNearbyPartyRadius => Base.FleeToNearbyPartyRadius;
    public override float FleeToNearbySettlementRadius => Base.FleeToNearbySettlementRadius;
    public override float HideoutPatrolDistanceAsDays => Base.HideoutPatrolDistanceAsDays;
    public override float FortificationPatrolDistanceAsDays => Base.FortificationPatrolDistanceAsDays;
    public override float FortificationPortPatrolDistanceAsDays => Base.FortificationPortPatrolDistanceAsDays;
    public override float VillagePatrolDistanceAsDays => Base.VillagePatrolDistanceAsDays;
    public override float SettlementDefendingNearbyPartyCheckRadius => Base.SettlementDefendingNearbyPartyCheckRadius;
    public override float SettlementDefendingWaitingPositionRadius => Base.SettlementDefendingWaitingPositionRadius;
    public override float NeededFoodsInDaysThresholdForSiege => Base.NeededFoodsInDaysThresholdForSiege;
    public override float NeededFoodsInDaysThresholdForRaid => Base.NeededFoodsInDaysThresholdForRaid;
    public override float GetPatrolRadius(MobileParty mobileParty, CampaignVec2 patrolPoint) => Base.GetPatrolRadius(mobileParty, patrolPoint);
    public override float GetSettlementNearbyThreatAndAllyCheckRadius(Settlement settlement, bool isPort) =>
        Base.GetSettlementNearbyThreatAndAllyCheckRadius(settlement, isPort);
    public override bool ShouldPartyCheckInitiativeBehavior(MobileParty mobileParty) => Base.ShouldPartyCheckInitiativeBehavior(mobileParty);
    public override void GetBestInitiativeBehavior(MobileParty mobileParty, out AiBehavior bestInitiativeBehavior,
        out MobileParty bestInitiativeTargetParty, out float bestInitiativeBehaviorScore, out Vec2 averageEnemyVec) =>
        Base.GetBestInitiativeBehavior(mobileParty, out bestInitiativeBehavior, out bestInitiativeTargetParty,
            out bestInitiativeBehaviorScore, out averageEnemyVec);

    public override bool ShouldConsiderAttacking(MobileParty party, MobileParty targetParty)
    {
        bool baseResult = Base.ShouldConsiderAttacking(party, targetParty);

        if (!StrategicAiTuning.EnableInitiativeShaping)
        {
            return baseResult;
        }

        try
        {
            if (party == null ||
                targetParty == null ||
                StrategicAiHelpers.IsPlayerControlled(party) ||
                StrategicAiHelpers.IsPlayerControlled(targetParty))
            {
                return baseResult;
            }

            float ours = OwnStrength(party);
            float theirs = OwnStrength(targetParty);
            if (ours <= 0f || theirs <= 0f)
            {
                return baseResult;
            }

            // Never talk a party into a fight it should lose, whatever vanilla says.
            if (theirs > ours * StrategicAiTuning.AvoidStrengthRatio)
            {
                return false;
            }

            // An army we assigned the interceptor role is deliberately keener to
            // run down a weaker enemy than vanilla would be.
            Army? army = party.Army;
            if (baseResult ||
                army == null ||
                army.LeaderParty != party ||
                StrategicAiState.GetRole(army) != StrategicArmyRole.Interceptor)
            {
                return baseResult;
            }

            return theirs <= ours * StrategicAiTuning.InterceptStrengthRatio;
        }
        catch (Exception)
        {
            return baseResult;
        }
    }

    public override bool ShouldConsiderAvoiding(MobileParty party, MobileParty targetParty)
    {
        bool baseResult = Base.ShouldConsiderAvoiding(party, targetParty);

        if (!StrategicAiTuning.EnableInitiativeShaping)
        {
            return baseResult;
        }

        try
        {
            if (party == null ||
                targetParty == null ||
                StrategicAiHelpers.IsPlayerControlled(party) ||
                baseResult)
            {
                return baseResult;
            }

            float ours = OwnStrength(party);
            float theirs = OwnStrength(targetParty);

            return ours > 0f &&
                   theirs > 0f &&
                   theirs > ours * StrategicAiTuning.AvoidStrengthRatio;
        }
        catch (Exception)
        {
            return baseResult;
        }
    }

    /// <summary>
    /// Strength of a party, counting its whole army once when it leads one.
    /// Uses EstimatedStrength rather than a followers sweep because this runs
    /// inside the initiative check.
    /// </summary>
    private static float OwnStrength(MobileParty party)
    {
        Army? army = party.Army;
        if (army != null && army.LeaderParty == party)
        {
            return army.EstimatedStrength;
        }

        return party.Party?.EstimatedStrength ?? 0f;
    }
}
