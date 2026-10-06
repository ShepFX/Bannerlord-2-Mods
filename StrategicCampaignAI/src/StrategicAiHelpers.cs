using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace StrategicCampaignAI;

internal static class StrategicAiHelpers
{
    public static bool IsFortification(Settlement? settlement)
    {
        return settlement != null && (settlement.IsTown || settlement.IsCastle);
    }

    /// <summary>
    /// True when the two factions are not shooting at each other. Neutral counts
    /// as friendly here, so this is only the right question for "will they leave
    /// us alone", never for "is this our territory" -- use
    /// <see cref="IsOwnTerritory"/> for that.
    /// </summary>
    public static bool IsFriendly(IFaction? observer, IFaction? other)
    {
        if (observer == null || other == null)
        {
            return false;
        }

        if (observer == other)
        {
            return true;
        }

        return !FactionManager.IsAtWarAgainstFaction(observer, other);
    }

    public static bool IsEnemy(IFaction? observer, IFaction? other)
    {
        if (observer == null || other == null)
        {
            return false;
        }

        return observer != other && FactionManager.IsAtWarAgainstFaction(observer, other);
    }

    /// <summary>
    /// A war that matters strategically: kingdom against kingdom.
    ///
    /// Every kingdom is permanently at war with the minor factions and bandit
    /// clans, so counting raw FactionsAtWarWith gives 9-12 "wars" for everyone
    /// and sums every looter clan into the enemy strength total. That made every
    /// kingdom permanently war-exhausted, which pinned them all to the
    /// peace-seeking goal and stopped any army ever being assigned to attack.
    /// </summary>
    public static bool IsMajorWarFaction(IFaction? faction)
    {
        return faction is Kingdom kingdom &&
               !kingdom.IsEliminated &&
               !kingdom.IsMinorFaction &&
               !kingdom.IsBanditFaction;
    }

    /// <summary>Strength of nearby hostile lords belonging to rival kingdoms only.</summary>
    public static float NearbyMajorEnemyLordStrength(Settlement settlement, IFaction faction, float radius)
    {
        return StrategicAiCache.NearbyEnemyLordStrength(settlement, faction, radius, majorOnly: true);
    }

    /// <summary>
    /// True when the settlement actually belongs to this faction. Supply bases,
    /// frontlines and retreat destinations must all use this rather than
    /// <see cref="IsFriendly"/>: a neutral third party's castle is not our base,
    /// and treating it as one made unrelated enemy towns score as "frontline".
    /// </summary>
    public static bool IsOwnTerritory(IFaction? faction, Settlement? settlement)
    {
        return faction != null && settlement?.MapFaction == faction;
    }

    /// <summary>Parties the mod must never issue orders to or take troops from.</summary>
    public static bool IsPlayerControlled(MobileParty? party)
    {
        if (party == null)
        {
            return false;
        }

        if (party.IsMainParty)
        {
            return true;
        }

        if (party.LeaderHero != null && party.LeaderHero.Clan == Clan.PlayerClan)
        {
            return true;
        }

        Army? army = party.Army;
        return army?.LeaderParty != null && army.LeaderParty.IsMainParty;
    }

    public static float Distance(Settlement settlement, Settlement other)
    {
        return settlement.GetPosition2D.Distance(other.GetPosition2D);
    }

    public static float Distance(MobileParty party, Settlement settlement)
    {
        return party.GetPosition2D.Distance(settlement.GetPosition2D);
    }

    public static float Distance(MobileParty party, MobileParty other)
    {
        return party.GetPosition2D.Distance(other.GetPosition2D);
    }

    public static bool HasOwnFortificationNear(Settlement target, IFaction faction, float radius)
    {
        return StrategicAiCache.HasFriendlyFortificationNear(target, faction, radius);
    }

    public static bool HasEnemyFortificationNear(Settlement target, IFaction faction, float radius)
    {
        return StrategicAiCache.HasEnemyFortificationNear(target, faction, radius);
    }

    /// <summary>An enemy fortification that sits within reach of one of our own.</summary>
    public static bool IsEnemyFrontlineTarget(Settlement target, IFaction faction)
    {
        return IsFortification(target) &&
               IsEnemy(faction, target.MapFaction) &&
               HasOwnFortificationNear(target, faction, StrategicAiTuning.FrontlineScanRadius);
    }

    public static bool IsCapital(Settlement settlement, IFaction? faction)
    {
        return faction is Kingdom kingdom && kingdom.InitialHomeSettlement == settlement;
    }

