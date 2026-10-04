using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterCreationContent;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace AlternateStart;

/// <summary>
/// Maps a start's <see cref="Outfit"/> to vanilla equipment rosters (SandBoxCore's lord, king, wanderer and gang
/// templates) and dresses the player in them. The same roster drives the creation-page preview, so what you see
/// is what you start with.
/// </summary>
internal static class Outfits
{
    public const string AgeMenuId = "narrative_age_selection_menu";

    /// <summary>The vanilla id of the player's preview character; vanilla refreshes its face after face editing.</summary>
    public const string PlayerCharacterId = "player_age_selection_character";

    private static string Code(string culture) => culture switch
    {
        "aserai" => "ase",
        "battania" => "bat",
        "empire" => "emp",
        "khuzait" => "khu",
        "sturgia" => "stu",
        _ => "vla",
    };

    private static IEnumerable<string> BattleIds(Outfit outfit, string culture, bool female)
    {
        string c = Code(culture);
        string mf = female ? "f" : "m";
        string king = c == "emp" ? "n_emp" : c;
        switch (outfit)
        {
            case Outfit.Rags:
            case Outfit.Wanderer:
                yield return $"npc_wanderer_equipment_template_{culture}";
                yield return "npc_wanderer_equipment_template_vlandia";
                break;
            case Outfit.Armed:
                yield return $"player_char_creation_{culture}_mercenary_{mf}";
                yield return $"npc_armed_wanderer_equipment_template_{culture}";
                yield return "npc_armed_wanderer_equipment_template_vlandia";
                break;
            case Outfit.Gangster:
                yield return "civillian_template_gangster_tier3_empire";
                break;
            case Outfit.Noble:
                yield return female ? $"{c}_bat_template_lady" : $"{c}_bat_template_medium";
                yield return female ? "vla_bat_template_lady" : "vla_bat_template_medium";
                break;
            case Outfit.Lord:
                yield return female ? $"{c}_bat_template_lady" : $"{c}_bat_template_heavy";
                yield return female ? "vla_bat_template_lady" : "vla_bat_template_heavy";
                break;
            case Outfit.King:
                yield return $"{king}_king_template_bat_{mf}";
                yield return $"vla_king_template_bat_{mf}";
                break;
        }
    }

    private static IEnumerable<string> CivilianIds(Outfit outfit, string culture, bool female)
    {
        string c = Code(culture);
        string mf = female ? "f" : "m";
        string king = c == "emp" ? "n_emp" : c;
        switch (outfit)
        {
            case Outfit.Rags:
            case Outfit.Wanderer:
                yield return $"npc_wanderer_equipment_template_{culture}_civilian";
                break;
            case Outfit.Merchant:
            case Outfit.Lord:
                yield return female ? $"{c}_civ_template_lady_normal" : $"{c}_civ_template_flamboyant";
                break;
            case Outfit.Noble:
                yield return female ? $"{c}_civ_template_lady_normal" : $"{c}_civ_template_default";
                break;
            case Outfit.King:
                yield return $"{king}_king_template_civ_{mf}";
                break;
        }
    }

    private static MBEquipmentRoster? First(IEnumerable<string> ids)
    {
        foreach (string id in ids)
        {
            MBEquipmentRoster? roster = MBObjectManager.Instance.GetObject<MBEquipmentRoster>(id);
            if (roster != null && roster.AllEquipments.Count > 0) return roster;
        }
        return null;
    }

    /// <summary>The roster shown on the creation pages for a start; merchants are shown in their town clothes.</summary>
    public static MBEquipmentRoster? PreviewRoster(StartDef start, CultureObject culture, bool female) =>
        start.Outfit == Outfit.Merchant
            ? First(CivilianIds(start.Outfit, culture.StringId, female))
            : First(BattleIds(start.Outfit, culture.StringId, female));

    /// <summary>The outfit the earlier pages chose, as shown on the vanilla Starting Age page.</summary>
    public static MBEquipmentRoster? VanillaRoster(CharacterCreationManager manager) =>
        manager.GetNarrativeMenuWithId(AgeMenuId)?.Characters.FirstOrDefault(c => c.StringId == PlayerCharacterId)?.Equipment;

    /// <summary>
    /// Dresses the player for the start. The vanilla outfit is restored first, so going back and picking a
    /// different start never leaves an earlier start's gear behind.
    /// </summary>
    public static void ApplyToPlayer(StartDef start, CharacterCreationManager manager)
    {
        CharacterObject player = CharacterObject.PlayerCharacter;
        Equipment battle = player.Equipment;
        Equipment civilian = player.FirstCivilianEquipment;

        MBEquipmentRoster? vanilla = VanillaRoster(manager);
        if (vanilla != null)
        {
            battle.FillFrom(vanilla.DefaultEquipment, true);
            Equipment? vanillaCivilian = vanilla.GetRandomCivilianEquipment();
            if (vanillaCivilian != null) civilian.FillFrom(vanillaCivilian, true);
        }

        if (start.Outfit == Outfit.Keep) return;

        string culture = manager.CharacterCreationContent.SelectedCulture?.StringId ?? player.Culture.StringId;
        bool replace = start.Outfit is Outfit.Rags or Outfit.Wanderer;

        MBEquipmentRoster? battleRoster = First(BattleIds(start.Outfit, culture, player.IsFemale));
        if (battleRoster != null) Dress(battle, battleRoster.DefaultEquipment, replace);
        if (start.Outfit == Outfit.Rags)
        {
            for (int i = (int)EquipmentIndex.Weapon0; i <= (int)EquipmentIndex.Weapon3; i++)
            {
                battle[(EquipmentIndex)i] = EquipmentElement.Invalid;
            }
        }

        MBEquipmentRoster? civilianRoster = First(CivilianIds(start.Outfit, culture, player.IsFemale));
        if (civilianRoster != null)
        {
            Equipment source = civilianRoster.GetRandomCivilianEquipment() ?? civilianRoster.DefaultEquipment;
            Dress(civilian, source, replace);
        }
    }

    /// <summary>Copies a roster's items onto an equipment set; banner slot untouched.</summary>
    private static void Dress(Equipment target, Equipment source, bool replace)
    {
        for (int i = 0; i < (int)EquipmentIndex.NumEquipmentSetSlots; i++)
        {
            var index = (EquipmentIndex)i;
            if (index == EquipmentIndex.ExtraWeaponSlot) continue;
            EquipmentElement item = source[index];
            if (replace || !item.IsEmpty) target[index] = item;
        }
    }
}
