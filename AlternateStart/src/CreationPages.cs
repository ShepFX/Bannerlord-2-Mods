using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterCreationContent;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AlternateStart;

/// <summary>What the player picked on the two creation pages, handed to the campaign once the map opens.</summary>
internal static class CreationChoice
{
    public static string? StartId;
    public static string? RealmId;

    public static void Reset()
    {
        StartId = null;
        RealmId = null;
    }
}

/// <summary>
/// Adds two pages to the end of the vanilla narrative stage (after Starting Age): "Your Start" lists the starts,
/// "Where Your Story Begins" lists the realms. Pages chain by id: the next page is the one whose input id is the
/// current page's id, and Back follows the input id the other way.
/// </summary>
internal sealed class CreationPages : ICharacterCreationContentHandler
{
    public const string StartMenuId = "alternate_start_menu";
    public const string RealmMenuId = "alternate_start_realm_menu";

    private readonly AlternateStartBehavior _behavior;
    private int _focusToAdd = 1;
    private int _skillLevelToAdd = 10;

    public CreationPages(AlternateStartBehavior behavior)
    {
        _behavior = behavior;
    }

    public void InitializeContent(CharacterCreationManager characterCreationManager)
    {
    }

    public void AfterInitializeContent(CharacterCreationManager characterCreationManager)
    {
        try
        {
            string? previous = FindLastMenuId(characterCreationManager);
            if (previous == null)
            {
                ErrorLog.Write("Could not find the last character creation page to attach to; the start pages were not added.");
                return;
            }

            _focusToAdd = characterCreationManager.CharacterCreationContent.FocusToAdd;
            _skillLevelToAdd = characterCreationManager.CharacterCreationContent.SkillLevelToAdd;
            characterCreationManager.AddNewMenu(BuildStartMenu(characterCreationManager, previous));
            characterCreationManager.AddNewMenu(BuildRealmMenu(characterCreationManager));
            ErrorLog.Debug($"Start pages attached after '{previous}' with {Starts.All.Count(s => s.Enabled)} starts.");
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Adding the start pages failed: " + ex);
        }
    }

    public void OnStageCompleted(CharacterCreationStageBase stage)
    {
    }

    public void OnCharacterCreationFinalize(CharacterCreationManager characterCreationManager)
    {
        _behavior.QueueStart(CreationChoice.StartId, CreationChoice.RealmId);
    }

    /// <summary>
    /// The vanilla Starting Age page when it ends the chain; otherwise whichever single page nothing follows,
    /// so the pages still attach if another mod has added its own pages at the end.
    /// </summary>
    private static string? FindLastMenuId(CharacterCreationManager manager)
    {
        var menus = manager.NarrativeMenus;
        var followed = new HashSet<string>(menus.Select(m => m.InputMenuId ?? ""));
        var last = menus.Where(m => !followed.Contains(m.StringId)).ToList();
        if (last.Any(m => m.StringId == Outfits.AgeMenuId)) return Outfits.AgeMenuId;
        return last.Count == 1 ? last[0].StringId : null;
    }

    private NarrativeMenu BuildStartMenu(CharacterCreationManager manager, string previous)
    {
        var menu = new NarrativeMenu(
            StartMenuId,
            previous,
            RealmMenuId,
            new TextObject("{=as_start_title}Your Start"),
            new TextObject("{=as_start_desc}Where does your story pick up? Choose how your life stands on the day the campaign begins."),
            new List<NarrativeMenuCharacter> { CreatePlayerCharacter(manager) },
            GetCharacterArgs);

        foreach (StartDef start in Starts.All.Where(s => s.Enabled))
        {
            StartDef captured = start;
            string summary = Starts.Summary(start, StartSettings.Current);
            string text = start.Description.ToString() + (summary.Length > 0 ? "\n\n" + summary : "");
            menu.AddNarrativeMenuOption(new NarrativeMenuOption(
                "alternate_start_" + start.Id,
                start.Title,
                new TextObject("{=!}" + text),
                args => SetSkillArgs(args, captured),
                _ => true,
                m => OnStartSelected(m, captured),
                m => OnStartChosen(m, captured)));
        }

        return menu;
    }