    public static bool IsHighValueTown(Settlement settlement)
    {
        return settlement.IsTown &&
               settlement.Town != null &&
               settlement.Town.Prosperity >= StrategicAiTuning.HighProsperityThreshold;
    }

    public static bool IsLowGarrisonFortification(Settlement settlement)
    {
        return IsFortification(settlement) &&
               settlement.Party != null &&
               settlement.Party.EstimatedStrength <= StrategicAiTuning.LowGarrisonStrength;
    }

    public static bool IsChokepoint(Settlement target)
    {
        return IsFortification(target) && StrategicAiCache.IsChokepoint(target);
    }

    public static StrategicLordPersonality GetPersonality(Hero? hero)
    {
        if (hero == null)
        {
            return StrategicLordPersonality.Balanced;
        }

        if (hero.GetTraitLevel(DefaultTraits.Valor) >= 1)
        {
            return StrategicLordPersonality.Aggressive;
        }

        if (hero.GetTraitLevel(DefaultTraits.Calculating) >= 1)
        {
            return StrategicLordPersonality.Cautious;
        }

        if (hero.GetTraitLevel(DefaultTraits.Generosity) <= -1)
        {
            return StrategicLordPersonality.Greedy;
        }

        if (hero.GetTraitLevel(DefaultTraits.Honor) >= 1 || hero.GetTraitLevel(DefaultTraits.Mercy) >= 1)
        {
            return StrategicLordPersonality.Honorable;
        }

        return StrategicLordPersonality.Balanced;
    }

    public static float GetPersonalityOffenseMultiplier(Hero? hero)
    {
        return GetPersonality(hero) switch
        {
            StrategicLordPersonality.Aggressive => 1.25f,
            StrategicLordPersonality.Cautious => 0.85f,
            StrategicLordPersonality.Greedy => 1.08f,
            StrategicLordPersonality.Honorable => 0.95f,
            _ => 1f
        };
    }

    public static float GetPersonalityDefenseMultiplier(Hero? hero)
    {
        return GetPersonality(hero) switch
        {
            StrategicLordPersonality.Cautious => 1.25f,
            StrategicLordPersonality.Honorable => 1.15f,
            StrategicLordPersonality.Aggressive => 0.9f,
            _ => 1f
        };
    }

    public static float GetEconomicValue(Settlement settlement)
    {
        return StrategicAiCache.GetEconomicValue(settlement);
    }

    public static bool HasCulturalClaim(Kingdom kingdom, Settlement settlement)
    {
        return (settlement.Culture != null && settlement.Culture == kingdom.Culture) ||
               StrategicAiState.GetLostClaim(kingdom) == settlement;
    }

    public static bool IsMercenaryLed(MobileParty? party)
    {
        Clan? clan = party?.ActualClan;
        return clan != null && (clan.IsClanTypeMercenary || clan.IsUnderMercenaryService);
    }

    public static bool IsMinorOrRebelFaction(IFaction? faction)
    {
        return faction != null && (faction.IsMinorFaction || faction.IsRebelClan);
    }

