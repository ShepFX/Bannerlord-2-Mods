using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace FieldFortifications;

/// <summary>
/// Builds the paid-for works once the player finishes deploying: a spiked barricade line, a ballista, a mangonel.
/// During deployment the works are ghosts the player can position (see <see cref="PlacementController"/>).
/// Afterwards it hurts horses that charge into the spikes and keeps a crewless mangonel loaded.
/// </summary>
public sealed class FortificationMissionBehavior : MissionBehavior
{
    public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

    private readonly struct Barricade
    {
        public readonly Vec2 Centre, Forward, Lateral;
        public Barricade(Vec2 centre, Vec2 forward) { Centre = centre; Forward = forward; Lateral = forward.LeftVec(); }
    }

    private bool _initialised, _spawned;
    private FortificationSettings _settings = new();
    private PlacementController? _placement;
    private readonly List<Barricade> _barricades = new();
    private readonly Dictionary<int, float> _spikeCooldown = new();
    private readonly List<RangedSiegeWeapon> _engines = new();
    private readonly List<CrewRequest> _crewPending = new();
    private float _reportAt = -1f;

    /// <summary>An engine waiting for its owner to have men to spare.</summary>
    private sealed class CrewRequest
    {
        public RangedSiegeWeapon Weapon = null!;
        public Team Team = null!;
        public Formation? Joined;
        public float NextTry;
        public float GiveUpAt;
    }
    private readonly List<(Agent horse, float damage, Vec2 velocity)> _spikeHits = new();
    private int _tickFaults;
    private readonly Dictionary<RangedSiegeWeapon, float> _reloadStuckSince = new();

