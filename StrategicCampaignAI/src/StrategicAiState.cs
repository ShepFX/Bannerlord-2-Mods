using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace StrategicCampaignAI;

internal static class StrategicAiState
{
    private static readonly Dictionary<string, int> EnemyTerritoryDaysByArmy = new();
    private static readonly Dictionary<string, int> RolesByArmy = new();
    private static readonly Dictionary<string, string> TargetLocksByArmy = new();
    private static readonly Dictionary<string, double> TargetLockDaysByArmy = new();
    private static readonly Dictionary<string, double> LastOrderDaysByArmy = new();
    private static readonly Dictionary<string, string> LastOrderTargetByArmy = new();
    private static readonly Dictionary<string, ArmyProgress> ArmyProgressByArmy = new();
    private static readonly Dictionary<string, double> RecentCaptureDaysBySettlement = new();
    private static readonly Dictionary<string, StrategicFactionStatus> FactionStatuses = new();
    private static readonly Dictionary<string, string> RaidedVillageByFaction = new();
    private static readonly Dictionary<string, double> FailedTargetCooldownDays = new();
    private static readonly Dictionary<string, string> LostSettlementClaimByFaction = new();
    private static readonly Dictionary<string, int> WarGoalsByFaction = new();
    private static readonly Dictionary<string, double> WarGoalUpdatedDaysByFaction = new();
    private static readonly Dictionary<string, string> OperationTargetByFaction = new();
    private static readonly Dictionary<string, double> ArmyCreationCooldownUntilDaysByHero = new();
    private static readonly Dictionary<string, double> WeakPartyCreatedDaysByHero = new();
    private static readonly Dictionary<string, double> GarrisonReinforcedDaysBySettlement = new();
    private static readonly Dictionary<string, double> RoleAssignedDaysByArmy = new();
    private static readonly Dictionary<string, string> SiegeTargetByParty = new();
    private static readonly Dictionary<string, double> SiegeHopelessSinceDaysByParty = new();
    private static readonly Dictionary<string, double> UrgentDefenseUntilDaysByFaction = new();

    private static readonly Dictionary<string, Settlement> SettlementsById = new();

    /// <summary>
    /// Persists the parts of the strategic memory that a campaign cannot cheaply
    /// rebuild: siege failures, lord recovery cooldowns, army objectives, war
    /// goals and lost-fief claims.
    ///
    /// Only dictionaries of primitives cross the save boundary, and every
    /// container type is registered in <see cref="StrategicAiSaveDefiner"/>.
    /// Saving a container the definer does not declare is what made the game
    /// fail to create a save in early versions.
    ///
    /// Anything recomputed within an hour (faction status, army roles, movement
    /// progress) is deliberately left out.
    /// </summary>
    public static void SyncData(IDataStore dataStore)
    {
        if (!StrategicAiTuning.EnablePersistentMemory)
        {
            return;
        }

        try
        {
            SyncStringMap(dataStore, "SCAI_TargetLocks", TargetLocksByArmy);
            SyncStringMap(dataStore, "SCAI_LostClaims", LostSettlementClaimByFaction);
            SyncStringMap(dataStore, "SCAI_OperationTargets", OperationTargetByFaction);
            SyncStringMap(dataStore, "SCAI_RaidedVillages", RaidedVillageByFaction);

            SyncIntMap(dataStore, "SCAI_WarGoals", WarGoalsByFaction);
            SyncIntMap(dataStore, "SCAI_EnemyTerritoryDays", EnemyTerritoryDaysByArmy);

            SyncDoubleMap(dataStore, "SCAI_TargetLockDays", TargetLockDaysByArmy);
            SyncDoubleMap(dataStore, "SCAI_FailedTargets", FailedTargetCooldownDays);
            SyncDoubleMap(dataStore, "SCAI_WarGoalUpdated", WarGoalUpdatedDaysByFaction);
            SyncDoubleMap(dataStore, "SCAI_ArmyCreationCooldown", ArmyCreationCooldownUntilDaysByHero);
            SyncDoubleMap(dataStore, "SCAI_RecentCaptures", RecentCaptureDaysBySettlement);
            SyncDoubleMap(dataStore, "SCAI_GarrisonReinforced", GarrisonReinforcedDaysBySettlement);
        }
        catch (Exception exception)
        {
            // A save must never fail because of the strategic layer. Losing this
            // memory only costs the AI some continuity.
            StrategicAiLog.WriteError("SyncData failed: " + exception.Message);
        }
    }

