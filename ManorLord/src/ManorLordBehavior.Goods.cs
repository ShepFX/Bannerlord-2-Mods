using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace ManorLord;

/// <summary>
/// Real produce. The estate's daily output is made of concrete goods — grain and dairy from mixed farming, a
/// culture's cash crop, meat, hides and wool from livestock, grain from the mill, fruit from the orchard, tools from
/// the workshop. The steward either sells it (the treasury income, as before) or, with an intact storehouse, stores
/// it as goods, worth a quarter more than selling locally since no middleman takes a cut.
/// </summary>
public sealed partial class ManorLordBehavior
{
    private const float StoredGoodsBonus = 1.25f;

    private bool _storeProduce;
    private int _lastStoredValue;

    private void SyncGoods(IDataStore store)
    {
        store.SyncData("ml_store_produce", ref _storeProduce);
        store.SyncData("ml_last_stored_value", ref _lastStoredValue);
    }

    /// <summary>The produce is stored only while the storehouse can hold it; a damaged one falls back to selling.</summary>
    private bool StoringProduce => _storeProduce && _storehouse && !_damaged;

    private void AddGoodsMenus(CampaignGameStarter starter)
    {
        AddMenuOption(starter, StewardshipMenuId, "ml_produce_sell", "{=ml_opt_produce_sell}Sell the estate's produce for income",
            a => ProducePolicyCondition(a, store: false), a => SetProducePolicy(false));
        AddMenuOption(starter, StewardshipMenuId, "ml_produce_store", "{=ml_opt_produce_store}Store the estate's produce in the storehouse",
            a => ProducePolicyCondition(a, store: true), a => SetProducePolicy(true));
    }

    private bool ProducePolicyCondition(MenuCallbackArgs args, bool store)
    {
        if (!_hasSteward || !AtOwnedManor(args) || _storeProduce == store) return false;
        args.IsEnabled = !store || (_storehouse && !_damaged);
        if (!args.IsEnabled) args.Tooltip = new TextObject("{=ml_tip_produce_store}Requires an intact storehouse.");
        return true;
    }

    private void SetProducePolicy(bool store)
    {
        _storeProduce = store;
        InformationManager.DisplayMessage(new InformationMessage((store
            ? new TextObject("{=ml_msg_produce_store}The steward will store the estate's produce in the storehouse.")
            : new TextObject("{=ml_msg_produce_sell}The steward will sell the estate's produce.")).ToString(), Colors.Green));
        GameMenu.SwitchToMenu(StewardshipMenuId);
    }

    /// <summary>One source of produce: the good it yields and how many denars of it a day.</summary>
    private readonly struct ProduceLine
    {
        public readonly ItemObject? Item;
        public readonly int Value;
        public ProduceLine(ItemObject? item, int value) { Item = item; Value = value; }
    }

    /// <summary>
    /// The estate's daily output by source. Its total value is the daily income figure the menus show, so the two
    /// policies are comparable: sell for that many denars, or store goods worth a quarter more.
    /// </summary>
    private List<ProduceLine> ProduceLines()
    {
        var lines = new List<ProduceLine>();
        int baseIncome = Math.Min(150, 30 + (int)(ManorSettlement?.Village?.Hearth ?? 0f) / 20);
        switch (_productionFocus ?? "mixed")
        {
            case "crops":
                lines.Add(new ProduceLine(CashCrop(), baseIncome * 130 / 100));
                break;
            case "livestock":
                int livestock = baseIncome * 85 / 100;
                lines.Add(new ProduceLine(DefaultItems.Meat, livestock * 40 / 100));
                lines.Add(new ProduceLine(DefaultItems.Hides, livestock * 35 / 100));
                lines.Add(new ProduceLine(Good("wool"), livestock - livestock * 40 / 100 - livestock * 35 / 100));
                break;
            default:
                lines.Add(new ProduceLine(DefaultItems.Grain, baseIncome / 2));
                lines.Add(new ProduceLine(Good("cheese"), baseIncome / 4));
                lines.Add(new ProduceLine(Good("butter"), baseIncome - baseIncome / 2 - baseIncome / 4));
                break;
        }
        if (_mill) lines.Add(new ProduceLine(DefaultItems.Grain, 25));
        if (_orchard) lines.Add(new ProduceLine(Fruit(), 15));
        if (_workshop) lines.Add(new ProduceLine(DefaultItems.Tools, 40));
        return lines;
    }

    private static ItemObject? Good(string id) => MBObjectManager.Instance.GetObject<ItemObject>(id);

    /// <summary>The village's cash crop, by culture: olives in the Empire, dates among the Aserai, grapes in Vlandia.</summary>
    private ItemObject? CashCrop() => (ManorSettlement?.Culture?.StringId ?? string.Empty) switch
    {
        "empire" => Good("olives"),
        "aserai" => Good("date_fruit"),
        "vlandia" => Good("grape"),
        "khuzait" => Good("wool"),
        _ => Good("flax")
    };

    private ItemObject? Fruit() => (ManorSettlement?.Culture?.StringId ?? string.Empty) switch
    {
        "aserai" => Good("date_fruit"),
        "empire" => Good("olives"),
        _ => Good("grape")
    };

    /// <summary>
    /// Puts a day's produce in the storehouse. Each good's quantity is its share of the value divided by its price,
    /// with the fractional remainder settled by chance so long-run output matches the value exactly. Returns the
    /// value stored.
    /// </summary>
    private int StoreDailyProduce()
    {
        _stash ??= new ItemRoster();
        float stored = 0f;
        foreach (ProduceLine line in ProduceLines())
        {
            if (line.Item == null || line.Item.Value <= 0 || line.Value <= 0) continue;
            float worth = line.Value * StoredGoodsBonus;
            float exact = worth / line.Item.Value;
            int count = (int)exact + (MBRandom.RandomFloat < exact - (int)exact ? 1 : 0);
            if (count <= 0) continue;
            _stash.AddToCounts(line.Item, count);
            stored += count * line.Item.Value;
        }
        return (int)stored;
    }

    /// <summary>The produce line for the stewardship menu: where the output goes, and what it is.</summary>
    private TextObject ProduceSummary()
    {
        if (_storeProduce && !StoringProduce)
            return new TextObject("{=ml_produce_fallback}Produce: storing is paused until the storehouse is built or repaired; it is sold meanwhile.");
        if (!_storeProduce)
            return new TextObject("{=ml_produce_selling}Produce: sold, with the income paid into the treasury.");

        IEnumerable<string> names = ProduceLines()
            .Where(line => line.Item != null)
            .Select(line => line.Item!.Name.ToString())
            .Distinct();
        int value = (int)(ProduceLines().Sum(line => line.Value) * StoredGoodsBonus);
        return new TextObject("{=ml_produce_storing}Produce: stored as goods in the storehouse ({ITEMS}), worth about {VALUE} denars a day.")
            .SetTextVariable("ITEMS", string.Join(ListSeparator, names))
            .SetTextVariable("VALUE", value);
    }
}
