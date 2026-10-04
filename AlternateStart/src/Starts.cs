using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace AlternateStart;

/// <summary>Where a start's troops are drawn from.</summary>
internal enum TroopPool
{
    None,
    /// <summary>The culture's basic recruit line.</summary>
    Basic,
    /// <summary>The culture's noble line.</summary>
    Elite,
    BasicAndElite,
    /// <summary>The culture's mercenary troops, falling back to basic and noble lines.</summary>
    Mercenary,
    /// <summary>Looters and the region's bandits.</summary>
    Bandit,
}

/// <summary>Which vanilla outfit the player is dressed in, both in the preview and as real starting gear.</summary>
internal enum Outfit
{
    /// <summary>Whatever the earlier creation pages gave you.</summary>
    Keep,
    /// <summary>Wanderer's clothes with the weapons stripped.</summary>
    Rags,
    /// <summary>Wanderer's clothes and a cheap sword; no horse.</summary>
    Wanderer,
    /// <summary>A sellsword's kit laid over your own.</summary>
    Armed,
    /// <summary>A gang leader's leathers laid over your own.</summary>
    Gangster,
    /// <summary>Fine town clothes; battle gear unchanged.</summary>
    Merchant,
    /// <summary>A lesser noble's mail, horse and town clothes.</summary>
    Noble,
    /// <summary>A great lord's plate, warhorse and finery.</summary>
    Lord,
    /// <summary>The culture's royal regalia, crown included.</summary>
    King,
}

/// <summary>What the start does to your clan's place in the political map.</summary>
internal enum Politics
{
    None,
    Mercenary,
    Independent,
    Vassal,
    Rebel,
    Ruler,
}

/// <summary>
/// One start on the "Your Start" creation page. The defaults here are overridden per start by settings.txt
/// (<c>&lt;id&gt;.gold=...</c> and so on), so numbers shown in the creation page always match what is applied.
/// </summary>
internal sealed class StartDef
{
    public string Id = "";
    public TextObject Title = TextObject.GetEmpty();
    public TextObject Description = TextObject.GetEmpty();

    public bool Enabled = true;
    public bool NeedsRealm;
    public Politics Politics;
    public bool Noble;

    public int Gold;
    /// <summary>True when <see cref="Gold"/> replaces your purse instead of adding to it.</summary>
    public bool SetGold;
    public int Troops;
    public int MinTier = 1;
    public int MaxTier = 6;
    public TroopPool Pool;
    public int ClanTier;
    public int Influence;
    public int Companions;
    public int RulerRelation;
    public int Towns;
    public int Castles;

    public Outfit Outfit;
    public string Animation = "act_childhood_schooled";
    public string[] Skills = new string[0];

    public bool IsVanilla => Id == "adventurer";
}

internal static class Starts
{
    public static readonly List<StartDef> All = new();

    public static StartDef? Find(string? id) => id == null ? null : All.FirstOrDefault(s => s.Id == id);

    public static StartDef Adventurer => All[0];