    private static void SyncStringMap(IDataStore dataStore, string key, Dictionary<string, string> target)
    {
        Dictionary<string, string>? buffer = dataStore.IsSaving ? new Dictionary<string, string>(target) : null;
        dataStore.SyncData(key, ref buffer);
        ReplaceOnLoad(dataStore, target, buffer);
    }

    private static void SyncIntMap(IDataStore dataStore, string key, Dictionary<string, int> target)
    {
        Dictionary<string, int>? buffer = dataStore.IsSaving ? new Dictionary<string, int>(target) : null;
        dataStore.SyncData(key, ref buffer);
        ReplaceOnLoad(dataStore, target, buffer);
    }

    private static void SyncDoubleMap(IDataStore dataStore, string key, Dictionary<string, double> target)
    {
        Dictionary<string, double>? buffer = dataStore.IsSaving ? new Dictionary<string, double>(target) : null;
        dataStore.SyncData(key, ref buffer);
        ReplaceOnLoad(dataStore, target, buffer);
    }

    /// <summary>
    /// On load, the live dictionary is always cleared first. A save written
    /// before this feature existed yields null, and the clear is what stops the
    /// previously played campaign's memory leaking into this one.
    /// </summary>
    private static void ReplaceOnLoad<TValue>(IDataStore dataStore, Dictionary<string, TValue> target, Dictionary<string, TValue>? loaded)
    {
        if (!dataStore.IsLoading)
        {
            return;
        }

        target.Clear();
        if (loaded == null)
        {
            return;
        }

        foreach (KeyValuePair<string, TValue> pair in loaded)
        {
            if (!string.IsNullOrEmpty(pair.Key))
            {
                target[pair.Key] = pair.Value;
            }
        }
    }

    /// <summary>
    /// Clears the derived state that must not survive a campaign switch but is
    /// cheap to rebuild. Safe to call after a load, unlike <see cref="Reset"/>,
    /// because it leaves the dictionaries that SyncData has just populated
    /// alone -- behaviour SyncData runs during deserialisation, before
    /// OnGameLoadedEvent fires.
    /// </summary>
    public static void ResetRuntimeOnly()
    {
        RolesByArmy.Clear();
        RoleAssignedDaysByArmy.Clear();
        LastOrderDaysByArmy.Clear();
        LastOrderTargetByArmy.Clear();
        ArmyProgressByArmy.Clear();
        FactionStatuses.Clear();
        WeakPartyCreatedDaysByHero.Clear();
        SiegeTargetByParty.Clear();
        SiegeHopelessSinceDaysByParty.Clear();
        UrgentDefenseUntilDaysByFaction.Clear();
        SettlementsById.Clear();
        StrategicAiCache.Reset();
    }

    /// <summary>
    /// Clears every cached decision, persisted memory included. Only valid for a
    /// brand new campaign -- calling this after a load would discard the state
    /// SyncData just restored.
    /// </summary>
    public static void Reset()
    {
        EnemyTerritoryDaysByArmy.Clear();
        RolesByArmy.Clear();
        RoleAssignedDaysByArmy.Clear();
        TargetLocksByArmy.Clear();
        TargetLockDaysByArmy.Clear();
        LastOrderDaysByArmy.Clear();
        LastOrderTargetByArmy.Clear();
        ArmyProgressByArmy.Clear();
        RecentCaptureDaysBySettlement.Clear();
        FactionStatuses.Clear();
        RaidedVillageByFaction.Clear();
        FailedTargetCooldownDays.Clear();
        LostSettlementClaimByFaction.Clear();
        WarGoalsByFaction.Clear();
        WarGoalUpdatedDaysByFaction.Clear();
        OperationTargetByFaction.Clear();
        ArmyCreationCooldownUntilDaysByHero.Clear();
        WeakPartyCreatedDaysByHero.Clear();
        GarrisonReinforcedDaysBySettlement.Clear();
        SiegeTargetByParty.Clear();
        SiegeHopelessSinceDaysByParty.Clear();
        UrgentDefenseUntilDaysByFaction.Clear();
        SettlementsById.Clear();
        StrategicAiCache.Reset();
    }