    private NarrativeMenu BuildRealmMenu(CharacterCreationManager manager)
    {
        var menu = new NarrativeMenu(
            RealmMenuId,
            StartMenuId,
            "",
            new TextObject("{=as_realm_title}Where Your Story Begins"),
            new TextObject("{=as_realm_desc}Choose a realm. You start in its lands, and for starts that serve, hold land in or seize a crown, this is that crown."),
            new List<NarrativeMenuCharacter> { CreatePlayerCharacter(manager) },
            GetCharacterArgs);

        menu.AddNarrativeMenuOption(new NarrativeMenuOption(
            "alternate_start_realm_home",
            new TextObject("{=as_realm_home_t}Your Homeland"),
            new TextObject("{=as_realm_home_d}Begin among your own people, where the game normally starts you. Starts that need a realm pick the one that shares your culture."),
            _ => { },
            _ => !(CurrentStart.NeedsRealm),
            _ => CreationChoice.RealmId = null,
            _ => CreationChoice.RealmId = null));

        foreach (Kingdom kingdom in Kingdom.All.Where(k => !k.IsEliminated && !k.IsMinorFaction && k.Settlements.Any(s => s.IsFortification)).OrderBy(k => k.Name.ToString()))
        {
            Kingdom captured = kingdom;
            menu.AddNarrativeMenuOption(new NarrativeMenuOption(
                "alternate_start_realm_" + kingdom.StringId,
                kingdom.Name,
                new TextObject("{=!}" + DescribeRealm(kingdom)),
                _ => { },
                _ => RealmSuits(captured, CurrentStart),
                _ => CreationChoice.RealmId = captured.StringId,
                _ => CreationChoice.RealmId = captured.StringId));
        }

        return menu;
    }

    private static StartDef CurrentStart => Starts.Find(CreationChoice.StartId) ?? Starts.Adventurer;

    /// <summary>A realm can host a start only if it has the fiefs the start hands out.</summary>
    private static bool RealmSuits(Kingdom kingdom, StartDef start)
    {
        if (start.Towns > 0 && kingdom.Settlements.Count(s => s.IsTown) < Math.Max(start.Towns, start.Politics == Politics.Rebel ? 2 : 1)) return false;
        if (start.Castles > 0 && !kingdom.Settlements.Any(s => s.IsCastle)) return false;
        return true;
    }

    private static string DescribeRealm(Kingdom kingdom)
    {
        int towns = kingdom.Settlements.Count(s => s.IsTown);
        int castles = kingdom.Settlements.Count(s => s.IsCastle);
        string ruler = kingdom.Leader != null ? $"Ruled by {kingdom.Leader.Name} of {kingdom.RulingClan?.Name}. " : "";
        string culture = kingdom.Culture != null ? $"{kingdom.Culture.Name} lands. " : "";
        return $"{ruler}{culture}{towns} towns and {castles} castles.";
    }

    private static NarrativeMenuCharacter CreatePlayerCharacter(CharacterCreationManager manager)
    {
        CharacterObject player = CharacterObject.PlayerCharacter;
        BodyProperties body = player.GetBodyProperties(player.Equipment, -1);
        body = FaceGen.GetBodyPropertiesWithAge(ref body, manager.CharacterCreationContent.StartingAge);
        return new NarrativeMenuCharacter(Outfits.PlayerCharacterId, body, player.Race, player.IsFemale);
    }

    private static List<NarrativeMenuCharacterArgs> GetCharacterArgs(CultureObject culture, string occupationType, CharacterCreationManager manager)
    {
        StartDef start = CurrentStart;
        string equipmentId = (Outfits.PreviewRoster(start, culture, CharacterObject.PlayerCharacter.IsFemale)
                              ?? Outfits.VanillaRoster(manager))?.StringId
                             ?? "player_char_creation_default";
        return new List<NarrativeMenuCharacterArgs>
        {
            new NarrativeMenuCharacterArgs(Outfits.PlayerCharacterId, manager.CharacterCreationContent.StartingAge, equipmentId,
                start.Animation, "spawnpoint_player_1", "", "", null, true, CharacterObject.PlayerCharacter.IsFemale),
        };
    }

    private void SetSkillArgs(NarrativeMenuOptionArgs args, StartDef start)
    {
        if (!StartSettings.Current.SkillBonuses || start.Skills.Length == 0) return;
        SkillObject[] skills = start.Skills
            .Select(id => MBObjectManager.Instance.GetObject<SkillObject>(id))
            .Where(s => s != null)
            .ToArray();
        if (skills.Length == 0) return;
        args.SetAffectedSkills(skills);
        args.SetFocusToSkills(_focusToAdd);
        args.SetLevelToSkills(_skillLevelToAdd);
    }

    /// <summary>Clicking a start re-dresses the preview character in that start's outfit.</summary>
    private static void OnStartSelected(CharacterCreationManager manager, StartDef start)
    {
        CreationChoice.StartId = start.Id;
        CultureObject culture = manager.CharacterCreationContent.SelectedCulture ?? CharacterObject.PlayerCharacter.Culture;
        MBEquipmentRoster? roster = Outfits.PreviewRoster(start, culture, CharacterObject.PlayerCharacter.IsFemale)
                                    ?? Outfits.VanillaRoster(manager);
        foreach (NarrativeMenuCharacter character in manager.CurrentMenu.Characters)
        {
            if (character.StringId != Outfits.PlayerCharacterId) continue;
            if (roster != null) character.SetEquipment(roster);
            character.SetAnimationId(start.Animation);
        }
    }

    private static void OnStartChosen(CharacterCreationManager manager, StartDef start)
    {
        CreationChoice.StartId = start.Id;
        try
        {
            Outfits.ApplyToPlayer(start, manager);
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"Dressing the player for '{start.Id}' failed: {ex}");
        }
    }
}
