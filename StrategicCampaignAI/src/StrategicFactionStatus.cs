using TaleWorlds.Library;

namespace StrategicCampaignAI;

internal sealed class StrategicFactionStatus
{
    public float OwnedStrength { get; set; }
    public float EnemyStrength { get; set; }
    public int OwnedFortifications { get; set; }
    public int ThreatenedFortifications { get; set; }
    public int ActiveWars { get; set; }
    public int RaidedVillages { get; set; }
    public int OwnedVillages { get; set; }
    public StrategicWarGoal WarGoal { get; set; } = StrategicWarGoal.BorderWar;
    public bool WantsPeace { get; set; }

    /// <summary>
    /// A faction that should be suing for peace rather than pressing an attack.
    ///
    /// The fortification test previously read
    /// <c>ThreatenedFortifications &gt;= OwnedFortifications / 3</c> using integer
    /// division, so any kingdom holding fewer than three fortifications compared
    /// against zero and was permanently exhausted -- which pinned it to the
    /// ForcePeace war goal and suppressed all of its offensive scoring. Small and
    /// newly formed kingdoms could therefore never go on the attack at all.
    /// </summary>
    public bool IsExhausted
    {
        get
        {
            if (ActiveWars <= 0)
            {
                return false;
            }

            bool outmatched = EnemyStrength > OwnedStrength * StrategicAiTuning.ExhaustionStrengthRatio;

            bool overrun = ThreatenedFortifications > 0 &&
                           ThreatenedFortifications >= MathF.Max(
                               StrategicAiTuning.ExhaustionMinThreatenedFortifications,
                               OwnedFortifications * StrategicAiTuning.ExhaustionThreatenedFraction);

            bool raidedOut = RaidedVillages > 0 &&
                             RaidedVillages >= MathF.Max(
                                 (float)StrategicAiTuning.ExhaustionRaidedVillages,
                                 OwnedVillages * StrategicAiTuning.ExhaustionRaidedFraction);

            return outmatched || overrun || raidedOut;
        }
    }
}