    /// <summary>
    /// Drops expired entries so the persisted dictionaries cannot grow without
    /// bound over a long campaign. Cooldowns for heroes who died are otherwise
    /// never queried again, so they would sit in every save forever.
    /// </summary>
    public static void PruneExpired()
    {
        double now = CampaignTime.Now.ToDays;

        PruneOlderThan(FailedTargetCooldownDays, now, StrategicAiTuning.TargetFailureCooldownHours / 24f);
        PruneOlderThan(RecentCaptureDaysBySettlement, now, StrategicAiTuning.RecentCaptureConsolidationHours / 24f);
        PruneOlderThan(GarrisonReinforcedDaysBySettlement, now, StrategicAiTuning.GarrisonReinforcementCooldownDays);
        PruneOlderThan(TargetLockDaysByArmy, now, StrategicAiTuning.ObjectiveCommitmentHours / 24f);

        // Army creation cooldowns store an absolute expiry rather than a stamp.
        var expiredHeroes = new List<string>();
        foreach (KeyValuePair<string, double> entry in ArmyCreationCooldownUntilDaysByHero)
        {
            if (now > entry.Value)
            {
                expiredHeroes.Add(entry.Key);
            }
        }

        foreach (string hero in expiredHeroes)
        {
            ArmyCreationCooldownUntilDaysByHero.Remove(hero);
        }
    }

    /// <summary>
    /// Drops per-army bookkeeping for armies that no longer exist.
    ///
    /// Time is the wrong axis for target locks. SetTargetLock only stamps
    /// TargetLockDaysByArmy when the objective *changes*, so an army holding one
    /// objective steadily ages out of that map while its lock is still current;
    /// pruning locks on that stamp would delete live objectives. Meanwhile
    /// TargetLocksByArmy was never pruned at all, and it is persisted, so an army
    /// that changed leader orphaned its entry (the key is the leader party's id)
    /// and IsTargetLockedByAnotherArmy went on applying DuplicateTargetPenalty
    /// for a dead army, to a set of settlements that only ever grew.
    /// </summary>
    public static void PruneDeadArmies(HashSet<string> liveArmyKeys)
    {
        PruneKeysNotIn(TargetLocksByArmy, liveArmyKeys);
        PruneKeysNotIn(TargetLockDaysByArmy, liveArmyKeys);
        PruneKeysNotIn(RolesByArmy, liveArmyKeys);
        PruneKeysNotIn(RoleAssignedDaysByArmy, liveArmyKeys);
        PruneKeysNotIn(EnemyTerritoryDaysByArmy, liveArmyKeys);
        PruneKeysNotIn(LastOrderDaysByArmy, liveArmyKeys);
        PruneKeysNotIn(LastOrderTargetByArmy, liveArmyKeys);
        PruneKeysNotIn(ArmyProgressByArmy, liveArmyKeys);
    }

    private static void PruneKeysNotIn<TValue>(Dictionary<string, TValue> map, HashSet<string> liveKeys)
    {
        List<string>? dead = null;
        foreach (KeyValuePair<string, TValue> entry in map)
        {
            if (!liveKeys.Contains(entry.Key))
            {
                (dead ??= new List<string>()).Add(entry.Key);
            }
        }

        if (dead == null)
        {
            return;
        }

        foreach (string key in dead)
        {
            map.Remove(key);
        }
    }

    /// <summary>The key this army's state is filed under. Public so the pruner can build the live set.</summary>
    public static string GetLiveArmyKey(Army army)
    {
        return GetArmyKey(army);
    }

