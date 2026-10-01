using System;

namespace FieldFortifications;

/// <summary>Campaign-to-mission handoff and the fixed tuning values.</summary>
public static class FortificationState
{
    public enum Work { Barricades, Ballista, Mangonel, Arrows, Tower }
    public const int WorkCount = 5;

    /// <summary>How many of each work were bought for the current encounter. Cleared when the player's map event ends.</summary>
    public static readonly int[] Bought = new int[WorkCount];

    /// <summary>Denars paid this encounter, for the summary and the refund.</summary>
    public static int Spent;

    /// <summary>
    /// Works the defending AI lord has dug in for the current encounter, and whether that has been worked out yet.
    /// Only filled when the player is attacking a lord in the field.
    /// </summary>
    public static readonly int[] Enemy = new int[WorkCount];

    /// <summary>
    /// Which enemy the works above were decided for. A plain "decided" flag could survive in a save and then block
    /// every later battle, so the decision is keyed to the enemy it was made against.
    /// </summary>
    public static string EnemyDecidedFor = "";

    public static int Count(Work work) => Bought[(int)work];

    public static int EnemyCount(Work work) => Enemy[(int)work];

    public static bool EnemyAnyPending
    {
        get
        {
            foreach (int n in Enemy) if (n > 0) return true;
            return false;
        }
    }

    public static bool AnyPending
    {
        get
        {
            foreach (int n in Bought) if (n > 0) return true;
            return false;
        }
    }

    public static void Clear()
    {
        Array.Clear(Bought, 0, Bought.Length);
        Array.Clear(Enemy, 0, Enemy.Length);
        EnemyDecidedFor = "";
        Spent = 0;
    }

    /// <summary>Metres between the centres of neighbouring barricade lines and neighbouring engines by default.</summary>
    public const float LinePitch = 34f;
    public const float EnginePitch = 9f;
    public const float ArrowsPitch = 10f;

    /// <summary>Scene prefab spawned for each obstacle segment.</summary>
    public const string SegmentPrefab = "siege_barricade_a";

    /// <summary>Dynamic blocker navmesh piece attached to each segment so the AI paths around it.</summary>
    public const string SegmentNavMeshPrefab = "siege_barricade_state1234_destroyed_blocker_dnm";

    public const string BallistaPrefab = "ballista_a";
    public const string MangonelPrefab = "mangonel_a";

    /// <summary>The game's arrow barrel: a usable machine archers refill their quivers from.</summary>
    public const string ArrowBarrelPrefab = "arrow_barrel";

    /// <summary>Barrels per stockpile and the spacing between them.</summary>
    public const int ArrowBarrelCount = 2;
    public const float ArrowBarrelPitch = 3f;


    /// <summary>Card icons, borrowed from the game's own UI.</summary>
    public const string BarricadeIcon = @"General\Icons\Walls";
    public const string BallistaIcon = @"Order\SiegeIcons\siege_ballista";
    public const string MangonelIcon = @"Order\SiegeIcons\siege_catapult";
    public const string ArrowsIcon = @"General\EquipmentIcons\equipment_type_quiver";
    public const string TowerIcon = @"Order\SiegeIcons\siege_tower";

    /// <summary>Alpha applied to placement ghosts.</summary>
    public const float GhostAlpha = 0.5f;

    /// <summary>Degrees per second the arrow keys turn a held item.</summary>
    public const float PlacementTurnSpeed = 60f;

    /// <summary>Furthest from the infantry spawn an item may be placed, in metres.</summary>
    public const float MaxPlacementDistance = 200f;

    /// <summary>Seconds a mangonel may sit waiting for a crew to reload before it reloads itself.</summary>
    public const float AutoReloadSeconds = 8f;

    /// <summary>
    /// How flat ground must be to carry a work: the upward part of the terrain normal, so 0.86 is about thirty
    /// degrees. Steeper than this and men slide off it and props sit in the air.
    /// </summary>
    public const float MinGroundFlatness = 0.86f;

    /// <summary>How far back toward its owner a work may be pulled to find ground men can reach, and in what steps.</summary>
    public const float MaxGroundSearch = 40f;
    public const float GroundSearchStep = 4f;

    /// <summary>Metres beyond the outermost barricade where an engine sits by default.</summary>
    public const float EngineFlankOffset = 10f;

    /// <summary>Default spots for the stockpile (behind the line, inside the deployment area) and the tower (behind the line, further out).</summary>
    public const float ArrowsBehindLine = 12f;
    public const float TowerFlankOffset = 24f;

    /// <summary>Number of segments in the line.</summary>
    public const int SegmentCount = 4;

    /// <summary>Metres between segment centres: roughly one barricade width plus a gap the AI can funnel through.</summary>
    public const float SegmentPitch = 8f;

    /// <summary>Half the width of one barricade's spiked face, for the cavalry hit test.</summary>
    public const float SegmentHalfWidth = 2.6f;

    /// <summary>Half the depth of the spiked zone in front of and behind a barricade's centre line.</summary>
    public const float SegmentHalfDepth = 1.6f;

    /// <summary>Horses slower than this (metres per second) just bump the stakes and take nothing.</summary>
    public const float SpikeMinSpeed = 3.5f;

    /// <summary>Damage to a horse hitting the spikes is SpikeBaseDamage plus SpikeDamagePerSpeed times its speed.</summary>
    public const float SpikeBaseDamage = 20f;
    public const float SpikeDamagePerSpeed = 6f;
    public const float SpikeMaxDamage = 90f;

    /// <summary>Seconds before the same horse can be hurt by the spikes again.</summary>
    public const float SpikeCooldown = 1.5f;

    /// <summary>Metres in front of the infantry spawn line where the works go when the deployment boundary is unknown.</summary>
    public const float DistanceInFront = 35f;

    /// <summary>Closest the works may sit to the spawn line, even on a cramped map.</summary>
    public const float MinDistanceInFront = 20f;

    /// <summary>How far forward to look for the boundary edge before giving up and using the fixed distance.</summary>
    public const float MaxDistanceInFront = 150f;

    /// <summary>Metres beyond the boundary edge, so the blocked ground can never overlap a deployment slot.</summary>
    public const float BoundaryMargin = 4f;
}
