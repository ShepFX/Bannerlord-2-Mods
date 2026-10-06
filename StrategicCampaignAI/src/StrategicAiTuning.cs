namespace StrategicCampaignAI;

/// <summary>
/// Tuning knobs for the strategic layer.
///
/// These are mutable statics rather than consts so that <see cref="StrategicAiSettings"/>
/// can override them from ModuleData/settings.txt without a rebuild.
/// </summary>
internal static class StrategicAiTuning
{
    // ---------------------------------------------------------------- geometry
    public static float FrontlineScanRadius = 55f;
    public static float DeepTargetFriendlyRadius = 75f;
    public static float OverextendedTargetDistance = 85f;
    public static float SiegeRadarRadius = 95f;
    public static float LocalAllyRadius = 55f;
    public static float HomelandDefenseThreatRadius = 80f;
    /// <summary>Enemy strength near the capital that triggers the DefendCapitalRegion goal.</summary>
    public static float CapitalThreatStrength = 900f;
    /// <summary>Radius used to judge whether one of our fiefs is actually in danger.</summary>
    public static float ThreatenedFortificationRadius = 45f;
    /// <summary>Nearby enemy strength must exceed the garrison by this factor to count as threatened.</summary>
    public static float ThreatenedGarrisonRatio = 1.5f;
    public static float VillageDefenseRadius = 65f;
    public static float InterceptorRadius = 70f;
    public static float ConsolidationPatrolRadius = 45f;
    public static float StagingDistance = 38f;
    public static float StagingTargetDistance = 105f;
    public static float ShadowMinDistance = 28f;
    public static float ShadowMaxDistance = 70f;
    public static float ObjectiveArrivalDistance = 5f;
    public static float GarrisonDonationRange = 8f;

    // ------------------------------------------------------------ score bounds
    // The single most important safety rail in the mod. Individual multipliers
    // below are *preferences*; multiplied together they used to drive offensive
    // scores to ~1/5000 of vanilla while inflating defensive scores ~20x, which
    // made every AI army refuse to besiege and patrol its own fiefs forever.
    // The combined multiplier is clamped into these bands so the AI can always
    // still see an enemy settlement as a worthwhile objective.
    public static float MinOffenseMultiplier = 0.45f;
    public static float MaxOffenseMultiplier = 3.0f;
    public static float MinDefenseMultiplier = 0.65f;
    public static float MaxDefenseMultiplier = 1.85f;

    // -------------------------------------------------------- offensive shaping
    public static float FrontlineTargetMultiplier = 1.9f;
    public static float DeepTargetMultiplier = 0.55f;
    public static float OverextendedTargetMultiplier = 0.7f;
    public static float ChokepointTargetMultiplier = 1.55f;
    public static float ClaimTargetMultiplier = 1.4f;
    public static float EconomicTargetMultiplier = 1.3f;
    public static float MinorFactionTargetMultiplier = 0.7f;
    public static float DuplicateTargetPenalty = 0.7f;
    public static float WarExhaustionOffenseMultiplier = 0.75f;
    public static float PeacePressureOffenseMultiplier = 0.7f;
    public static float SiegeViabilityWeakReliefMultiplier = 0.6f;
    public static float NoblePrisonerReliefMultiplier = 1.4f;
    public static float MercenaryEconomicTargetMultiplier = 1.2f;
    public static float SevereWeatherTargetMultiplier = 0.85f;
    public static float WinterCampaignMultiplier = 0.9f;

    /// <summary>
    /// Hysteresis. An army that already owns a target keeps a bonus on it so a
    /// marginal rival target cannot flip the decision every evaluation. Without
    /// this, competing penalties made armies reverse direction repeatedly --
    /// the "running in circles" report.
    /// </summary>
    public static float CurrentObjectiveStickinessMultiplier = 1.4f;

    // -------------------------------------------------------- defensive shaping
    public static float CapitalDefenseMultiplier = 1.5f;
    public static float HighProsperityDefenseMultiplier = 1.25f;
    public static float ThreatenedFortificationDefenseMultiplier = 1.6f;
    public static float WarExhaustionDefenseMultiplier = 1.3f;
    public static float MercenaryDefenseMultiplier = 0.8f;

    // ------------------------------------------------------------------ cadence
    public static int StrategicUpdateIntervalHours = 4;
    /// <summary>Minimum campaign hours between two strategic move orders for one army.</summary>
    public static float ArmyOrderMinIntervalHours = 12f;
    public static float ObjectiveCommitmentHours = 36f;
    /// <summary>Do not re-issue the same destination to the same army within this window.</summary>
    public static float RepeatOrderSuppressionHours = 48f;
    public static float TargetFailureCooldownHours = 96f;
    public static float RecentCaptureConsolidationHours = 48f;
    public static float WarGoalReevaluationDays = 7f;
    public static int StuckArmyCheckTicks = 3;
    public static float StuckArmyMovementThreshold = 1.5f;