    private static void PruneOlderThan(Dictionary<string, double> map, double now, double maxAgeDays)
    {
        var expired = new List<string>();
        foreach (KeyValuePair<string, double> entry in map)
        {
            if (now - entry.Value > maxAgeDays)
            {
                expired.Add(entry.Key);
            }
        }

        foreach (string key in expired)
        {
            map.Remove(key);
        }
    }

    /// <summary>Drops per-army bookkeeping when an army stops existing.</summary>
    public static void ForgetArmy(Army army)
    {
        string key = GetArmyKey(army);
        EnemyTerritoryDaysByArmy.Remove(key);
        RolesByArmy.Remove(key);
        RoleAssignedDaysByArmy.Remove(key);
        TargetLocksByArmy.Remove(key);
        TargetLockDaysByArmy.Remove(key);
        LastOrderDaysByArmy.Remove(key);
        LastOrderTargetByArmy.Remove(key);
        ArmyProgressByArmy.Remove(key);
    }

    public static int GetEnemyTerritoryDays(Army army)
    {
        return EnemyTerritoryDaysByArmy.TryGetValue(GetArmyKey(army), out int days) ? days : 0;
    }

    public static void SetEnemyTerritoryDays(Army army, int days)
    {
        string key = GetArmyKey(army);
        if (days <= 0)
        {
            EnemyTerritoryDaysByArmy.Remove(key);
            return;
        }

        EnemyTerritoryDaysByArmy[key] = days;
    }

    public static StrategicArmyRole GetRole(Army army)
    {
        return RolesByArmy.TryGetValue(GetArmyKey(army), out int role) &&
               role >= (int)StrategicArmyRole.Aggressor &&
               role <= (int)StrategicArmyRole.Reserve
            ? (StrategicArmyRole)role
            : StrategicArmyRole.Aggressor;
    }

    public static void SetRole(Army army, StrategicArmyRole role)
    {
        string key = GetArmyKey(army);
        if (!RolesByArmy.TryGetValue(key, out int existing) || existing != (int)role)
        {
            RoleAssignedDaysByArmy[key] = CampaignTime.Now.ToDays;
        }

        RolesByArmy[key] = (int)role;
    }

    /// <summary>
    /// True when this army has held its current role long enough to be given a
    /// different one.
    ///
    /// Roles are handed out by strength ranking on every strategic tick, so
    /// without a hold a few casualties reorder the list and two armies trade
    /// Aggressor and Defender with each other -- each trade swinging the
    /// besiege-versus-defend preference by RoleAligned / RoleMismatch. An army
    /// that has never been given a role is always assignable.
    /// </summary>
    public static bool CanChangeRole(Army army)
    {
        string key = GetArmyKey(army);
        if (!RolesByArmy.ContainsKey(key))
        {
            return true;
        }

        return !RoleAssignedDaysByArmy.TryGetValue(key, out double assignedDays) ||
               (CampaignTime.Now.ToDays - assignedDays) * 24d >= StrategicAiTuning.RoleMinimumHoldHours;
    }

    /// <summary>
    /// Puts the kingdom on a defensive footing and keeps it there for
    /// UrgentDefenseLatchHours.
    ///
    /// The threat count this latches on is a hard threshold over a 45-unit
    /// sweep, so one enemy party wandering across that circle used to re-role
    /// every army in the realm and wandering back out re-roled them again.
    /// Entering the defensive posture is immediate; leaving it takes a quiet
    /// spell.
    /// </summary>
    public static void LatchUrgentDefense(Kingdom kingdom)
    {
        UrgentDefenseUntilDaysByFaction[kingdom.StringId] =
            CampaignTime.Now.ToDays + (StrategicAiTuning.UrgentDefenseLatchHours / 24d);
    }

    public static bool IsUrgentDefenseLatched(Kingdom kingdom)
    {
        return UrgentDefenseUntilDaysByFaction.TryGetValue(kingdom.StringId, out double untilDays) &&
               CampaignTime.Now.ToDays < untilDays;
    }