    /// <summary>Rebuilds the catalogue with defaults, then applies settings.txt on top.</summary>
    public static void Build(StartSettings settings)
    {
        All.Clear();

        All.Add(new StartDef
        {
            Id = "adventurer",
            Title = new TextObject("{=as_adventurer_t}Adventurer"),
            Description = new TextObject("{=as_adventurer_d}The road as the game intends it: modest savings, no ties, and all of Calradia to make a name in. The realm you choose next only decides where you set out from."),
            Animation = "act_childhood_athlete",
        });
        All.Add(new StartDef
        {
            Id = "wanderer",
            Title = new TextObject("{=as_wanderer_t}Destitute Wanderer"),
            Description = new TextObject("{=as_wanderer_d}Everything you owned is gone. You walk the roads with a battered sword, the clothes on your back and a handful of coins."),
            Gold = 30, SetGold = true,
            Outfit = Outfit.Wanderer, Animation = "act_childhood_tough",
            Skills = new[] { "Scouting", "Athletics" },
        });
        All.Add(new StartDef
        {
            Id = "captive",
            Title = new TextObject("{=as_captive_t}Escaped Captive"),
            Description = new TextObject("{=as_captive_d}Slavers took you on the road. Last night you and a few other prisoners slipped your chains near their hideout. You are hurt, unarmed and penniless, but free."),
            Gold = 0, SetGold = true, Troops = 4, MinTier = 1, MaxTier = 1, Pool = TroopPool.Basic,
            Outfit = Outfit.Rags, Animation = "act_childhood_hardened",
            Skills = new[] { "Athletics", "Roguery" },
        });
        All.Add(new StartDef
        {
            Id = "militia",
            Title = new TextObject("{=as_militia_t}Militia Captain"),
            Description = new TextObject("{=as_militia_d}When raiders came, the village looked to you. Now a band of farmhands with spears follows you, and the village headmen owe you a debt."),
            Gold = 1500, Troops = 15, MinTier = 1, MaxTier = 2, Pool = TroopPool.Basic,
            Animation = "act_childhood_leader",
            Skills = new[] { "Polearm", "Leadership" },
        });
        All.Add(new StartDef
        {
            Id = "veteran",
            Title = new TextObject("{=as_veteran_t}Discharged Veteran"),
            Description = new TextObject("{=as_veteran_d}Fifteen years in the ranks have left you scarred, skilled and owed back pay. A few comrades from your old company came with you."),
            Gold = 4000, Troops = 8, MinTier = 3, MaxTier = 4, Pool = TroopPool.BasicAndElite, ClanTier = 1,
            Outfit = Outfit.Armed, Animation = "act_childhood_tough",
            Skills = new[] { "OneHanded", "Tactics" },
        });
        All.Add(new StartDef
        {
            Id = "merchant",
            Title = new TextObject("{=as_merchant_t}Merchant"),
            Description = new TextObject("{=as_merchant_d}The family trading house is yours: workshops in a busy town, a caravan on the road, pack mules laden with goods and a strongbox of silver."),
            Gold = 10000, ClanTier = 1,
            Outfit = Outfit.Merchant, Animation = "act_childhood_numbers",
            Skills = new[] { "Trade", "Steward" },
        });
        All.Add(new StartDef
        {
            Id = "mercenary",
            Title = new TextObject("{=as_mercenary_t}Mercenary Captain"),
            Description = new TextObject("{=as_mercenary_d}You lead a free company of hardened sellswords. Pick a realm and you begin under contract to it; pick your homeland and you start as free blades."),
            Gold = 5000, Troops = 25, MinTier = 2, MaxTier = 4, Pool = TroopPool.Mercenary, ClanTier = 2, Companions = 1,
            Politics = Politics.Mercenary,
            Outfit = Outfit.Armed, Animation = "act_childhood_guard_up_staff",
            Skills = new[] { "Leadership", "Tactics" },
        });
        All.Add(new StartDef
        {
            Id = "outlaw",
            Title = new TextObject("{=as_outlaw_t}Outlaw Chief"),
            Description = new TextObject("{=as_outlaw_d}You lead a band of brigands out of a hideout in the wilds. The local lords have put a price on your head, and the roads are yours to plunder."),
            Gold = 2500, Troops = 20, Pool = TroopPool.Bandit, ClanTier = 1,
            Outfit = Outfit.Gangster, Animation = "act_childhood_hardened",
            Skills = new[] { "Roguery", "Scouting" },
        });
        All.Add(new StartDef
        {
            Id = "noble",
            Title = new TextObject("{=as_noble_t}Landless Noble"),
            Description = new TextObject("{=as_noble_d}You are the younger child of a noble house: a known name, a fine horse and a household guard, but no land to call your own."),
            Gold = 10000, Troops = 12, MinTier = 3, MaxTier = 5, Pool = TroopPool.Elite, ClanTier = 2, Noble = true, RulerRelation = 10,
            Outfit = Outfit.Noble, Animation = "act_childhood_manners",
            Skills = new[] { "Riding", "Leadership" },
        });
        All.Add(new StartDef
        {
            Id = "landholder",
            Title = new TextObject("{=as_landholder_t}Independent Lord"),
            Description = new TextObject("{=as_landholder_d}You hold a castle and its villages by your own right, sworn to no crown. Your neighbours are watching to see what you will do with it."),
            NeedsRealm = true, Politics = Politics.Independent, Noble = true,
            Gold = 15000, Troops = 30, MinTier = 2, MaxTier = 4, Pool = TroopPool.BasicAndElite, ClanTier = 3, Companions = 2, Castles = 1,
            Outfit = Outfit.Lord, Animation = "act_childhood_leader",
            Skills = new[] { "Steward", "Leadership" },
        });
        All.Add(new StartDef
        {
            Id = "vassal",
            Title = new TextObject("{=as_vassal_t}Sworn Vassal"),
            Description = new TextObject("{=as_vassal_d}You have knelt before the ruler of a realm and been granted a castle for your loyalty. Your sword now answers to the crown."),
            NeedsRealm = true, Politics = Politics.Vassal, Noble = true,
            Gold = 20000, Troops = 35, MinTier = 2, MaxTier = 5, Pool = TroopPool.BasicAndElite, ClanTier = 3, Companions = 2, Castles = 1,
            Influence = 150, RulerRelation = 20,
            Outfit = Outfit.Lord, Animation = "act_childhood_manners",
            Skills = new[] { "Leadership", "Steward" },
        });
        All.Add(new StartDef
        {
            Id = "duke",
            Title = new TextObject("{=as_duke_t}Great Lord"),
            Description = new TextObject("{=as_duke_d}Your house is a pillar of the realm: a prosperous town, a castle, a voice in every council and the ear of the ruler."),
            NeedsRealm = true, Politics = Politics.Vassal, Noble = true,
            Gold = 40000, Troops = 50, MinTier = 3, MaxTier = 5, Pool = TroopPool.BasicAndElite, ClanTier = 4, Companions = 3, Towns = 1, Castles = 1,
            Influence = 300, RulerRelation = 30,
            Outfit = Outfit.Lord, Animation = "act_childhood_leader",
            Skills = new[] { "Leadership", "Steward", "Charm" },
        });
        All.Add(new StartDef
        {
            Id = "rebel",
            Title = new TextObject("{=as_rebel_t}Rebel King"),
            Description = new TextObject("{=as_rebel_d}You raised your banner against your overlord, seized a town and a castle, and proclaimed a kingdom of your own. The realm you broke from wants its land back."),
            NeedsRealm = true, Politics = Politics.Rebel, Noble = true,
            Gold = 40000, Troops = 60, MinTier = 2, MaxTier = 5, Pool = TroopPool.BasicAndElite, ClanTier = 4, Companions = 3, Towns = 1, Castles = 1,
            Influence = 200,
            Outfit = Outfit.King, Animation = "act_childhood_leader",
            Skills = new[] { "Leadership", "Tactics" },
        });
        All.Add(new StartDef
        {
            Id = "ruler",
            Title = new TextObject("{=as_ruler_t}Ruler of a Realm"),
            Description = new TextObject("{=as_ruler_d}The old ruler is dead and the crown is yours. Your vassals expect leadership, your treasury expects coin, and your neighbours expect weakness."),
            NeedsRealm = true, Politics = Politics.Ruler, Noble = true,
            Gold = 60000, Troops = 60, MinTier = 3, MaxTier = 6, Pool = TroopPool.BasicAndElite, ClanTier = 5, Companions = 3, Towns = 1,
            Influence = 400,
            Outfit = Outfit.King, Animation = "act_childhood_leader",
            Skills = new[] { "Leadership", "Steward", "Charm" },
        });

        foreach (StartDef start in All)
        {
            settings.ApplyOverrides(start);
        }

        // A page with no options cannot be left, so the vanilla start always stays available.
        Adventurer.Enabled = true;
    }

