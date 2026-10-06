using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace StrategicCampaignAI;

public sealed class StrategicCampaignAIBehavior : CampaignBehaviorBase
{
    private static int LastStrategicUpdateHour = -1;
    private static readonly Dictionary<string, int> LastDiagnosticDayByKingdom = new();

    public override void RegisterEvents()
    {
        CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGameCreated);
        CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
        CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
        CampaignEvents.VillageBeingRaided.AddNonSerializedListener(this, OnVillageBeingRaided);
        CampaignEvents.ArmyCreated.AddNonSerializedListener(this, OnArmyCreated);
        CampaignEvents.ArmyDispersed.AddNonSerializedListener(this, OnArmyDispersed);
        CampaignEvents.MobilePartyCreated.AddNonSerializedListener(this, OnMobilePartyCreated);
        CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
    }

    public override void SyncData(IDataStore dataStore)
    {
        StrategicAiState.SyncData(dataStore);
    }

    private static void OnNewGameCreated(CampaignGameStarter starter) => ResetForNewCampaign();

    // Behaviour SyncData has already restored the persisted memory by the time
    // this fires, so only the derived runtime state may be cleared here.
    private static void OnGameLoaded(CampaignGameStarter starter)
    {
        LastStrategicUpdateHour = -1;
        StrategicAiState.ResetRuntimeOnly();
        StrategicAiSettings.LoadOnce();
        LogStartupBanner("save loaded");
    }

    private static void ResetForNewCampaign()
    {
        // These caches are process-wide statics, so a second campaign in the same
        // session would otherwise inherit the previous one's decisions.
        LastStrategicUpdateHour = -1;
        StrategicAiState.Reset();
        StrategicAiSettings.LoadOnce();
        LogStartupBanner("new campaign");
    }

    /// <summary>
    /// Records the effective configuration once per campaign so a user log shows
    /// whether their settings file was picked up and what the key bounds are.
    /// </summary>
    private static void LogStartupBanner(string context)
    {
        StrategicAiLog.Write("--- Strategic Campaign AI ready (" + context + ") ---");
        StrategicAiLog.Write("settings file: " + (StrategicAiSettings.FileFound ? "loaded" : "not found, using defaults") +
                             " (" + (StrategicAiSettings.ResolvedPath ?? "unresolved") + ")");
        StrategicAiLog.Write("strategic orders: " + StrategicAiTuning.EnableStrategicOrders +
                             ", persistent memory: " + StrategicAiTuning.EnablePersistentMemory +
                             ", siege retreat: " + StrategicAiTuning.EnableSiegeRetreat +
                             ", garrison donation: " + StrategicAiTuning.EnableGarrisonReinforcement +
                             ", weather: " + StrategicAiTuning.EnableWeatherEffects +
                             ", initiative: " + StrategicAiTuning.EnableInitiativeShaping);
        StrategicAiLog.Write("offence clamp: " + StrategicAiTuning.MinOffenseMultiplier + " to " + StrategicAiTuning.MaxOffenseMultiplier +
                             ", defence clamp: " + StrategicAiTuning.MinDefenseMultiplier + " to " + StrategicAiTuning.MaxDefenseMultiplier);
    }

    private static void Log(string message)
    {
        StrategicAiLog.Write(message);
    }

    private static void OnDailyTick()
    {
        try
        {
            StrategicAiState.PruneExpired();
            PruneStateForLiveArmies();

            foreach (Kingdom kingdom in Kingdom.All)
            {
                if (kingdom.IsEliminated)
                {
                    continue;
                }

                RefreshFactionStatus(kingdom);

                foreach (Army army in kingdom.Armies.ToList())
                {
                    int currentDays = StrategicAiState.GetEnemyTerritoryDays(army);
                    StrategicAiState.SetEnemyTerritoryDays(
                        army,
                        StrategicAiHelpers.IsDeepInEnemyTerritory(army) ? currentDays + 1 : 0);
                }
            }
        }
        catch (Exception exception)
        {
            Log("Daily tick failed: " + exception.Message);
        }
    }

    private static void OnHourlyTick()
    {
        // Runs every hour, ahead of the cadence gate below. The siege verdict
        // needs finer resolution than the four-hourly planner, and it must keep
        // ticking while a siege is under way rather than only on planning hours.
        UpdateSiegeWatch();

        int currentHour = (int)(CampaignTime.Now.ToDays * 24d);
        if (currentHour == LastStrategicUpdateHour ||
            currentHour % StrategicAiTuning.StrategicUpdateIntervalHours != 0)
        {
            return;
        }

        LastStrategicUpdateHour = currentHour;

        foreach (Kingdom kingdom in Kingdom.All)
        {
            if (kingdom.IsEliminated)
            {
                continue;
            }

            try
            {
                RefreshFactionStatus(kingdom);
                AssignArmyRoles(kingdom);
                ObserveArmyObjectives(kingdom);
                LogKingdomDiagnostics(kingdom);

                foreach (Army army in kingdom.Armies.ToList())
                {
                    if (army?.LeaderParty == null)
                    {
                        continue;
                    }

                    TryRunAssignedArmyRole(kingdom, army);
                }
            }
            catch (Exception exception)
            {
                // One bad kingdom must not stop the rest of the map from planning.
                Log("Strategic update failed for " + kingdom.Name + ": " + exception.Message);
            }
        }
    }

    /// <summary>
    /// Decides, once an hour, whether each AI siege in progress is hopeless, and
    /// records the verdict for the score model to read.
    ///
    /// This exists because the verdict cannot be reached correctly from the
    /// scoring path. There it was recomputed per query and could only ever apply
    /// to the settlement a party was already besieging, so lifting the siege
    /// deleted the penalty and the abandoned castle immediately scored full value
    /// again -- two castles close enough to share one relief force traded the
    /// same army back and forth indefinitely. Reaching it here gives the verdict
    /// somewhere to persist, lets it be confirmed over several hours before it
    /// bites, and gives us the one moment at which a siege is genuinely
    /// abandoned, which is when the failure cooldown should be recorded.
    /// </summary>
    private static void UpdateSiegeWatch()
    {
        try
        {
            var besieging = new HashSet<string>();

            foreach (MobileParty party in StrategicAiCache.GetActiveLordParties())
            {
                if (party == null || StrategicAiHelpers.IsPlayerControlled(party))
                {
                    continue;
                }

                // Every party in a besieging army shares its BesiegedSettlement,
                // but only the leader's score decides whether the siege holds --
                // and a member judged on its own strength alone would call almost
                // any siege hopeless. Watch the leader, and lone lords.
                if (party.Army != null && party.Army.LeaderParty != party)
                {
                    continue;
                }

                Settlement? current = party.BesiegedSettlement;
                Settlement? previous = StrategicAiState.GetWatchedSiegeTarget(party);

                if (previous != null && previous != current)
                {
                    // The siege ended. If the place is still in enemy hands it
                    // was not taken, so record the failure -- this is the call
                    // that was unreachable on default settings, which is why
                    // TargetFailureCooldownHours never applied to anything and
                    // an army could turn straight back around.
                    if (StrategicAiHelpers.IsEnemy(party.MapFaction, previous.MapFaction))
                    {
                        StrategicAiState.MarkFailedTarget(party.MapFaction, previous);
                        Log(party.Name + " gave up the siege of " + previous.Name + ".");
                    }
                }

                if (current == null)
                {
                    StrategicAiState.SetWatchedSiegeTarget(party, null);
                    continue;
                }

                string? partyKey = party.Party?.Id;
                if (!string.IsNullOrEmpty(partyKey))
                {
                    besieging.Add(partyKey!);
                }

                StrategicAiState.SetWatchedSiegeTarget(party, current);
                StrategicAiState.SetSiegeHopeless(party, IsSiegeHopeless(party, current));
            }

            StrategicAiState.PruneSiegeWatch(besieging);
        }
        catch (Exception exception)
        {
            Log("Siege watch failed: " + exception.Message);
        }
    }

    /// <summary>
    /// True when the relief force around a settlement this party is besieging
    /// clearly outmatches it.
    ///
    /// The relief sweep excludes anyone already inside the walls or already
    /// fighting here. Counting them made a castle with two or three lords holed
    /// up in it read as hopelessly relieved the moment the siege camp went up --
    /// those troops are what the siege is against, not a force coming to break
    /// it.
    /// </summary>
    private static bool IsSiegeHopeless(MobileParty party, Settlement besieged)
    {
        if (party.MapFaction == null)
        {
            return false;
        }

        float relief = StrategicAiHelpers.SiegeReliefStrength(
            besieged,
            party.MapFaction,
            StrategicAiTuning.SiegeReliefRadius);

        Army? army = party.Army;
        float ourTotal = army != null && army.LeaderParty == party
            ? army.EstimatedStrength
            : party.Party?.EstimatedStrength ?? 0f;

        return ourTotal > 0f && relief > ourTotal * StrategicAiTuning.SiegeAbandonReliefRatio;
    }

    /// <summary>
    /// Drops per-army memory for armies that no longer exist. Target locks are
    /// persisted, so without this a save accumulates locks belonging to armies
    /// that dissolved campaigns ago, each one holding a settlement under
    /// DuplicateTargetPenalty for every army that is still alive.
    /// </summary>
    private static void PruneStateForLiveArmies()
    {
        var live = new HashSet<string>();

        foreach (Kingdom kingdom in Kingdom.All)
        {
            if (kingdom.Armies == null)
            {
                continue;
            }

            foreach (Army army in kingdom.Armies)
            {
                if (army != null)
                {
                    live.Add(StrategicAiState.GetLiveArmyKey(army));
                }
            }
        }

        StrategicAiState.PruneDeadArmies(live);
    }

    /// <summary>
    /// Recomputes the faction snapshot then the war goal. Order matters: the war
    /// goal is derived from the fresh status, and the status caches the goal, so
    /// the snapshot is stored again afterwards.
    /// </summary>
    private static void RefreshFactionStatus(Kingdom kingdom)
    {
        StrategicFactionStatus status = CalculateFactionStatus(kingdom);
        StrategicAiState.SetFactionStatus(kingdom, status);
        UpdateWarGoal(kingdom);
        StrategicAiState.SetFactionStatus(kingdom, status);
    }

    private static StrategicFactionStatus CalculateFactionStatus(Kingdom kingdom)
    {
        var status = new StrategicFactionStatus
        {
            OwnedStrength = kingdom.CurrentTotalStrength,
            OwnedFortifications = kingdom.Settlements.Count(StrategicAiHelpers.IsFortification)
        };

        // Only rival kingdoms count. Every kingdom is permanently at war with the
        // minor and bandit factions, so the raw FactionsAtWarWith list reports
        // 9-12 wars for everyone and its combined strength dwarfs any single
        // realm -- which made every faction permanently war-exhausted.
        foreach (IFaction enemyFaction in kingdom.FactionsAtWarWith)
        {
            if (!StrategicAiHelpers.IsMajorWarFaction(enemyFaction))
            {
                continue;
            }

            status.ActiveWars++;

            // An enemy fighting several wars cannot turn its whole strength on us, so count only our share of
            // it. Summing every enemy's full strength made both sides of a war "outmatched" whenever either was
            // fighting on another front too, and on maps with many kingdoms nearly every realm went on the
            // defensive at once and wars stalled.
            int enemyWars = enemyFaction.FactionsAtWarWith.Count(StrategicAiHelpers.IsMajorWarFaction);
            status.EnemyStrength += enemyFaction.CurrentTotalStrength / MathF.Max(1, enemyWars);
        }

        // "Threatened" must mean genuinely in danger, not merely near a border.
        // Using the wide homeland radius and a bare garrison comparison flagged
        // most of a realm's fiefs on the opening day of a fresh campaign, which
        // then read as war exhaustion before a shot had been fired.
        status.ThreatenedFortifications = kingdom.Settlements.Count(settlement =>
            StrategicAiHelpers.IsFortification(settlement) &&
            (settlement.IsUnderSiege ||
             StrategicAiHelpers.NearbyMajorEnemyLordStrength(settlement, kingdom, StrategicAiTuning.ThreatenedFortificationRadius) >
             (settlement.Party?.EstimatedStrength ?? 0f) * StrategicAiTuning.ThreatenedGarrisonRatio));

        var villages = kingdom.Settlements.SelectMany(settlement => settlement.BoundVillages).ToList();
        status.OwnedVillages = villages.Count;
        status.RaidedVillages = villages.Count(village => village.Settlement.IsUnderRaid || village.Settlement.IsRaided);

        return status;
    }

    private static void UpdateWarGoal(Kingdom kingdom)
    {
        StrategicFactionStatus status = StrategicAiState.GetFactionStatus(kingdom);

        // The goal is normally held for WarGoalReevaluationDays so it does not
        // flap. The exception is a kingdom still sitting on ForcePeace after it
        // has stopped being exhausted: that damps its offence for up to a week
        // for no reason, so let it come off the peace footing immediately.
        bool stalePeaceGoal = status.WarGoal == StrategicWarGoal.ForcePeace && !status.IsExhausted;

        if (!stalePeaceGoal && !StrategicAiState.ShouldReevaluateWarGoal(kingdom))
        {
            return;
        }

        StrategicWarGoal goal;

        if (status.IsExhausted)
        {
            goal = StrategicWarGoal.ForcePeace;
        }
        else if (kingdom.InitialHomeSettlement != null &&
                 StrategicAiHelpers.NearbyMajorEnemyLordStrength(kingdom.InitialHomeSettlement, kingdom, StrategicAiTuning.HomelandDefenseThreatRadius) > StrategicAiTuning.CapitalThreatStrength)
        {
            goal = StrategicWarGoal.DefendCapitalRegion;
        }
        else if (StrategicAiState.GetLostClaim(kingdom) != null)
        {
            goal = StrategicWarGoal.ReclaimLostFief;
        }
        else if (status.RaidedVillages > 0)
        {
            goal = StrategicWarGoal.WeakenEconomy;
        }
        else
        {
            goal = StrategicWarGoal.BorderWar;
        }

        StrategicAiState.SetWarGoal(kingdom, goal);
        Log(kingdom.Name + " war goal: " + goal);
    }

    /// <summary>
    /// Once-per-campaign-day summary of why a kingdom is behaving as it is.
    /// The war goal drives role assignment, so when armies look too passive this
    /// is the line that says whether the cause is exhaustion, a threatened
    /// capital, or something else.
    /// </summary>
    private static void LogKingdomDiagnostics(Kingdom kingdom)
    {
        if (!StrategicAiTuning.VerboseLogging)
        {
            return;
        }

        int today = (int)CampaignTime.Now.ToDays;
        if (LastDiagnosticDayByKingdom.TryGetValue(kingdom.StringId, out int lastDay) && lastDay == today)
        {
            return;
        }

        LastDiagnosticDayByKingdom[kingdom.StringId] = today;

        StrategicFactionStatus status = StrategicAiState.GetFactionStatus(kingdom);
        if (status.ActiveWars == 0)
        {
            return;
        }

        var roles = new List<string>();
        foreach (Army army in kingdom.Armies)
        {
            if (army?.LeaderParty != null)
            {
                roles.Add(army.LeaderParty.Name + "=" + StrategicAiState.GetRole(army));
            }
        }

        Log(kingdom.Name +
            ": goal=" + status.WarGoal +
            " exhausted=" + status.IsExhausted +
            " wars=" + status.ActiveWars +
            " own=" + (int)status.OwnedStrength +
            " enemy=" + (int)status.EnemyStrength +
            " threatened=" + status.ThreatenedFortifications + "/" + status.OwnedFortifications +
            " raided=" + status.RaidedVillages +
            " armies=[" + string.Join(", ", roles) + "]");
    }

    /// <summary>
    /// Records each army's current objective without commanding it.
    ///
    /// Target locks drive two scoring behaviours: a bonus on the objective an
    /// army already holds, and a penalty on one another army has taken. Both
    /// used to be populated only by the direct-order layer, so with orders
    /// disabled they never fired at all. Reading the objective vanilla has
    /// already chosen restores both without issuing a single order.
    ///
    /// Only runs when the order layer is off; when it is on, that layer records
    /// its own intent and overwriting it here would reset objective commitment
    /// timing on every tick.
    /// </summary>
    private static void ObserveArmyObjectives(Kingdom kingdom)
    {
        if (StrategicAiTuning.EnableStrategicOrders)
        {
            return;
        }

        foreach (Army army in kingdom.Armies)
        {
            MobileParty? leader = army?.LeaderParty;
            if (army == null || leader == null || StrategicAiHelpers.IsPlayerControlled(leader))
            {
                continue;
            }

            // Only ever record a real objective. Writing through a null here
            // deleted the lock -- and with it the 1.4x commitment bonus -- every
            // time vanilla momentarily cleared TargetSettlement, which is exactly
            // the moment before it re-picks and the anchor is most needed.
            Settlement? observed = leader.BesiegedSettlement ?? leader.TargetSettlement;
            if (observed != null)
            {
                StrategicAiState.SetTargetLock(army, observed);
            }
        }
    }

    private static void AssignArmyRoles(Kingdom kingdom)
    {
        List<Army> armies = kingdom.Armies
            .Where(army => army?.LeaderParty != null && !StrategicAiHelpers.IsPlayerControlled(army.LeaderParty))
            .OrderByDescending(army => army.EstimatedStrength)
            .ToList();

        if (armies.Count == 0)
        {
            return;
        }

        StrategicFactionStatus status = StrategicAiState.GetFactionStatus(kingdom);

        // Entering the defensive posture is immediate; leaving it takes a quiet
        // spell. The trigger is a bare "one or more fiefs threatened" count over
        // its own hard threshold, so without the latch a single enemy party
        // drifting across a 45-unit circle re-roled every army in the realm, and
        // drifting back out re-roled them again -- each flip worth
        // RoleAligned / RoleMismatch on every besiege-versus-defend decision.
        if (status.ThreatenedFortifications > 0 ||
            status.RaidedVillages > 1 ||
            status.WarGoal == StrategicWarGoal.DefendCapitalRegion ||
            status.WarGoal == StrategicWarGoal.ForcePeace)
        {
            StrategicAiState.LatchUrgentDefense(kingdom);
        }

        bool urgentDefense = StrategicAiState.IsUrgentDefenseLatched(kingdom);

        for (int i = 0; i < armies.Count; i++)
        {
            // Roles are handed out by strength ranking, and that ranking moves
            // with every casualty and every recruit. Holding an assigned role for
            // RoleMinimumHoldHours stops two armies trading Aggressor and
            // Defender with each other over noise.
            if (!StrategicAiState.CanChangeRole(armies[i]))
            {
                continue;
            }

            StrategicArmyRole role;
            if (status.WarGoal == StrategicWarGoal.ForcePeace || status.WarGoal == StrategicWarGoal.DefendCapitalRegion)
            {
                role = i == 0 ? StrategicArmyRole.Defender : i == 1 ? StrategicArmyRole.Interceptor : StrategicArmyRole.Reserve;
            }
            else if (urgentDefense)
            {
                role = i == 0 ? StrategicArmyRole.Aggressor : i == 1 ? StrategicArmyRole.Defender : i == 2 ? StrategicArmyRole.Interceptor : StrategicArmyRole.Aggressor;
            }
            else
            {
                role = i <= 1 ? StrategicArmyRole.Aggressor : i == 2 ? StrategicArmyRole.Interceptor : StrategicArmyRole.Aggressor;
            }

            StrategicAiState.SetRole(armies[i], role);
        }
    }

    /// <summary>
    /// True when the mod may issue a move order to this army's leader. Anything
    /// already resolved -- a siege in progress, a battle, a party inside a
    /// settlement -- is left to the vanilla AI, as is anything the player owns.
    /// </summary>
    private static bool CanCommandArmy(Army army, MobileParty? leader)
    {
        return StrategicAiTuning.EnableStrategicOrders &&
               leader != null &&
               leader.Army == army &&
               leader.IsActive &&
               !leader.IsDisbanding &&
               leader.MapFaction != null &&
               !StrategicAiHelpers.IsPlayerControlled(leader) &&
               leader.BesiegedSettlement == null &&
               leader.SiegeEvent == null &&
               leader.MapEvent == null &&
               leader.CurrentSettlement == null;
    }

    private static void TryRunAssignedArmyRole(Kingdom kingdom, Army army)
    {
        MobileParty? leader = army.LeaderParty;
        if (!CanCommandArmy(army, leader))
        {
            return;
        }

        if (TryRecoverStuckArmy(army, leader!))
        {
            return;
        }

        // Rate limit: an army gets at most one strategic redirect per
        // ArmyOrderMinIntervalHours, so the planner can never out-run the army.
        if (!StrategicAiState.CanIssueOrder(army))
        {
            return;
        }

        if (TryKeepCommittedObjective(kingdom, army, leader!))
        {
            return;
        }

        if (TryStageForSharedOperation(kingdom, army, leader!))
        {
            return;
        }

        Settlement? consolidationTarget = StrategicAiHelpers.FindRecentFriendlyCapture(kingdom);
        if (consolidationTarget != null &&
            StrategicAiHelpers.Distance(leader!, consolidationTarget) <= StrategicAiTuning.ConsolidationPatrolRadius * 2f)
        {
            StrategicAiState.SetTargetLock(army, consolidationTarget);
            SetMovePatrolAroundSettlementIfNeeded(army, leader!, consolidationTarget);
            return;
        }

        switch (StrategicAiState.GetRole(army))
        {
            case StrategicArmyRole.Defender:
                MoveToDefensiveTarget(kingdom, army, leader!);
                break;
            case StrategicArmyRole.Interceptor:
                MoveToInterceptorTarget(kingdom, army, leader!);
                break;
            case StrategicArmyRole.Reserve:
                // Deliberately hands control back to vanilla rather than parking
                // the army: an idle army with no order is what players saw as
                // "standing in place".
                StrategicAiState.SetTargetLock(army, null);
                StrategicAiState.ResetArmyProgress(army);
                break;
            default:
                MoveToAggressiveTarget(kingdom, army, leader!);
                break;
        }
    }

    private static bool TryRecoverStuckArmy(Army army, MobileParty leader)
    {
        if (!StrategicAiState.UpdateArmyProgressAndIsStuck(army, leader))
        {
            return false;
        }

        Settlement? lockedTarget = StrategicAiState.GetTargetLock(army);
        if (lockedTarget != null)
        {
            StrategicAiState.MarkFailedTarget(leader.MapFaction, lockedTarget);
            Log(leader.Name + " stuck heading to " + lockedTarget.Name + "; dropping objective.");
        }

        StrategicAiState.SetTargetLock(army, null);
        StrategicAiState.ResetArmyProgress(army);
        return true;
    }

    private static void MoveToAggressiveTarget(Kingdom kingdom, Army army, MobileParty leader)
    {
        Settlement? target = StrategicAiHelpers.FindBestAggressiveTarget(kingdom, army);
        if (target == null)
        {
            return;
        }

        StrategicAiState.SetTargetLock(army, target);
        StrategicAiState.SetOperationTarget(kingdom, target);
        SetMoveBesiegeSettlementIfNeeded(army, leader, target);
    }

    private static bool TryKeepCommittedObjective(Kingdom kingdom, Army army, MobileParty leader)
    {
        if (!StrategicAiState.IsCommittedToCurrentObjective(army) ||
            leader.GetNumDaysForFoodToLast() < 3 ||
            StrategicAiState.GetFactionStatus(kingdom).WarGoal == StrategicWarGoal.ForcePeace)
        {
            return false;
        }

        Settlement? lockedTarget = StrategicAiState.GetTargetLock(army);
        if (lockedTarget == null || StrategicAiState.IsTargetOnCooldown(kingdom, lockedTarget))
        {
            return false;
        }

        if (StrategicAiHelpers.IsEnemy(kingdom, lockedTarget.MapFaction))
        {
            if (!StrategicAiHelpers.IsEnemyFrontlineTarget(lockedTarget, kingdom))
            {
                return false;
            }

            SetMoveBesiegeSettlementIfNeeded(army, leader, lockedTarget);
            return true;
        }

        if (StrategicAiHelpers.IsOwnTerritory(kingdom, lockedTarget))
        {
            if (lockedTarget.IsUnderSiege || StrategicAiHelpers.IsLowGarrisonFortification(lockedTarget))
            {
                SetMoveDefendSettlementIfNeeded(army, leader, lockedTarget);
            }
            else
            {
                SetMovePatrolAroundSettlementIfNeeded(army, leader, lockedTarget);
            }

            return true;
        }

        return false;
    }

    private static void MoveToDefensiveTarget(Kingdom kingdom, Army army, MobileParty leader)
    {
        Settlement? raidedVillage = StrategicAiState.GetRaidedVillageSettlement(kingdom);
        if (raidedVillage != null &&
            StrategicAiHelpers.Distance(leader, raidedVillage) <= StrategicAiTuning.VillageDefenseRadius * 2f)
        {
            StrategicAiState.SetTargetLock(army, raidedVillage);
            SetMovePatrolAroundSettlementIfNeeded(army, leader, raidedVillage);
            return;
        }

        Settlement? target = StrategicAiHelpers.FindBestDefensiveSettlement(kingdom);
        if (target != null)
        {
            StrategicAiState.SetTargetLock(army, target);
            SetMoveDefendSettlementIfNeeded(army, leader, target);
            return;
        }

        MobileParty? shadowTarget = StrategicAiHelpers.FindEnemyArmyToShadow(kingdom, leader);
        if (shadowTarget != null)
        {
            StrategicAiState.SetTargetLock(army, shadowTarget.TargetSettlement);
            SetMoveGoAroundPartyIfNeeded(army, leader, shadowTarget);
            return;
        }

        // Nothing to defend and nothing to shadow: fall through to offence rather
        // than leaving the army idle.
        MoveToAggressiveTarget(kingdom, army, leader);
    }

    private static void MoveToInterceptorTarget(Kingdom kingdom, Army army, MobileParty leader)
    {
        if (StrategicAiHelpers.IsMercenaryLed(leader))
        {
            MoveToAggressiveTarget(kingdom, army, leader);
            return;
        }

        MobileParty? target = StrategicAiHelpers.FindBestInterceptorTarget(kingdom, leader);
        if (target != null)
        {
            StrategicAiState.SetTargetLock(army, target.CurrentSettlement);
            SetMoveEngagePartyIfNeeded(army, leader, target);
            return;
        }

        MobileParty? shadowTarget = StrategicAiHelpers.FindEnemyArmyToShadow(kingdom, leader);
        if (shadowTarget != null)
        {
            StrategicAiState.SetTargetLock(army, shadowTarget.TargetSettlement);
            SetMoveGoAroundPartyIfNeeded(army, leader, shadowTarget);
            return;
        }

        MoveToAggressiveTarget(kingdom, army, leader);
    }

    private static bool TryStageForSharedOperation(Kingdom kingdom, Army army, MobileParty leader)
    {
        if (StrategicAiState.GetRole(army) == StrategicArmyRole.Aggressor ||
            army.EstimatedStrength < StrategicAiTuning.SharedOperationMinStrength)
        {
            return false;
        }

        Settlement? operationTarget = StrategicAiState.GetOperationTarget(kingdom);
        if (operationTarget == null ||
            operationTarget.MapFaction == kingdom ||
            StrategicAiState.IsTargetOnCooldown(kingdom, operationTarget))
        {
            return false;
        }

        float distanceToTarget = StrategicAiHelpers.Distance(leader, operationTarget);
        if (distanceToTarget <= StrategicAiTuning.StagingDistance || distanceToTarget > StrategicAiTuning.StagingTargetDistance)
        {
            return false;
        }

        Settlement? staging = StrategicAiHelpers.FindBestStagingSettlement(kingdom, operationTarget);
        if (staging == null)
        {
            return false;
        }

        StrategicAiState.SetTargetLock(army, staging);
        SetMoveGoToSettlementIfNeeded(army, leader, staging);
        return true;
    }

    // ---------------------------------------------------------------- ordering
    // Each helper is a no-op when the order would not change anything, and only
    // stamps the rate limiter when an order is genuinely issued.

    private static void SetMoveBesiegeSettlementIfNeeded(Army army, MobileParty leader, Settlement target)
    {
        if (leader.TargetSettlement == target || leader.BesiegedSettlement == target || StrategicAiState.WasRecentlyOrderedTo(army, target))
        {
            return;
        }

        leader.SetMoveBesiegeSettlement(target, MobileParty.NavigationType.Default);
        StrategicAiState.MarkOrderIssued(army, target);
        Log(leader.Name + " ordered to besiege " + target.Name + ".");
    }

    private static void SetMoveDefendSettlementIfNeeded(Army army, MobileParty leader, Settlement target)
    {
        if (leader.TargetSettlement == target || leader.CurrentSettlement == target || StrategicAiState.WasRecentlyOrderedTo(army, target))
        {
            return;
        }

        leader.SetMoveDefendSettlement(target, false, MobileParty.NavigationType.Default);
        StrategicAiState.MarkOrderIssued(army, target);
        Log(leader.Name + " ordered to defend " + target.Name + ".");
    }

    private static void SetMovePatrolAroundSettlementIfNeeded(Army army, MobileParty leader, Settlement target)
    {
        if (leader.TargetSettlement == target ||
            leader.CurrentSettlement == target ||
            StrategicAiState.WasRecentlyOrderedTo(army, target) ||
            StrategicAiHelpers.Distance(leader, target) <= StrategicAiTuning.ObjectiveArrivalDistance)
        {
            return;
        }

        leader.SetMovePatrolAroundSettlement(target, MobileParty.NavigationType.Default, false);
        StrategicAiState.MarkOrderIssued(army, target);
    }

    private static void SetMoveGoToSettlementIfNeeded(Army army, MobileParty leader, Settlement target)
    {
        if (leader.TargetSettlement == target || leader.CurrentSettlement == target || StrategicAiState.WasRecentlyOrderedTo(army, target))
        {
            return;
        }

        leader.SetMoveGoToSettlement(target, MobileParty.NavigationType.Default, false);
        StrategicAiState.MarkOrderIssued(army, target);
    }

    private static void SetMoveEngagePartyIfNeeded(Army army, MobileParty leader, MobileParty target)
    {
        if (leader.TargetParty == target)
        {
            return;
        }

        leader.SetMoveEngageParty(target, MobileParty.NavigationType.Default);
        StrategicAiState.MarkOrderIssued(army);
    }

    private static void SetMoveGoAroundPartyIfNeeded(Army army, MobileParty leader, MobileParty target)
    {
        if (leader.TargetParty == target)
        {
            return;
        }

        leader.SetMoveGoAroundParty(target, MobileParty.NavigationType.Default);
        StrategicAiState.MarkOrderIssued(army);
    }

    // ------------------------------------------------------------------ events

    private static void OnSettlementOwnerChanged(
        Settlement settlement,
        bool openToClaim,
        Hero newOwner,
        Hero oldOwner,
        Hero capturerHero,
        ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
    {
        if (!StrategicAiHelpers.IsFortification(settlement))
        {
            return;
        }

        // A fief changing hands inside one realm (the ruler granting a fresh conquest, a gift, a barter) is not a
        // capture. Treating it as one logged a bogus CAPTURE and filed the realm's own fief as a lost claim, which
        // overwrote any real loss and could leave the realm trying to "reclaim" a castle it already held.
        if (oldOwner?.MapFaction != null && oldOwner.MapFaction == newOwner?.MapFaction)
        {
            return;
        }

        StrategicAiState.MarkRecentlyCaptured(settlement);
        if (oldOwner?.MapFaction is Kingdom oldKingdom)
        {
            StrategicAiState.MarkLostClaim(oldKingdom, settlement);
        }

        // The decisive measure of whether the AI is actually prosecuting a war:
        // ordering sieges means nothing if no fief ever changes hands.
        Log("CAPTURE: " + settlement.Name +
            " taken by " + (newOwner?.MapFaction?.Name?.ToString() ?? "?") +
            " from " + (oldOwner?.MapFaction?.Name?.ToString() ?? "?") +
            " (" + detail + ")");
    }

    private static void OnVillageBeingRaided(Village village)
    {
        if (village.Bound?.MapFaction is Kingdom kingdom)
        {
            StrategicAiState.SetRaidedVillage(kingdom, village);
        }
    }

    private static void OnArmyCreated(Army army)
    {
        // Intentionally does not disband. Dissolving an army from inside its own
        // creation event fought the kingdom AI, which simply re-formed it on the
        // next tick, so lords churned between forming and disbanding instead of
        // campaigning. Gating now happens up front in
        // StrategicArmyManagementModel.CanLordCreateArmy.
        MobileParty? leader = army.LeaderParty;
        if (leader != null && !StrategicAiHelpers.IsPlayerControlled(leader))
        {
            StrategicAiState.ResetArmyProgress(army);
        }
    }

    private static void OnArmyDispersed(Army army, Army.ArmyDispersionReason reason, bool isPlayersArmy)
    {
        if (!isPlayersArmy)
        {
            StrategicAiState.SetArmyCreationCooldown(army.ArmyOwner, StrategicAiTuning.DispersedLeaderArmyCooldownDays);
        }

        StrategicAiState.ForgetArmy(army);
    }

    private static void OnMobilePartyCreated(MobileParty party)
    {
        if (party.IsLordParty &&
            !StrategicAiHelpers.IsPlayerControlled(party) &&
            party.LeaderHero != null &&
            !StrategicAiHelpers.IsRecoveredEnoughToJoinArmy(party))
        {
            StrategicAiState.MarkWeakPartyCreated(party);
        }
    }

    private static void OnMobilePartyDestroyed(MobileParty mobileParty, PartyBase destroyerParty)
    {
        if (mobileParty.IsLordParty &&
            !StrategicAiHelpers.IsPlayerControlled(mobileParty) &&
            mobileParty.LeaderHero != null)
        {
            StrategicAiState.SetArmyCreationCooldown(mobileParty.LeaderHero, StrategicAiTuning.DefeatedLeaderArmyCooldownDays);
        }

        // Destroyed while besieging: the siege failed as decisively as it can,
        // so the target goes on cooldown for that faction rather than the wreck
        // of its army being sent straight back.
        Settlement? besieged = StrategicAiState.GetWatchedSiegeTarget(mobileParty);
        if (besieged != null && StrategicAiHelpers.IsEnemy(mobileParty.MapFaction, besieged.MapFaction))
        {
            StrategicAiState.MarkFailedTarget(mobileParty.MapFaction, besieged);
        }

        StrategicAiState.ForgetSiegeWatch(mobileParty);
    }
}
