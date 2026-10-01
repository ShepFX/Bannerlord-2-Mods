using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace ManorLord;

/// <summary>What the estate looks like at the moment a scene is entered. Built by the campaign behavior and handed to the mission.</summary>
internal sealed class EstateSnapshot
{
    public int Tier = 1;
    public bool Palisade, TrainingField, Storehouse, GuardQuarters, Mill, Orchard, Workshop, Damaged;
    public bool Steward, Captain, Physician;
    public int GuardCount;
    public CharacterObject? GuardTroop;
    public CharacterObject? StaffTroop;
    /// <summary>Healthy stationed troops to stand in the yard, one entry per soldier.</summary>
    public List<CharacterObject> Stationed = new();

    /// <summary>Resolves a prop's <c>when</c> condition against the estate state.</summary>
    public bool Satisfies(string when) => when switch
    {
        "always" => true,
        "palisade" => Palisade,
        "training" => TrainingField,
        "storehouse" => Storehouse,
        "quarters" => GuardQuarters,
        "mill" => Mill,
        "orchard" => Orchard,
        "workshop" => Workshop,
        "tier2" => Tier >= 2,
        "tier3" => Tier >= 3,
        // Exact ranks, for props that are replaced rather than added as the estate grows (the manor house itself).
        "rank1" => Tier == 1,
        "rank2" => Tier == 2,
        "rank3" => Tier >= 3,
        "damaged" => Damaged,
        "no_palisade" => !Palisade,
        "steward" => Steward,
        "captain" => Captain,
        "physician" => Physician,
        _ => false
    };
}

/// <summary>
/// A prefab to place when its condition holds. <c>Count</c>/<c>Dx</c>/<c>Dy</c> repeat it along a line (fences, tree rows);
/// <c>Stack</c> piles copies on top of each other; <c>AlignToSlope</c> tilts each copy so its long axis follows the ground.
/// </summary>
internal sealed class ManorProp
{
    public string When = "always";
    public string Prefab = string.Empty;
    public float X, Y, YawDegrees, ZOffset;
    public int Count = 1;
    public float Dx, Dy;
    public int Stack = 1;
    public bool AlignToSlope;
    /// <summary>For "_barrier" blocker runs only: override the strip length (else from count/step) and its width (default 1 m).</summary>
    public float Length, Width = 1f;
}

/// <summary>Where a group of agents stands. <c>Role</c> is guards, captain, steward or physician.</summary>
internal sealed class ManorSpawn
{
    public string Role = string.Empty;
    public float X, Y, YawDegrees;
}

/// <summary>Where the player can press the interact key. <c>Kind</c> is estate (the management menu) or storehouse (straight to the stash).</summary>
internal sealed class ManorAccess
{
    public string Kind = "estate";
    public float X, Y, Radius = 3f;
}

/// <summary>Scene entities of a given prefab inside a rectangle to remove before dressing the yard (leftover farm fences, say).</summary>
internal sealed class ManorClear
{
    public string Prefab = string.Empty;
    public float X1, Y1, X2, Y2;
}