    public static bool HasFriendlyNoblePrisoners(Settlement settlement, IFaction faction)
    {
        PartyBase? party = settlement.Party;
        if (party == null)
        {
            return false;
        }

        var prisoners = party.PrisonerHeroes;
        if (prisoners == null)
        {
            return false;
        }

        foreach (var character in prisoners)
        {
            Hero? hero = character.HeroObject;
            if (hero != null && hero.IsLord && IsFriendly(faction, hero.MapFaction))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsFactionLeadershipImprisoned(Kingdom kingdom)
    {
        return kingdom.Leader != null && kingdom.Leader.IsPrisoner;
    }

    public static float GetWeatherSeasonTargetMultiplier(Settlement target, IFaction faction)
    {
        return StrategicAiCache.GetWeatherSeasonMultiplier(target, faction);
    }

    /// <summary>
    /// Uncached implementation. Only <see cref="StrategicAiCache"/> should call
    /// this -- the enum-to-string comparisons are far too expensive to run on
    /// the raw scoring path.
    /// </summary>
    public static float ComputeWeatherSeasonTargetMultiplier(Settlement target, IFaction faction)
    {
        float multiplier = 1f;

        try
        {
            // Season comes straight off CampaignTime and is a pure read.
            string season = CampaignTime.Now.GetSeasonOfYear.ToString();
            if (season.IndexOf("Winter", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (faction.Culture == null || target.Culture != faction.Culture))
            {
                multiplier *= StrategicAiTuning.WinterCampaignMultiplier;
            }

            if (!StrategicAiTuning.EnableWeatherEffects)
            {
                return multiplier;
            }

            var weatherModel = Campaign.Current?.Models?.MapWeatherModel;
            if (weatherModel != null)
            {
                // GetWeatherEventInPosition is a plain getter. The previous code
                // called UpdateWeatherForPosition, which by its name and the
                // presence of InitializeCaches on the same model mutates the
                // engine's cached weather state -- and we were driving it a few
                // thousand times an hour from the AI scoring path with arbitrary
                // settlement positions. Never call an engine "Update" from a
                // read-only query.
                string weather = weatherModel.GetWeatherEventInPosition(target.GetPosition2D).ToString();

                if (weather.IndexOf("Blizzard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    weather.IndexOf("Storm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    weather.IndexOf("Snow", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    multiplier *= StrategicAiTuning.SevereWeatherTargetMultiplier;
                }
            }
        }
        catch (Exception)
        {
            return 1f;
        }

        return multiplier;
    }

    public static float NearbyEnemyLordStrength(Settlement settlement, IFaction faction, float radius)
    {
        return StrategicAiCache.NearbyEnemyLordStrength(settlement, faction, radius);
    }

    /// <summary>
    /// Strength of hostile lords who could actually relieve a siege of this
    /// settlement.
    ///
    /// Deliberately not the cached sweep. That one sums every hostile lord party
    /// within the radius, and lords sheltering inside the settlement under siege
    /// sit at distance zero -- so two or three of them were enough to push the
    /// relief total past SiegeAbandonReliefRatio the moment the siege camp went
    /// up, and the siege was judged hopeless before it had begun. Troops already
    /// inside the walls are what the siege is against; they are not a relief
    /// force. Parties locked in a battle here are likewise already committed.
    ///
    /// Only ever called for settlements currently under siege, so this runs a
    /// handful of times an hour rather than from the scoring hot path, and does
    /// not need memoising.
    /// </summary>
    public static float SiegeReliefStrength(Settlement besieged, IFaction besiegerFaction, float radius)
    {
        float total = 0f;

        foreach (MobileParty party in StrategicAiCache.GetActiveLordParties())
        {
            // Rival kingdoms only. Minor-faction lords wandering past are not a
            // relief force; counting them was what made armies walk away from
            // sieges they were winning.
            if (!IsEnemy(besiegerFaction, party.MapFaction) ||
                !IsMajorWarFaction(party.MapFaction) ||
                party.CurrentSettlement == besieged ||
                party.BesiegedSettlement == besieged ||
                party.MapEvent != null ||
                party.SiegeEvent != null)
            {
                continue;
            }

            if (Distance(party, besieged) <= radius)
            {
                total += party.GetTotalLandStrengthWithFollowers(false);
            }
        }

        return total;
    }

    public static float PredictedEnemyThreatStrength(Settlement settlement, IFaction faction, float radius)
    {
        float total = 0f;

        foreach (MobileParty party in StrategicAiCache.GetActiveLordParties())
        {
            if (!IsEnemy(faction, party.MapFaction) || Distance(party, settlement) > radius)
            {
                continue;
            }

            if (party.TargetSettlement == settlement ||
                party.ShortTermTargetSettlement == settlement ||
                party.AiBehaviorTarget.Distance(settlement.Position) <= StrategicAiTuning.FrontlineScanRadius)
            {
                total += party.GetTotalLandStrengthWithFollowers(false);
            }
        }

        return total;
    }

    public static Settlement? FindBestDefensiveSettlement(Kingdom kingdom)
    {
        Settlement? bestSettlement = null;
        float bestScore = 0f;

        foreach (Settlement settlement in kingdom.Settlements)
        {
            if (!IsFortification(settlement))
            {
                continue;
            }

            float enemyStrength = NearbyEnemyLordStrength(settlement, kingdom, StrategicAiTuning.HomelandDefenseThreatRadius) +
                                  PredictedEnemyThreatStrength(settlement, kingdom, StrategicAiTuning.HomelandDefenseThreatRadius * 1.35f);
            float localDefense = settlement.Party?.EstimatedStrength ?? 0f;

            if (enemyStrength <= 1f && !settlement.IsUnderSiege && !IsLowGarrisonFortification(settlement))
            {
                continue;
            }

            float score = enemyStrength - localDefense * 0.35f;
            if (settlement.IsUnderSiege)
            {
                score += 900f;
            }

            if (IsCapital(settlement, kingdom))
            {
                score *= StrategicAiTuning.CapitalDefenseMultiplier;
            }

            if (IsHighValueTown(settlement))
            {
                score *= StrategicAiTuning.HighProsperityDefenseMultiplier;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestSettlement = settlement;
            }
        }

        return bestSettlement;
    }

    public static Settlement? FindBestAggressiveTarget(Kingdom kingdom, Army army)
    {
        MobileParty? leader = army.LeaderParty;
        if (leader == null)
        {
            return null;
        }

        Settlement? bestSettlement = null;
        float bestScore = 0f;
        StrategicFactionStatus status = StrategicAiState.GetFactionStatus(kingdom);
        Settlement? currentLock = StrategicAiState.GetTargetLock(army);

        foreach (Settlement settlement in StrategicAiCache.GetFortifications())
        {
            if (!IsEnemy(kingdom, settlement.MapFaction))
            {
                continue;
            }

            bool isFrontline = IsEnemyFrontlineTarget(settlement, kingdom);
            float distanceToOwnBase = StrategicAiCache.DistanceToNearestFriendlyFortification(settlement, kingdom);

            if (StrategicAiState.IsTargetOnCooldown(kingdom, settlement) ||
                (!isFrontline && distanceToOwnBase > StrategicAiTuning.OverextendedTargetDistance * 1.65f) ||
                (IsMinorOrRebelFaction(settlement.MapFaction) && !settlement.IsTown && GetEconomicValue(settlement) < 500f))
            {
                continue;
            }

            float distance = Distance(leader, settlement);
            float defense = settlement.Party?.EstimatedStrength ?? 0f;
            float relief = NearbyEnemyLordStrength(settlement, kingdom, StrategicAiTuning.SiegeRadarRadius);

            float score = 10000f / MathF.Max(20f, distance + 20f);
            score += settlement.IsTown ? 350f : 160f;
            score += GetEconomicValue(settlement);
            score -= defense * 0.25f;
            score -= relief * 0.18f;

            if (score <= 0f)
            {
                continue;
            }

            if (!isFrontline)
            {
                score *= StrategicAiTuning.OverextendedTargetMultiplier;
            }

            if (IsChokepoint(settlement))
            {
                score *= StrategicAiTuning.ChokepointTargetMultiplier;
            }

            if (HasCulturalClaim(kingdom, settlement))
            {
                score *= StrategicAiTuning.ClaimTargetMultiplier;
            }

            if (status.WarGoal == StrategicWarGoal.WeakenEconomy && settlement.IsTown)
            {
                score *= StrategicAiTuning.EconomicTargetMultiplier;
            }

            if (status.WarGoal == StrategicWarGoal.ReclaimLostFief && StrategicAiState.GetLostClaim(kingdom) == settlement)
            {
                score *= StrategicAiTuning.ClaimTargetMultiplier;
            }

            if (status.WarGoal == StrategicWarGoal.ForcePeace)
            {
                score *= StrategicAiTuning.PeacePressureOffenseMultiplier;
            }

            if (HasFriendlyNoblePrisoners(settlement, kingdom))
            {
                score *= StrategicAiTuning.NoblePrisonerReliefMultiplier;
            }

            if (IsMercenaryLed(leader))
            {
                score *= StrategicAiTuning.MercenaryEconomicTargetMultiplier;
            }

            // Another army already committed here: prefer to spread out, but do
            // not zero the option -- concentrating two armies on one capital is
            // sometimes correct.
            if (StrategicAiState.IsTargetLockedByAnotherArmy(army, settlement))
            {
                score *= StrategicAiTuning.DuplicateTargetPenalty;
            }

            // Hysteresis: hold the objective we already picked unless something
            // is clearly better. This is what stops armies reversing direction.
            if (currentLock == settlement)
            {
                score *= StrategicAiTuning.CurrentObjectiveStickinessMultiplier;
            }

            score *= GetWeatherSeasonTargetMultiplier(settlement, kingdom);
            score *= GetPersonalityOffenseMultiplier(leader.LeaderHero);

            if (score > bestScore)
            {
                bestScore = score;
                bestSettlement = settlement;
            }
        }

        return bestSettlement;
    }

    public static Settlement? FindBestStagingSettlement(Kingdom kingdom, Settlement target)
    {
        Settlement? best = null;
        float bestDistance = float.MaxValue;

        foreach (Settlement settlement in kingdom.Settlements)
        {
            if (!IsFortification(settlement))
            {
                continue;
            }

            float distance = Distance(settlement, target);
            if (distance <= StrategicAiTuning.StagingTargetDistance && distance < bestDistance)
            {
                bestDistance = distance;
                best = settlement;
            }
        }

        return best;
    }

    public static MobileParty? FindEnemyArmyToShadow(Kingdom kingdom, MobileParty leader)
    {
        MobileParty? best = null;
        float bestStrength = 0f;

        foreach (MobileParty party in StrategicAiCache.GetActiveLordParties())
        {
            Army? army = party.Army;
            if (army == null || army.LeaderParty != party || !IsEnemy(kingdom, party.MapFaction))
            {
                continue;
            }

            float distance = Distance(leader, party);
            if (distance > StrategicAiTuning.ShadowMaxDistance || distance < StrategicAiTuning.ShadowMinDistance)
            {
                continue;
            }

            float strength = army.EstimatedStrength;
            if (strength > bestStrength)
            {
                bestStrength = strength;
                best = party;
            }
        }

        return best;
    }

    public static MobileParty? FindBestInterceptorTarget(Kingdom kingdom, MobileParty leader)
    {
        MobileParty? best = null;
        float bestDistance = float.MaxValue;
        float ourStrength = leader.GetTotalLandStrengthWithFollowers(true);

        foreach (MobileParty party in StrategicAiCache.GetActiveLordParties())
        {
            if (!IsEnemy(kingdom, party.MapFaction))
            {
                continue;
            }

            float distance = Distance(leader, party);
            if (distance > StrategicAiTuning.InterceptorRadius || distance >= bestDistance)
            {
                continue;
            }

            if (party.GetTotalLandStrengthWithFollowers(false) > ourStrength * 0.9f)
            {
                continue;
            }

            bestDistance = distance;
            best = party;
        }

        return best;
    }

    public static Settlement? FindRecentFriendlyCapture(Kingdom kingdom)
    {
        return kingdom.Settlements
            .Where(settlement => IsFortification(settlement) && StrategicAiState.WasRecentlyCaptured(settlement))
            .OrderBy(settlement => settlement.Party?.EstimatedStrength ?? float.MaxValue)
            .FirstOrDefault();
    }

    public static float DistanceToNearestFriendlyFortification(Settlement target, IFaction faction)
    {
        return StrategicAiCache.DistanceToNearestFriendlyFortification(target, faction);
    }

    public static Settlement? FindNearestFriendlyFortification(MobileParty party)
    {
        Settlement? bestSettlement = null;
        float bestDistance = float.MaxValue;
        IFaction? faction = party.MapFaction;

        foreach (Settlement settlement in StrategicAiCache.GetFortifications())
        {
            if (!IsOwnTerritory(faction, settlement))
            {
                continue;
            }

            float distance = Distance(party, settlement);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestSettlement = settlement;
            }
        }

        return bestSettlement;
    }

    public static bool IsDeepInEnemyTerritory(Army army)
    {
        MobileParty? leader = army.LeaderParty;
        if (leader == null || leader.MapFaction == null)
        {
            return false;
        }

        Settlement? nearestOwn = FindNearestFriendlyFortification(leader);
        if (nearestOwn == null || Distance(leader, nearestOwn) < StrategicAiTuning.DeepTargetFriendlyRadius)
        {
            return false;
        }

        foreach (Settlement settlement in StrategicAiCache.GetFortifications())
        {
            if (IsEnemy(leader.MapFaction, settlement.MapFaction) &&
                Distance(leader, settlement) <= StrategicAiTuning.FrontlineScanRadius)
            {
                return true;
            }
        }

        return false;
    }

    public static int GetAllowedArmyCount(Kingdom kingdom)
    {
        return kingdom.CurrentTotalStrength >= StrategicAiTuning.StrongKingdomStrength
            ? StrategicAiTuning.MaxArmiesPerStrongKingdom
            : StrategicAiTuning.MaxArmiesPerKingdom;
    }

    public static bool IsRecoveredEnoughToLeadArmy(MobileParty? party)
    {
        if (party?.MemberRoster == null)
        {
            return false;
        }

        return party.MemberRoster.TotalHealthyCount >= StrategicAiTuning.MinimumArmyLeaderHealthyTroops &&
               party.Party.EstimatedStrength >= StrategicAiTuning.MinimumArmyLeaderStrength &&
               !StrategicAiState.IsRecentlyRespawnedWeakParty(party);
    }

    public static bool IsRecoveredEnoughToJoinArmy(MobileParty? party)
    {
        if (party?.MemberRoster == null)
        {
            return false;
        }

        return party.MemberRoster.TotalHealthyCount >= StrategicAiTuning.MinimumArmyMemberHealthyTroops &&
               party.Party.EstimatedStrength >= StrategicAiTuning.MinimumArmyMemberStrength &&
               !StrategicAiState.IsRecentlyRespawnedWeakParty(party);
    }

}