    // ------------------------------------------------------------- army gating
    public static int SupplyGraceDays = 5;
    public static float DeepTerritoryCohesionPenalty = -3.5f;
    public static float SharedOperationMinStrength = 520f;
    // ------------------------------------------------------- siege abandonment
    /// <summary>Relief-to-our-strength ratio above which a siege is judged hopeless.</summary>
    public static float SiegeAbandonReliefRatio = 1.6f;
    /// <summary>
    /// Radius for the relief sweep. Deliberately tighter than SiegeRadarRadius:
    /// a besieging army is by definition deep in enemy land, so a 95-unit sweep
    /// finds enemy lords who are nowhere near relieving the siege.
    /// </summary>
    public static float SiegeReliefRadius = 55f;
    /// <summary>
    /// Score multiplier applied to a hopeless siege. Deliberately below the
    /// offensive clamp floor: this is the signal that makes the vanilla AI lift
    /// the siege itself, so it has to beat every alternative objective.
    /// </summary>
    public static float SiegeAbandonMultiplier = 0.08f;
    /// <summary>
    /// Score multiplier for an offensive mission against a settlement this
    /// faction recently failed to take.
    ///
    /// Applied outside the clamp, like SiegeAbandonMultiplier. The cooldown used
    /// to be a 0.6x factor inside a clamp whose floor is 0.45, which cannot
    /// change the AI's mind about its dominant target: an army that gave up a
    /// siege walked straight back, re-sieged, waited out the confirmation window
    /// and gave up again, on a 15-21 hour cycle, for days.
    /// </summary>
    public static float FailedTargetMultiplier = 0.12f;
    /// <summary>
    /// How long the hopeless verdict must hold before the abandon multiplier is
    /// applied.
    ///
    /// Without this the verdict was re-evaluated from scratch on every score
    /// query, and because it can only ever apply to the settlement a party is
    /// *currently* besieging, lifting the siege deleted the penalty and the
    /// abandoned castle immediately looked attractive again. With two castles
    /// close enough to share one relief force that closed into a loop: besiege
    /// A, lift, besiege B, lift, besiege A. Requiring the verdict to survive a
    /// few campaign hours means a relief force has to actually stay before an
    /// army walks away from a siege.
    /// </summary>
    public static float SiegeAbandonConfirmationHours = 8f;

    // ------------------------------------------------------------- army roles
    /// <summary>Bonus when a mission matches the army role we assigned.</summary>
    public static float RoleAlignedMultiplier = 1.25f;
    /// <summary>Penalty when it does not.</summary>
    public static float RoleMismatchMultiplier = 0.8f;
    /// <summary>
    /// Minimum campaign hours an army keeps an assigned role.
    ///
    /// Roles used to be rebuilt from scratch on every strategic tick, and they
    /// are handed out by strength ranking, so a handful of casualties reordered
    /// the list and two armies swapped Aggressor and Defender with each other.
    /// Each swap moves the besiege-versus-defend preference by
    /// RoleAlignedMultiplier / RoleMismatchMultiplier, which is what players saw
    /// as armies turning around every few hours.
    /// </summary>
    public static float RoleMinimumHoldHours = 24f;

    /// <summary>
    /// How long the kingdom stays on a defensive footing after the last of its
    /// fiefs stops looking threatened.
    ///
    /// The gate is a bare "one or more fiefs threatened" count, and that count
    /// comes from its own hard threshold, so a single enemy party drifting in
    /// and out of a 45-unit circle re-roled every army in the realm. This latch
    /// is the dead-band: entering is immediate, leaving takes a quiet spell.
    /// </summary>
    public static float UrgentDefenseLatchHours = 24f;

    // ------------------------------------------------------------ garrisoning
    /// <summary>Extra troops an AI lord will leave to a dangerously weak garrison.</summary>
    public static int GarrisonReinforcementBonus = 18;

    // -------------------------------------------------- raid and consolidation
    /// <summary>Defensive weight for a village being raided right now.</summary>
    public static float RaidedVillageDefenseMultiplier = 1.6f;
    /// <summary>Defensive weight for a fief captured in the last couple of days.</summary>
    public static float RecentCaptureDefenseMultiplier = 1.35f;

    // ------------------------------------------------------------- initiative
    /// <summary>Target must be at or below this share of our strength to be run down.</summary>
    public static float InterceptStrengthRatio = 0.8f;
    /// <summary>Enemy at or above this share of our strength is avoided.</summary>
    public static float AvoidStrengthRatio = 1.5f;