    /// <summary>A one-line list of what the start gives, shown under its description.</summary>
    public static string Summary(StartDef start, StartSettings settings)
    {
        var parts = new List<string>();
        if (start.SetGold) parts.Add(start.Gold == 0 ? "no money" : $"{start.Gold:N0} denars only");
        else if (start.Gold > 0) parts.Add($"+{start.Gold:N0} denars");
        if (start.Troops > 0) parts.Add(start.Id == "captive" ? $"{start.Troops} fellow escapees" : $"{start.Troops} troops");
        if (start.Companions > 0) parts.Add(start.Companions == 1 ? "1 companion" : $"{start.Companions} companions");
        if (start.ClanTier > 0) parts.Add($"clan tier {start.ClanTier}");
        if (start.Towns > 0) parts.Add(start.Towns == 1 ? "a town" : $"{start.Towns} towns");
        if (start.Castles > 0) parts.Add(start.Castles == 1 ? "a castle" : $"{start.Castles} castles");
        if (start.Influence > 0) parts.Add($"{start.Influence} influence");
        switch (start.Id)
        {
            case "merchant":
                if (settings.MerchantWorkshops > 0) parts.Add(settings.MerchantWorkshops == 1 ? "a workshop" : $"{settings.MerchantWorkshops} workshops");
                if (settings.MerchantCaravan) parts.Add("a caravan");
                parts.Add("trade goods");
                break;
            case "captive": parts.Add("badly wounded"); break;
            case "militia": parts.Add("village headmen's favour"); break;
            case "outlaw": parts.Add("wanted by the realm"); break;
        }
        switch (start.Politics)
        {
            case Politics.Mercenary: parts.Add("mercenary contract"); break;
            case Politics.Independent: parts.Add("sworn to no one"); break;
            case Politics.Vassal: parts.Add("vassal of the realm"); break;
            case Politics.Rebel: parts.Add("your own kingdom, at war with the realm"); break;
            case Politics.Ruler: parts.Add("the realm's crown"); break;
        }
        return parts.Count == 0 ? "" : "Starts with: " + string.Join(", ", parts) + ".";
    }
}