    // Protected on MissionObject / RangedSiegeWeapon; the engine sets them from prefab XML, we set them from code.
    private static readonly FieldInfo? NavMeshPrefabField = typeof(MissionObject).GetField("NavMeshPrefabName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly MethodInfo? AttachNavMeshMethod = typeof(MissionObject).GetMethod("AttachDynamicNavmeshToEntity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo? DefaultSideField = typeof(RangedSiegeWeapon).GetField("DefaultSide", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo? StartingAmmoField = typeof(RangedSiegeWeapon).GetField("StartingAmmoCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly MethodInfo? SetStartAmmoMethod = typeof(RangedSiegeWeapon).GetMethod("SetStartAmmo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo? MissileItemIdField = typeof(RangedSiegeWeapon).GetField("MissileItemID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo? LoadedMissileField = typeof(RangedSiegeWeapon).GetField("_loadedMissileItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly PropertyInfo? StateProperty = typeof(RangedSiegeWeapon).GetProperty("State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly MethodInfo? UpdateAmmoMeshMethod = typeof(RangedSiegeWeapon).GetMethod("UpdateAmmoMesh", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    public override void OnMissionTick(float dt)
    {
        base.OnMissionTick(dt);
        if (_spawned)
        {
            try
            {
                if (_barricades.Count > 0) TickSpikes();
                if (_engines.Count > 0) TickAutoReload();
                if (_crewPending.Count > 0) TickCrew();
                if (_reportAt > 0f && Mission.CurrentTime >= _reportAt) ReportEngines();
            }
            catch (Exception ex)
            {
                if (_tickFaults++ < 3) ErrorLog.Write("Battle tick failed: " + ex);
            }
            return;
        }
        if (!_initialised)
        {
            Initialise();
            return;
        }
        if (Mission.IsDeploymentFinished || Mission.Mode != MissionMode.Deployment)
        {
            SpawnAll();
            return;
        }
        try { _placement?.Tick(dt); }
        catch (Exception ex) { ErrorLog.Write("Placement tick failed: " + ex); }
    }

    public override void OnRemoveBehavior()
    {
        base.OnRemoveBehavior();
        _placement?.Dispose();
        _placement = null;
    }

    /// <summary>Waits for the deployment plan, then sets up every bought item as a ghost at its default spot.</summary>
    private void Initialise()
    {
        Mission mission = Mission;
        if (!mission.IsFieldBattle)
        {
            _initialised = true;
            _spawned = true;
            return;
        }
        Team? player = mission.PlayerTeam;
        if (player == null) return;
        if (!mission.DeploymentPlan.IsPlanMade(player)) return;

        // The defender's works may not have been worked out yet when this mission was created.
        FortificationCampaignBehavior.EnsureDecided();
        if (!FortificationState.AnyPending && !FortificationState.EnemyAnyPending)
        {
            _initialised = true;
            _spawned = true;
            return;
        }

        mission.GetFormationSpawnFrame(player, FormationClass.Infantry, false, out WorldPosition spawn, out Vec2 direction, true);
        if (!spawn.IsValid) return;
        _initialised = true;

        _settings = FortificationSettings.Load();
        if (_settings.Debug)
            ErrorLog.Debug($"Mission ready: scene {mission.SceneName}, mine [{string.Join(",", FortificationState.Bought)}], theirs [{string.Join(",", FortificationState.Enemy)}].");
        FortificationSettings.Current = _settings;
        SiegeAiPatches.BarrelPoints.Clear();
        Vec2 forward = direction.Normalized();
        Vec2 lateral = forward.LeftVec();
        float halfSpan = (FortificationState.SegmentCount - 1) * FortificationState.SegmentPitch * 0.5f;
        bool useBoundary = mission.DeploymentPlan.HasDeploymentBoundaries(player);

        float Distance(float lateralOffset)
        {
            Vec2 origin = spawn.AsVec2 + lateral * lateralOffset;
            return useBoundary ? DistanceToBoundary(mission, player, origin, forward) : FortificationState.DistanceInFront;
        }

        _placement = new PlacementController(mission, _settings, spawn.AsVec2);
        int lines = FortificationState.Count(FortificationState.Work.Barricades);
        int ballistas = FortificationState.Count(FortificationState.Work.Ballista);
        int mangonels = FortificationState.Count(FortificationState.Work.Mangonel);
        int stockpiles = FortificationState.Count(FortificationState.Work.Arrows);
        int platforms = FortificationState.Count(FortificationState.Work.Tower);

        // Barricade lines sit side by side across the front; everything else hangs off the ends of that front.
        float frontHalfWidth = halfSpan + Math.Max(0, lines - 1) * FortificationState.LinePitch * 0.5f;
        for (int n = 0; n < lines; n++)
        {
            float lineOffset = (n - (lines - 1) * 0.5f) * FortificationState.LinePitch;
            // The line is one straight piece, so use the furthest boundary distance of its segments: none may start inside.
            float lineDistance = 0f;
            for (int i = 0; i < FortificationState.SegmentCount; i++)
                lineDistance = Math.Max(lineDistance, Distance(lineOffset + i * FortificationState.SegmentPitch - halfSpan));
            _placement.AddItem(PlacementController.Kind.BarricadeLine, Numbered("Barricades", n, lines), FortificationState.BarricadeIcon,
                spawn.AsVec2 + lateral * lineOffset + forward * lineDistance, forward);
        }
        for (int n = 0; n < ballistas; n++)
        {
            float offset = frontHalfWidth + FortificationState.EngineFlankOffset + n * FortificationState.EnginePitch;
            _placement.AddItem(PlacementController.Kind.Ballista, Numbered("Ballista", n, ballistas), FortificationState.BallistaIcon,
                spawn.AsVec2 + lateral * offset + forward * Distance(offset), forward);
        }
        for (int n = 0; n < mangonels; n++)
        {
            float offset = -(frontHalfWidth + FortificationState.EngineFlankOffset + n * FortificationState.EnginePitch);
            _placement.AddItem(PlacementController.Kind.Mangonel, Numbered("Catapult", n, mangonels), FortificationState.MangonelIcon,
                spawn.AsVec2 + lateral * offset + forward * Distance(offset), forward);
        }
        for (int n = 0; n < stockpiles; n++)
        {
            // Behind the infantry line, inside the deployment area, where the archers stand.
            float offset = (n - (stockpiles - 1) * 0.5f) * FortificationState.ArrowsPitch;
            _placement.AddItem(PlacementController.Kind.ArrowBarrels, Numbered("Arrows", n, stockpiles), FortificationState.ArrowsIcon,
                spawn.AsVec2 + lateral * offset - forward * FortificationState.ArrowsBehindLine, forward);
        }
        for (int n = 0; n < platforms; n++)
        {
            float offset = frontHalfWidth + FortificationState.TowerFlankOffset + ballistas * FortificationState.EnginePitch + n * (PlatformBuilder.DeckWidth + 4f);
            _placement.AddItem(PlacementController.Kind.ArcherTower, Numbered("Platform", n, platforms), FortificationState.TowerIcon,
                spawn.AsVec2 + lateral * offset + forward * Distance(offset), forward);
        }

        // No deployment phase (a later round, or reinforcements): build straight away at the defaults.
        if (mission.IsDeploymentFinished || mission.Mode != MissionMode.Deployment)
        {
            SpawnAll();
            return;
        }
        if (_placement.Items.Count > 0) _placement.ShowPanel();
    }

    private static string Numbered(string name, int index, int total) => total > 1 ? name + " " + (index + 1) : name;

    /// <summary>Turns every ghost into the real thing, wherever it ended up.</summary>
    private void SpawnAll()
    {
        _spawned = true;
        Mission mission = Mission;
        Team? player = mission.PlayerTeam;
        PlacementController? placement = _placement;
        _placement = null;
        BuildEnemyWorks(mission);
        if (placement == null || player == null) return;
        placement.Dispose();

        foreach (PlacementController.Item item in placement.Items)
        {
            switch (item.Kind)
            {
                case PlacementController.Kind.BarricadeLine:
                    for (int i = 0; i < FortificationState.SegmentCount; i++)
                    {
                        placement.Placement(item, i, out Vec2 at, out Vec2 facing);
                        if (SpawnSegment(mission, player, at, facing)) _barricades.Add(new Barricade(at, item.Forward));
                    }
                    break;
                case PlacementController.Kind.Ballista:
                case PlacementController.Kind.Mangonel:
                {
                    placement.Placement(item, 0, out Vec2 at, out Vec2 facing);
                    string prefab = item.Kind == PlacementController.Kind.Ballista ? FortificationState.BallistaPrefab : FortificationState.MangonelPrefab;
                    SpawnEngine(mission, player, prefab, at, facing);
                    break;
                }
                case PlacementController.Kind.ArrowBarrels:
                    for (int i = 0; i < FortificationState.ArrowBarrelCount; i++)
                    {
                        placement.Placement(item, i, out Vec2 at, out Vec2 facing);
                        SpawnArrowBarrel(mission, player, at, facing);
                    }
                    break;
                case PlacementController.Kind.ArcherTower:
                {
                    placement.Placement(item, 0, out Vec2 at, out Vec2 facing);
                    MatrixFrame root = PlacementController.GroundFrame(mission.Scene, at, facing);
                    PlatformBuilder.Build(mission, in root);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Ground a work can stand on and men can get to: flat enough, on the navigation mesh, and joined by a walkable
    /// path to where its owner's troops start. A mountainside or the far bank of a ravine fails all three.
    /// </summary>
    private static bool IsUsableGround(Mission mission, Team owner, Vec2 from, Vec2 at, bool keepOutsideBoundary)
    {
        try
        {
            Scene scene = mission.Scene;
            float z = scene.GetGroundHeightAtPosition(at.ToVec3(1000f), out Vec3 normal, BodyFlags.CommonCollisionExcludeFlagsForAgent);
            if (normal.z < FortificationState.MinGroundFlatness) return false;

            Vec3 point = at.ToVec3(z);
            PathFaceRecord record = PathFaceRecord.NullFaceRecord;
            scene.GetNavMeshFaceIndex(ref record, point, true);
            if (!record.IsValid()) return false;

            IMissionDeploymentPlan plan = mission.DeploymentPlan;
            if (keepOutsideBoundary && plan.HasDeploymentBoundaries(owner) && plan.IsPositionInsideDeploymentBoundaries(owner, in at)) return false;

            var start = new WorldPosition(scene, from.ToVec3(scene.GetGroundHeightAtPosition(from.ToVec3(1000f), BodyFlags.CommonCollisionExcludeFlagsForAgent)));
            var end = new WorldPosition(scene, point);
            return scene.DoesPathExistBetweenPositions(start, end);
        }
        catch
        {
            // If the scene cannot answer, take the spot as given rather than dropping the work.
            return true;
        }
    }

    /// <summary>
    /// Finds a workable spot at or near the one the layout asked for, pulling it back toward its owner's line and
    /// trying a little to each side. Returns false when the ground nearby is hopeless, and the work is skipped.
    /// </summary>
    private static bool PlaceOnGround(Mission mission, Team owner, Vec2 from, Vec2 wanted, Vec2 forward, bool keepOutsideBoundary, out Vec2 chosen)
    {
        chosen = wanted;
        if (IsUsableGround(mission, owner, from, wanted, keepOutsideBoundary)) return true;
        Vec2 lateral = forward.LeftVec();
        for (float back = FortificationState.GroundSearchStep; back <= FortificationState.MaxGroundSearch; back += FortificationState.GroundSearchStep)
        {
            foreach (float side in new[] { 0f, 6f, -6f, 12f, -12f })
            {
                Vec2 candidate = wanted - forward * back + lateral * side;
                if (!IsUsableGround(mission, owner, from, candidate, keepOutsideBoundary)) continue;
                chosen = candidate;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Builds the defending lord's works in front of his own infantry, laid out the way the player's defaults are.
    /// He places nothing by hand, so these go straight down with no ghosts or cards.
    /// </summary>
    private void BuildEnemyWorks(Mission mission)
    {
        try
        {
            bool debug = _settings.Debug;
            if (!FortificationState.EnemyAnyPending)
            {
                if (debug) ErrorLog.Debug("Defender works: nothing was decided for this battle.");
                return;
            }
            Team? enemy = mission.PlayerEnemyTeam;
            if (enemy == null)
            {
                if (debug) ErrorLog.Debug("Defender works: no enemy team in this mission.");
                return;
            }
            mission.GetFormationSpawnFrame(enemy, FormationClass.Infantry, false, out WorldPosition spawn, out Vec2 direction, true);
            if (!spawn.IsValid)
            {
                if (debug) ErrorLog.Debug("Defender works: the enemy has no valid infantry spawn frame.");
                return;
            }
            if (debug) ErrorLog.Debug($"Defender works: building [{string.Join(",", FortificationState.Enemy)}] for side {enemy.Side} at {spawn.AsVec2}.");

            Vec2 forward = direction.Normalized();
            Vec2 lateral = forward.LeftVec();
            Vec2 origin = spawn.AsVec2;
            float halfSpan = (FortificationState.SegmentCount - 1) * FortificationState.SegmentPitch * 0.5f;
            bool useBoundary = mission.DeploymentPlan.HasDeploymentBoundaries(enemy);

            float Distance(float lateralOffset)
            {
                Vec2 from = origin + lateral * lateralOffset;
                return useBoundary ? DistanceToBoundary(mission, enemy, from, forward) : FortificationState.DistanceInFront;
            }

            int lines = FortificationState.EnemyCount(FortificationState.Work.Barricades);
            int ballistas = FortificationState.EnemyCount(FortificationState.Work.Ballista);
            int mangonels = FortificationState.EnemyCount(FortificationState.Work.Mangonel);
            int stockpiles = FortificationState.EnemyCount(FortificationState.Work.Arrows);
            int platforms = FortificationState.EnemyCount(FortificationState.Work.Tower);
            float frontHalfWidth = halfSpan + Math.Max(0, lines - 1) * FortificationState.LinePitch * 0.5f;

            for (int n = 0; n < lines; n++)
            {
                float lineOffset = (n - (lines - 1) * 0.5f) * FortificationState.LinePitch;
                float lineDistance = 0f;
                for (int i = 0; i < FortificationState.SegmentCount; i++)
                    lineDistance = Math.Max(lineDistance, Distance(lineOffset + i * FortificationState.SegmentPitch - halfSpan));
                Vec2 wanted = origin + lateral * lineOffset + forward * lineDistance;
                if (!PlaceOnGround(mission, enemy, origin, wanted, forward, true, out Vec2 centre))
                {
                    if (debug) ErrorLog.Debug($"Defender works: no ground for a barricade line near {wanted}, skipped.");
                    continue;
                }
                for (int i = 0; i < FortificationState.SegmentCount; i++)
                {
                    Vec2 at = centre + lateral * (i * FortificationState.SegmentPitch - halfSpan);
                    if (SpawnSegment(mission, enemy, at, -forward)) _barricades.Add(new Barricade(at, forward));
                }
            }
            for (int n = 0; n < ballistas; n++)
            {
                float offset = frontHalfWidth + FortificationState.EngineFlankOffset + n * FortificationState.EnginePitch;
                Vec2 wanted = origin + lateral * offset + forward * Distance(offset);
                if (!PlaceOnGround(mission, enemy, origin, wanted, forward, false, out Vec2 at))
                {
                    if (debug) ErrorLog.Debug($"Defender works: no ground for a ballista near {wanted}, skipped.");
                    continue;
                }
                SpawnEngine(mission, enemy, FortificationState.BallistaPrefab, at, -forward);
            }
            for (int n = 0; n < mangonels; n++)
            {
                float offset = -(frontHalfWidth + FortificationState.EngineFlankOffset + n * FortificationState.EnginePitch);
                Vec2 wanted = origin + lateral * offset + forward * Distance(offset);
                if (!PlaceOnGround(mission, enemy, origin, wanted, forward, false, out Vec2 at))
                {
                    if (debug) ErrorLog.Debug($"Defender works: no ground for a catapult near {wanted}, skipped.");
                    continue;
                }
                SpawnEngine(mission, enemy, FortificationState.MangonelPrefab, at, -forward);
            }
            for (int n = 0; n < stockpiles; n++)
            {
                float offset = (n - (stockpiles - 1) * 0.5f) * FortificationState.ArrowsPitch;
                Vec2 wantedBarrels = origin + lateral * offset - forward * FortificationState.ArrowsBehindLine;
                if (!PlaceOnGround(mission, enemy, origin, wantedBarrels, forward, false, out Vec2 centre))
                {
                    if (debug) ErrorLog.Debug($"Defender works: no ground for an arrow stockpile near {wantedBarrels}, skipped.");
                    continue;
                }
                float halfBarrels = (FortificationState.ArrowBarrelCount - 1) * FortificationState.ArrowBarrelPitch * 0.5f;
                for (int i = 0; i < FortificationState.ArrowBarrelCount; i++)
                    SpawnArrowBarrel(mission, enemy, centre + lateral * (i * FortificationState.ArrowBarrelPitch - halfBarrels), -forward);
            }
            for (int n = 0; n < platforms; n++)
            {
                float offset = frontHalfWidth + FortificationState.TowerFlankOffset + ballistas * FortificationState.EnginePitch + n * (PlatformBuilder.DeckWidth + 4f);
                MatrixFrame root = PlacementController.GroundFrame(mission.Scene, origin + lateral * offset + forward * Distance(offset), forward);
                PlatformBuilder.Build(mission, in root);
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Defender works failed: " + ex);
        }
    }

    /// <summary>
    /// Walks forward from the spawn line until it leaves the player's deployment boundary, then adds a margin so the
    /// works sit just outside it and can never overlap a deployment slot. Falls back to the fixed distance if the
    /// boundary is never left or the origin is already outside it.
    /// </summary>
    private static float DistanceToBoundary(Mission mission, Team team, Vec2 origin, Vec2 forward)
    {
        IMissionDeploymentPlan plan = mission.DeploymentPlan;
        if (!plan.IsPositionInsideDeploymentBoundaries(team, in origin)) return FortificationState.DistanceInFront;
        for (float d = FortificationState.MinDistanceInFront; d <= FortificationState.MaxDistanceInFront; d += 1f)
        {
            Vec2 probe = origin + forward * d;
            if (!plan.IsPositionInsideDeploymentBoundaries(team, in probe))
                return d + FortificationState.BoundaryMargin;
        }
        return FortificationState.DistanceInFront;
    }

    private static bool SpawnSegment(Mission mission, Team owner, Vec2 at, Vec2 facing)
    {
        try
        {
            MatrixFrame frame = PlacementController.GroundFrame(mission.Scene, at, facing);
            GameEntity? entity = GameEntity.Instantiate(mission.Scene, FortificationState.SegmentPrefab, frame, false);
            if (entity == null)
            {
                ErrorLog.Write("Prefab " + FortificationState.SegmentPrefab + " not found.");
                return false;
            }
            DestructableComponent? destructable = entity.GetFirstScriptOfType<DestructableComponent>();
            if (destructable != null) destructable.BattleSide = owner.Side;
            entity.CallScriptCallbacks(true);
            destructable ??= entity.GetFirstScriptOfType<DestructableComponent>();
            if (destructable != null)
            {
                NavMeshPrefabField?.SetValue(destructable, FortificationState.SegmentNavMeshPrefab);
                AttachNavMeshMethod?.Invoke(destructable, null);
            }
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Barricade spawn failed: " + ex);
            return false;
        }
    }

    private void SpawnEngine(Mission mission, Team owner, string prefab, Vec2 at, Vec2 facing)
    {
        try
        {
            MatrixFrame frame = PlacementController.GroundFrame(mission.Scene, at, facing);
            GameEntity? entity = GameEntity.Instantiate(mission.Scene, prefab, frame, false);
            if (entity == null)
            {
                ErrorLog.Write("Prefab " + prefab + " not found.");
                return;
            }

            RangedSiegeWeapon? weapon = entity.GetFirstScriptOfTypeRecursive<RangedSiegeWeapon>();
            if (weapon != null)
            {
                DefaultSideField?.SetValue(weapon, owner.Side);
                StartingAmmoField?.SetValue(weapon, _settings.EngineAmmo);
            }
            entity.CallScriptCallbacks(true);
            weapon ??= entity.GetFirstScriptOfTypeRecursive<RangedSiegeWeapon>();
            if (weapon == null)
            {
                ErrorLog.Write(prefab + " has no RangedSiegeWeapon script.");
                return;
            }

            // Re-assert the side after OnInit in case the game's default-side logic overrode it, and top up ammo.
            DefaultSideField?.SetValue(weapon, owner.Side);
            if (weapon.AmmoCount < _settings.EngineAmmo) SetStartAmmoMethod?.Invoke(weapon, new object[] { _settings.EngineAmmo });

            if (_settings.CrewAi)
            {
                var request = new CrewRequest { Weapon = weapon, Team = owner, GiveUpAt = mission.CurrentTime + 120f };
                _crewPending.Add(request);
                TryCrew(request);
            }
            else
            {
                weapon.SetIsDisabledForAI(true);
            }
            _engines.Add(weapon);
            if (_settings.Debug && _reportAt < 0f) _reportAt = mission.CurrentTime + 20f;
        }
        catch (Exception ex)
        {
            ErrorLog.Write(prefab + " spawn failed: " + ex);
        }
    }

    /// <summary>
    /// An arrow barrel is a usable machine; registering it as a detachment of the archers' formation lets men who run
    /// low walk over and refill, exactly as in a siege. The player can use it too.
    /// </summary>
    private static void SpawnArrowBarrel(Mission mission, Team owner, Vec2 at, Vec2 facing)
    {
        try
        {
            MatrixFrame frame = PlacementController.GroundFrame(mission.Scene, at, facing);
            GameEntity? entity = GameEntity.Instantiate(mission.Scene, FortificationState.ArrowBarrelPrefab, frame, false);
            if (entity == null)
            {
                ErrorLog.Write("Prefab " + FortificationState.ArrowBarrelPrefab + " not found.");
                return;
            }
            entity.CallScriptCallbacks(true);
            UsableMachine? barrel = entity.GetFirstScriptOfTypeRecursive<UsableMachine>();
            if (barrel == null)
            {
                ErrorLog.Write("Arrow barrel has no UsableMachine script.");
                return;
            }
            foreach (StandingPoint point in barrel.StandingPoints)
            {
                if (point is StandingPointWithWeaponRequirement withWeapon) withWeapon.SetUsingBattleSide(owner.Side);
                SiegeAiPatches.BarrelPoints.Add(point);
            }
            // Barrels ship without an AI object (only players use them in sieges); the crew assignment code needs one.
            if (barrel.Ai == null) barrel.SetAI(new BarrelAi(barrel));
            DetachmentManager detachments = owner.DetachmentManager;
            if (!detachments.ContainsDetachment(barrel)) detachments.MakeDetachment(barrel);
            PickCrewFormation(owner)?.JoinDetachment(barrel);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Arrow barrel spawn failed: " + ex);
        }
    }

    /// <summary>The stock machine AI with nothing added: it walks eligible agents to the standing points and back.</summary>
    private sealed class BarrelAi : UsableMachineAIBase
    {
        public BarrelAi(UsableMachine machine) : base(machine) { }
    }

    /// <summary>
    /// Hands an engine to a formation that can spare men. An army that has not formed up yet has nobody to send,
    /// which is the usual case for the enemy when its works are built, so this is retried until a pilot climbs on.
    /// </summary>
    private bool TryCrew(CrewRequest request)
    {
        try
        {
            DetachmentManager detachments = request.Team.DetachmentManager;
            if (!detachments.ContainsDetachment(request.Weapon)) detachments.MakeDetachment(request.Weapon);
            Formation? formation = PickCrewFormation(request.Team);
            if (formation == null) return false;
            if (formation != request.Joined)
            {
                formation.JoinDetachment(request.Weapon);
                request.Joined = formation;
            }
            request.Weapon.SetForcedUse(true);
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Crewing an engine failed: " + ex);
            return false;
        }
    }

    private void TickCrew()
    {
        float now = Mission.CurrentTime;
        for (int i = _crewPending.Count - 1; i >= 0; i--)
        {
            CrewRequest request = _crewPending[i];
            if (request.Weapon.PilotAgent != null)
            {
                if (_settings.Debug) ErrorLog.Debug($"Engine crewed: side {request.Team.Side}, formation {request.Joined?.FormationIndex}.");
                _crewPending.RemoveAt(i);
                continue;
            }
            if (now >= request.GiveUpAt)
            {
                if (_settings.Debug) ErrorLog.Debug($"Engine never crewed: side {request.Team.Side} had nobody to spare.");
                _crewPending.RemoveAt(i);
                continue;
            }
            if (now < request.NextTry) continue;
            request.NextTry = now + 2f;
            TryCrew(request);
        }
    }

    /// <summary>One line per engine a short while into the battle, so a silent engine can be explained.</summary>
    private void ReportEngines()
    {
        _reportAt = -1f;
        foreach (RangedSiegeWeapon weapon in _engines)
        {
            try
            {
                ErrorLog.Debug($"Engine check: {weapon.GetType().Name} side={weapon.Side} ammo={weapon.AmmoCount} state={weapon.State} " +
                               $"pilot={(weapon.PilotAgent != null ? "yes" : "no")} forAI={!weapon.IsDisabledForAI} deactivated={weapon.IsDeactivated}.");
            }
            catch (Exception ex) { ErrorLog.Debug("Engine check failed: " + ex.Message); }
        }
    }

    private static Formation? PickCrewFormation(Team team)
    {
        Formation? ranged = team.GetFormation(FormationClass.Ranged);
        if (ranged != null && ranged.CountOfUnits > 0) return ranged;
        Formation? infantry = team.GetFormation(FormationClass.Infantry);
        if (infantry != null && infantry.CountOfUnits > 0) return infantry;
        foreach (Formation formation in team.FormationsIncludingEmpty)
            if (formation.CountOfUnits > 0) return formation;
        return null;
    }

    /// <summary>Horses that run into a barricade's spiked face take damage scaled by their speed.</summary>
    private void TickSpikes()
    {
        float now = Mission.CurrentTime;
        _spikeHits.Clear();
        foreach (Agent agent in Mission.Agents)
        {
            if (!agent.IsMount || !agent.IsActive()) continue;
            Vec2 velocity = agent.MovementVelocity;
            float speed = velocity.Length;
            if (speed < FortificationState.SpikeMinSpeed) continue;
            if (_spikeCooldown.TryGetValue(agent.Index, out float until) && now < until) continue;

            Vec2 position = agent.Position.AsVec2;
            foreach (Barricade barricade in _barricades)
            {
                Vec2 d = position - barricade.Centre;
                float along = Vec2.DotProduct(d, barricade.Forward);
                float across = Vec2.DotProduct(d, barricade.Lateral);
                if (Math.Abs(along) > FortificationState.SegmentHalfDepth || Math.Abs(across) > FortificationState.SegmentHalfWidth) continue;

                float damage = Math.Min(FortificationState.SpikeMaxDamage, FortificationState.SpikeBaseDamage + FortificationState.SpikeDamagePerSpeed * speed);
                _spikeHits.Add((agent, damage, velocity));
                _spikeCooldown[agent.Index] = now + FortificationState.SpikeCooldown;
                break;
            }
        }
        // Applied after the walk: a fatal blow removes the horse from the agent list, which must not change mid-walk.
        foreach ((Agent horse, float damage, Vec2 velocity) in _spikeHits)
            if (horse.IsActive()) Impale(horse, damage, velocity);
        _spikeHits.Clear();
    }

    private static void Impale(Agent horse, float damage, Vec2 velocity)
    {
        try
        {
            Vec3 direction = velocity.Normalized().ToVec3(0f);
            Blow blow = new Blow(horse.Index)
            {
                DamageType = DamageTypes.Pierce,
                StrikeType = StrikeType.Thrust,
                AttackType = AgentAttackType.Collision,
                BoneIndex = horse.Monster.HeadLookDirectionBoneIndex,
                VictimBodyPart = BoneBodyPartType.Chest,
                GlobalPosition = horse.GetChestGlobalPosition(),
                BaseMagnitude = damage,
                InflictedDamage = (int)damage,
                SwingDirection = direction,
                Direction = direction,
                DamageCalculated = true,
                BlowFlag = BlowFlags.None,
            };
            blow.WeaponRecord.FillAsMeleeBlow(null, null, -1, -1);
            AttackCollisionData collision = AttackCollisionData.GetAttackCollisionDataForDebugPurpose(
                false, false, false, true, false, false, false, false, false, false, false, false,
                CombatCollisionResult.StrikeAgent, -1, 0, (int)DamageTypes.Pierce, blow.BoneIndex, blow.VictimBodyPart, -1,
                Agent.UsageDirection.AttackLeft, -1, CombatHitResultFlags.NormalHit, 0.5f, 1f, 0f, 0f, 0f, 0f, 0f, 0f,
                Vec3.Up, blow.Direction, blow.GlobalPosition, Vec3.Zero, Vec3.Zero, horse.Velocity, Vec3.Up);
            horse.RegisterBlow(blow, in collision);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Spike damage failed: " + ex);
        }
    }

    /// <summary>
    /// The mangonel is built to be reloaded by a crew. If nobody reloads it for a while (crew dead, or crews turned
    /// off), the next stone is loaded and the machine returns to idle by itself.
    /// </summary>
    private void TickAutoReload()
    {
        float now = Mission.CurrentTime;
        foreach (RangedSiegeWeapon weapon in _engines)
        {
            if (weapon is not Mangonel) continue;
            RangedSiegeWeapon.WeaponState state = weapon.State;
            bool waiting = state == RangedSiegeWeapon.WeaponState.WaitingBeforeReloading || state == RangedSiegeWeapon.WeaponState.Reloading
                        || state == RangedSiegeWeapon.WeaponState.ReloadingPaused || state == RangedSiegeWeapon.WeaponState.LoadingAmmo;
            if (!waiting)
            {
                _reloadStuckSince.Remove(weapon);
                continue;
            }
            if (!_reloadStuckSince.TryGetValue(weapon, out float since))
            {
                _reloadStuckSince[weapon] = now;
                continue;
            }
            if (now - since < FortificationState.AutoReloadSeconds || weapon.AmmoCount <= 0) continue;
            _reloadStuckSince.Remove(weapon);
            try
            {
                string? missileId = MissileItemIdField?.GetValue(weapon) as string;
                ItemObject? missile = string.IsNullOrEmpty(missileId) ? null : MBObjectManager.Instance.GetObject<ItemObject>(missileId);
                if (missile != null) LoadedMissileField?.SetValue(weapon, missile);
                StateProperty?.SetValue(weapon, RangedSiegeWeapon.WeaponState.Idle);
                UpdateAmmoMeshMethod?.Invoke(weapon, null);
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Mangonel self-reload failed: " + ex);
            }
        }
    }
}
