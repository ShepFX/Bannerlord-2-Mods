using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace ManorLord;

public sealed partial class ManorLordBehavior : CampaignBehaviorBase
{
    private const string MenuId = "manor_lord_estate";
    private const string ImprovementsMenuId = "manor_lord_improvements";
    private const string HouseholdMenuId = "manor_lord_household";
    private const string StorehouseMenuId = "manor_lord_storehouse";
    private const string StewardshipMenuId = "manor_lord_stewardship";
    private const string ProductionMenuId = "manor_lord_production";
    private const string ManorLocationId = "manor_lord_manor";
    private const string ManorSceneId = "manor_lord_estate_v1";
    private string? _villageId;
    private int _purchasePrice;
    private int _treasury;
    private int _guardCount;
    private int _guardExperience;
    private bool _palisade;
    private bool _trainingField;
    private bool _storehouse;
    private bool _guardQuarters;
    private bool _damaged;
    private int _lastRestDay = -1;
    private ItemRoster? _stash;
    private bool _underThreat;
    private bool _defenseAttempted;
    private bool _manorProtectedThisRaid;
    private bool? _pendingDefenseResult;
    private string? _manorName;
    private string? _activeProject;
    private double _projectCompletionDay;
    private bool _hasSteward;
    private int _supplies;
    private double _raidDeadlineDay;
    private bool _raidResolvedByDeadline;
    private string? _productionFocus;
    private int _activeProjectCost;
    private int _lastGrossIncome;
    private int _lastWages;
    private int _lastSupplyChange;
    private int _recordedIncome;
    private int _recordedExpenses;
    private int _daysOwned;
    private int _estateTier;
    private bool _hasCaptain;
    private bool _hasPhysician;
    private bool _mill;
    private bool _orchard;
    private bool _workshop;
    private string? _guardOrder;

    private bool OwnsManor => !string.IsNullOrEmpty(_villageId);
    private Settlement? ManorSettlement => OwnsManor ? Settlement.Find(_villageId) : null;

    /// <summary>
    /// Gives a manor menu the village's own backdrop. Without this the engine paints its placeholder
    /// ("temp") texture, since custom menus have no background mesh of their own.
    /// </summary>
    private static void SetMenuBackground(MenuCallbackArgs args)
    {
        string? mesh = Settlement.CurrentSettlement?.SettlementComponent?.WaitMeshName;
        if (!string.IsNullOrEmpty(mesh)) args.MenuContext.SetBackgroundMeshName(mesh);
    }

    // State the estate scene reads to decide which in-scene actions are available.
    internal bool HasStorehouse => _storehouse;
    internal bool StorehouseUsable => _storehouse && !_damaged;
    internal int Treasury => _treasury;

    /// <summary>Blank text, used where a localized fragment is conditionally omitted.</summary>
    private static TextObject Empty => new TextObject(string.Empty);

    /// <summary>Separator for the comma-joined building and staff lists. Translatable so CJK can use a full-width comma.</summary>
    private static string ListSeparator => new TextObject("{=ml_list_separator}, ").ToString();

    // Shared dialog button labels.
    private static string Return => new TextObject("{=ml_btn_return}Return").ToString();
    private static string Cancel => new TextObject("{=ml_btn_cancel}Cancel").ToString();
    private static string Victory => new TextObject("{=ml_btn_victory}Victory").ToString();
    private static string SceneUnavailable => new TextObject("{=ml_scene_unavailable}Manor scene unavailable").ToString();
    private static string DefenseWonTitle => new TextObject("{=ml_defense_won_title}Manor defense won").ToString();

    /// <summary>Captures the estate for the scene behaviors, resolving the troops that stand in for guards and staff.</summary>
    private EstateSnapshot BuildSnapshot(Settlement? settlement) => new EstateSnapshot
    {
        Tier = _estateTier,
        Palisade = _palisade,
        TrainingField = _trainingField,
        Storehouse = _storehouse,
        GuardQuarters = _guardQuarters,
        Mill = _mill,
        Orchard = _orchard,
        Workshop = _workshop,
        Damaged = _damaged,
        Steward = _hasSteward,
        Captain = _hasCaptain,
        Physician = _hasPhysician,
        GuardCount = _guardCount,
        GuardTroop = GetGuardTroop(settlement),
        StaffTroop = settlement?.Culture?.Villager ?? settlement?.Culture?.BasicTroop,
        Stationed = StationedForScene(12)
    };

    private static TextObject Employed(bool employed) => employed
        ? new TextObject("{=ml_w_employed}employed")
        : new TextObject("{=ml_w_none}none");