    // ------------------------------------------------------------- war exhaustion
    /// <summary>Enemy-to-own strength ratio above which a faction counts as exhausted.</summary>
    public static float ExhaustionStrengthRatio = 1.5f;
    /// <summary>Fraction of our fortifications under threat that counts as exhausted.</summary>
    public static float ExhaustionThreatenedFraction = 0.34f;
    /// <summary>Floor for the above, so small kingdoms are not permanently exhausted.</summary>
    public static float ExhaustionMinThreatenedFortifications = 2f;
    /// <summary>Simultaneously raided villages that count as exhausted, at the least.</summary>
    public static int ExhaustionRaidedVillages = 3;
    /// <summary>
    /// Share of our villages raided at once that counts as exhausted. The flat count above is only a floor, so a
    /// realm with dozens of villages is not exhausted by the same three raids as a city-state with six.
    /// </summary>
    public static float ExhaustionRaidedFraction = 0.25f;
    public static double GarrisonReinforcementCooldownDays = 3d;
    public static int ReinforcementTroopDonation = 18;
    public static int MinimumLeaderPartyTroopsAfterDonation = 65;
    public static float LowGarrisonStrength = 420f;
    public static float HighProsperityThreshold = 4500f;
    public static double DefeatedLeaderArmyCooldownDays = 2d;
    public static double DispersedLeaderArmyCooldownDays = 0.75d;
    public static double NewWeakPartyGraceDays = 0.35d;
    public static float MinimumArmyLeaderStrength = 80f;
    public static float MinimumArmyMemberStrength = 35f;
    public static int MinimumArmyLeaderHealthyTroops = 22;
    public static int MinimumArmyMemberHealthyTroops = 10;
    public static int MinimumArmyMemberParties = 2;
    public static float MinimumProspectiveArmyStrength = 260f;
    public static int MaxArmiesPerKingdom = 99;
    public static int MaxArmiesPerStrongKingdom = 99;
    public static float StrongKingdomStrength = 9000f;

    // ------------------------------------------------------------------ toggles
    /// <summary>
    /// Master switch for the direct move-order layer (army roles, staging,
    /// interception). When false the mod only shapes vanilla's target scores and
    /// never issues a move order itself.
    ///
    /// Off by default, for two measured reasons.
    ///
    /// It fights the vanilla AI. Vanilla re-plans on its own schedule and clears
    /// our move orders, so the layer re-imposes them and the army never arrives;
    /// in testing one lord was re-sent to the same castle six times across three
    /// days without reaching it. Suppression now limits that, but the tug of war
    /// is inherent to issuing orders against an AI that is also issuing them.
    ///
    /// It reduces results. Over comparable runs the corrected scoring alone
    /// produced roughly three times the successful sieges that it did with this
    /// layer active: vanilla executes our scoring perfectly well once the scores
    /// are right, and pulling leaders off objectives only interrupts it.
    /// </summary>
    public static bool EnableStrategicOrders = false;

    /// <summary>
    /// Allow army leaders to top up a dangerously weak friendly garrison in passing.
    ///
    /// ON by default, and answered through
    /// SettlementGarrisonModel.FindNumberOfTroopsToLeaveToGarrison so the engine
    /// performs the transfer itself. The history below is why nothing here
    /// touches a roster any more.
    ///
    /// The old implementation moved troops by mutating rosters directly
    /// (RemoveNumberOfNonHeroTroopsRandomly then MemberRoster.Add) because the
    /// campaign system exposes no action for transferring troops to a garrison.
    /// Across five test runs every crash had this enabled and the only clean run
    /// had it disabled; a run with everything else off still crashed with just
    /// this active, while the same configuration without it ran nearly twice as
    /// long. It was also the feature behind the earlier "troops disappear when
    /// entering a city" report. Both problems belonged to the roster mutation,
    /// not to the idea, which is why the model-based version is on.
    /// </summary>
    public static bool EnableGarrisonReinforcement = true;

    /// <summary>
    /// Allow armies to abandon a siege they are clearly going to lose.
    ///
    /// ON by default, and expressed purely through the target score: a siege
    /// judged hopeless is scored near zero so the vanilla AI lifts it itself.
    /// Nothing reaches into LiftSiegeAction by reflection any more.
    ///
    /// The verdict is confirmed over SiegeAbandonConfirmationHours before it is
    /// applied. Turning this off disables siege abandonment entirely, which is
    /// still the quickest way to rule it out when diagnosing an army that keeps
    /// changing its mind about a siege.
    /// </summary>
    public static bool EnableSiegeRetreat = true;

    /// <summary>
    /// Query live map weather when scoring targets.
    ///
    /// Off by default while an unexplained hard crash is being bisected: this is
    /// the mod's remaining call into the engine from the scoring hot path.
    /// Season effects (WinterCampaignMultiplier) are unaffected -- those read
    /// CampaignTime only and are always safe.
    /// </summary>
    public static bool EnableWeatherEffects = true;

    /// <summary>
    /// Shape whether parties engage or avoid each other (interception and
    /// shadowing). Answers the engine's initiative questions instead of issuing
    /// move orders.
    /// </summary>
    public static bool EnableInitiativeShaping = true;

    /// <summary>Write strategic decisions to the game log for debugging.</summary>
    public static bool VerboseLogging = false;

    /// <summary>
    /// Persist siege failures, lord cooldowns, army objectives and war goals
    /// across save/load. The save definer stays registered either way, so
    /// turning this off cannot make an existing save unloadable.
    /// </summary>
    public static bool EnablePersistentMemory = true;
}