/// <summary>Reads <c>ModuleData/manor_lord_props.xml</c>. Coordinates are scene metres; height is snapped to the terrain at runtime.</summary>
internal static class ManorPropCatalog
{
    public static void Load(out List<ManorProp> props, out List<ManorSpawn> spawns, out List<ManorAccess> access, out List<ManorClear> clears)
    {
        props = new List<ManorProp>();
        spawns = new List<ManorSpawn>();
        access = new List<ManorAccess>();
        clears = new List<ManorClear>();
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(ManorPropCatalog).Assembly.Location) ?? string.Empty, "..", "..", "ModuleData", "manor_lord_props.xml"));
        if (!File.Exists(path))
        {
            Debug.Print($"[ManorLord] Prop layout not found at {path}; the estate scene will show no upgrade props.");
            return;
        }
        try
        {
            var doc = new XmlDocument();
            doc.Load(path);
            foreach (XmlNode node in doc.SelectNodes("//Prop") ?? throw new InvalidOperationException("no Prop nodes"))
            {
                if (node.Attributes == null) continue;
                props.Add(new ManorProp
                {
                    When = Attr(node, "when", "always"),
                    Prefab = Attr(node, "prefab", string.Empty),
                    X = Num(node, "x"),
                    Y = Num(node, "y"),
                    YawDegrees = Num(node, "yaw"),
                    ZOffset = Num(node, "z_offset"),
                    Count = Math.Max(1, (int)Num(node, "count", 1f)),
                    Dx = Num(node, "dx"),
                    Dy = Num(node, "dy"),
                    Stack = Math.Max(1, (int)Num(node, "stack", 1f)),
                    AlignToSlope = string.Equals(Attr(node, "align", string.Empty), "slope", StringComparison.OrdinalIgnoreCase),
                    Length = Num(node, "length"),
                    Width = Math.Max(0.5f, Num(node, "width", 1f))
                });
            }
            foreach (XmlNode node in doc.SelectNodes("//Spawn") ?? throw new InvalidOperationException("no Spawn nodes"))
            {
                if (node.Attributes == null) continue;
                spawns.Add(new ManorSpawn { Role = Attr(node, "role", string.Empty), X = Num(node, "x"), Y = Num(node, "y"), YawDegrees = Num(node, "yaw") });
            }
            foreach (XmlNode node in doc.SelectNodes("//Access") ?? throw new InvalidOperationException("no Access nodes"))
            {
                if (node.Attributes == null) continue;
                access.Add(new ManorAccess { Kind = Attr(node, "kind", "estate"), X = Num(node, "x"), Y = Num(node, "y"), Radius = Math.Max(1f, Num(node, "radius", 3f)) });
            }
            foreach (XmlNode node in doc.SelectNodes("//Clear") ?? throw new InvalidOperationException("no Clear nodes"))
            {
                if (node.Attributes == null) continue;
                clears.Add(new ManorClear { Prefab = Attr(node, "prefab", string.Empty), X1 = Num(node, "x1"), Y1 = Num(node, "y1"), X2 = Num(node, "x2"), Y2 = Num(node, "y2") });
            }
        }
        catch (Exception ex)
        {
            Debug.Print($"[ManorLord] Failed to read {path}: {ex.Message}");
        }
    }

    private static string Attr(XmlNode node, string name, string fallback) => node.Attributes?[name]?.Value ?? fallback;

    private static float Num(XmlNode node, string name, float fallback = 0f) =>
        float.TryParse(node.Attributes?[name]?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
}

/// <summary>
/// Dresses the estate scene to match the campaign state: places a prop for every completed upgrade and
/// stands the household guard and staff in the yard. Runs in both the visit and the defense mission.
/// </summary>
public sealed class ManorEstateSceneBehavior : MissionBehavior
{
    private readonly EstateSnapshot _estate;
    private readonly bool _spawnPeople;
    private bool _propsPlaced;
    private bool _peopleSpawned;
    private List<ManorProp>? _props;
    private List<ManorSpawn>? _spawns;
    private List<ManorAccess>? _access;
    private List<ManorClear>? _clears;
    private ManorAccess? _currentAccess;
    private readonly HashSet<string> _measured = new HashSet<string>(StringComparer.Ordinal);

    internal ManorEstateSceneBehavior(EstateSnapshot estate, bool spawnPeople)
    {
        _estate = estate;
        _spawnPeople = spawnPeople;
    }

    public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

    public override void OnMissionTick(float dt)
    {
        base.OnMissionTick(dt);
        if (Mission?.Scene == null) return;
        if (_props == null) ManorPropCatalog.Load(out _props, out _spawns, out _access, out _clears);
        if (!_propsPlaced) PlaceProps();
        TickWalls();
        if (!_spawnPeople) return; // a defense: no idle staff, no estate desk
        if (!_peopleSpawned) TrySpawnPeople();
        TickAccess();
    }

    /// <summary>Prompts when the player reaches the access point and opens the estate menu on the interact key.</summary>
    private void TickAccess()
    {
        Agent? player = Agent.Main;
        if (_access == null || _access.Count == 0 || player == null || !player.IsActive()) return;
        Vec2 at = player.Position.AsVec2;
        ManorAccess? near = null;
        foreach (ManorAccess a in _access)
            if ((at - new Vec2(a.X, a.Y)).LengthSquared <= a.Radius * a.Radius) { near = a; break; }
        if (near != null && near != _currentAccess)
            MBInformationManager.AddQuickInformation(near.Kind == "storehouse"
                ? new TextObject("{=ml_scene_store_hint}Press F to open the storehouse.")
                : new TextObject("{=ml_scene_access_hint}Press F to manage the estate."));
        _currentAccess = near;
        if (near == null || !Input.IsKeyPressed(InputKey.F) || InformationManager.IsAnyInquiryActive()) return;
        if (near.Kind == "storehouse") OpenStorehouse(); else ShowEstateMenu();
    }