    public static void SetTargetLock(Army army, Settlement? settlement)
    {
        string key = GetArmyKey(army);
        if (settlement == null)
        {
            TargetLocksByArmy.Remove(key);
            TargetLockDaysByArmy.Remove(key);
            return;
        }

        if (!TargetLocksByArmy.TryGetValue(key, out string existingTarget) || existingTarget != settlement.StringId)
        {
            TargetLockDaysByArmy[key] = CampaignTime.Now.ToDays;
        }

        TargetLocksByArmy[key] = settlement.StringId;
    }

    public static Settlement? GetTargetLock(Army army)
    {
        return TargetLocksByArmy.TryGetValue(GetArmyKey(army), out string settlementId)
            ? FindSettlement(settlementId)
            : null;
    }

    public static bool IsCommittedToCurrentObjective(Army army)
    {
        return TargetLockDaysByArmy.TryGetValue(GetArmyKey(army), out double lockDays) &&
               (CampaignTime.Now.ToDays - lockDays) * 24d < StrategicAiTuning.ObjectiveCommitmentHours;
    }

    public static bool IsTargetLockedByAnotherArmy(Army army, Settlement settlement)
    {
        string armyKey = GetArmyKey(army);
        foreach (KeyValuePair<string, string> targetLock in TargetLocksByArmy)
        {
            if (targetLock.Key != armyKey && targetLock.Value == settlement.StringId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Rate limit on strategic move orders. Without this the four-hourly planner
    /// can re-order an army faster than it can act on the previous order, which
    /// reads in game as an army jittering back and forth.
    /// </summary>
    public static bool CanIssueOrder(Army army)
    {
        return !LastOrderDaysByArmy.TryGetValue(GetArmyKey(army), out double lastOrderDays) ||
               (CampaignTime.Now.ToDays - lastOrderDays) * 24d >= StrategicAiTuning.ArmyOrderMinIntervalHours;
    }

    public static void MarkOrderIssued(Army army, Settlement? target = null)
    {
        string key = GetArmyKey(army);
        LastOrderDaysByArmy[key] = CampaignTime.Now.ToDays;

        if (target == null)
        {
            LastOrderTargetByArmy.Remove(key);
        }
        else
        {
            LastOrderTargetByArmy[key] = target.StringId;
        }
    }

    /// <summary>
    /// True when we already sent this army to this destination recently.
    ///
    /// Checking only MobileParty.TargetSettlement is not enough: the vanilla AI
    /// re-plans on its own schedule and clears the target, so an unconditional
    /// re-issue turns into a tug of war that re-orders the same army to the same
    /// place every ArmyOrderMinIntervalHours indefinitely, and it never arrives.
    /// If our order did not stick the first time, repeating it will not help.
    /// </summary>
    public static bool WasRecentlyOrderedTo(Army army, Settlement target)
    {
        string key = GetArmyKey(army);
        return LastOrderTargetByArmy.TryGetValue(key, out string lastTarget) &&
               lastTarget == target.StringId &&
               LastOrderDaysByArmy.TryGetValue(key, out double lastDays) &&
               (CampaignTime.Now.ToDays - lastDays) * 24d < StrategicAiTuning.RepeatOrderSuppressionHours;
    }

    public static void MarkRecentlyCaptured(Settlement settlement)
    {
        RecentCaptureDaysBySettlement[settlement.StringId] = CampaignTime.Now.ToDays;
    }

    public static bool WasRecentlyCaptured(Settlement settlement)
    {
        return RecentCaptureDaysBySettlement.TryGetValue(settlement.StringId, out double captureDays) &&
               (CampaignTime.Now.ToDays - captureDays) * 24d <= StrategicAiTuning.RecentCaptureConsolidationHours;
    }

    public static void SetFactionStatus(Kingdom kingdom, StrategicFactionStatus status)
    {
        status.WarGoal = GetWarGoal(kingdom);
        status.WantsPeace = status.IsExhausted && status.ActiveWars > 0;
        FactionStatuses[kingdom.StringId] = status;
    }

    public static StrategicFactionStatus GetFactionStatus(IFaction? faction)
    {
        if (faction is Kingdom kingdom && FactionStatuses.TryGetValue(kingdom.StringId, out StrategicFactionStatus status))
        {
            return status;
        }

        return new StrategicFactionStatus();
    }

    public static void SetRaidedVillage(Kingdom kingdom, Village village)
    {
        RaidedVillageByFaction[kingdom.StringId] = village.Settlement.StringId;
    }

    public static Settlement? GetRaidedVillageSettlement(Kingdom kingdom)
    {
        return RaidedVillageByFaction.TryGetValue(kingdom.StringId, out string settlementId)
            ? FindSettlement(settlementId)
            : null;
    }

    /// <summary>
    /// Remembers that this faction's attempt on this settlement came to nothing.
    ///
    /// Scoped to the faction that failed. The cooldown used to be keyed by
    /// settlement alone, which was harmless only because nothing ever reached
    /// this method on default settings; now that an abandoned siege records one,
    /// a global key would let one minor faction's failed siege suppress that
    /// castle as a target for every kingdom on the map for four days.
    /// </summary>
    public static void MarkFailedTarget(IFaction? faction, Settlement settlement)
    {
        string? key = GetFailedTargetKey(faction, settlement);
        if (key != null)
        {
            FailedTargetCooldownDays[key] = CampaignTime.Now.ToDays;
        }
    }

    public static bool IsTargetOnCooldown(IFaction? faction, Settlement settlement)
    {
        string? key = GetFailedTargetKey(faction, settlement);
        return key != null &&
               FailedTargetCooldownDays.TryGetValue(key, out double failedDays) &&
               (CampaignTime.Now.ToDays - failedDays) * 24d <= StrategicAiTuning.TargetFailureCooldownHours;
    }

    private static string? GetFailedTargetKey(IFaction? faction, Settlement? settlement)
    {
        if (faction == null || settlement == null ||
            string.IsNullOrEmpty(faction.StringId) || string.IsNullOrEmpty(settlement.StringId))
        {
            return null;
        }

        return faction.StringId + "|" + settlement.StringId;
    }

    // ------------------------------------------------------------- siege watch
    //
    // The hopeless-siege verdict is evaluated once an hour by the campaign
    // behaviour and only read from the scoring path. It used to be recomputed
    // inside every score query, where it could only ever apply to the settlement
    // a party was currently besieging -- so lifting the siege deleted the
    // penalty, the abandoned castle immediately scored full value again, and two
    // castles sharing one relief force traded the army back and forth forever.

    /// <summary>The settlement this party was besieging as of the last hourly check.</summary>
    public static Settlement? GetWatchedSiegeTarget(MobileParty party)
    {
        string? key = GetPartyKey(party);
        return key != null && SiegeTargetByParty.TryGetValue(key, out string settlementId)
            ? FindSettlement(settlementId)
            : null;
    }

    public static void SetWatchedSiegeTarget(MobileParty party, Settlement? settlement)
    {
        string? key = GetPartyKey(party);
        if (key == null)
        {
            return;
        }

        if (settlement == null)
        {
            SiegeTargetByParty.Remove(key);
            SiegeHopelessSinceDaysByParty.Remove(key);
            return;
        }

        if (!SiegeTargetByParty.TryGetValue(key, out string existing) || existing != settlement.StringId)
        {
            // A different siege is a fresh verdict.
            SiegeHopelessSinceDaysByParty.Remove(key);
        }

        SiegeTargetByParty[key] = settlement.StringId;
    }

    /// <summary>Records this hour's verdict, starting the confirmation clock on the first hopeless one.</summary>
    public static void SetSiegeHopeless(MobileParty party, bool hopeless)
    {
        string? key = GetPartyKey(party);
        if (key == null)
        {
            return;
        }

        if (!hopeless)
        {
            SiegeHopelessSinceDaysByParty.Remove(key);
            return;
        }

        if (!SiegeHopelessSinceDaysByParty.ContainsKey(key))
        {
            SiegeHopelessSinceDaysByParty[key] = CampaignTime.Now.ToDays;
        }
    }

    /// <summary>
    /// True when this party is besieging this settlement and the hopeless verdict
    /// has held for SiegeAbandonConfirmationHours. Read-only, so it is safe on
    /// the scoring hot path.
    /// </summary>
    public static bool IsSiegeAbandonConfirmed(MobileParty party, Settlement settlement)
    {
        string? key = GetPartyKey(party);
        return key != null &&
               SiegeTargetByParty.TryGetValue(key, out string settlementId) &&
               settlementId == settlement.StringId &&
               SiegeHopelessSinceDaysByParty.TryGetValue(key, out double sinceDays) &&
               (CampaignTime.Now.ToDays - sinceDays) * 24d >= StrategicAiTuning.SiegeAbandonConfirmationHours;
    }

    /// <summary>
    /// Drops watch entries for parties that are no longer besieging anything,
    /// including parties that stopped existing between two checks.
    /// </summary>
    public static void PruneSiegeWatch(HashSet<string> besiegingPartyKeys)
    {
        PruneKeysNotIn(SiegeTargetByParty, besiegingPartyKeys);
        PruneKeysNotIn(SiegeHopelessSinceDaysByParty, besiegingPartyKeys);
    }

    public static void ForgetSiegeWatch(MobileParty party)
    {
        string? key = GetPartyKey(party);
        if (key != null)
        {
            SiegeTargetByParty.Remove(key);
            SiegeHopelessSinceDaysByParty.Remove(key);
        }
    }

    public static void MarkLostClaim(Kingdom kingdom, Settlement settlement)
    {
        LostSettlementClaimByFaction[kingdom.StringId] = settlement.StringId;
    }

    /// <summary>
    /// The fief this realm most recently lost, while it is still in other hands. A claim on a fief the realm holds
    /// again (retaken, or filed by an older version from an internal grant) is dropped.
    /// </summary>
    public static Settlement? GetLostClaim(Kingdom kingdom)
    {
        if (!LostSettlementClaimByFaction.TryGetValue(kingdom.StringId, out string settlementId))
        {
            return null;
        }

        Settlement? settlement = FindSettlement(settlementId);
        if (settlement == null || settlement.MapFaction == kingdom)
        {
            LostSettlementClaimByFaction.Remove(kingdom.StringId);
            return null;
        }

        return settlement;
    }

    public static StrategicWarGoal GetWarGoal(Kingdom kingdom)
    {
        return WarGoalsByFaction.TryGetValue(kingdom.StringId, out int goal) &&
               goal >= (int)StrategicWarGoal.BorderWar &&
               goal <= (int)StrategicWarGoal.ForcePeace
            ? (StrategicWarGoal)goal
            : StrategicWarGoal.BorderWar;
    }

    public static bool ShouldReevaluateWarGoal(Kingdom kingdom)
    {
        return !WarGoalUpdatedDaysByFaction.TryGetValue(kingdom.StringId, out double updatedDays) ||
               CampaignTime.Now.ToDays - updatedDays >= StrategicAiTuning.WarGoalReevaluationDays;
    }

    public static void SetWarGoal(Kingdom kingdom, StrategicWarGoal goal)
    {
        WarGoalsByFaction[kingdom.StringId] = (int)goal;
        WarGoalUpdatedDaysByFaction[kingdom.StringId] = CampaignTime.Now.ToDays;
    }

    public static void SetOperationTarget(Kingdom kingdom, Settlement? settlement)
    {
        if (settlement == null)
        {
            OperationTargetByFaction.Remove(kingdom.StringId);
            return;
        }

        OperationTargetByFaction[kingdom.StringId] = settlement.StringId;
    }

    public static Settlement? GetOperationTarget(Kingdom kingdom)
    {
        return OperationTargetByFaction.TryGetValue(kingdom.StringId, out string settlementId)
            ? FindSettlement(settlementId)
            : null;
    }

    public static void SetArmyCreationCooldown(Hero? hero, double cooldownDays)
    {
        string? key = GetHeroKey(hero);
        if (key != null)
        {
            ArmyCreationCooldownUntilDaysByHero[key] = CampaignTime.Now.ToDays + cooldownDays;
        }
    }

    public static bool IsArmyCreationOnCooldown(Hero? hero)
    {
        string? key = GetHeroKey(hero);
        if (key == null || !ArmyCreationCooldownUntilDaysByHero.TryGetValue(key, out double cooldownUntilDays))
        {
            return false;
        }

        if (CampaignTime.Now.ToDays <= cooldownUntilDays)
        {
            return true;
        }

        ArmyCreationCooldownUntilDaysByHero.Remove(key);
        return false;
    }

    public static void MarkWeakPartyCreated(MobileParty party)
    {
        string? key = GetHeroKey(party.LeaderHero);
        if (key != null)
        {
            WeakPartyCreatedDaysByHero[key] = CampaignTime.Now.ToDays;
        }
    }

    public static bool IsRecentlyRespawnedWeakParty(MobileParty party)
    {
        string? key = GetHeroKey(party.LeaderHero);
        return key != null &&
               WeakPartyCreatedDaysByHero.TryGetValue(key, out double createdDays) &&
               CampaignTime.Now.ToDays - createdDays <= StrategicAiTuning.NewWeakPartyGraceDays;
    }

    public static bool UpdateArmyProgressAndIsStuck(Army army, MobileParty leader)
    {
        string key = GetArmyKey(army);
        Vec2 position = leader.GetPosition2D;

        // An army sitting in a siege camp is working, not stuck.
        if (leader.BesiegedSettlement != null || leader.MapEvent != null || leader.SiegeEvent != null)
        {
            ArmyProgressByArmy.Remove(key);
            return false;
        }

        if (!ArmyProgressByArmy.TryGetValue(key, out ArmyProgress progress))
        {
            ArmyProgressByArmy[key] = new ArmyProgress(position.X, position.Y, 0);
            return false;
        }

        float xDelta = position.X - progress.X;
        float yDelta = position.Y - progress.Y;
        float moved = MathF.Sqrt(xDelta * xDelta + yDelta * yDelta);
        int stuckTicks = moved <= StrategicAiTuning.StuckArmyMovementThreshold ? progress.StuckTicks + 1 : 0;
        ArmyProgressByArmy[key] = new ArmyProgress(position.X, position.Y, stuckTicks);
        return stuckTicks >= StrategicAiTuning.StuckArmyCheckTicks;
    }

    public static void ResetArmyProgress(Army army)
    {
        ArmyProgressByArmy.Remove(GetArmyKey(army));
    }

    public static bool CanReinforceGarrison(Settlement settlement)
    {
        return !GarrisonReinforcedDaysBySettlement.TryGetValue(settlement.StringId, out double reinforcedDays) ||
               CampaignTime.Now.ToDays - reinforcedDays >= StrategicAiTuning.GarrisonReinforcementCooldownDays;
    }

    public static void MarkGarrisonReinforced(Settlement settlement)
    {
        GarrisonReinforcedDaysBySettlement[settlement.StringId] = CampaignTime.Now.ToDays;
    }

    /// <summary>
    /// O(1) settlement lookup. This used to be a linear scan of Settlement.All on
    /// every call, and it is reached from the target scoring hot path.
    /// </summary>
    private static Settlement? FindSettlement(string settlementId)
    {
        if (SettlementsById.Count == 0)
        {
            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement != null)
                {
                    SettlementsById[settlement.StringId] = settlement;
                }
            }
        }

        return SettlementsById.TryGetValue(settlementId, out Settlement result) ? result : null;
    }

    private sealed class ArmyProgress
    {
        public ArmyProgress(float x, float y, int stuckTicks)
        {
            X = x;
            Y = y;
            StuckTicks = stuckTicks;
        }

        public float X { get; }
        public float Y { get; }
        public int StuckTicks { get; }
    }

    private static string GetArmyKey(Army army)
    {
        return army.LeaderParty?.Party?.Id ?? army.GetHashCode().ToString();
    }

    private static string? GetPartyKey(MobileParty? party)
    {
        string? id = party?.Party?.Id;
        return string.IsNullOrEmpty(id) ? null : id;
    }

    private static string? GetHeroKey(Hero? hero)
    {
        return hero == null || string.IsNullOrEmpty(hero.StringId) ? null : hero.StringId;
    }
}