    // Tooltips shared by several menu-option conditions.
    private static TextObject RepairFirst => new TextObject("{=ml_tip_repair_first}Repair the manor first.");
    private static TextObject ProjectUnderway => new TextObject("{=ml_tip_project_underway}Another project is already underway.");
    private static TextObject RequiresLandedEstate => new TextObject("{=ml_tip_requires_landed}Requires a landed estate.");
    private static TextObject NeedDenars(int amount) => new TextObject("{=ml_tip_need_denars}You need {AMOUNT} denars.").SetTextVariable("AMOUNT", amount);

    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        CampaignEvents.VillageBeingRaided.AddNonSerializedListener(this, OnVillageBeingRaided);
        CampaignEvents.VillageLooted.AddNonSerializedListener(this, OnVillageLooted);
        CampaignEvents.VillageBecomeNormal.AddNonSerializedListener(this, OnVillageBecomeNormal);
        CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
    }

    public override void SyncData(IDataStore store)
    {
        store.SyncData("ml_village_id", ref _villageId);
        store.SyncData("ml_purchase_price", ref _purchasePrice);
        store.SyncData("ml_treasury", ref _treasury);
        store.SyncData("ml_guard_count", ref _guardCount);
        store.SyncData("ml_guard_experience", ref _guardExperience);
        store.SyncData("ml_palisade", ref _palisade);
        store.SyncData("ml_training_field", ref _trainingField);
        store.SyncData("ml_storehouse", ref _storehouse);
        store.SyncData("ml_guard_quarters", ref _guardQuarters);
        store.SyncData("ml_damaged", ref _damaged);
        store.SyncData("ml_last_rest_day", ref _lastRestDay);
        store.SyncData("ml_stash", ref _stash);
        store.SyncData("ml_under_threat", ref _underThreat);
        store.SyncData("ml_defense_attempted", ref _defenseAttempted);
        store.SyncData("ml_manor_protected_this_raid", ref _manorProtectedThisRaid);
        store.SyncData("ml_manor_name", ref _manorName);
        store.SyncData("ml_active_project", ref _activeProject);
        store.SyncData("ml_project_completion_day", ref _projectCompletionDay);
        store.SyncData("ml_has_steward", ref _hasSteward);
        store.SyncData("ml_supplies", ref _supplies);
        store.SyncData("ml_raid_deadline_day", ref _raidDeadlineDay);
        store.SyncData("ml_raid_resolved_by_deadline", ref _raidResolvedByDeadline);
        store.SyncData("ml_production_focus", ref _productionFocus);
        store.SyncData("ml_active_project_cost", ref _activeProjectCost);
        store.SyncData("ml_last_gross_income", ref _lastGrossIncome);
        store.SyncData("ml_last_wages", ref _lastWages);
        store.SyncData("ml_last_supply_change", ref _lastSupplyChange);
        store.SyncData("ml_recorded_income", ref _recordedIncome);
        store.SyncData("ml_recorded_expenses", ref _recordedExpenses);
        store.SyncData("ml_days_owned", ref _daysOwned);
        store.SyncData("ml_estate_tier", ref _estateTier);
        store.SyncData("ml_has_captain", ref _hasCaptain);
        store.SyncData("ml_has_physician", ref _hasPhysician);
        store.SyncData("ml_mill", ref _mill);
        store.SyncData("ml_orchard", ref _orchard);
        store.SyncData("ml_workshop", ref _workshop);
        store.SyncData("ml_guard_order", ref _guardOrder);
        SyncStationed(store);
        SyncGoods(store);
        SyncVillage(store);
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        _stash ??= new ItemRoster();
        _productionFocus ??= "mixed";
        if (_estateTier <= 0) _estateTier = 1;
        _guardOrder ??= "watch";
        if (_underThreat && _raidDeadlineDay <= 0d)
            _raidDeadlineDay = CampaignTime.Now.ToDays + 2d;

        starter.AddGameMenu(MenuId, "{ML_MANOR_TEXT}", InitMenu, GameMenu.MenuOverlayType.SettlementWithBoth);
        starter.AddGameMenu(ImprovementsMenuId, "{ML_IMPROVEMENTS_TEXT}", InitImprovementsMenu, GameMenu.MenuOverlayType.SettlementWithBoth);
        starter.AddGameMenu(HouseholdMenuId, "{ML_HOUSEHOLD_TEXT}", InitHouseholdMenu, GameMenu.MenuOverlayType.SettlementWithBoth);
        starter.AddGameMenu(StorehouseMenuId, "{ML_STOREHOUSE_TEXT}", InitStorehouseMenu, GameMenu.MenuOverlayType.SettlementWithBoth);
        starter.AddGameMenu(StewardshipMenuId, "{ML_STEWARDSHIP_TEXT}", InitStewardshipMenu, GameMenu.MenuOverlayType.SettlementWithBoth);
        starter.AddGameMenu(ProductionMenuId, "{ML_PRODUCTION_TEXT}", InitProductionMenu, GameMenu.MenuOverlayType.SettlementWithBoth);
        AddVillageEntry(starter);

        // Estate hub
        AddOption(starter, "ml_buy", "{ML_BUY_TEXT}", BuyCondition, Buy);
        AddOption(starter, "ml_overview", "{=ml_opt_ledger}Review the estate ledger", AtOwnedManor, ShowLedger);
        AddSubmenuOption(starter, MenuId, "ml_improvements_menu", "{=ml_opt_improvements}Manage buildings and repairs", ImprovementsMenuId);
        AddSubmenuOption(starter, MenuId, "ml_household_menu", "{=ml_opt_household}Manage the household guard", HouseholdMenuId);
        AddSubmenuOption(starter, MenuId, "ml_storehouse_menu", "{=ml_opt_storehouse}Manage treasury and storehouse", StorehouseMenuId);
        AddSubmenuOption(starter, MenuId, "ml_stewardship_menu", "{=ml_opt_stewardship}Manage estate staff and policy", StewardshipMenuId);
        AddSubmenuOption(starter, MenuId, "ml_production_menu", "{=ml_opt_production}Manage estate production buildings", ProductionMenuId);
        AddOption(starter, "ml_rename", "{=ml_opt_rename}Name or rename the estate", AtOwnedManor, RenameManor);
        AddOption(starter, "ml_walk", "{=ml_opt_walk}Walk the estate grounds", WalkCondition, EnterManorScene);
        AddOption(starter, "ml_rest", "{=ml_opt_rest}Rest and recover at the manor", AtOwnedManor, Rest);
        AddOption(starter, "ml_defend", "{=ml_opt_defend}Lead your household guard in defense of the manor", DefenseCondition, StartDefense);
        AddOption(starter, "ml_sell", "{=ml_opt_sell}Sell the manor ({ML_SALE_PRICE}{GOLD_ICON})", AtOwnedManor, ConfirmSale);
        starter.AddGameMenuOption(MenuId, "ml_leave", "{=ml_opt_leave}Return to the village", a => { a.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; }, a => GameMenu.SwitchToMenu("village"), true);

        // Improvements
        AddMenuOption(starter, ImprovementsMenuId, "ml_repair", "{=ml_opt_repair}Begin repairs ({ML_REPAIR_COST}{GOLD_ICON}, 2 days)", RepairCondition, Repair);
        AddMenuOption(starter, ImprovementsMenuId, "ml_palisade", "{=ml_opt_palisade}Build a palisade (6000{GOLD_ICON}, 4 days) - reduces raid losses", a => UpgradeCondition(a, !_palisade, 6000), a => StartProject("palisade", 6000, 4));
        AddMenuOption(starter, ImprovementsMenuId, "ml_training", "{=ml_opt_training}Build a training field (8000{GOLD_ICON}, 5 days) - trains guards daily", a => UpgradeCondition(a, !_trainingField, 8000), a => StartProject("training", 8000, 5));
        AddMenuOption(starter, ImprovementsMenuId, "ml_storehouse", "{=ml_opt_build_storehouse}Build a storehouse (5000{GOLD_ICON}, 3 days) - unlocks secure storage", a => UpgradeCondition(a, !_storehouse, 5000), a => StartProject("storehouse", 5000, 3));
        AddMenuOption(starter, ImprovementsMenuId, "ml_quarters", "{=ml_opt_quarters}Build guard quarters (7000{GOLD_ICON}, 4 days) - unlocks household recruitment", a => UpgradeCondition(a, !_guardQuarters, 7000), a => StartProject("quarters", 7000, 4));
        AddMenuOption(starter, ImprovementsMenuId, "ml_tier_two", "{=ml_opt_tier_two}Expand to a landed estate (10000{GOLD_ICON}, 6 days)", a => EstateTierCondition(a, 2, 10000), a => StartProject("tier2", 10000, 6));
        AddMenuOption(starter, ImprovementsMenuId, "ml_tier_three", "{=ml_opt_tier_three}Expand to a grand estate (18000{GOLD_ICON}, 8 days)", a => EstateTierCondition(a, 3, 18000), a => StartProject("tier3", 18000, 8));
        AddMenuOption(starter, ImprovementsMenuId, "ml_cancel_project", "{ML_CANCEL_PROJECT_TEXT}", CancelProjectCondition, CancelProject);
        AddBackOption(starter, ImprovementsMenuId, "ml_improvements_back");

        // Household
        AddMenuOption(starter, HouseholdMenuId, "ml_hire_guard", "{=ml_opt_hire_one}Hire one manor guard (750{GOLD_ICON})", HireGuardCondition, a => HireGuards(1));
        AddMenuOption(starter, HouseholdMenuId, "ml_hire_three_guards", "{=ml_opt_hire_three}Hire three manor guards (2250{GOLD_ICON})", a => HireSeveralGuardCondition(a, 3), a => HireGuards(3));
        AddMenuOption(starter, HouseholdMenuId, "ml_dismiss_guard", "{=ml_opt_dismiss}Release one household guard", DismissGuardCondition, DismissGuard);
        AddMenuOption(starter, HouseholdMenuId, "ml_supplies", "{=ml_opt_supplies_10}Purchase 10 household supplies (500{GOLD_ICON})", a => SuppliesCondition(a, 500), a => BuySupplies(10, 500));
        AddMenuOption(starter, HouseholdMenuId, "ml_supplies_bulk", "{=ml_opt_supplies_50}Purchase 50 household supplies (2250{GOLD_ICON})", a => SuppliesCondition(a, 2250), a => BuySupplies(50, 2250));
        AddMenuOption(starter, HouseholdMenuId, "ml_guard_watch", "{=ml_opt_order_watch}Order: close watch - strongest manor defense", a => GuardOrderCondition(a, "watch"), a => SetGuardOrder("watch"));
        AddMenuOption(starter, HouseholdMenuId, "ml_guard_patrol", "{=ml_opt_order_patrol}Order: village patrols - stronger defense, higher upkeep", a => GuardOrderCondition(a, "patrol"), a => SetGuardOrder("patrol"));
        AddMenuOption(starter, HouseholdMenuId, "ml_guard_drill", "{=ml_opt_order_drill}Order: intensive drills - faster training, higher upkeep", a => GuardOrderCondition(a, "drill"), a => SetGuardOrder("drill"));
        AddStationedMenus(starter);
        AddBackOption(starter, HouseholdMenuId, "ml_household_back");

        // Treasury and storage
        AddMenuOption(starter, StorehouseMenuId, "ml_open_stash", "{=ml_opt_open_stash}Open the manor item storehouse", StorehouseCondition, OpenStash);
        AddMenuOption(starter, StorehouseMenuId, "ml_deposit_1000", "{=ml_opt_deposit_1000}Deposit 1000{GOLD_ICON}", a => MoneyCondition(a, true, 1000), a => TransferTreasury(1000));
        AddMenuOption(starter, StorehouseMenuId, "ml_deposit_5000", "{=ml_opt_deposit_5000}Deposit 5000{GOLD_ICON}", a => MoneyCondition(a, true, 5000), a => TransferTreasury(5000));
        AddMenuOption(starter, StorehouseMenuId, "ml_deposit_custom", "{=ml_opt_deposit_custom}Deposit a custom amount", a => CustomMoneyCondition(a, true), a => ShowCustomTransfer(true));
        AddMenuOption(starter, StorehouseMenuId, "ml_withdraw_1000", "{=ml_opt_withdraw_1000}Withdraw 1000{GOLD_ICON}", a => MoneyCondition(a, false, 1000), a => TransferTreasury(-1000));
        AddMenuOption(starter, StorehouseMenuId, "ml_withdraw_custom", "{=ml_opt_withdraw_custom}Withdraw a custom amount", a => CustomMoneyCondition(a, false), a => ShowCustomTransfer(false));
        AddMenuOption(starter, StorehouseMenuId, "ml_withdraw_all", "{=ml_opt_withdraw_all}Withdraw all manor funds", WithdrawAllCondition, WithdrawAll);
        AddBackOption(starter, StorehouseMenuId, "ml_storehouse_back");

        // Stewardship
        AddMenuOption(starter, StewardshipMenuId, "ml_steward", "{=ml_opt_steward}Hire an estate steward (2500{GOLD_ICON})", StewardCondition, HireSteward);
        AddMenuOption(starter, StewardshipMenuId, "ml_captain", "{=ml_opt_captain}Hire a household captain (4000{GOLD_ICON})", CaptainCondition, HireCaptain);
        AddMenuOption(starter, StewardshipMenuId, "ml_physician", "{=ml_opt_physician}Retain an estate physician (3500{GOLD_ICON})", PhysicianCondition, HirePhysician);
        AddMenuOption(starter, StewardshipMenuId, "ml_focus_mixed", "{=ml_opt_focus_mixed}Adopt mixed farming - balanced income and supplies", a => ProductionFocusCondition(a, "mixed"), a => SetProductionFocus("mixed"));
        AddMenuOption(starter, StewardshipMenuId, "ml_focus_crops", "{=ml_opt_focus_crops}Plant cash crops - 30% more income", a => ProductionFocusCondition(a, "crops"), a => SetProductionFocus("crops"));
        AddMenuOption(starter, StewardshipMenuId, "ml_focus_livestock", "{=ml_opt_focus_livestock}Raise livestock - lower income, more supplies", a => ProductionFocusCondition(a, "livestock"), a => SetProductionFocus("livestock"));
        AddGoodsMenus(starter);
        AddBackOption(starter, StewardshipMenuId, "ml_stewardship_back");

        // Production buildings
        AddMenuOption(starter, ProductionMenuId, "ml_mill", "{=ml_opt_mill}Build a mill (6500{GOLD_ICON}, 4 days) - increases estate income", a => ProductionBuildingCondition(a, !_mill, 2, 6500), a => StartProject("mill", 6500, 4));
        AddMenuOption(starter, ProductionMenuId, "ml_orchard", "{=ml_opt_orchard}Plant an orchard (5500{GOLD_ICON}, 3 days) - produces supplies", a => ProductionBuildingCondition(a, !_orchard, 2, 5500), a => StartProject("orchard", 5500, 3));
        AddMenuOption(starter, ProductionMenuId, "ml_workshop", "{=ml_opt_workshop}Build an estate workshop (9000{GOLD_ICON}, 5 days) - produces valuable goods", a => ProductionBuildingCondition(a, !_workshop, 3, 9000), a => StartProject("workshop", 9000, 5));
        AddBackOption(starter, ProductionMenuId, "ml_production_back");
    }

    private static void AddOption(CampaignGameStarter starter, string id, string text, GameMenuOption.OnConditionDelegate condition, GameMenuOption.OnConsequenceDelegate consequence) =>
        starter.AddGameMenuOption(MenuId, id, text, condition, consequence, false);

    private static void AddMenuOption(CampaignGameStarter starter, string menuId, string id, string text, GameMenuOption.OnConditionDelegate condition, GameMenuOption.OnConsequenceDelegate consequence) =>
        starter.AddGameMenuOption(menuId, id, text, condition, consequence, false);

    private void AddSubmenuOption(CampaignGameStarter starter, string menuId, string id, string text, string targetMenuId) =>
        starter.AddGameMenuOption(menuId, id, text, args =>
        {
            bool visible = AtOwnedManor(args);
            if (visible) args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
            return visible;
        }, args => GameMenu.SwitchToMenu(targetMenuId), false);

    private static void AddBackOption(CampaignGameStarter starter, string menuId, string id) =>
        starter.AddGameMenuOption(menuId, id, "{=ml_opt_back}Return to the estate overview", args =>
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Leave;
            return true;
        }, args => GameMenu.SwitchToMenu(MenuId), true);

    private void AddVillageEntry(CampaignGameStarter starter) => starter.AddGameMenuOption(
        "village", "ml_visit_manor", "{ML_ENTRY_TEXT}", args =>
        {
            Settlement? here = Settlement.CurrentSettlement;
            if (here?.IsVillage != true) return false;
            if (!OwnsManor) MBTextManager.SetTextVariable("ML_ENTRY_TEXT", new TextObject("{=ml_entry_inquire}Inquire about purchasing a manor"));
            else if (here.StringId == _villageId) MBTextManager.SetTextVariable("ML_ENTRY_TEXT", new TextObject("{=ml_entry_visit}Visit your manor"));
            else MBTextManager.SetTextVariable("ML_ENTRY_TEXT", new TextObject("{=ml_entry_holdings}Manor holdings (owned near {SETTLEMENT})")
                .SetTextVariable("SETTLEMENT", ManorSettlement?.Name ?? new TextObject("{=ml_w_another_village}another village")));
            args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
            return true;
        }, args => GameMenu.SwitchToMenu(MenuId), false, 5);

    private void InitMenu(MenuCallbackArgs args)
    {
        SetMenuBackground(args);
        Settlement? here = Settlement.CurrentSettlement;
        TextObject text;
        if (!OwnsManor)
            text = new TextObject("{=ml_menu_offer}A local family offers a modest farmhouse and its surrounding land for {PRICE} denars. Ownership grants no title to the village and no claim over its people.")
                .SetTextVariable("PRICE", _purchasePrice = CalculatePrice(here));
        else if (here?.StringId != _villageId)
            text = new TextObject("{=ml_menu_elsewhere}You own a manor near {SETTLEMENT}. You may own only one manor at a time; travel there to manage or sell it.")
                .SetTextVariable("SETTLEMENT", ManorSettlement?.Name ?? Empty);
        else
        {
            GetProjectedDailyEstate(out int income, out int producedSupplies);
            int upkeep = TotalDailyWages();
            text = new TextObject("{=ml_menu_main}{MANOR_NAME} near {SETTLEMENT}\n{ESTATE_RANK} | {CONDITION}\n\nTreasury: {TREASURY} denars\nProjected balance: {BALANCE} denars/day\nSupplies: {SUPPLIES} ({SUPPLY_CHANGE}/day)\nGuards: {GUARDS}/{GUARD_CAP} - {TRAINING}, {ORDERS}\nStaff: {STAFF}\nBuildings: {BUILDINGS}\n\n{PROJECT}{THREAT}")
                .SetTextVariable("MANOR_NAME", ManorDisplayName())
                .SetTextVariable("SETTLEMENT", here!.Name)
                .SetTextVariable("ESTATE_RANK", EstateTierDisplayName())
                .SetTextVariable("CONDITION", _damaged
                    ? new TextObject("{=ml_cond_damaged}DAMAGED")
                    : _underThreat ? new TextObject("{=ml_cond_threat}UNDER THREAT") : new TextObject("{=ml_cond_secure}Secure"))
                .SetTextVariable("TREASURY", _treasury)
                .SetTextVariable("BALANCE", FormatSigned((_hasSteward && !_damaged ? income : 0) - upkeep))
                .SetTextVariable("SUPPLIES", _supplies)
                .SetTextVariable("SUPPLY_CHANGE", FormatSigned((_hasSteward && !_damaged ? producedSupplies : 0) - DailySupplyUse()))
                .SetTextVariable("GUARDS", _guardCount)
                .SetTextVariable("GUARD_CAP", GuardCapacity())
                .SetTextVariable("TRAINING", GuardTrainingSummary())
                .SetTextVariable("ORDERS", GuardOrderDisplayName())
                .SetTextVariable("STAFF", StaffSummary())
                .SetTextVariable("BUILDINGS", UpgradeSummary())
                .SetTextVariable("PROJECT", ProjectSummary())
                .SetTextVariable("THREAT", _underThreat
                    ? new TextObject("{=ml_menu_threat_line}\nRaiders are attacking; {DAYS} days remain to respond.")
                        .SetTextVariable("DAYS", Math.Max(0, (int)Math.Ceiling(_raidDeadlineDay - CampaignTime.Now.ToDays)))
                    : Empty);
        }
        MBTextManager.SetTextVariable("ML_MANOR_TEXT", text);
        MBTextManager.SetTextVariable("ML_BUY_TEXT", new TextObject("{=ml_opt_buy}Purchase this manor ({PRICE}{GOLD_ICON})").SetTextVariable("PRICE", CalculatePrice(here)));
        MBTextManager.SetTextVariable("ML_REPAIR_COST", RepairCost());
        MBTextManager.SetTextVariable("ML_SALE_PRICE", SalePrice());
    }

    private void InitImprovementsMenu(MenuCallbackArgs args)
    {
        SetMenuBackground(args);
        TextObject project = string.IsNullOrEmpty(_activeProject)
            ? new TextObject("{=ml_project_none}No construction project is active.")
            : new TextObject("{=ml_project_current}Current project: {PROJECT}. Completion is expected in {DAYS} days.")
                .SetTextVariable("PROJECT", ProjectDisplayName(_activeProject!))
                .SetTextVariable("DAYS", ProjectDaysRemaining());
        MBTextManager.SetTextVariable("ML_IMPROVEMENTS_TEXT", new TextObject("{=ml_menu_improvements}{MANOR_NAME} - Buildings and Repairs\n\nEstate rank: {ESTATE_RANK}.\nCompleted improvements: {BUILDINGS}.\nEstate condition: {CONDITION}.\n{PROJECT}\n\nHigher estate ranks unlock additional staff, production buildings, and guard capacity.")
            .SetTextVariable("MANOR_NAME", ManorDisplayName())
            .SetTextVariable("ESTATE_RANK", EstateTierDisplayName())
            .SetTextVariable("BUILDINGS", UpgradeSummary())
            .SetTextVariable("CONDITION", _damaged
                ? new TextObject("{=ml_cond_needs_repair}damaged and in need of repair")
                : new TextObject("{=ml_cond_sound}sound"))
            .SetTextVariable("PROJECT", project));
        MBTextManager.SetTextVariable("ML_REPAIR_COST", RepairCost());
        MBTextManager.SetTextVariable("ML_CANCEL_PROJECT_TEXT", new TextObject("{=ml_opt_cancel_project}Cancel {PROJECT} and recover {REFUND}{GOLD_ICON}")
            .SetTextVariable("PROJECT", ProjectDisplayName(_activeProject ?? "project"))
            .SetTextVariable("REFUND", ProjectRefund()));
    }

    private void InitHouseholdMenu(MenuCallbackArgs args)
    {
        SetMenuBackground(args);
        int dailyUse = DailySupplyUse();
        TextObject household = new TextObject("{=ml_menu_household}{MANOR_NAME} - Household\n\nGuards: {GUARDS}/{GUARD_CAP} ({TRAINING}).\nStanding orders: {ORDERS}.\nDaily guard wages: {WAGES} denars.\nSupplies: {SUPPLIES} ({SUPPLY_STATUS}).\nCaptain: {CAPTAIN}.\nTraining field: {TRAINING_FIELD}. Guard quarters: {QUARTERS}.\n\nUnpaid guards may desert, while shortages gradually erode their training.")
            .SetTextVariable("MANOR_NAME", ManorDisplayName())
            .SetTextVariable("GUARDS", _guardCount)
            .SetTextVariable("GUARD_CAP", GuardCapacity())
            .SetTextVariable("TRAINING", GuardTrainingSummary())
            .SetTextVariable("ORDERS", GuardOrderDisplayName())
            .SetTextVariable("WAGES", GuardDailyWages())
            .SetTextVariable("SUPPLIES", _supplies)
            .SetTextVariable("SUPPLY_STATUS", dailyUse == 0
                ? new TextObject("{=ml_supply_unused}no current use")
                : new TextObject("{=ml_supply_days}about {DAYS} days remaining").SetTextVariable("DAYS", _supplies / dailyUse))
            .SetTextVariable("CAPTAIN", _hasCaptain
                ? new TextObject("{=ml_captain_commanding}commanding the household")
                : new TextObject("{=ml_w_none}none"))
            .SetTextVariable("TRAINING_FIELD", _trainingField
                ? new TextObject("{=ml_w_active}active")
                : new TextObject("{=ml_w_not_built}not built"))
            .SetTextVariable("QUARTERS", _guardQuarters
                ? new TextObject("{=ml_w_built}built")
                : new TextObject("{=ml_quarters_required}required before recruiting"));
        // The stationed-troops section is appended so the existing translated body is left as it was.
        MBTextManager.SetTextVariable("ML_HOUSEHOLD_TEXT", WithLine(household, StationedSummary()));
    }

    private void InitStorehouseMenu(MenuCallbackArgs args)
    {
        SetMenuBackground(args);
        TextObject access = !_storehouse
            ? new TextObject("{=ml_store_missing}A storehouse must be built before funds or goods can be secured here.")
            : _damaged
                ? new TextObject("{=ml_store_damaged}The damaged storehouse cannot be opened until repairs are complete.")
                : new TextObject("{=ml_store_ready}The storehouse is secure and available.");
        MBTextManager.SetTextVariable("ML_STOREHOUSE_TEXT", new TextObject("{=ml_menu_storehouse}{MANOR_NAME} - Treasury and Storehouse\n\nTreasury: {TREASURY} denars.\nStored goods: {STACKS} stacks worth {VALUE} denars.\n\n{ACCESS}")
            .SetTextVariable("MANOR_NAME", ManorDisplayName())
            .SetTextVariable("TREASURY", _treasury)
            .SetTextVariable("STACKS", _stash?.Count ?? 0)
            .SetTextVariable("VALUE", _stash?.TotalValue ?? 0)
            .SetTextVariable("ACCESS", access));
    }

    private void InitStewardshipMenu(MenuCallbackArgs args)
    {
        SetMenuBackground(args);
        GetProjectedDailyEstate(out int gross, out int supplies);
        TextObject stewardship = new TextObject("{=ml_menu_stewardship}{MANOR_NAME} - Stewardship\n\nSteward: {STEWARD} (20 denars/day).\nCaptain: {CAPTAIN} (20 denars/day).\nPhysician: {PHYSICIAN} (15 denars/day).\nProduction focus: {FOCUS}.\nProjected daily production: {OUTPUT}.\nTotal estate upkeep: {UPKEEP} denars/day.\nVillage hearths: {HEARTHS}.\n\nThe steward deposits estate income directly into the manor treasury.")
            .SetTextVariable("MANOR_NAME", ManorDisplayName())
            .SetTextVariable("STEWARD", Employed(_hasSteward))
            .SetTextVariable("CAPTAIN", Employed(_hasCaptain))
            .SetTextVariable("PHYSICIAN", Employed(_hasPhysician))
            .SetTextVariable("FOCUS", ProductionFocusDisplayName())
            .SetTextVariable("OUTPUT", _hasSteward && !_damaged
                ? new TextObject("{=ml_output_daily}{INCOME} denars and {SUPPLIES} supplies")
                    .SetTextVariable("INCOME", gross)
                    .SetTextVariable("SUPPLIES", supplies)
                : new TextObject("{=ml_w_inactive}inactive"))
            .SetTextVariable("UPKEEP", TotalDailyWages())
            .SetTextVariable("HEARTHS", (int)(ManorSettlement?.Village?.Hearth ?? 0f));
        MBTextManager.SetTextVariable("ML_STEWARDSHIP_TEXT", WithLine(new TextObject(WithLine(stewardship, ProduceSummary())), VillageStandingSummary()));
    }

    private void InitProductionMenu(MenuCallbackArgs args)
    {
        SetMenuBackground(args);
        GetProjectedDailyEstate(out int income, out int supplies);
        MBTextManager.SetTextVariable("ML_PRODUCTION_TEXT", new TextObject("{=ml_menu_production}{MANOR_NAME} - Production\n\nEstate rank: {ESTATE_RANK}.\nProduction buildings: {BUILDINGS}.\nFocus: {FOCUS}.\nProjected output: {OUTPUT}.\n\nLanded estates unlock mills and orchards. Grand estates can support a workshop.")
            .SetTextVariable("MANOR_NAME", ManorDisplayName())
            .SetTextVariable("ESTATE_RANK", EstateTierDisplayName())
            .SetTextVariable("BUILDINGS", ProductionBuildingSummary())
            .SetTextVariable("FOCUS", ProductionFocusDisplayName())
            .SetTextVariable("OUTPUT", _hasSteward && !_damaged
                ? new TextObject("{=ml_output_per_day}{INCOME} denars and {SUPPLIES} supplies per day")
                    .SetTextVariable("INCOME", income)
                    .SetTextVariable("SUPPLIES", supplies)
                : new TextObject("{=ml_output_inactive}inactive until a steward is employed and repairs are complete")));
    }

    private void ShowLedger(MenuCallbackArgs args) => ShowLedgerInquiry();

    /// <summary>The ledger popup; also reachable from inside the estate scene.</summary>
    internal void ShowLedgerInquiry()
    {
        GetProjectedDailyEstate(out int projectedGross, out int projectedSupplies);
        int projectedWages = TotalDailyWages();
        TextObject threat = _underThreat
            ? new TextObject("{=ml_ledger_threat}Raiders are active; {DAYS} days remain to respond.")
                .SetTextVariable("DAYS", Math.Max(0, (int)Math.Ceiling(_raidDeadlineDay - CampaignTime.Now.ToDays)))
            : new TextObject("{=ml_ledger_no_threat}No active threat.");
        TextObject text = new TextObject("{=ml_ledger_body}Estate age: {DAYS_OWNED} recorded days.\nRank: {ESTATE_RANK}.\nCondition: {CONDITION}. {THREAT}\n\nLast day\nIncome: {LAST_INCOME} denars\nTotal upkeep: {LAST_UPKEEP} denars\nSupply change: {LAST_SUPPLY_CHANGE}\n\nProjected next day\nIncome: {NEXT_INCOME} denars\nTotal upkeep: {NEXT_UPKEEP} denars\nNet balance: {NEXT_BALANCE} denars\nSupplies produced: {NEXT_SUPPLIES}\nSupplies consumed: {SUPPLY_USE}\n\nRecorded totals since this feature was installed\nEstate income: {TOTAL_INCOME} denars\nEstate expenses: {TOTAL_EXPENSES} denars")
            .SetTextVariable("DAYS_OWNED", _daysOwned)
            .SetTextVariable("ESTATE_RANK", EstateTierDisplayName())
            .SetTextVariable("CONDITION", _damaged
                ? new TextObject("{=ml_w_damaged}damaged")
                : new TextObject("{=ml_cond_sound}sound"))
            .SetTextVariable("THREAT", threat)
            .SetTextVariable("LAST_INCOME", _lastGrossIncome)
            .SetTextVariable("LAST_UPKEEP", _lastWages)
            .SetTextVariable("LAST_SUPPLY_CHANGE", FormatSigned(_lastSupplyChange))
            .SetTextVariable("NEXT_INCOME", _hasSteward && !_damaged ? projectedGross : 0)
            .SetTextVariable("NEXT_UPKEEP", projectedWages)
            .SetTextVariable("NEXT_BALANCE", FormatSigned((_hasSteward && !_damaged ? projectedGross : 0) - projectedWages))
            .SetTextVariable("NEXT_SUPPLIES", _hasSteward && !_damaged ? projectedSupplies : 0)
            .SetTextVariable("SUPPLY_USE", DailySupplyUse())
            .SetTextVariable("TOTAL_INCOME", _recordedIncome)
            .SetTextVariable("TOTAL_EXPENSES", _recordedExpenses);
        TextObject title = new TextObject("{=ml_ledger_title}{MANOR_NAME} Ledger").SetTextVariable("MANOR_NAME", ManorDisplayName());
        InformationManager.ShowInquiry(new InquiryData(title.ToString(), text.ToString(), true, false, Return, string.Empty, null, null), true);
    }

    private bool BuyCondition(MenuCallbackArgs args)
    {
        if (OwnsManor || Settlement.CurrentSettlement?.IsVillage != true) return false;
        int price = CalculatePrice(Settlement.CurrentSettlement);
        args.IsEnabled = Hero.MainHero.Gold >= price;
        if (!args.IsEnabled) args.Tooltip = new TextObject("{=ml_tip_afford_property}You cannot afford this property.");
        args.optionLeaveType = GameMenuOption.LeaveType.Continue;
        return true;
    }

    private void Buy(MenuCallbackArgs args)
    {
        Settlement village = Settlement.CurrentSettlement;
        _purchasePrice = CalculatePrice(village);
        Hero.MainHero.ChangeHeroGold(-_purchasePrice);
        _villageId = village.StringId;
        _manorName = new TextObject("{=ml_default_manor_name}{SETTLEMENT} Manor").SetTextVariable("SETTLEMENT", village.Name).ToString();
        _damaged = false;
        _productionFocus = "mixed";
        _daysOwned = 0;
        _lastGrossIncome = _lastWages = _lastSupplyChange = 0;
        _recordedIncome = _recordedExpenses = 0;
        _estateTier = 1;
        _hasCaptain = _hasPhysician = _mill = _orchard = _workshop = false;
        _guardOrder = "watch";
        _storeProduce = false; _lastStoredValue = 0; _daysSinceDues = 0; _incomeSinceDues = 0;
        _stationed = TroopRoster.CreateDummyTroopRoster();
        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_purchased}You purchased a manor near {SETTLEMENT}.").SetTextVariable("SETTLEMENT", village.Name).ToString(), Colors.Green));
        GameMenu.SwitchToMenu(MenuId);
    }

    private bool AtOwnedManor(MenuCallbackArgs args)
    {
        bool result = OwnsManor && Settlement.CurrentSettlement?.StringId == _villageId;
        if (result) args.optionLeaveType = GameMenuOption.LeaveType.Continue;
        return result;
    }

    private bool UpgradeCondition(MenuCallbackArgs args, bool available, int cost)
    {
        if (!available || !AtOwnedManor(args)) return false;
        args.IsEnabled = !_damaged && string.IsNullOrEmpty(_activeProject) && Hero.MainHero.Gold >= cost;
        if (!args.IsEnabled) args.Tooltip = _damaged ? RepairFirst : !string.IsNullOrEmpty(_activeProject) ? ProjectUnderway : new TextObject("{=ml_tip_afford_improvement}You cannot afford this improvement.");
        return true;
    }

    private bool EstateTierCondition(MenuCallbackArgs args, int targetTier, int cost)
    {
        if (_estateTier != targetTier - 1 || !AtOwnedManor(args)) return false;
        args.IsEnabled = !_damaged && string.IsNullOrEmpty(_activeProject) && Hero.MainHero.Gold >= cost;
        if (!args.IsEnabled) args.Tooltip = _damaged ? RepairFirst : !string.IsNullOrEmpty(_activeProject) ? ProjectUnderway : new TextObject("{=ml_tip_afford_expansion}You cannot afford this expansion.");
        return true;
    }

    private bool ProductionBuildingCondition(MenuCallbackArgs args, bool available, int requiredTier, int cost)
    {
        if (!available || !AtOwnedManor(args)) return false;
        args.IsEnabled = _estateTier >= requiredTier && !_damaged && string.IsNullOrEmpty(_activeProject) && Hero.MainHero.Gold >= cost;
        if (!args.IsEnabled)
            args.Tooltip = _estateTier < requiredTier
                ? new TextObject("{=ml_tip_requires_rank}Requires estate rank {RANK}.").SetTextVariable("RANK", requiredTier)
                : _damaged ? RepairFirst : !string.IsNullOrEmpty(_activeProject) ? ProjectUnderway : new TextObject("{=ml_tip_afford_building}You cannot afford this building.");
        return true;
    }

    private void StartProject(string project, int cost, int days)
    {
        Hero.MainHero.ChangeHeroGold(-cost);
        _activeProject = project;
        _activeProjectCost = cost;
        _projectCompletionDay = CampaignTime.Now.ToDays + days;
        _recordedExpenses += cost;
        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_work_begun}Work has begun and should be complete in {DAYS} days.").SetTextVariable("DAYS", days).ToString(), Colors.Green));
        GameMenu.SwitchToMenu(ImprovementsMenuId);
    }

    private bool CancelProjectCondition(MenuCallbackArgs args)
    {
        if (string.IsNullOrEmpty(_activeProject) || !AtOwnedManor(args)) return false;
        args.optionLeaveType = GameMenuOption.LeaveType.Continue;
        return true;
    }

    private void CancelProject(MenuCallbackArgs args)
    {
        TextObject project = ProjectDisplayName(_activeProject ?? "project");
        int refund = ProjectRefund();
        TextObject body = new TextObject("{=ml_inq_cancel_body}Stop work on the {PROJECT}? Half of the recorded project cost ({REFUND} denars) will be recovered.")
            .SetTextVariable("PROJECT", project)
            .SetTextVariable("REFUND", refund);
        InformationManager.ShowInquiry(new InquiryData(new TextObject("{=ml_inq_cancel_title}Cancel construction?").ToString(), body.ToString(), true, true, new TextObject("{=ml_btn_cancel_project}Cancel project").ToString(), new TextObject("{=ml_btn_keep_building}Keep building").ToString(), () =>
        {
            Hero.MainHero.ChangeHeroGold(refund);
            _recordedExpenses = Math.Max(0, _recordedExpenses - refund);
            _activeProject = null;
            _activeProjectCost = 0;
            _projectCompletionDay = 0d;
            GameMenu.SwitchToMenu(ImprovementsMenuId);
        }, null), true);
    }

    private void RenameManor(MenuCallbackArgs args)
    {
        InformationManager.ShowTextInquiry(new TextInquiryData(
            new TextObject("{=ml_inq_rename_title}Name the estate").ToString(),
            new TextObject("{=ml_inq_rename_body}Choose a name for your manor.").ToString(),
            true, true,
            new TextObject("{=ml_btn_accept}Accept").ToString(), Cancel, value =>
        {
            _manorName = value.Trim();
            GameMenu.SwitchToMenu(MenuId);
        }, null, false, value => new Tuple<bool, string>(!string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 40, new TextObject("{=ml_inq_rename_invalid}Enter a name between 1 and 40 characters.").ToString()), _manorName ?? string.Empty, string.Empty), true);
    }

    private bool WalkCondition(MenuCallbackArgs args)
    {
        if (!AtOwnedManor(args)) return false;
        args.optionLeaveType = GameMenuOption.LeaveType.Mission;
        return true;
    }

    private void WalkEstate(MenuCallbackArgs args)
    {
        TextObject status = _damaged
            ? new TextObject("{=ml_walk_damaged}The buildings still show the scars of a recent attack.")
            : new TextObject("{=ml_walk_secure}The estate is orderly and secure.");
        TextObject project = string.IsNullOrEmpty(_activeProject)
            ? new TextObject("{=ml_walk_no_project}No construction is currently underway.")
            : new TextObject("{=ml_walk_project}Workers are busy with the {PROJECT}.").SetTextVariable("PROJECT", ProjectDisplayName(_activeProject!));
        TextObject body = new TextObject("{=ml_walk_body}You tour the {ESTATE_RANK} and surrounding land. {STATUS}\n\nImprovements: {BUILDINGS}.\nHousehold guards: {GUARDS}/{GUARD_CAP} ({TRAINING}).\nStaff: {STAFF}.\nSupplies: {SUPPLIES}.\n\n{PROJECT}")
            .SetTextVariable("ESTATE_RANK", EstateTierDisplayName())
            .SetTextVariable("STATUS", status)
            .SetTextVariable("BUILDINGS", UpgradeSummary())
            .SetTextVariable("GUARDS", _guardCount)
            .SetTextVariable("GUARD_CAP", GuardCapacity())
            .SetTextVariable("TRAINING", GuardTrainingSummary())
            .SetTextVariable("STAFF", StaffSummary())
            .SetTextVariable("SUPPLIES", _supplies)
            .SetTextVariable("PROJECT", project);
        InformationManager.ShowInquiry(new InquiryData(ManorDisplayName().ToString(), body.ToString(), true, false, Return, string.Empty, null, null), true);
    }

    private bool ManorSceneCondition(MenuCallbackArgs args)
    {
        if (!AtOwnedManor(args)) return false;
        args.optionLeaveType = GameMenuOption.LeaveType.Mission;
        return true;
    }

    private void EnterManorScene(MenuCallbackArgs args)
    {
        try
        {
            Settlement? settlement = Settlement.CurrentSettlement;
            LocationComplex? complex = settlement?.LocationComplex;
            if (complex == null)
            {
                InformationManager.ShowInquiry(new InquiryData(SceneUnavailable, new TextObject("{=ml_scene_no_complex}The village location complex is unavailable.").ToString(), true, false, Return, string.Empty, null, null), true);
                return;
            }

            // Use Bannerlord's fully initialized village location controller. Injecting a new
            // Location into the live registry loads the scene but causes an unmanaged crash
            // during team/agent initialization on v1.4.8.
            Location? villageCenter = complex.GetLocationWithId("village_center");
            if (villageCenter == null)
            {
                InformationManager.ShowInquiry(new InquiryData(SceneUnavailable, new TextObject("{=ml_scene_no_center}The village center location could not be resolved.").ToString(), true, false, Return, string.Empty, null, null), true);
                return;
            }

            // Arm the visit so the mission dresses the yard with the estate's upgrades, guards and staff.
            ManorDefenseMissionState.ArmEstateVisit(BuildSnapshot(settlement));
            IMission? mission = CampaignMission.OpenVillageMission(ManorSceneId, villageCenter, null);
            if (mission == null)
            {
                ManorDefenseMissionState.CancelArm();
                InformationManager.ShowInquiry(new InquiryData(SceneUnavailable, new TextObject("{=ml_scene_declined}Bannerlord declined scene '{SCENE}'.").SetTextVariable("SCENE", ManorSceneId).ToString(), true, false, Return, string.Empty, null, null), true);
            }
        }
        catch (Exception ex)
        {
            // Exception detail is diagnostic and deliberately left untranslated.
            InformationManager.ShowInquiry(new InquiryData(new TextObject("{=ml_scene_error_title}Manor scene error").ToString(), ex.GetType().Name + ": " + ex.Message, true, false, Return, string.Empty, null, null), true);
        }
    }

    private static Location? EnsureManorLocation(LocationComplex complex)
    {
        Location? existing = complex.GetLocationWithId(ManorLocationId);
        if (existing != null) return existing;

        var manor = new Location(
            ManorLocationId,
            new TextObject("{=ml_loc_manor_grounds}Manor Grounds"),
            new TextObject("{=ml_loc_enter_grounds}Enter the manor grounds"),
            0,
            false,
            false,
            "CanAlways",
            "CanAlways",
            "CanAlways",
            "CanAlways",
            new[] { ManorSceneId, ManorSceneId, ManorSceneId, ManorSceneId },
            complex);

        FieldInfo? field = typeof(LocationComplex).GetField("_locations", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(complex) is not Dictionary<string, Location> locations) return null;
        locations[ManorLocationId] = manor;
        manor.SetOwnerComplex(complex);
        MarkLocationInitialized(manor);

        Location? villageCenter = complex.GetLocationWithId("village_center");
        if (villageCenter != null)
        {
            complex.AddPassage(villageCenter, manor);
            complex.AddPassage(manor, villageCenter);
        }
        return manor;
    }

    private static void MarkLocationInitialized(Location location)
    {
        PropertyInfo? property = typeof(Location).GetProperty("IsInitialized", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property?.SetValue(location, true);
    }

    private bool StewardCondition(MenuCallbackArgs args)
    {
        if (_hasSteward || !AtOwnedManor(args)) return false;
        args.IsEnabled = !_damaged && Hero.MainHero.Gold >= 2500;
        return true;
    }

    private void HireSteward(MenuCallbackArgs args)
    {
        Hero.MainHero.ChangeHeroGold(-2500);
        _hasSteward = true;
        _recordedExpenses += 2500;
        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_steward_hired}A steward takes charge of the estate's fields and accounts.").ToString(), Colors.Green));
        GameMenu.SwitchToMenu(StewardshipMenuId);
    }

    private bool CaptainCondition(MenuCallbackArgs args)
    {
        if (_hasCaptain || !AtOwnedManor(args)) return false;
        args.IsEnabled = _estateTier >= 2 && !_damaged && Hero.MainHero.Gold >= 4000;
        if (!args.IsEnabled) args.Tooltip = _estateTier < 2 ? RequiresLandedEstate : _damaged ? RepairFirst : NeedDenars(4000);
        return true;
    }

    private void HireCaptain(MenuCallbackArgs args)
    {
        Hero.MainHero.ChangeHeroGold(-4000);
        _hasCaptain = true;
        _recordedExpenses += 4000;
        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_captain_hired}A veteran captain takes command of the household guard.").ToString(), Colors.Green));
        GameMenu.SwitchToMenu(StewardshipMenuId);
    }

    private bool PhysicianCondition(MenuCallbackArgs args)
    {
        if (_hasPhysician || !AtOwnedManor(args)) return false;
        args.IsEnabled = _estateTier >= 2 && !_damaged && Hero.MainHero.Gold >= 3500;
        if (!args.IsEnabled) args.Tooltip = _estateTier < 2 ? RequiresLandedEstate : _damaged ? RepairFirst : NeedDenars(3500);
        return true;
    }

    private void HirePhysician(MenuCallbackArgs args)
    {
        Hero.MainHero.ChangeHeroGold(-3500);
        _hasPhysician = true;
        _recordedExpenses += 3500;
        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_physician_hired}A physician establishes a small surgery at the estate.").ToString(), Colors.Green));
        GameMenu.SwitchToMenu(StewardshipMenuId);
    }

    private bool SuppliesCondition(MenuCallbackArgs args, int cost)
    {
        if (!AtOwnedManor(args)) return false;
        args.IsEnabled = Hero.MainHero.Gold >= cost;
        if (!args.IsEnabled) args.Tooltip = new TextObject("{=ml_tip_afford_supplies}You cannot afford these supplies.");
        return true;
    }

    private void BuySupplies(int amount, int cost)
    {
        Hero.MainHero.ChangeHeroGold(-cost);
        _supplies += amount;
        _recordedExpenses += cost;
        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_supplies_delivered}{AMOUNT} supplies were delivered to the manor.").SetTextVariable("AMOUNT", amount).ToString()));
        GameMenu.SwitchToMenu(HouseholdMenuId);
    }

    private bool HireGuardCondition(MenuCallbackArgs args)
    {
        if (!_guardQuarters || _guardCount >= GuardCapacity() || !AtOwnedManor(args)) return false;
        args.IsEnabled = !_damaged && Hero.MainHero.Gold >= 750;
        if (!args.IsEnabled) args.Tooltip = _damaged ? RepairFirst : NeedDenars(750);
        return true;
    }

    private bool HireSeveralGuardCondition(MenuCallbackArgs args, int amount)
    {
        if (!_guardQuarters || _guardCount + amount > GuardCapacity() || !AtOwnedManor(args)) return false;
        int cost = 750 * amount;
        args.IsEnabled = !_damaged && Hero.MainHero.Gold >= cost;
        if (!args.IsEnabled) args.Tooltip = _damaged ? RepairFirst : NeedDenars(cost);
        return true;
    }

    private void HireGuards(int amount)
    {
        int cost = 750 * amount;
        Hero.MainHero.ChangeHeroGold(-cost);
        _guardCount += amount;
        _recordedExpenses += cost;
        // Singular and plural are separate entries so translators can inflect the whole sentence.
        TextObject joined = amount == 1
            ? new TextObject("{=ml_msg_guard_joined_one}{AMOUNT} new guard joins your household. Guard strength: {GUARDS}/{GUARD_CAP}.")
            : new TextObject("{=ml_msg_guards_joined}{AMOUNT} new guards join your household. Guard strength: {GUARDS}/{GUARD_CAP}.");
        InformationManager.DisplayMessage(new InformationMessage(joined
            .SetTextVariable("AMOUNT", amount)
            .SetTextVariable("GUARDS", _guardCount)
            .SetTextVariable("GUARD_CAP", GuardCapacity())
            .ToString()));
        GameMenu.SwitchToMenu(HouseholdMenuId);
    }

    private bool GuardOrderCondition(MenuCallbackArgs args, string order)
    {
        if (_guardCount <= 0 || string.Equals(_guardOrder, order, StringComparison.Ordinal) || !AtOwnedManor(args)) return false;
        args.optionLeaveType = GameMenuOption.LeaveType.Continue;
        return true;
    }

    private void SetGuardOrder(string order)
    {
        _guardOrder = order;
        // Full sentences per order: lower-casing a translated noun is wrong in German and meaningless in Chinese.
        TextObject message = order switch
        {
            "patrol" => new TextObject("{=ml_msg_order_patrol}The household guard now follows village patrol orders."),
            "drill" => new TextObject("{=ml_msg_order_drill}The household guard now follows intensive drill orders."),
            _ => new TextObject("{=ml_msg_order_watch}The household guard now follows close watch orders.")
        };
        InformationManager.DisplayMessage(new InformationMessage(message.ToString()));
        GameMenu.SwitchToMenu(HouseholdMenuId);
    }

    private bool DismissGuardCondition(MenuCallbackArgs args) => _guardCount > 0 && AtOwnedManor(args);

    private void DismissGuard(MenuCallbackArgs args)
    {
        _guardCount--;
        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_guard_released}A household guard was released from service. {GUARDS} guards remain.").SetTextVariable("GUARDS", _guardCount).ToString()));
        GameMenu.SwitchToMenu(HouseholdMenuId);
    }

    private bool MoneyCondition(MenuCallbackArgs args, bool deposit, int amount)
    {
        if (!_storehouse || !AtOwnedManor(args)) return false;
        args.IsEnabled = deposit ? Hero.MainHero.Gold >= amount : _treasury >= amount;
        if (!args.IsEnabled) args.Tooltip = deposit
            ? new TextObject("{=ml_tip_not_enough_gold}You do not have enough denars.")
            : new TextObject("{=ml_tip_treasury_short}The manor treasury does not contain enough denars.");
        return true;
    }

    private bool CustomMoneyCondition(MenuCallbackArgs args, bool deposit)
    {
        if (!_storehouse || !AtOwnedManor(args)) return false;
        args.IsEnabled = deposit ? Hero.MainHero.Gold > 0 : _treasury > 0;
        return true;
    }

    private bool StorehouseCondition(MenuCallbackArgs args) => _storehouse && !_damaged && AtOwnedManor(args);

    private void OpenStash(MenuCallbackArgs args) => OpenStashScreen();

    internal void OpenStashScreen()
    {
        _stash ??= new ItemRoster();
        Helpers.InventoryScreenHelper.OpenScreenAsStash(_stash);
    }

    // switchMenu is false when called from inside the estate scene, where there is no game menu to return to.
    private void TransferTreasury(int amount, bool switchMenu = true)
    {
        Hero.MainHero.ChangeHeroGold(-amount);
        _treasury += amount;
        if (switchMenu) GameMenu.SwitchToMenu(StorehouseMenuId);
    }

    internal void ShowCustomTransfer(bool deposit, bool switchMenu = true)
    {
        int available = deposit ? Hero.MainHero.Gold : _treasury;
        // Whole titles rather than "{verb} manor funds": word order and case vary by language.
        string title = (deposit
            ? new TextObject("{=ml_inq_deposit_title}Deposit manor funds")
            : new TextObject("{=ml_inq_withdraw_title}Withdraw manor funds")).ToString();
        string action = (deposit
            ? new TextObject("{=ml_btn_deposit}Deposit")
            : new TextObject("{=ml_btn_withdraw}Withdraw")).ToString();
        string body = new TextObject("{=ml_inq_transfer_body}Enter an amount between 1 and {MAX} denars.").SetTextVariable("MAX", available).ToString();
        InformationManager.ShowTextInquiry(new TextInquiryData(title, body, true, true, action, Cancel, value =>
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int amount)) return;
            TransferTreasury(deposit ? amount : -amount, switchMenu);
        }, null, false, value =>
        {
            bool valid = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int amount) && amount > 0 && amount <= available;
            return new Tuple<bool, string>(valid, valid ? string.Empty : new TextObject("{=ml_inq_transfer_invalid}Enter a whole number between 1 and {MAX}.").SetTextVariable("MAX", available).ToString());
        }, string.Empty, string.Empty), true);
    }

    private bool WithdrawAllCondition(MenuCallbackArgs args)
    {
        if (!_storehouse || _treasury <= 0 || !AtOwnedManor(args)) return false;
        args.optionLeaveType = GameMenuOption.LeaveType.Continue;
        return true;
    }

    private void WithdrawAll(MenuCallbackArgs args)
    {
        Hero.MainHero.ChangeHeroGold(_treasury);
        _treasury = 0;
        GameMenu.SwitchToMenu(StorehouseMenuId);
    }

    private bool ProductionFocusCondition(MenuCallbackArgs args, string focus)
    {
        if (!_hasSteward || !AtOwnedManor(args) || string.Equals(_productionFocus, focus, StringComparison.Ordinal)) return false;
        args.IsEnabled = !_damaged;
        if (!args.IsEnabled) args.Tooltip = new TextObject("{=ml_tip_repair_before_plan}Repair the manor before changing its production plan.");
        return true;
    }

    private void SetProductionFocus(string focus)
    {
        _productionFocus = focus;
        TextObject message = focus switch
        {
            "crops" => new TextObject("{=ml_msg_focus_crops}The estate will now follow a cash crop plan."),
            "livestock" => new TextObject("{=ml_msg_focus_livestock}The estate will now follow a livestock plan."),
            _ => new TextObject("{=ml_msg_focus_mixed}The estate will now follow a mixed farming plan.")
        };
        InformationManager.DisplayMessage(new InformationMessage(message.ToString(), Colors.Green));
        GameMenu.SwitchToMenu(StewardshipMenuId);
    }

    private void Rest(MenuCallbackArgs args)
    {
        int day = (int)CampaignTime.Now.ToDays;
        if (_lastRestDay == day) { InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_already_rested}You have already taken a full rest here today.").ToString())); return; }
        int heal = (_damaged ? 10 : 25) + (_hasPhysician ? 20 : 0);
        Hero.MainHero.HitPoints = Math.Min(Hero.MainHero.MaxHitPoints, Hero.MainHero.HitPoints + heal);
        _lastRestDay = day;
        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_rested}You rest safely and recover {AMOUNT} hit points.").SetTextVariable("AMOUNT", heal).ToString()));
    }

    private bool DefenseCondition(MenuCallbackArgs args)
    {
        if (!_underThreat || _defenseAttempted || !AtOwnedManor(args)) return false;
        args.IsEnabled = _guardCount + StationedHealthy > 0;
        if (!args.IsEnabled) args.Tooltip = new TextObject("{=ml_tip_no_guard}You have no household guard with whom to mount a defense.");
        args.optionLeaveType = GameMenuOption.LeaveType.HostileAction;
        return true;
    }

    private void StartDefense(MenuCallbackArgs args)
    {
        _defenseAttempted = true;
        if (TryStartPlayableDefense()) return;

        ResolveCalculatedDefense();
    }

    private bool TryStartPlayableDefense()
    {
        try
        {
            Settlement? settlement = Settlement.CurrentSettlement;
            LocationComplex? complex = settlement?.LocationComplex;
            CharacterObject? guardTroop = GetGuardTroop(settlement);
            CharacterObject? raiderTroop = MBObjectManager.Instance.GetObject<CharacterObject>("looter")
                ?? MBObjectManager.Instance.GetObject<CharacterObject>("mountain_bandits_bandit");
            if (complex == null || guardTroop == null || raiderTroop == null) return false;

            Location? villageCenter = complex.GetLocationWithId("village_center");
            if (villageCenter == null) return false;

            TroopRoster defenders = TroopRoster.CreateDummyTroopRoster();
            if (_guardCount > 0) defenders.AddToCounts(guardTroop, _guardCount);
            AddStationedDefenders(defenders);
            TroopRoster attackers = TroopRoster.CreateDummyTroopRoster();
            // Raiders come in strength to match the household, hired and stationed alike.
            int attackerCount = Math.Max(8, 6 + _estateTier * 3 + ((_guardCount + StationedHealthy) / 2));
            attackers.AddToCounts(raiderTroop, attackerCount);

            ManorDefenseMissionState.ArmDefense(BuildSnapshot(settlement));
            IMission? mission = CampaignMission.OpenAlleyFightMission(ManorSceneId, 0, villageCenter, defenders, attackers);
            if (mission != null) return true;
            ManorDefenseMissionState.CancelArm();
        }
        catch (Exception ex)
        {
            ManorDefenseMissionState.CancelArm();
            Debug.Print($"[ManorLord] Playable defense unavailable; using calculated resolution: {ex}");
        }
        return false;
    }

    private void ResolveCalculatedDefense()
    {
        int trainingBonus = _guardExperience >= 1000 ? 35 : _guardExperience >= 400 ? 22 : _guardExperience >= 100 ? 10 : 0;
        int defenseScore = (_guardCount * 6) + StationedDefenseScore() + trainingBonus + (_palisade ? 25 : 0) + (_hasCaptain ? 20 : 0) + GuardOrderDefenseBonus();
        int roll = MBRandom.RandomInt(100);
        if (roll < Math.Min(90, defenseScore))
        {
            _underThreat = false;
            _manorProtectedThisRaid = true;
            InformationManager.ShowInquiry(new InquiryData(DefenseWonTitle, new TextObject("{=ml_defense_won_calc}Under your direction, the household guard holds the estate and drives the raiders away. The manor and its stores are safe.").ToString(), true, false, Victory, string.Empty, null, null), true);
            RewardDefenseWithVillageGratitude();
        }
        else
        {
            _underThreat = false;
            _manorProtectedThisRaid = false;
            ApplyDefenseDefeat();
        }
        _defenseAttempted = false;
        GameMenu.SwitchToMenu(MenuId);
    }

    private CharacterObject? GetGuardTroop(Settlement? settlement)
    {
        CharacterObject? troop = settlement?.Culture?.BasicTroop;
        int upgrades = _guardExperience >= 1000 ? 3 : _guardExperience >= 400 ? 2 : _guardExperience >= 100 ? 1 : 0;
        while (troop != null && upgrades-- > 0 && troop.UpgradeTargets.Length > 0)
            troop = troop.UpgradeTargets[0];
        return troop;
    }

    private bool RepairCondition(MenuCallbackArgs args)
    {
        if (!_damaged || !AtOwnedManor(args)) return false;
        args.IsEnabled = string.IsNullOrEmpty(_activeProject) && Hero.MainHero.Gold >= RepairCost();
        return true;
    }

    private void Repair(MenuCallbackArgs args)
    {
        int cost = RepairCost();
        Hero.MainHero.ChangeHeroGold(-cost);
        _activeProject = "repair";
        _activeProjectCost = cost;
        _projectCompletionDay = CampaignTime.Now.ToDays + 2d;
        _recordedExpenses += cost;
        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_repairs_begun}Repairs have begun and should take two days.").ToString(), Colors.Green));
        GameMenu.SwitchToMenu(ImprovementsMenuId);
    }

    private void ConfirmSale(MenuCallbackArgs args)
    {
        int value = SalePrice();
        TextObject body = new TextObject("{=ml_inq_sell_body}Sell your manor near {SETTLEMENT} for {VALUE} denars? Stored funds will be returned, but the household guard and estate staff will disband.")
            .SetTextVariable("SETTLEMENT", ManorSettlement?.Name ?? Empty)
            .SetTextVariable("VALUE", value);
        InformationManager.ShowInquiry(new InquiryData(new TextObject("{=ml_inq_sell_title}Sell the manor?").ToString(), body.ToString(), true, true, new TextObject("{=ml_btn_sell}Sell").ToString(), new TextObject("{=ml_btn_keep_it}Keep it").ToString(), () => Sell(value), null), true);
    }

    private void Sell(int value)
    {
        if (_stash != null && _stash.Count > 0)
        {
            InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_goods_remain}Stored goods remain in the manor. Empty the item storehouse before selling.").ToString()));
            GameMenu.SwitchToMenu(MenuId);
            return;
        }
        Hero.MainHero.ChangeHeroGold(value + _treasury);
        ReturnStationedToParty();
        _storeProduce = false; _lastStoredValue = 0; _daysSinceDues = 0; _incomeSinceDues = 0;
        _villageId = null; _purchasePrice = 0; _treasury = 0; _guardCount = 0; _guardExperience = 0;
        _palisade = _trainingField = _storehouse = _guardQuarters = _damaged = false;
        _manorName = null; _activeProject = null; _projectCompletionDay = 0d; _hasSteward = false; _supplies = 0;
        _underThreat = false; _defenseAttempted = false; _manorProtectedThisRaid = false; _raidResolvedByDeadline = false; _raidDeadlineDay = 0d;
        _productionFocus = "mixed"; _activeProjectCost = 0; _lastGrossIncome = 0; _lastWages = 0; _lastSupplyChange = 0;
        _recordedIncome = 0; _recordedExpenses = 0; _daysOwned = 0;
        _estateTier = 1; _hasCaptain = false; _hasPhysician = false; _mill = false; _orchard = false; _workshop = false; _guardOrder = "watch";
        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_sold}The property was sold for {VALUE} denars.").SetTextVariable("VALUE", value).ToString()));
        GameMenu.SwitchToMenu("village");
    }

    private void OnVillageBeingRaided(Village village)
    {
        if (village?.Settlement?.StringId != _villageId) return;
        _underThreat = true;
        _defenseAttempted = false;
        _manorProtectedThisRaid = false;
        _raidDeadlineDay = CampaignTime.Now.ToDays + 2d;
        _raidResolvedByDeadline = false;
        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_raid_warning}Your manor near {SETTLEMENT} is threatened by raiders!").SetTextVariable("SETTLEMENT", village!.Name).ToString(), Colors.Red));
    }

    private void OnVillageLooted(Village village)
    {
        if (village?.Settlement?.StringId != _villageId) return;
        _underThreat = false;
        if (_raidResolvedByDeadline) { _raidResolvedByDeadline = false; return; }
        if (_manorProtectedThisRaid || _pendingDefenseResult == true)
        {
            _pendingDefenseResult = null;
            _defenseAttempted = false;
            _manorProtectedThisRaid = false;
            InformationManager.ShowInquiry(new InquiryData(new TextObject("{=ml_defended_title}Manor defended").ToString(), new TextObject("{=ml_defended_body}Your stand broke the raiders before they could burn or loot the manor.").ToString(), true, false, Victory, string.Empty, null, null), true);
            return;
        }
        _damaged = true;
        int protectedPercent = _palisade ? 75 : 50;
        int lost = _treasury * (100 - protectedPercent) / 100;
        _treasury -= lost;
        int trainingProtection = _guardExperience >= 1000 ? 2 : _guardExperience >= 400 ? 1 : 0;
        int staffProtection = (_hasCaptain ? 1 : 0) + (_hasPhysician ? 1 : 0);
        int guardLoss = Math.Min(_guardCount, Math.Max(0, (_palisade ? 2 : 4) - trainingProtection - staffProtection));
        _guardCount -= guardLoss;
        int goodsLost = LootStoredGoods(protectedPercent);
        TextObject raidedBody = new TextObject("{=ml_raided_body}The estate was damaged. Raiders stole {GOLD} denars, carried off {GOODS} stored items, and {GUARDS} household guards were lost.")
            .SetTextVariable("GOLD", lost)
            .SetTextVariable("GOODS", goodsLost)
            .SetTextVariable("GUARDS", guardLoss);
        TextObject stationedLoss = ApplyStationedCasualties(_palisade ? 0.05f : 0.1f, 0.3f);
        InformationManager.ShowInquiry(new InquiryData(new TextObject("{=ml_raided_title}Your manor was raided").ToString(), WithLine(raidedBody, stationedLoss), true, false, new TextObject("{=ml_btn_understood}Understood").ToString(), string.Empty, null, null), true);
        _defenseAttempted = false;
    }

    private void OnVillageBecomeNormal(Village village)
    {
        if (village?.Settlement?.StringId != _villageId) return;
        _underThreat = false;
        _defenseAttempted = false;
        _manorProtectedThisRaid = false;
        _raidResolvedByDeadline = false;
    }

    private void OnTick(float dt)
    {
        if (_underThreat && !_defenseAttempted && CampaignTime.Now.ToDays >= _raidDeadlineDay)
        {
            _underThreat = false;
            _raidResolvedByDeadline = true;
            ApplyDefenseDefeat();
        }
        if (ManorDefenseMissionState.TryTakeResult(out bool victory))
            _pendingDefenseResult = victory;
        if (!_pendingDefenseResult.HasValue) return;
        bool result = _pendingDefenseResult.Value;
        _pendingDefenseResult = null;
        if (result)
        {
            _underThreat = false;
            _manorProtectedThisRaid = true;
            InformationManager.ShowInquiry(new InquiryData(DefenseWonTitle, new TextObject("{=ml_defense_won_played}You and your household guard drove the raiders from the estate. Your manor and its stores are safe.").ToString(), true, false, Victory, string.Empty, null, null), true);
            RewardDefenseWithVillageGratitude();
        }
        else
        {
            _underThreat = false;
            _manorProtectedThisRaid = false;
            ApplyDefenseDefeat();
        }
        _defenseAttempted = false;
        GameMenu.SwitchToMenu(MenuId);
    }

    private void ApplyDefenseDefeat()
    {
        _damaged = true;
        int protectedPercent = _palisade ? 75 : 50;
        int goldLost = _treasury * (100 - protectedPercent) / 100;
        _treasury -= goldLost;
        int guardsLost = Math.Min(_guardCount, Math.Max(1, _guardCount / (_hasPhysician ? 3 : 2)));
        _guardCount -= guardsLost;
        int goodsLost = LootStoredGoods(protectedPercent);
        TextObject lostBody = new TextObject("{=ml_defense_lost_body}The defenders were overwhelmed. {GUARDS} guards fell, {GOLD} denars and {GOODS} stored items were lost, and the estate was damaged.")
            .SetTextVariable("GUARDS", guardsLost)
            .SetTextVariable("GOLD", goldLost)
            .SetTextVariable("GOODS", goodsLost);
        TextObject stationedLoss = ApplyStationedCasualties(_hasPhysician ? 1f / 3f : 0.5f, 0.3f);
        InformationManager.ShowInquiry(new InquiryData(new TextObject("{=ml_defense_lost_title}Manor defense lost").ToString(), WithLine(lostBody, stationedLoss), true, false, new TextObject("{=ml_btn_continue}Continue").ToString(), string.Empty, null, null), true);
    }

    private void OnDailyTick()
    {
        if (!OwnsManor) return;
        _daysOwned++;
        _lastGrossIncome = 0;
        _lastWages = 0;
        int suppliesBefore = _supplies;
        CompleteProjectIfReady();
        int earnedToday = 0;
        _lastStoredValue = 0;
        if (_hasSteward && !_damaged)
        {
            GetProjectedDailyEstate(out int grossIncome, out int producedSupplies);
            if (StoringProduce)
            {
                // The produce goes into the storehouse as goods instead of being sold for the treasury.
                _lastStoredValue = StoreDailyProduce();
                earnedToday = _lastStoredValue;
            }
            else
            {
                _treasury += grossIncome;
                _lastGrossIncome = grossIncome;
                _recordedIncome += grossIncome;
                earnedToday = grossIncome;
            }
            _supplies += producedSupplies;
        }
        int supplyUse = DailySupplyUse();
        bool suppliesShort = false;
        if (supplyUse > 0 && _supplies >= supplyUse) _supplies -= supplyUse;
        else if (supplyUse > 0)
        {
            _supplies = 0;
            suppliesShort = true;
            _guardExperience = Math.Max(0, _guardExperience - _guardCount);
        }
        StationedDailyTick(suppliesShort);
        VillageDailyTick(earnedToday);
        if (_guardCount > 0 && _trainingField && !_damaged)
        {
            int dailyTraining = _guardCount * 2;
            if (_hasCaptain) dailyTraining += _guardCount;
            if ((_guardOrder ?? "watch") == "drill") dailyTraining += _guardCount * 2;
            _guardExperience += dailyTraining;
        }
        int wages = TotalDailyWages();
        _lastWages = wages;
        _recordedExpenses += wages;
        _lastSupplyChange = _supplies - suppliesBefore;
        if (wages <= 0) return;
        if (_treasury >= wages)
        {
            _treasury -= wages;
            return;
        }
        int shortfall = wages - _treasury;
        _treasury = 0;
        if (Hero.MainHero.Gold >= shortfall)
        {
            Hero.MainHero.ChangeHeroGold(-shortfall);
            return;
        }
        if (_guardCount > 0)
        {
            _guardCount--;
            InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_guard_deserted}An unpaid manor guard has deserted. {GUARDS} guards remain.").SetTextVariable("GUARDS", _guardCount).ToString(), Colors.Red));
        }
        else if (TryStationedDesertion())
        {
            // A stationed soldier left instead.
        }
        else if (_hasPhysician)
        {
            _hasPhysician = false;
            InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_physician_left}The unpaid estate physician has left your service.").ToString(), Colors.Red));
        }
        else if (_hasCaptain)
        {
            _hasCaptain = false;
            InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_captain_left}The unpaid household captain has left your service.").ToString(), Colors.Red));
        }
        else if (_hasSteward)
        {
            _hasSteward = false;
            InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_steward_left}The unpaid estate steward has left your service.").ToString(), Colors.Red));
        }
    }

    private void CompleteProjectIfReady()
    {
        if (string.IsNullOrEmpty(_activeProject) || CampaignTime.Now.ToDays < _projectCompletionDay) return;
        string completed = _activeProject!;
        switch (completed)
        {
            case "palisade": _palisade = true; break;
            case "training": _trainingField = true; break;
            case "storehouse": _storehouse = true; break;
            case "quarters": _guardQuarters = true; break;
            case "tier2": _estateTier = Math.Max(_estateTier, 2); break;
            case "tier3": _estateTier = Math.Max(_estateTier, 3); break;
            case "mill": _mill = true; break;
            case "orchard": _orchard = true; break;
            case "workshop": _workshop = true; break;
            case "repair": _damaged = false; break;
        }
        _activeProject = null;
        _activeProjectCost = 0;
        _projectCompletionDay = 0d;
        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=ml_msg_work_complete}Work at {MANOR_NAME} is complete: {PROJECT}.")
            .SetTextVariable("MANOR_NAME", ManorDisplayName())
            .SetTextVariable("PROJECT", ProjectDisplayName(completed))
            .ToString(), Colors.Green));
    }

    private int LootStoredGoods(int protectedPercent)
    {
        if (_stash == null || _stash.Count == 0) return 0;
        int removed = 0;
        for (int i = _stash.Count - 1; i >= 0; i--)
        {
            ItemRosterElement element = _stash.GetElementCopyAtIndex(i);
            int take = element.Amount * (100 - protectedPercent) / 100;
            if (take == 0 && element.Amount > 1) take = 1;
            if (take <= 0) continue;
            _stash.AddToCounts(element.EquipmentElement, -take);
            removed += take;
        }
        return removed;
    }

    private int SalePrice()
    {
        int improvements = (_palisade ? 6000 : 0) + (_trainingField ? 8000 : 0) + (_storehouse ? 5000 : 0) + (_guardQuarters ? 7000 : 0)
            + (_estateTier >= 2 ? 10000 : 0) + (_estateTier >= 3 ? 18000 : 0) + (_mill ? 6500 : 0) + (_orchard ? 5500 : 0) + (_workshop ? 9000 : 0);
        int value = (_purchasePrice * 70 / 100) + (improvements * 50 / 100);
        return _damaged ? value * 75 / 100 : value;
    }

    private void GetProjectedDailyEstate(out int income, out int producedSupplies)
    {
        // Income is the value of the day's produce (see ProduceLines), whether it is sold or stored.
        income = 0;
        foreach (ProduceLine line in ProduceLines()) income += line.Value;
        producedSupplies = (_productionFocus ?? "mixed") switch
        {
            "crops" => 0,
            "livestock" => 3,
            _ => 1
        };
        if (_orchard) producedSupplies += 2;
    }

    private TextObject ProductionFocusDisplayName() => (_productionFocus ?? "mixed") switch
    {
        "crops" => new TextObject("{=ml_focus_crops_name}Cash crops"),
        "livestock" => new TextObject("{=ml_focus_livestock_name}Livestock"),
        _ => new TextObject("{=ml_focus_mixed_name}Mixed farming")
    };

    private int ProjectDaysRemaining() => string.IsNullOrEmpty(_activeProject) ? 0 : Math.Max(0, (int)Math.Ceiling(_projectCompletionDay - CampaignTime.Now.ToDays));

    private int ProjectRefund() => ProjectRecordedCost() / 2;

    private int ProjectRecordedCost()
    {
        if (_activeProjectCost > 0) return _activeProjectCost;
        return (_activeProject ?? string.Empty) switch
        {
            "palisade" => 6000,
            "training" => 8000,
            "storehouse" => 5000,
            "quarters" => 7000,
            "tier2" => 10000,
            "tier3" => 18000,
            "mill" => 6500,
            "orchard" => 5500,
            "workshop" => 9000,
            "repair" => RepairCost(),
            _ => 0
        };
    }

    private static string FormatSigned(int value) => value > 0 ? $"+{value}" : value.ToString(CultureInfo.InvariantCulture);

    private int GuardCapacity() => !_guardQuarters ? 0 : _estateTier >= 3 ? 20 : _estateTier >= 2 ? 15 : 10;
    private int GuardDailyWages() => _guardCount * (5 + ((_guardOrder ?? "watch") == "patrol" ? 2 : (_guardOrder ?? "watch") == "drill" ? 1 : 0));
    private int StaffDailyWages() => (_hasSteward ? 20 : 0) + (_hasCaptain ? 20 : 0) + (_hasPhysician ? 15 : 0);
    private int TotalDailyWages() => GuardDailyWages() + StationedDailyWages() + StaffDailyWages();
    private int DailySupplyUse() => (_guardCount <= 0 ? 0 : Math.Max(1, (_guardCount + 1) / 2) + ((_guardOrder ?? "watch") == "patrol" ? 1 : 0)) + StationedSupplyUse();
    private int GuardOrderDefenseBonus() => (_guardOrder ?? "watch") switch { "patrol" => 20, "drill" => 5, _ => 12 };
    private TextObject GuardOrderDisplayName() => (_guardOrder ?? "watch") switch
    {
        "patrol" => new TextObject("{=ml_order_patrol_name}Village patrols"),
        "drill" => new TextObject("{=ml_order_drill_name}Intensive drills"),
        _ => new TextObject("{=ml_order_watch_name}Close watch")
    };

    private TextObject EstateTierDisplayName() => _estateTier >= 3
        ? new TextObject("{=ml_tier_grand}Grand estate")
        : _estateTier >= 2 ? new TextObject("{=ml_tier_landed}Landed estate") : new TextObject("{=ml_tier_manor}Manor holding");

    private TextObject StaffSummary() => new TextObject("{=ml_staff_summary}steward {STEWARD}, captain {CAPTAIN}, physician {PHYSICIAN}")
        .SetTextVariable("STEWARD", YesNo(_hasSteward))
        .SetTextVariable("CAPTAIN", YesNo(_hasCaptain))
        .SetTextVariable("PHYSICIAN", YesNo(_hasPhysician));

    private static TextObject YesNo(bool value) => value
        ? new TextObject("{=ml_w_yes}yes")
        : new TextObject("{=ml_w_no}no");

    private TextObject ProductionBuildingSummary() => JoinBuilt(
        (_mill, "{=ml_b_mill}mill"),
        (_orchard, "{=ml_b_orchard}orchard"),
        (_workshop, "{=ml_b_workshop}workshop"));

    /// <summary>Joins the names of the completed entries with the translatable list separator, or "none".</summary>
    private static TextObject JoinBuilt(params (bool Built, string Name)[] entries)
    {
        var names = new List<string>();
        foreach ((bool built, string name) in entries)
            if (built) names.Add(new TextObject(name).ToString());
        return names.Count == 0 ? new TextObject("{=ml_w_none}none") : new TextObject(string.Join(ListSeparator, names));
    }

    private int RepairCost() => 1500 + ((_palisade ? 1 : 0) + (_trainingField ? 1 : 0) + (_storehouse ? 1 : 0) + (_guardQuarters ? 1 : 0) + (_mill ? 1 : 0) + (_orchard ? 1 : 0) + (_workshop ? 1 : 0)) * 750 + Math.Max(0, _estateTier - 1) * 1000;
    private static int CalculatePrice(Settlement? village) => village?.Village == null ? 10000 : Math.Max(8000, Math.Min(20000, 7000 + (int)village.Village.Hearth));
    private TextObject UpgradeSummary() => JoinBuilt(
        (_palisade, "{=ml_b_palisade}palisade"),
        (_trainingField, "{=ml_b_training_field}training field"),
        (_storehouse, "{=ml_b_storehouse}storehouse"),
        (_guardQuarters, "{=ml_b_quarters}guard quarters"),
        (_mill, "{=ml_b_mill}mill"),
        (_orchard, "{=ml_b_orchard}orchard"),
        (_workshop, "{=ml_b_workshop}workshop"));

    private TextObject GuardTrainingSummary() => _guardExperience >= 1000
        ? new TextObject("{=ml_train_veteran}veteran")
        : _guardExperience >= 400 ? new TextObject("{=ml_train_trained}trained")
        : _guardExperience >= 100 ? new TextObject("{=ml_train_drilled}drilled")
        : new TextObject("{=ml_train_green}green");

    // The player-chosen estate name is user input, so it is passed through untranslated.
    private TextObject ManorDisplayName() => string.IsNullOrWhiteSpace(_manorName)
        ? new TextObject("{=ml_w_your_manor}Your manor")
        : new TextObject(_manorName!);

    private TextObject ProjectSummary() => string.IsNullOrEmpty(_activeProject)
        ? Empty
        : new TextObject("{=ml_project_summary}Project underway: {PROJECT}, due in {DAYS} days.")
            .SetTextVariable("PROJECT", ProjectDisplayName(_activeProject!))
            .SetTextVariable("DAYS", Math.Max(0, (int)Math.Ceiling(_projectCompletionDay - CampaignTime.Now.ToDays)));

    private static TextObject ProjectDisplayName(string id) => id switch
    {
        "palisade" => new TextObject("{=ml_p_palisade}palisade"),
        "training" => new TextObject("{=ml_p_training}training field"),
        "storehouse" => new TextObject("{=ml_p_storehouse}storehouse"),
        "quarters" => new TextObject("{=ml_p_quarters}guard quarters"),
        "tier2" => new TextObject("{=ml_p_tier2}landed estate expansion"),
        "tier3" => new TextObject("{=ml_p_tier3}grand estate expansion"),
        "mill" => new TextObject("{=ml_p_mill}estate mill"),
        "orchard" => new TextObject("{=ml_p_orchard}orchard"),
        "workshop" => new TextObject("{=ml_p_workshop}estate workshop"),
        "repair" => new TextObject("{=ml_p_repair}estate repairs"),
        _ => new TextObject(id)
    };
}