    private static void OpenStorehouse()
    {
        ManorLordBehavior? estate = Campaign.Current?.GetCampaignBehavior<ManorLordBehavior>();
        if (estate == null) return;
        if (estate.StorehouseUsable) { estate.OpenStashScreen(); return; }
        MBInformationManager.AddQuickInformation(estate.HasStorehouse
            ? new TextObject("{=ml_store_damaged}The damaged storehouse cannot be opened until repairs are complete.")
            : new TextObject("{=ml_store_missing}A storehouse must be built before funds or goods can be secured here."));
    }

    /// <summary>The in-scene estate menu: the actions that make sense without leaving the grounds.</summary>
    private static void ShowEstateMenu()
    {
        ManorLordBehavior? estate = Campaign.Current?.GetCampaignBehavior<ManorLordBehavior>();
        if (estate == null) return;
        string needsStorehouse = new TextObject("{=ml_scene_tip_storehouse}Requires a storehouse.").ToString();
        var options = new List<InquiryElement>
        {
            new InquiryElement("ledger", new TextObject("{=ml_opt_ledger}Review the estate ledger").ToString(), null),
            new InquiryElement("stash", new TextObject("{=ml_opt_open_stash}Open the manor item storehouse").ToString(), null, estate.StorehouseUsable,
                new TextObject("{=ml_scene_tip_storehouse_intact}Requires an intact storehouse.").ToString()),
            new InquiryElement("deposit", new TextObject("{=ml_opt_deposit_custom}Deposit a custom amount").ToString(), null, estate.HasStorehouse, needsStorehouse),
            new InquiryElement("withdraw", new TextObject("{=ml_opt_withdraw_custom}Withdraw a custom amount").ToString(), null, estate.HasStorehouse && estate.Treasury > 0,
                new TextObject("{=ml_scene_tip_withdraw}Requires a storehouse with funds in it.").ToString())
        };
        MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
            new TextObject("{=ml_scene_menu_title}The estate").ToString(),
            new TextObject("{=ml_scene_menu_body}Your steward awaits instructions.").ToString(),
            options, true, 1, 1,
            new TextObject("{=ml_btn_accept}Accept").ToString(),
            new TextObject("{=ml_btn_cancel}Cancel").ToString(),
            selected =>
            {
                switch (selected.Count > 0 ? selected[0].Identifier as string : null)
                {
                    case "ledger": estate.ShowLedgerInquiry(); break;
                    case "stash": estate.OpenStashScreen(); break;
                    case "deposit": estate.ShowCustomTransfer(true, switchMenu: false); break;
                    case "withdraw": estate.ShowCustomTransfer(false, switchMenu: false); break;
                }
            },
            null), false, false);
    }

    /// <summary>Removes scene entities the layout asks to clear, so the yard starts as open ground.</summary>
    private void ClearEntities()
    {
        if (_clears == null || _clears.Count == 0) return;
        var all = new List<GameEntity>();
        Mission.Scene.GetEntities(ref all);
        int removed = 0;
        foreach (GameEntity entity in all)
        {
            string prefab = entity.GetPrefabName();
            if (string.IsNullOrEmpty(prefab)) continue;
            Vec3 p = entity.GetGlobalFrame().origin;
            foreach (ManorClear clear in _clears)
            {
                if (!string.Equals(prefab, clear.Prefab, StringComparison.Ordinal)) continue;
                if (p.x < Math.Min(clear.X1, clear.X2) || p.x > Math.Max(clear.X1, clear.X2) || p.y < Math.Min(clear.Y1, clear.Y2) || p.y > Math.Max(clear.Y1, clear.Y2)) continue;
                try { entity.Remove(0); removed++; } catch (Exception ex) { Debug.Print($"[ManorLord] Could not remove {prefab}: {ex.Message}"); }
                break;
            }
        }
        if (removed > 0) Debug.Print($"[ManorLord] Cleared {removed} scene entities from the yard.");
    }

    private void PlaceProps()
    {
        _propsPlaced = true;
        ClearEntities();
        int placed = 0;
        _blockersOk = true;
        foreach (ManorProp prop in _props!)
        {
            if (string.IsNullOrEmpty(prop.Prefab) || !_estate.Satisfies(prop.When)) continue;
            // "_barrier" lines are not props at all: each run becomes one navmesh blocker, which is what stops agents.
            if (prop.Prefab.StartsWith("_barrier", StringComparison.Ordinal)) { PlaceBlockerRun(prop); continue; }
            float length = 0f, height = 0f; // learned from the first instance's bounding box
            for (int i = 0; i < prop.Count; i++)
            {
                MatrixFrame column = GroundFrame(prop.X + prop.Dx * i, prop.Y + prop.Dy * i, prop.YawDegrees, prop.ZOffset);
                if (prop.AlignToSlope && length > 0f) TiltToSlope(ref column, length, prop.ZOffset);
                bool failed = false;
                for (int level = 0; level < prop.Stack && !failed; level++)
                {
                    try
                    {
                        MatrixFrame frame = column;
                        if (level > 0) frame.origin += frame.rotation.u * (height * level);
                        // The frame overload of Instantiate skips physics; this one builds the collision bodies, then we place it.
                        GameEntity? entity = GameEntity.Instantiate(Mission.Scene, prop.Prefab, callScriptCallbacks: false, createPhysics: true);
                        if (entity == null) { Debug.Print($"[ManorLord] Prefab '{prop.Prefab}' could not be instantiated."); failed = true; break; }
                        // A Stationary entity's collision body is baked where it was created (the origin) and never
                        // follows the entity, so make it movable before placing it, and make sure physics is on.
                        entity.SetMobility(GameEntity.Mobility.Dynamic);
                        entity.EntityFlags &= ~EntityFlags.PhysicsDisabled;
                        entity.RemoveBodyFlags(BodyFlags.Disabled | BodyFlags.DontTransferToPhysicsEngine, true);
                        // Runtime bodies come up with no owner bits, and the agent collision filter only sees bodies
                        // owned by an entity. Moveable is what native kinematic props (siege engines) carry; Dynamic
                        // must stay clear because agents are filtered against it.
                        entity.AddBodyFlags(BodyFlags.BodyOwnerEntity | BodyFlags.Moveable, true);
                        // Our frames have unit axes; keep whatever scale the prefab bakes into its own transform
                        // (the invisible agent barriers are 1 m planes scaled to 4 x 4).
                        Vec3 scale = entity.GetGlobalScale();
                        MatrixFrame scaled = frame;
                        scaled.rotation.s *= scale.x;
                        scaled.rotation.f *= scale.y;
                        scaled.rotation.u *= scale.z;
                        entity.SetGlobalFrame(scaled, true);
                        // The callback pass is what finalises a placed prop's collision (the props have no scripts).
                        entity.CallScriptCallbacks(true);
                        entity.SetPhysicsState(true, true);
                        entity.SetVisibilityExcludeParents(true);
                        placed++;
                        if (length <= 0f)
                        {
                            Vec3 min = entity.GetBoundingBoxMin(), max = entity.GetBoundingBoxMax();
                            length = max.x - min.x;
                            height = max.z - min.z;
                            // Log each prefab's footprint once so manor_lord_props.xml can be tuned from real numbers,
                            // plus where the engine thinks its collision body is, for diagnosing missing collision.
                            if (_measured.Add(prop.Prefab))
                            {
                                (Vec3 pMin, Vec3 pMax) = entity.ComputeGlobalPhysicsBoundingBoxMinMax();
                                Debug.Print($"[ManorLord] Footprint {prop.Prefab}: {length:F2} x {max.y - min.y:F2} x {height:F2} m (x, y, height); local min ({min.x:F2}, {min.y:F2}, {min.z:F2})");
                                Debug.Print($"[ManorLord] Physics {prop.Prefab}: mobility={entity.GetMobility()} entityFlags={entity.EntityFlags} bodyFlags={entity.BodyFlag} physicsBox=({pMin.x:F1},{pMin.y:F1},{pMin.z:F1})..({pMax.x:F1},{pMax.y:F1},{pMax.z:F1}) placedAt=({frame.origin.x:F1},{frame.origin.y:F1},{frame.origin.z:F1})");
                            }
                            // The first copy went in flat because its size was unknown; re-seat it now.
                            if (prop.AlignToSlope)
                            {
                                TiltToSlope(ref column, length, prop.ZOffset);
                                scaled = column;
                                scaled.rotation.s *= scale.x;
                                scaled.rotation.f *= scale.y;
                                scaled.rotation.u *= scale.z;
                                entity.SetGlobalFrame(scaled, true);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.Print($"[ManorLord] Placing '{prop.Prefab}' failed: {ex.Message}");
                        failed = true;
                    }
                }
                if (failed) break;
            }
        }
        Debug.Print($"[ManorLord] Estate scene dressed with {placed} props; navmesh blockers {(_blockersOk ? "attached" : "FAILED - falling back to code-side wall pushing")}.");
    }

    // The game attaches a dynamic navmesh through MissionObject; these members are protected there.
    private static readonly FieldInfo? NavMeshPrefabField = typeof(MissionObject).GetField("NavMeshPrefabName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo? IdStartField = typeof(MissionObject).GetField("DynamicNavmeshIdStart", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly MethodInfo? AttachMethod = typeof(MissionObject).GetMethod("AttachDynamicNavmeshToEntity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private bool _blockersOk;

    /// <summary>Ground sampling must see terrain only, or a prop placed earlier (the base fence) reads as "ground" for the next.</summary>
    private const BodyFlags GroundFlags = BodyFlags.CommonCollisionExcludeFlags | BodyFlags.BodyOwnerEntity | BodyFlags.BodyOwnerFlora;

    /// <summary>A "_barrier" line in the layout: one blocker navmesh strip covering the whole run of slabs.</summary>
    /// <summary>The blocker strip a "_barrier" layout line describes: centre, yaw, length along the run, width across it.</summary>
    internal static (Vec2 Center, float Yaw, float Length, float Width) BlockerRun(ManorProp prop)
    {
        float step = (float)Math.Sqrt(prop.Dx * prop.Dx + prop.Dy * prop.Dy);
        float length = prop.Length > 0f ? prop.Length : prop.Count > 1 ? (prop.Count - 1) * step + 4f : 4f;
        float lastX = prop.X + prop.Dx * (prop.Count - 1), lastY = prop.Y + prop.Dy * (prop.Count - 1);
        var center = new Vec2((prop.X + lastX) * 0.5f, (prop.Y + lastY) * 0.5f);
        float yaw = prop.Count > 1 ? (float)(Math.Atan2(prop.Dy, prop.Dx) * 180.0 / Math.PI) : prop.YawDegrees;
        return (center, yaw, length, prop.Width);
    }

    /// <summary>The game's own barricade blocker: a ~3.2 x 2.2 m pad of blocked navmesh, long axis along local x.</summary>
    private const string BlockerPrefab = "siege_barricade_state1234_destroyed_blocker_dnm";
    private const float BlockerSpacing = 2f; // pads overlap, so a run has no gap an agent could slip through

    private void PlaceBlockerRun(ManorProp prop)
    {
        (Vec2 center, float yaw, float length, float width) = BlockerRun(prop);
        MatrixFrame frame = GroundFrame(center.x, center.y, yaw, 0f);
        Vec2 axis = frame.rotation.s.AsVec2;
        if (axis.LengthSquared < 0.01f) return;
        axis.Normalize();
        // One barricade pad every couple of metres along the run, exactly as FieldFortifications lays its barricades.
        int pads = Math.Max(1, (int)Math.Ceiling(length / BlockerSpacing));
        float first = -(pads - 1) * BlockerSpacing * 0.5f;
        int attached = 0;
        for (int i = 0; i < pads; i++)
            if (AttachBlocker(center + axis * (first + i * BlockerSpacing), yaw)) attached++;
        if (attached < pads) _blockersOk = false;
        // Remember the footprint for the code-side fallback as well.
        _walls.Add((center, axis, length * 0.5f, width * 0.5f));
    }

    /// <summary>
    /// Attaches the game's barricade blocker navmesh at a point, the way the game attaches dynamic navmesh to its
    /// own props: an owner entity with a SynchedMissionObject script, NavMeshPrefabName set, then the game's own
    /// AttachDynamicNavmeshToEntity. Agents path around it and cannot walk through it.
    /// </summary>
    private bool AttachBlocker(Vec2 at, float yawDegrees)
    {
        try
        {
            const string name = BlockerPrefab;
            MatrixFrame frame = GroundFrame(at.x, at.y, yawDegrees, 0f);
            GameEntity root = GameEntity.CreateEmpty(Mission.Scene, false, false, false);
            root.SetGlobalFrame(frame, true);
            root.CreateAndAddScriptComponent("SynchedMissionObject", false);
            MissionObject? owner = root.GetFirstScriptOfType<MissionObject>();
            if (owner == null) { Debug.Print("[ManorLord] Blocker: could not add a SynchedMissionObject owner."); return false; }
            NavMeshPrefabField?.SetValue(owner, name);
            root.CallScriptCallbacks(true);
            int faces = root.GetAttachedNavmeshFaceCount();
            if (faces == 0)
            {
                AttachMethod?.Invoke(owner, null);
                faces = root.GetAttachedNavmeshFaceCount();
            }
            // OnInit disables the faces when the owner has no physics body; enable them by hand.
            int start = IdStartField != null ? (int)IdStartField.GetValue(owner) : 0;
            if (start > 0) for (int id = start; id < start + 10; id++) Mission.Scene.SetAbilityOfFacesWithId(id, true);
            if (faces == 0) Debug.Print($"[ManorLord] Blocker at ({at.x:F1},{at.y:F1}) yaw {yawDegrees:F0} attached no faces (id block {start}).");
            return faces > 0;
        }
        catch (Exception ex)
        {
            Debug.Print($"[ManorLord] Blocker attach failed: {ex}");
            return false;
        }
    }

    /// <summary>Blocker footprints the player is held out of: centre, unit axis along the run, half-length, half-width.</summary>
    private readonly List<(Vec2 Center, Vec2 Axis, float HalfLength, float HalfWidth)> _walls = new List<(Vec2, Vec2, float, float)>();
    private readonly Dictionary<int, float> _wallSide = new Dictionary<int, float>();

    /// <summary>
    /// The engine only stops agents with the navmesh, which needs the scene editor to rebake. Until that is done,
    /// hold the player out of every wall slab by hand: if they push into one, slide them back to its face.
    /// </summary>
    private void TickWalls()
    {
        // Only needed if the navmesh blockers could not be attached; otherwise the engine stops the player itself.
        if (_blockersOk) return;
        Agent? player = Agent.Main;
        if (_walls.Count == 0 || player == null || !player.IsActive()) return;
        Agent body = player.MountAgent ?? player;
        float radius = player.HasMount ? 0.9f : 0.45f;
        const float maxStep = 0.12f; // never move the player further than this in one tick, so a correction can't fling them
        Vec3 position = body.Position;
        Vec2 p = position.AsVec2;
        Vec2 push = Vec2.Zero;
        for (int i = 0; i < _walls.Count; i++)
        {
            (Vec2 center, Vec2 axis, float halfLength, float halfWidth) = _walls[i];
            Vec2 d = p - center;
            float along = d.x * axis.x + d.y * axis.y;
            if (Math.Abs(along) > halfLength + radius) continue;
            var normal = new Vec2(-axis.y, axis.x);
            float across = d.x * normal.x + d.y * normal.y;
            float keepOut = radius + halfWidth;
            if (Math.Abs(across) >= keepOut)
            {
                // Clear of this footprint: remember which side the player is on, so a later push is always back the way they came.
                _wallSide[i] = across >= 0f ? 1f : -1f;
                continue;
            }
            float side = _wallSide.TryGetValue(i, out float remembered) ? remembered : (across >= 0f ? 1f : -1f);
            push += normal * (keepOut - across * side) * side;
        }
        if (push.LengthSquared < 0.0004f) return;
        if (push.Length > maxStep) push = push.Normalized() * maxStep;
        position.x += push.x;
        position.y += push.y;
        body.TeleportToPosition(position);
    }


    /// <summary>
    /// Pitches a frame so its side axis (the prefab's long dimension) runs between the ground heights at its two ends,
    /// and seats the origin midway. Samples slightly inside the ends so a butted neighbour's collision is not hit.
    /// </summary>
    private void TiltToSlope(ref MatrixFrame frame, float length, float zOffset)
    {
        Vec3 side = frame.rotation.s;
        side.z = 0f;
        if (side.LengthSquared < 0.01f) return;
        side.Normalize();
        float half = length * 0.45f;
        Vec3 a = frame.origin + side * half, b = frame.origin - side * half;
        a.z = Ground(a);
        b.z = Ground(b);
        frame.origin.z = (a.z + b.z) * 0.5f + zOffset;
        Vec3 slope = a - b;
        slope.Normalize();
        // Rebuild the basis directly rather than rotating, so there is no sign to get wrong: u = s x f in this engine.
        frame.rotation.s = slope;
        frame.rotation.u = Vec3.CrossProduct(slope, frame.rotation.f);
    }

    private float Ground(Vec3 p) => Mission.Scene.GetGroundHeightAtPosition(p, GroundFlags);

    private void TrySpawnPeople()
    {
        Agent? player = Agent.Main;
        Team? team = Mission.PlayerTeam ?? Mission.DefenderTeam;
        if (player == null || team == null || !player.IsActive()) return;
        _peopleSpawned = true;

        foreach (ManorSpawn spawn in _spawns!)
        {
            switch (spawn.Role)
            {
                case "stationed":
                    SpawnRanks(team, _estate.Stationed, spawn, false);
                    break;
                case "guards":
                    if (_estate.GuardTroop != null && _estate.GuardCount > 0)
                        SpawnFormation(team, _estate.GuardTroop, Math.Min(12, _estate.GuardCount), spawn, false);
                    break;
                case "captain":
                    if (_estate.Captain && _estate.GuardTroop != null) SpawnFormation(team, _estate.GuardTroop, 1, spawn, false);
                    break;
                case "steward":
                    if (_estate.Steward && _estate.StaffTroop != null) SpawnFormation(team, _estate.StaffTroop, 1, spawn, true);
                    break;
                case "physician":
                    if (_estate.Physician && _estate.StaffTroop != null) SpawnFormation(team, _estate.StaffTroop, 1, spawn, true);
                    break;
            }
        }
    }

    /// <summary>Stands <paramref name="count"/> agents in ranks of four, facing the spawn's yaw.</summary>
    private void SpawnFormation(Team team, CharacterObject troop, int count, ManorSpawn spawn, bool civilian)
    {
        var troops = new List<CharacterObject>();
        for (int i = 0; i < count; i++) troops.Add(troop);
        SpawnRanks(team, troops, spawn, civilian);
    }

    /// <summary>Stands each troop in the list in ranks of four behind the spawn point, facing its yaw.</summary>
    private void SpawnRanks(Team team, IReadOnlyList<CharacterObject> troops, ManorSpawn spawn, bool civilian)
    {
        int count = troops.Count;
        if (count == 0) return;
        MatrixFrame anchor = GroundFrame(spawn.X, spawn.Y, spawn.YawDegrees, 0f);
        Vec2 forward = anchor.rotation.f.AsVec2;
        if (forward.LengthSquared < 0.1f) forward = new Vec2(0f, 1f);
        forward.Normalize();
        Vec2 side = new Vec2(-forward.y, forward.x);

        for (int i = 0; i < count; i++)
        {
            CharacterObject troop = troops[i];
            int row = i / 4;
            int column = i % 4;
            float sideOffset = (Math.Min(count, 4) == 1 ? 0f : column - 1.5f) * 1.8f;
            float backOffset = row * 2.2f;
            Vec3 position = anchor.origin;
            position.x += side.x * sideOffset - forward.x * backOffset;
            position.y += side.y * sideOffset - forward.y * backOffset;
            position.z = Mission.Scene.GetGroundHeightAtPosition(position, GroundFlags);
            try
            {
                var buildData = new AgentBuildData(new BasicBattleAgentOrigin(troop))
                    .Team(team)
                    .InitialPosition(position)
                    .InitialDirection(forward)
                    .NoHorses(true)
                    .CivilianEquipment(civilian);
                Mission.SpawnAgent(buildData, false);
            }
            catch (Exception ex)
            {
                Debug.Print($"[ManorLord] Spawning {spawn.Role} failed: {ex.Message}");
                return;
            }
        }
    }

    /// <summary>A frame at (x, y) resting on the terrain, rotated about the up axis by <paramref name="yawDegrees"/>.</summary>
    private MatrixFrame GroundFrame(float x, float y, float yawDegrees, float zOffset)
    {
        var position = new Vec3(x, y, 0f);
        position.z = Mission.Scene.GetGroundHeightAtPosition(position, GroundFlags) + zOffset;
        MatrixFrame frame = MatrixFrame.Identity;
        frame.origin = position;
        frame.rotation.RotateAboutUp(yawDegrees * (float)Math.PI / 180f);
        return frame;
    }
}
