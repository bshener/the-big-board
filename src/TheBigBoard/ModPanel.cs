using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BAModAPI;
using BigAmbitions.Factories.Recipes;
using static TheBigBoard.DataGrid;

namespace TheBigBoard;

/// <summary>
/// Vanilla-style panel: compact text tabs + dark data grid (light header row,
/// dark rows, right-aligned numbers, scrollable). SALES has two sub-tabs:
/// SEARCH (product sales search per store + total, from
/// BuildingRegistration.orderHistory) and BEST SELLERS (per-product top list
/// with sold/produced/revenue/profit/top-store).
/// Tab 2: current stock per player building (warehouses, factories, stores)
/// for a product name, via Helpers.BuildingHelper.CountTotalResourcesInStock,
/// plus TODAY / YESTERDAY / LAST WEEK grouped columns with IN / OUT flows:
/// warehouses from Entities.Warehouse.deliveryTransactions (+in / -out),
/// other buildings OUT from orderHistory (+ today's live paid orders),
/// IN untracked there (game keeps no inbound history per store).
/// Tab 3: placeholder.
/// Lives under FullMenu.appsContainer.
/// </summary>
public static class ModPanel
{
    const string PanelName = "TheBigBoardPanel";
    // Display version (keep in sync with the csproj) + supporter names
    // (Patreon/BMC tiers). Updated with every release.
    const string ModVersion = "0.9.0 beta";
    static readonly string[] Supporters = { };
    static string SupportersLine()
    {
        if (Supporters == null || Supporters.Length == 0)
            return "v" + ModVersion + " - supporters: be the first";
        return "v" + ModVersion + " - thanks: " + string.Join(", ", Supporters);
    }
    static GameObject _panel;
    static GameObject[] _pages = new GameObject[6];
    static GameObject[] _tabUnderlines = new GameObject[6];
    static TMPro.TMP_Text[] _tabLabels = new TMPro.TMP_Text[6];
    static TMPro.TMP_Text _queryText;
    static TMPro.TMP_Text _matchText;
    static SalesTable _salesTable;
    static Transform _productList;
    static Dictionary<string, Dictionary<string, long>> _lastStores;
    static Dictionary<string, KeyValuePair<string, long>> _lastProducts;
    static long _lastTotal;
    static string _lastSelectedKey; // null = ALL products
    static string _lastRaw = "";
    static ModContext _lastContext;

    // --- SALES sub-tabs: 0 = SEARCH (existing product search), 1 = BEST
    // SELLERS (per-product top list). The search view keeps its layout; the
    // best view has its own pills + store strip + table below.
    static int _salesSubTab;
    static GameObject _salesSearchView;
    static GameObject _salesBestView;
    static readonly Image[] _salesSubTabBgs = new Image[2];
    static readonly TMPro.TMP_Text[] _salesSubTabLabels = new TMPro.TMP_Text[2];
    static ModContext _salesContext;

    // --- BEST SELLERS state: flat hit list (retail sales with money +
    // factory production), re-aggregated on pill/store switches without
    // re-scanning. Produced hits carry produced=true and no money (factory
    // transfer pricing is not retail profit).
    static BestSellersTable _bestTable;
    static Transform _bestStoreList;
    static TMPro.TMP_Text _bestStoreText;
    static GameObject _bestPillBar;
    static readonly List<TimePill> _bestTimePills = new List<TimePill>();
    static List<BestHit> _bestHits;
    static int _bestRange;
    static string _bestStore; // null = ALL stores
    static int _bestDay;
    // Fallback unit costs (pkey -> unit cost, -1 = none): the game records
    // zero cost for own-factory goods, so profit would equal revenue.
    static readonly Dictionary<string, float> _bestFallbackCost = new Dictionary<string, float>();

    sealed class BestHit
    {
        public string pkey;
        public string raw;
        public string store;
        public int day;
        public long amt;
        public double revenue;
        public double cost;
        public bool produced;
    }

    sealed class BestAgg
    {
        public string raw;
        public long sold;
        public double revenue;
        public double cost;
        public long produced;
        public Dictionary<string, long> perStore = new Dictionary<string, long>();
        public Dictionary<string, double> perStoreRev = new Dictionary<string, double>();
        public Dictionary<string, double> perStoreCost = new Dictionary<string, double>();
    }

    const string FactoryBusinessType = "ba:businesstype_factory";

    // --- TAB 2 (stock) state: mirrors the sales tab, but the data source is
    // current stock in the player's own buildings (warehouses, factories,
    // stores) instead of order history. No time filter.
    static TMPro.TMP_Text _stockQueryText;
    static TMPro.TMP_Text _stockMatchText;
    static StockTable _stockTable;
    static Transform _stockProductList;
    static Dictionary<string, Dictionary<string, long>> _stockWarehouses;
    static Dictionary<string, KeyValuePair<string, long>> _stockProducts;
    static long _stockTotal;
    static string _stockSelectedKey; // null = ALL products
    static string _stockRaw = "";
    static int _stockWarehouseCount;
    static int _stockDay;
    // building -> pkey -> flows. Warehouses: deliveryTransactions
    // (positive = IN, negative = OUT). Others: OUT from sales history.
    static Dictionary<string, Dictionary<string, StockMoves>> _stockMoves;
    // Buildings whose IN is tracked via deliveryTransactions
    // (Entities.Warehouse). Others show numbers once the ledger covers them,
    // "..." while history is still accumulating.
    static HashSet<string> _stockIsWarehouse;
    // Raw-material relevance: pkey -> building names that need/order it
    // (factory recipe, importer assignment, wholesale contract, logistics
    // target). Renders zero-stock rows for the selected product.
    static Dictionary<string, HashSet<string>> _stockRelevant;
    static string _stockDiag = ""; // per-search attribution diagnostics for the log

    public sealed class StockMoves
    {
        public long inToday;
        public long outToday;
        public long inYest;
        public long outYest;
        public long inWeek;
        public long outWeek;
        // Ledger coverage per bucket: exact IN was derived from snapshots.
        public bool covT;
        public bool covY;
        public bool covW;
    }

    public static void AddMoves(StockMoves dst, StockMoves src)
    {
        if (dst == null || src == null) return;
        dst.inToday += src.inToday;
        dst.outToday += src.outToday;
        dst.inYest += src.inYest;
        dst.outYest += src.outYest;
        dst.inWeek += src.inWeek;
        dst.outWeek += src.outWeek;
        dst.covT |= src.covT;
        dst.covY |= src.covY;
        dst.covW |= src.covW;
    }

    static TMPro.TMP_Text _dayLabel;
    static Transform _timeBar;
    static readonly List<TimePill> _timePills = new List<TimePill>();
    static List<SaleHit> _lastHits;
    static int _currentDay;
    static int _timeRange; // 0 = today, 1 = yesterday, 2 = last week

    sealed class SaleHit
    {
        public string store;
        public string pkey; // lowercase product key
        public string raw; // raw itemName for display lookup
        public int day;
        public long amt;
    }

    sealed class TimePill
    {
        public int index;
        public Image bg;
        public TMPro.TMP_Text label;
    }
    static readonly Color TabIdle = DataGrid.TabIdle;
    static readonly Color TabActive = DataGrid.TabActive;
    static readonly Color HeaderBg = DataGrid.HeaderBg;
    static readonly Color HeaderInk = DataGrid.HeaderInk;
    static readonly Color RowBg = DataGrid.RowBg;
    static readonly Color RowBgAlt = DataGrid.RowBgAlt;
    static readonly Color TotalBg = DataGrid.TotalBg;
    static readonly Color InkLight = DataGrid.InkLight;
    static readonly Color InkGrey = DataGrid.InkGrey;
    static readonly Color Accent = DataGrid.Accent;
    static readonly Color ProfitGreen = DataGrid.ProfitGreen;
    static readonly Color LossRed = DataGrid.LossRed;

    public static void Toggle(UI.Smartphone.FullMenu menu, ModContext context)
    {
        if (menu == null) return;
        Ensure(menu, context);
        if (_panel.activeSelf)
        {
            menu.Toggle(false);
            FactoryPanel.SetVisible(false);
            EmployeePanel.SetVisible(false);
            BuildingsPanel.SetVisible(false);
            BusinessesPanel.SetVisible(false);
            return;
        }
        if (!UI.Smartphone.FullMenu.IsOpen) menu.Toggle(true);
        foreach (Transform child in menu.appsContainer)
            child.gameObject.SetActive(child.gameObject == _panel);
        _panel.SetActive(true);
        if (menu.appNameLabel != null) menu.appNameLabel.Key = PhoneButtonInstaller.TitleLocaleKey;
        // BizMan/schedule/staff changes happen while our panel is closed:
        // drop cached factory plans so the next view is fresh (4ms).
        FactoryPanel.InvalidatePlanCache();
        bool factoryVisible = _pages[3] != null && _pages[3].activeSelf;
        FactoryPanel.SetVisible(factoryVisible);
        if (factoryVisible) FactoryPanel.Refresh();
        bool empVisible = _pages[2] != null && _pages[2].activeSelf;
        EmployeePanel.SetVisible(empVisible);
        if (empVisible) EmployeePanel.Refresh();
        bool oppVisible = _pages[0] != null && _pages[0].activeSelf;
        BuildingsPanel.SetVisible(oppVisible);
        if (oppVisible) BuildingsPanel.Refresh();
        bool bizVisible = _pages[1] != null && _pages[1].activeSelf;
        BusinessesPanel.SetVisible(bizVisible);
        if (bizVisible) BusinessesPanel.Refresh();
        context?.Logger.Info("The Big Board panel opened.");
    }

    static void Ensure(UI.Smartphone.FullMenu menu, ModContext context)
    {
        if (_panel != null) return;
        try
        {
            BuildPanel(menu, context);
        }
        catch (System.Exception ex)
        {
            context?.Logger.Warn("The Big Board panel build failed: " + ex.GetType().Name + ": " + ex.Message);
            if (_panel != null) UnityEngine.Object.Destroy(_panel);
            _panel = null;
            _salesTable = null;
            return;
        }
        _panel.SetActive(false);
        context?.Logger.Info("The Big Board panel built.");
    }

    static void BuildPanel(UI.Smartphone.FullMenu menu, ModContext context)
    {
        _panel = new GameObject(PanelName);
        _panel.transform.SetParent(menu.appsContainer, false);
        Stretch(_panel);

        var header = Text("Header", "THE BIG BOARD", 44, TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, Color.white);
        header.transform.SetParent(_panel.transform, false);
        Anchor(header.gameObject, 0f, 1f, 1f, 1f, 80f, -140f, -60f, -60f);

        // Supporters line (Patreon/BMC promise): names live here, updated
        // with every release. Keep in sync with the csproj Version.
        var supporters = Text("Supporters", SupportersLine(), 24, TMPro.HorizontalAlignmentOptions.Right, TMPro.VerticalAlignmentOptions.Middle, InkGrey);
        supporters.transform.SetParent(_panel.transform, false);
        Anchor(supporters.gameObject, 1f, 1f, 1f, 1f, -1100f, -140f, -60f, -60f);
        supporters.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        supporters.overflowMode = TMPro.TextOverflowModes.Ellipsis;

        var tabBar = new GameObject("TabBar");
        tabBar.transform.SetParent(_panel.transform, false);
        Anchor(tabBar, 0f, 1f, 0f, 1f, 80f, -230f, 1900f, -150f);
        var layout = tabBar.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 50f;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        string[] tabNames = { "BUILDINGS", "BUSINESSES", "EMPLOYEE", "FACTORY", "SALES", "STOCK" };
        for (int i = 0; i < 6; i++)
        {
            int index = i; // capture
            var tab = new GameObject($"Tab{i + 1}");
            tab.transform.SetParent(tabBar.transform, false);
            var hit = tab.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            var btn = tab.AddComponent<Button>();
            btn.targetGraphic = hit;
            var size = tab.AddComponent<LayoutElement>();
            size.preferredWidth = 270f;

            var label = Text($"Tab{i + 1}Label", tabNames[i], 30, TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, i == 0 ? TabActive : TabIdle);
            label.transform.SetParent(tab.transform, false);
            Stretch(label.gameObject);
            label.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            label.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            _tabLabels[i] = label;

            var underline = new GameObject("Underline");
            underline.transform.SetParent(tab.transform, false);
            var bar = underline.AddComponent<Image>();
            bar.color = Color.white;
            var r = Rect(underline);
            r.anchorMin = new Vector2(0, 0);
            r.anchorMax = new Vector2(1, 0);
            r.offsetMin = new Vector2(0, 0);
            r.offsetMax = new Vector2(0, 6);
            underline.SetActive(i == 0);
            _tabUnderlines[i] = underline;

            btn.onClick.AddListener(() => SelectTab(index));
        }

        for (int i = 0; i < 6; i++)
        {
            var page = new GameObject($"Page{i + 1}");
            page.transform.SetParent(_panel.transform, false);
            Anchor(page, 0f, 0f, 1f, 1f, 80f, 80f, -80f, -260f);
            page.SetActive(i == 0);
            _pages[i] = page;
        }
        BuildingsPanel.BuildPage(_pages[0], context);
        BusinessesPanel.BuildPage(_pages[1], context);
        EmployeePanel.BuildPage(_pages[2], context);
        FactoryPanel.BuildPage(_pages[3], context);
        BuildSalesPage(_pages[4], context);
        BuildStockPage(_pages[5], context);
    }

    static void BuildPlaceholderPage(GameObject page, string title, string body)
    {
        var card = Card(page, 0f);
        var t = Text("CardTitle", title, 30, TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Top, InkGrey);
        t.transform.SetParent(card.transform, false);
        Anchor(t.gameObject, 0f, 1f, 1f, 1f, 40f, -110f, -40f, -30f);
        var v = Text("CardValue", body, 54, TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Top, InkLight);
        v.transform.SetParent(card.transform, false);
        Anchor(v.gameObject, 0f, 1f, 1f, 1f, 40f, -260f, -40f, -120f);
    }

    static void BuildSalesPage(GameObject page, ModContext context)
    {
        _salesContext = context;
        // SALES sub-tabs: SEARCH (product search per store, existing) and
        // BEST SELLERS (per-product top list with profit + production). The
        // search keeps its two-column layout inside its own view container.
        BuildSalesSubTabs(page);
        _salesSearchView = new GameObject("SalesSearchView");
        _salesSearchView.transform.SetParent(page.transform, false);
        Anchor(_salesSearchView, 0f, 0f, 1f, 1f, 0f, 0f, 0f, -104f);
        // Two columns like the vanilla BizMan screens: search +
        // filters + product list on the left, store data grid on the right.
        var leftGO = new GameObject("LeftCol");
        leftGO.transform.SetParent(_salesSearchView.transform, false);
        Anchor(leftGO, 0f, 0f, 0.36f, 1f, 0f, 0f, -24f, 0f);
        var leftLayout = leftGO.AddComponent<VerticalLayoutGroup>();
        leftLayout.spacing = 14f;
        leftLayout.childControlWidth = true;
        leftLayout.childControlHeight = false;
        leftLayout.childForceExpandWidth = true;
        leftLayout.childForceExpandHeight = false;

        var rightGO = new GameObject("RightCol");
        rightGO.transform.SetParent(_salesSearchView.transform, false);
        Anchor(rightGO, 0.36f, 0f, 1f, 1f, 24f, 0f, 0f, 0f);

        // --- LEFT: search box. Readout + tap: typing goes through the GAME's
        // own text dialog (TextInputUI), so keystrokes never trigger hotkeys.
        var inputGO = new GameObject("ProductInput");
        inputGO.transform.SetParent(leftGO.transform, false);
        var inputBg = inputGO.AddComponent<Image>();
        inputBg.color = new Color(0.10f, 0.12f, 0.15f, 1f);
        var inputSize = inputGO.AddComponent<LayoutElement>();
        inputSize.minHeight = 100f;
        _queryText = Text("Query", "", 34, TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, Color.white);
        _queryText.transform.SetParent(inputGO.transform, false);
        Anchor(_queryText.gameObject, 0f, 0f, 1f, 1f, 30f, 10f, -30f, -10f);
        var queryHint = Text("QueryHint", "Product name... (e.g. cloth)", 34, TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, InkGrey);
        queryHint.transform.SetParent(inputGO.transform, false);
        Anchor(queryHint.gameObject, 0f, 0f, 1f, 1f, 30f, 10f, -30f, -10f);
        var inputBtn = inputGO.AddComponent<Button>();
        inputBtn.targetGraphic = inputBg;
        inputBtn.onClick.AddListener(() => OpenSearchDialog(context, queryHint));

        // --- LEFT: SEARCH button.
        var searchGO = new GameObject("SearchButton");
        searchGO.transform.SetParent(leftGO.transform, false);
        var searchBg = searchGO.AddComponent<Image>();
        searchBg.color = Accent;
        var searchSize = searchGO.AddComponent<LayoutElement>();
        searchSize.minHeight = 78f;
        var searchBtn = searchGO.AddComponent<Button>();
        searchBtn.targetGraphic = searchBg;
        var searchLabel = Text("SearchLabel", "SEARCH", 34, TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, Color.white);
        searchLabel.transform.SetParent(searchGO.transform, false);
        Stretch(searchLabel.gameObject);
        searchBtn.onClick.AddListener(() => OpenSearchDialog(context, null));

        // --- LEFT: day readout + time buttons (stacked full width).
        _dayLabel = Text("DayLabel", "DAY -", 26, TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, InkGrey);
        _dayLabel.transform.SetParent(leftGO.transform, false);
        var daySize = _dayLabel.gameObject.AddComponent<LayoutElement>();
        daySize.minHeight = 50f;
        _timeBar = leftGO.transform;
        for (int ti = 0; ti < 3; ti++) AddTimePill(ti);

        // --- RIGHT: product filter strip (bordered card, horizontal tabs).
        // A "phone" search can match N products: they all sit here side by
        // side, ALL (combined) plus one tab per product. A single hit shows
        // only that product's tab. Tapping a tab filters the stores below.
        var filterCardGO = new GameObject("ProductFilterCard");
        filterCardGO.transform.SetParent(rightGO.transform, false);
        Anchor(filterCardGO, 0f, 1f, 1f, 1f, 0f, -184f, 0f, 0f);
        var filterCardBg = filterCardGO.AddComponent<Image>();
        filterCardBg.color = new Color(0.12f, 0.14f, 0.18f, 1f);
        var filterCardOutline = filterCardGO.AddComponent<Outline>();
        filterCardOutline.effectColor = new Color(0.32f, 0.36f, 0.42f, 1f);
        filterCardOutline.effectDistance = new Vector2(2f, 2f);
        var filterCardLayout = filterCardGO.AddComponent<VerticalLayoutGroup>();
        filterCardLayout.padding = new RectOffset(18, 14, 18, 14);
        filterCardLayout.spacing = 8f;
        filterCardLayout.childControlWidth = true;
        filterCardLayout.childControlHeight = false;
        filterCardLayout.childForceExpandWidth = true;
        filterCardLayout.childForceExpandHeight = false;

        // Header: "PRODUCTS (3) - tap a tab to filter:".
        _matchText = Text("MatchText", "PRODUCTS", 26, TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, InkGrey);
        _matchText.transform.SetParent(filterCardGO.transform, false);
        var matchSize = _matchText.gameObject.AddComponent<LayoutElement>();
        matchSize.minHeight = 44f;
        _matchText.textWrappingMode = TMPro.TextWrappingModes.Normal;

        // Horizontal tab strip (scrolls sideways when many products match).
        var prodScrollGO = new GameObject("ProductStripScroll");
        prodScrollGO.transform.SetParent(filterCardGO.transform, false);
        var prodScrollSize = prodScrollGO.AddComponent<LayoutElement>();
        prodScrollSize.minHeight = 96f;
        prodScrollSize.flexibleWidth = 1f;
        var prodScroll = prodScrollGO.AddComponent<ScrollRect>();
        prodScroll.horizontal = true;
        prodScroll.vertical = false;
        prodScroll.movementType = ScrollRect.MovementType.Clamped;

        var prodViewportGO = new GameObject("ProductStripViewport");
        prodViewportGO.transform.SetParent(prodScrollGO.transform, false);
        Stretch(prodViewportGO);
        prodViewportGO.AddComponent<RectMask2D>();

        var prodContentGO = new GameObject("Products");
        prodContentGO.transform.SetParent(prodViewportGO.transform, false);
        var prodContentRect = Rect(prodContentGO);
        prodContentRect.anchorMin = new Vector2(0f, 0f);
        prodContentRect.anchorMax = new Vector2(0f, 1f);
        prodContentRect.pivot = new Vector2(0f, 0.5f);
        prodContentRect.offsetMin = Vector2.zero;
        prodContentRect.offsetMax = Vector2.zero;
        var prodFitter = prodContentGO.AddComponent<ContentSizeFitter>();
        prodFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        prodFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        var prodList = prodContentGO.AddComponent<HorizontalLayoutGroup>();
        prodList.spacing = 10f;
        prodList.childControlWidth = true;
        prodList.childControlHeight = true;
        prodList.childForceExpandWidth = false;
        prodList.childForceExpandHeight = true;
        prodScroll.viewport = prodViewportGO.GetComponent<RectTransform>();
        prodScroll.content = prodContentRect;
        _productList = prodContentGO.transform;

        // --- RIGHT: sortable + resizable store table (absolute mode).
        _salesTable = new SalesTable();
        _salesTable.Init(context);
        _salesTable.BuildHeader(rightGO, "GridHeader", 0f, 0f, -256f, -192f);
        _salesTable.BuildRows(rightGO,
            "RowsScroll", "Viewport", "Rows", "Scrollbar", -264f);
        _salesTable.ShowInfo("Type a product name and press SEARCH.", 30);

        BuildBestView(page, context);
        SelectSalesSubTab(0);
    }

    static void BuildStockPage(GameObject page, ModContext context)
    {
        // Same two-column layout as the sales page, but the data source is
        // CURRENT stock in own buildings (no time filter): type a product
        // name, see per-building amounts right now.
        var leftGO = new GameObject("LeftCol");
        leftGO.transform.SetParent(page.transform, false);
        Anchor(leftGO, 0f, 0f, 0.36f, 1f, 0f, 0f, -24f, 0f);
        var leftLayout = leftGO.AddComponent<VerticalLayoutGroup>();
        leftLayout.spacing = 14f;
        leftLayout.childControlWidth = true;
        leftLayout.childControlHeight = false;
        leftLayout.childForceExpandWidth = true;
        leftLayout.childForceExpandHeight = false;

        var rightGO = new GameObject("RightCol");
        rightGO.transform.SetParent(page.transform, false);
        Anchor(rightGO, 0.36f, 0f, 1f, 1f, 24f, 0f, 0f, 0f);

        var inputGO = new GameObject("StockInput");
        inputGO.transform.SetParent(leftGO.transform, false);
        var inputBg = inputGO.AddComponent<Image>();
        inputBg.color = new Color(0.10f, 0.12f, 0.15f, 1f);
        var inputSize = inputGO.AddComponent<LayoutElement>();
        inputSize.minHeight = 100f;
        _stockQueryText = Text("StockQuery", "", 34, TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, Color.white);
        _stockQueryText.transform.SetParent(inputGO.transform, false);
        Anchor(_stockQueryText.gameObject, 0f, 0f, 1f, 1f, 30f, 10f, -30f, -10f);
        var queryHint = Text("StockQueryHint", "Product name... (e.g. cloth)", 34, TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, InkGrey);
        queryHint.transform.SetParent(inputGO.transform, false);
        Anchor(queryHint.gameObject, 0f, 0f, 1f, 1f, 30f, 10f, -30f, -10f);
        var inputBtn = inputGO.AddComponent<Button>();
        inputBtn.targetGraphic = inputBg;
        inputBtn.onClick.AddListener(() => OpenStockSearchDialog(context, queryHint));

        var searchGO = new GameObject("StockSearchButton");
        searchGO.transform.SetParent(leftGO.transform, false);
        var searchBg = searchGO.AddComponent<Image>();
        searchBg.color = Accent;
        var searchSize = searchGO.AddComponent<LayoutElement>();
        searchSize.minHeight = 78f;
        var searchBtn = searchGO.AddComponent<Button>();
        searchBtn.targetGraphic = searchBg;
        var searchLabel = Text("StockSearchLabel", "SEARCH", 34, TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, Color.white);
        searchLabel.transform.SetParent(searchGO.transform, false);
        Stretch(searchLabel.gameObject);
        searchBtn.onClick.AddListener(() => OpenStockSearchDialog(context, null));

        var filterCardGO = new GameObject("StockFilterCard");
        filterCardGO.transform.SetParent(rightGO.transform, false);
        Anchor(filterCardGO, 0f, 1f, 1f, 1f, 0f, -184f, 0f, 0f);
        var filterCardBg = filterCardGO.AddComponent<Image>();
        filterCardBg.color = new Color(0.12f, 0.14f, 0.18f, 1f);
        var filterCardOutline = filterCardGO.AddComponent<Outline>();
        filterCardOutline.effectColor = new Color(0.32f, 0.36f, 0.42f, 1f);
        filterCardOutline.effectDistance = new Vector2(2f, 2f);
        var filterCardLayout = filterCardGO.AddComponent<VerticalLayoutGroup>();
        filterCardLayout.padding = new RectOffset(18, 14, 18, 14);
        filterCardLayout.spacing = 8f;
        filterCardLayout.childControlWidth = true;
        filterCardLayout.childControlHeight = false;
        filterCardLayout.childForceExpandWidth = true;
        filterCardLayout.childForceExpandHeight = false;

        _stockMatchText = Text("StockMatchText", "PRODUCTS", 26, TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, InkGrey);
        _stockMatchText.transform.SetParent(filterCardGO.transform, false);
        var matchSize = _stockMatchText.gameObject.AddComponent<LayoutElement>();
        matchSize.minHeight = 44f;
        _stockMatchText.textWrappingMode = TMPro.TextWrappingModes.Normal;

        var prodScrollGO = new GameObject("StockStripScroll");
        prodScrollGO.transform.SetParent(filterCardGO.transform, false);
        var prodScrollSize = prodScrollGO.AddComponent<LayoutElement>();
        prodScrollSize.minHeight = 96f;
        prodScrollSize.flexibleWidth = 1f;
        var prodScroll = prodScrollGO.AddComponent<ScrollRect>();
        prodScroll.horizontal = true;
        prodScroll.vertical = false;
        prodScroll.movementType = ScrollRect.MovementType.Clamped;

        var prodViewportGO = new GameObject("StockStripViewport");
        prodViewportGO.transform.SetParent(prodScrollGO.transform, false);
        Stretch(prodViewportGO);
        prodViewportGO.AddComponent<RectMask2D>();

        var prodContentGO = new GameObject("StockProducts");
        prodContentGO.transform.SetParent(prodViewportGO.transform, false);
        var prodContentRect = Rect(prodContentGO);
        prodContentRect.anchorMin = new Vector2(0f, 0f);
        prodContentRect.anchorMax = new Vector2(0f, 1f);
        prodContentRect.pivot = new Vector2(0f, 0.5f);
        prodContentRect.offsetMin = Vector2.zero;
        prodContentRect.offsetMax = Vector2.zero;
        var prodFitter = prodContentGO.AddComponent<ContentSizeFitter>();
        prodFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        prodFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        var prodList = prodContentGO.AddComponent<HorizontalLayoutGroup>();
        prodList.spacing = 10f;
        prodList.childControlWidth = true;
        prodList.childControlHeight = true;
        prodList.childForceExpandWidth = false;
        prodList.childForceExpandHeight = true;
        prodScroll.viewport = prodViewportGO.GetComponent<RectTransform>();
        prodScroll.content = prodContentRect;
        _stockProductList = prodContentGO.transform;

        _stockTable = new StockTable();
        _stockTable.Init(context);
        _stockTable.BuildHeader(rightGO, "StockGridHeader", -236f, -184f, -296f, -240f);
        _stockTable.BuildRows(rightGO,
            "StockRowsScroll", "StockViewport", "StockRows", "StockScrollbar", -304f);
        _stockTable.ShowInfo("Type a product name and press SEARCH.", 30);
    }

    static void OpenStockSearchDialog(ModContext context, TMPro.TMP_Text hint)
    {
        var uis = UI.UIs.Instance;
        if (uis == null || uis.textInputUi == null) return;
        string current = _stockQueryText != null && _stockQueryText.text != null ? _stockQueryText.text : "";
        uis.textInputUi.Show("thebigboard_search_hint", "common_confirm",
            (string s) =>
            {
                if (_stockQueryText != null) _stockQueryText.text = s != null ? s : "";
                if (hint != null) hint.gameObject.SetActive(string.IsNullOrWhiteSpace(s));
                if (!string.IsNullOrWhiteSpace(s)) SearchStock(context);
            },
            null, current, true);
        SelectSearchInputText(uis.textInputUi);
    }

    // Preselect the old text when the popup opens, so the second input
    // can be overwritten directly without deleting first.
    static void SelectSearchInputText(MonoBehaviour ui)
    {
        try
        {
            if (ui == null) return;
            var input = ui.GetComponentInChildren<TMPro.TMP_InputField>(true);
            if (input == null || string.IsNullOrEmpty(input.text)) return;
            string txt = input.text;
            PhoneButtonInstaller.RunNextFrame(() =>
            {
                try
                {
                    input.ActivateInputField();
                    input.Select();
                    input.selectionAnchorPosition = 0;
                    input.selectionFocusPosition = txt != null ? txt.Length : 0;
                }
                catch { }
            });
        }
        catch { }
    }

    static void SearchStock(ModContext context)
    {
        if (_stockQueryText == null || _stockTable == null) return;
        string raw = _stockQueryText.text != null ? _stockQueryText.text.Trim() : "";
        string query = raw.ToLowerInvariant();
        if (query.Length == 0) return;
        _stockTable.ShowInfo("Searching '" + raw + "'...", 32);
        if (_stockMatchText != null) { _stockMatchText.gameObject.SetActive(true); _stockMatchText.text = "Searching '" + raw + "'..."; }
        PhoneButtonInstaller.RunNextFrame(() => DoStockSearch(context, query, raw));
    }

    static bool StockIdMatchesQuery(string itemId, string query)
    {
        if (string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(query)) return false;
        if (itemId.ToLowerInvariant().Contains(query)) return true;
        try
        {
            string disp = GetProductDisplayName(itemId);
            return disp != null && disp.ToLowerInvariant().Contains(query);
        }
        catch { return false; }
    }

    static void AddStockRelevant(Dictionary<string, HashSet<string>> relevant, string pkey, string bname)
    {
        if (relevant == null || string.IsNullOrEmpty(pkey) || string.IsNullOrEmpty(bname)) return;
        HashSet<string> set;
        if (!relevant.TryGetValue(pkey, out set))
        {
            set = new HashSet<string>();
            relevant[pkey] = set;
        }
        set.Add(bname);
    }

    static void DoStockSearch(ModContext context, string query, string raw)
    {
        if (_stockTable == null) return;
        try
        {
            _lastContext = context;
            _stockRaw = raw;
            var game = SaveGameManager.Current;
            if (game == null || game.BuildingRegistrations == null) return;
            // Player buildings (factories, stores, HQs...) via the game's own
            // list, plus every warehouse registration as a safety net: a
            // factory is NOT an Entities.Warehouse, so a warehouse-only scan
            // hides factory stock.
            var warehouses = new List<BuildingRegistration>();
            try
            {
                var own = Helpers.BuildingHelper.GetPlayerBuildingRegistrations(null, null);
                if (own != null)
                    foreach (var reg in own)
                        if (reg != null && !warehouses.Contains(reg)) warehouses.Add(reg);
            }
            catch { }
            foreach (var reg in game.BuildingRegistrations)
            {
                if (reg == null || !(reg is Entities.Warehouse)) continue;
                if (!warehouses.Contains(reg)) warehouses.Add(reg);
            }
            _stockWarehouseCount = warehouses.Count;
            _stockDay = game.Day;
            if (warehouses.Count == 0)
            {
                _stockWarehouses = new Dictionary<string, Dictionary<string, long>>();
                _stockProducts = new Dictionary<string, KeyValuePair<string, long>>();
                _stockTotal = 0;
                _stockMoves = new Dictionary<string, Dictionary<string, StockMoves>>();
                _stockIsWarehouse = new HashSet<string>();
                _stockRelevant = new Dictionary<string, HashSet<string>>();
                _stockSelectedKey = null;
                UpdateStockMatchBar(raw);
                BuildStockProductTabs();
                ShowStockResults();
                return;
            }
            // Candidates: every stocked item id whose id or display name
            // contains the query. Display names are resolved lazily and only
            // for ids that fail the id match, to keep searches cheap.
            var stockedIds = new Dictionary<string, string>();
            foreach (var wh in warehouses)
            {
                if (wh.itemInstances == null) continue;
                foreach (var inst in wh.itemInstances.Values)
                {
                    if (inst == null) continue;
                    if (inst.cargoInstances != null)
                    {
                        foreach (var c in inst.cargoInstances)
                        {
                            if (c == null || c.itemName == null || c.amount <= 0) continue;
                            if (!stockedIds.ContainsKey(c.itemName)) stockedIds[c.itemName] = c.itemName;
                        }
                    }
#pragma warning disable 0618 // shelfItem obsolete since EA 0.8, still read for candidate discovery
                    var sh = inst.shelfItem;
#pragma warning restore 0618
                    if (sh != null && sh.itemName != null && sh.stock > 0)
                    {
                        if (!stockedIds.ContainsKey(sh.itemName)) stockedIds[sh.itemName] = sh.itemName;
                    }
                }
            }
            var candidates = new Dictionary<string, string>(); // pkey -> raw itemId
            foreach (var id in stockedIds.Keys)
            {
                string key = id.ToLowerInvariant();
                if (key.Contains(query))
                {
                    candidates[key] = id;
                    continue;
                }
                string disp = GetProductDisplayName(id);
                if (disp != null && disp.ToLowerInvariant().Contains(query))
                    candidates[key] = id;
            }
            // Raw materials: factory recipe ingredients + importer products +
            // delivery-contract items + logistics targets. These are usually
            // zero-stock (or never ordered), so the cargo scan above misses
            // them - e.g. searching "fabric" with empty shelves. Match them
            // against the same query and remember which building cares.
            var relevant = new Dictionary<string, HashSet<string>>();
            var byAddr = new Dictionary<string, string>();
            foreach (var reg in warehouses)
            {
                if (reg == null) continue;
                try { byAddr[StockAddrKey(reg.StreetName, reg.StreetNumber)] = StockBuildingName(reg); }
                catch { }
            }
            try
            {
                foreach (var reg in warehouses)
                {
                    if (reg == null || reg.itemInstances == null) continue;
                    string bname = StockBuildingName(reg);
                    foreach (var inst in reg.itemInstances.Values)
                    {
                        var ws = inst as FactoryWorkstationInstance;
                        if (ws == null) continue;
                        Recipe recipe = null;
                        try { recipe = ws.SelectedRecipe; } catch { continue; }
                        if ((UnityEngine.Object)recipe == null || recipe.ingredients == null) continue;
                        foreach (var ing in recipe.ingredients)
                        {
                            if (ing.item == null) continue;
                            string pkey = ing.item.ToLowerInvariant();
                            if (candidates.ContainsKey(pkey)) { AddStockRelevant(relevant, pkey, bname); continue; }
                            if (StockIdMatchesQuery(ing.item, query)) { candidates[pkey] = ing.item; AddStockRelevant(relevant, pkey, bname); }
                        }
                    }
                }
            }
            catch { }
            try
            {
                if (game.importPartnerships != null)
                {
                    foreach (var partnership in game.importPartnerships)
                    {
                        if (partnership == null || partnership.products == null) continue;
                        foreach (var product in partnership.products)
                        {
                            if (product == null || product.itemName == null) continue;
                            string pkey = product.itemName.ToLowerInvariant();
                            if (candidates.ContainsKey(pkey))
                            {
                                try
                                {
                                    if (product.assignedWarehouse != null && product.assignedWarehouse.streetName != null)
                                    {
                                        string wn;
                                        if (byAddr.TryGetValue(StockAddrKey(product.assignedWarehouse.streetName, product.assignedWarehouse.streetNumber), out wn))
                                            AddStockRelevant(relevant, pkey, wn);
                                    }
                                }
                                catch { }
                                continue;
                            }
                            if (!StockIdMatchesQuery(product.itemName, query)) continue;
                            candidates[pkey] = product.itemName;
                            try
                            {
                                if (product.assignedWarehouse != null && product.assignedWarehouse.streetName != null)
                                {
                                    string wn;
                                    if (byAddr.TryGetValue(StockAddrKey(product.assignedWarehouse.streetName, product.assignedWarehouse.streetNumber), out wn))
                                        AddStockRelevant(relevant, pkey, wn);
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
            try
            {
                if (game.DeliveryContracts != null)
                {
                    foreach (var c in game.DeliveryContracts)
                    {
                        if (c == null || c.items == null || c.businessAddress == null) continue;
                        if (c.businessAddress.streetName == null) continue;
                        string bname;
                        if (!byAddr.TryGetValue(StockAddrKey(c.businessAddress.streetName, c.businessAddress.streetNumber), out bname)) continue;
                        foreach (var item in c.items)
                        {
                            if (item == null || item.itemName == null) continue;
                            string pkey = item.itemName.ToLowerInvariant();
                            if (candidates.ContainsKey(pkey)) { AddStockRelevant(relevant, pkey, bname); continue; }
                            if (StockIdMatchesQuery(item.itemName, query)) { candidates[pkey] = item.itemName; AddStockRelevant(relevant, pkey, bname); }
                        }
                    }
                }
            }
            catch { }
            try
            {
                if (game.logisticsManagerPlans != null)
                {
                    foreach (var plan in game.logisticsManagerPlans)
                    {
                        if (plan == null || plan.destinations == null) continue;
                        string srcName = null;
                        try
                        {
                            if (plan.targetAddress != null && plan.targetAddress.streetName != null)
                                byAddr.TryGetValue(StockAddrKey(plan.targetAddress.streetName, plan.targetAddress.streetNumber), out srcName);
                        }
                        catch { }
                        foreach (var dest in plan.destinations)
                        {
                            if (dest == null || dest.stockTargets == null || dest.deliveryTargetAddress == null) continue;
                            if (dest.deliveryTargetAddress.streetName == null) continue;
                            string dstName;
                            if (!byAddr.TryGetValue(StockAddrKey(dest.deliveryTargetAddress.streetName, dest.deliveryTargetAddress.streetNumber), out dstName)) continue;
                            foreach (var st in dest.stockTargets)
                            {
                                if (st == null || st.itemName == null) continue;
                                string pkey = st.itemName.ToLowerInvariant();
                                if (candidates.ContainsKey(pkey))
                                {
                                    AddStockRelevant(relevant, pkey, dstName);
                                    if (srcName != null) AddStockRelevant(relevant, pkey, srcName);
                                    continue;
                                }
                                if (!StockIdMatchesQuery(st.itemName, query)) continue;
                                candidates[pkey] = st.itemName;
                                AddStockRelevant(relevant, pkey, dstName);
                                if (srcName != null) AddStockRelevant(relevant, pkey, srcName);
                            }
                        }
                    }
                }
            }
            catch { }
            // Authoritative per-building counts via the game's own helper
            // (includes pallets + producers).
            // Zero-stock raw candidates stay in the product list with total 0
            // so their tabs still appear and select a relevant-row view.
            var stores = new Dictionary<string, Dictionary<string, long>>();
            var products = new Dictionary<string, KeyValuePair<string, long>>();
            foreach (var kv in candidates)
                products[kv.Key] = new KeyValuePair<string, long>(kv.Value, 0);
            long total = 0;
            foreach (var wh in warehouses)
            {
                string whName = !string.IsNullOrEmpty(wh.BusinessName)
                    ? wh.BusinessName
                    : (wh.StreetName + " " + wh.StreetNumber);
                foreach (var kv in candidates)
                {
                    int amt = 0;
                    try { amt = Helpers.BuildingHelper.CountTotalResourcesInStock(wh, kv.Value); }
                    catch { amt = 0; }
                    if (amt <= 0) continue;
                    Dictionary<string, long> byProduct;
                    if (!stores.TryGetValue(whName, out byProduct))
                    {
                        byProduct = new Dictionary<string, long>();
                        stores[whName] = byProduct;
                    }
                    byProduct[kv.Key] = amt;
                    KeyValuePair<string, long> found;
                    if (products.TryGetValue(kv.Key, out found))
                        products[kv.Key] = new KeyValuePair<string, long>(found.Key, found.Value + amt);
                    else
                        products[kv.Key] = new KeyValuePair<string, long>(kv.Value, amt);
                    total += amt;
                }
            }
            _stockWarehouses = stores;
            _stockProducts = products;
            _stockTotal = total;
            _stockRelevant = relevant;
            _stockIsWarehouse = new HashSet<string>();
            foreach (var wh in warehouses)
            {
                if (!(wh is Entities.Warehouse)) continue;
                string n = !string.IsNullOrEmpty(wh.BusinessName)
                    ? wh.BusinessName
                    : (wh.StreetName + " " + wh.StreetNumber);
                _stockIsWarehouse.Add(n);
            }
            _stockDiag = "";
            _stockMoves = ComputeStockMoves(warehouses, candidates, game.Day, stores);
            if (products.Count == 1)
            {
                foreach (var only in products.Keys) { _stockSelectedKey = only; break; }
            }
            else
            {
                _stockSelectedKey = null;
            }
            UpdateStockMatchBar(raw);
            BuildStockProductTabs();
            ShowStockResults();
            context?.Logger.Info("The Big Board stock search '" + query + "': " + _stockWarehouseCount + " buildings, " + products.Count + " products (" + candidates.Count + " candidates, " + relevant.Count + " with route), total " + total + "." + _stockDiag);
        }
        catch (System.Exception ex)
        {
            context?.Logger.Warn("The Big Board stock search failed: " + ex.Message);
        }
    }

    static void UpdateStockMatchBar(string raw)
    {
        if (_stockMatchText == null) return;
        if (_stockWarehouseCount == 0)
        {
            _stockMatchText.gameObject.SetActive(true);
            _stockMatchText.text = "No own buildings found.";
            return;
        }
        if (_stockProducts == null || _stockProducts.Count == 0)
        {
            _stockMatchText.gameObject.SetActive(true);
            _stockMatchText.text = "PRODUCTS (0) - no stock for '" + raw + "'.";
            return;
        }
        if (_stockProducts.Count == 1)
        {
            _stockMatchText.text = "PRODUCT (1):";
            return;
        }
        _stockMatchText.text = "PRODUCTS (" + _stockProducts.Count + ") - tap a tab to filter:";
    }

    static void BuildStockProductTabs()
    {
        if (_stockProductList == null) return;
        DataGrid.ClearRows(_stockProductList);
        if (_stockProductList != null)
        {
            DataGrid.Warmup(_stockProductList);
        }
        if (_stockProducts == null || _stockProducts.Count == 0) return;
        var sorted = new List<KeyValuePair<string, KeyValuePair<string, long>>>(_stockProducts);
        sorted.Sort((a, b) => b.Value.Value.CompareTo(a.Value.Value));
        if (sorted.Count == 1)
        {
            var only = sorted[0];
            AddStockProductTab(only.Key, GetProductDisplayName(only.Value.Key), only.Value.Value);
        }
        else
        {
            AddStockProductTab(null, "ALL", _stockTotal);
            foreach (var kv in sorted)
                AddStockProductTab(kv.Key, GetProductDisplayName(kv.Value.Key), kv.Value.Value);
        }
    }

    static void AddStockProductTab(string key, string name, long qty)
    {
        if (_stockProductList == null) return;
        bool sel = key == _stockSelectedKey;
        var tabGO = new GameObject(key == null ? "StockProductAll" : "StockProductTab");
        tabGO.transform.SetParent(_stockProductList, false);
        var bg = tabGO.AddComponent<Image>();
        bg.color = sel ? Accent : RowBgAlt;
        var btn = tabGO.AddComponent<Button>();
        btn.targetGraphic = bg;
        var label = Text("StockProductTabLabel", name + " (" + qty.ToString("N0") + ")", 26,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle,
            sel ? Color.white : InkLight);
        label.transform.SetParent(tabGO.transform, false);
        Anchor(label.gameObject, 0f, 0f, 1f, 1f, 16f, 8f, -16f, -8f);
        label.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        label.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        var size = tabGO.AddComponent<LayoutElement>();
        size.minWidth = 200f;
        size.preferredWidth = Mathf.Clamp(120f + (name != null ? name.Length : 3) * 17f, 220f, 560f);
        size.minHeight = 88f;
        string captured = key;
        btn.onClick.AddListener(() => SelectStockProduct(captured));
    }

    static void SelectStockProduct(string key)
    {
        _stockSelectedKey = key;
        BuildStockProductTabs();
        ShowStockResults();
        if (_lastContext != null)
        {
            string what = key == null ? "ALL" : GetProductDisplayName(key);
            _lastContext.Logger.Info("The Big Board stock filter: " + what);
        }
    }

    static void ShowStockResults()
    {
        if (_stockTable == null) return;
        _stockTable.ShowResults(_stockDay, _stockWarehouses, _stockMoves, _stockRelevant,
            _stockIsWarehouse, _stockSelectedKey, _stockProducts, _stockRaw);
    }

    // Stock table (columns, sorting, resize) lives in StockTable, a
    // TablePanel<StockRowData> derivative. Data fetching stays here.

    // Buckets mirror the sales tab: TODAY = day D, YESTERDAY = D-1,
    // LAST WEEK = rolling 7 days D-6..D (same window as the sales filter).
    static string StockBuildingName(BuildingRegistration reg)
    {
        if (reg == null) return "?";
        return !string.IsNullOrEmpty(reg.BusinessName)
            ? reg.BusinessName
            : (reg.StreetName + " " + reg.StreetNumber);
    }

    static string StockAddrKey(string street, int number)
    {
        return (street != null ? street : "") + "#" + number;
    }

    static Dictionary<string, Dictionary<string, StockMoves>> ComputeStockMoves(
        List<BuildingRegistration> buildings, Dictionary<string, string> candidates, int today,
        Dictionary<string, Dictionary<string, long>> stockByName)
    {
        var moves = new Dictionary<string, Dictionary<string, StockMoves>>();
        if (buildings == null || candidates == null || candidates.Count == 0) return moves;
        System.Func<string, string, StockMoves> slot = (string bname, string pkey) =>
        {
            Dictionary<string, StockMoves> bp;
            if (!moves.TryGetValue(bname, out bp))
            {
                bp = new Dictionary<string, StockMoves>();
                moves[bname] = bp;
            }
            StockMoves m;
            if (!bp.TryGetValue(pkey, out m))
            {
                m = new StockMoves();
                bp[pkey] = m;
            }
            return m;
        };
        // Address -> display name + registration for the searched buildings.
        // Matched by street+number (never via BuildingHelper.GetBuildingRegistration:
        // that helper CREATES and saves a junk registration for unknown addresses).
        var byAddr = new Dictionary<string, string>();
        var byReg = new Dictionary<string, BuildingRegistration>();
        foreach (var reg in buildings)
        {
            if (reg == null) continue;
            string n = StockBuildingName(reg);
            byAddr[StockAddrKey(reg.StreetName, reg.StreetNumber)] = n;
            byReg[n] = reg;
        }
        // Current logistics routing: (source warehouse, product) -> destination
        // names. DeliveryTransaction records no destination, so a warehouse OUT
        // can only be attributed when the product has a SOLE destination.
        // Caveat: plans are current-state; applied to the whole 7-day window,
        // so a routing changed mid-week can misattribute older days.
        var transferRoutes = new Dictionary<string, HashSet<string>>();
        try
        {
            var save = SaveGameManager.Current;
            if (save != null && save.logisticsManagerPlans != null)
            {
                foreach (var plan in save.logisticsManagerPlans)
                {
                    if (plan == null || plan.destinations == null || plan.targetAddress == null) continue;
                    if (plan.targetAddress.streetName == null) continue;
                    string srcName;
                    if (!byAddr.TryGetValue(StockAddrKey(plan.targetAddress.streetName, plan.targetAddress.streetNumber), out srcName)) continue;
                    foreach (var dest in plan.destinations)
                    {
                        if (dest == null || dest.stockTargets == null || dest.deliveryTargetAddress == null) continue;
                        if (dest.deliveryTargetAddress.streetName == null) continue;
                        string dstName;
                        if (!byAddr.TryGetValue(StockAddrKey(dest.deliveryTargetAddress.streetName, dest.deliveryTargetAddress.streetNumber), out dstName)) continue;
                        if (dstName == srcName) continue;
                        BuildingRegistration dstReg;
                        // Warehouses track their own IN via deliveryTransactions;
                        // attribution is only for stores/factories (plain regs).
                        if (!byReg.TryGetValue(dstName, out dstReg) || dstReg is Entities.Warehouse) continue;
                        foreach (var st in dest.stockTargets)
                        {
                            if (st == null || st.itemName == null || st.targetAmount <= 0) continue;
                            string pkey = st.itemName.ToLowerInvariant();
                            if (!candidates.ContainsKey(pkey)) continue;
                            string rkey = srcName + "\n" + pkey;
                            HashSet<string> set;
                            if (!transferRoutes.TryGetValue(rkey, out set))
                            {
                                set = new HashSet<string>();
                                transferRoutes[rkey] = set;
                            }
                            set.Add(dstName);
                        }
                    }
                }
            }
        }
        catch { }
        foreach (var reg in buildings)
        {
            if (reg == null) continue;
            string name = StockBuildingName(reg);
            var wh = reg as Entities.Warehouse;
            if (wh != null && wh.deliveryTransactions != null)
            {
                // Logistics + import history: positive = received (IN), negative
                // = sent (OUT). ImportPartnership.DoDeliveries appends positive
                // entries; logistics Deliver appends negative (source) and
                // positive (warehouse destination). Wholesale direct-to-store
                // arrivals are NOT recorded (weekly counters only, see below).
                foreach (var tx in wh.deliveryTransactions)
                {
                    if (tx == null || tx.deliveryItems == null) continue;
                    bool isToday = tx.dayOfDelivery == today;
                    bool isYest = tx.dayOfDelivery == today - 1;
                    bool inWeek = tx.dayOfDelivery > today - 7 && tx.dayOfDelivery <= today;
                    if (!isToday && !isYest && !inWeek) continue;
                    foreach (var di in tx.deliveryItems)
                    {
                        if (di == null || di.itemName == null || di.amountDelivered == 0) continue;
                        string pkey = di.itemName.ToLowerInvariant();
                        if (!candidates.ContainsKey(pkey)) continue;
                        var m = slot(name, pkey);
                        if (di.amountDelivered > 0)
                        {
                            if (isToday) m.inToday += di.amountDelivered;
                            if (isYest) m.inYest += di.amountDelivered;
                            if (inWeek) m.inWeek += di.amountDelivered;
                        }
                        else
                        {
                            long o = -(long)di.amountDelivered;
                            if (isToday) m.outToday += o;
                            if (isYest) m.outYest += o;
                            if (inWeek) m.outWeek += o;
                            HashSet<string> dests;
                            if (transferRoutes.TryGetValue(name + "\n" + pkey, out dests) && dests.Count == 1)
                            {
                                string only = null;
                                foreach (var d in dests) { only = d; break; }
                                if (only != null && only != name)
                                {
                                    var dm = slot(only, pkey);
                                    if (isToday) dm.inToday += o;
                                    if (isYest) dm.inYest += o;
                                    if (inWeek) dm.inWeek += o;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                // Sales/exports (OUT). Same day buckets as the sales tab:
                // orderHistory (day-stamped at rollover; factories record daily
                // production exports via ProcessDailyFactoryOrders) + today's
                // live paid orders.
                if (reg.orderHistory != null)
                {
                    foreach (var entry in reg.orderHistory)
                    {
                        if (entry == null || entry.itemSales == null) continue;
                        bool isToday = entry.dayNumber == today;
                        bool isYest = entry.dayNumber == today - 1;
                        bool inWeek = entry.dayNumber > today - 7 && entry.dayNumber <= today;
                        if (!isToday && !isYest && !inWeek) continue;
                        foreach (var rep in entry.itemSales)
                        {
                            if (rep == null || rep.itemName == null || rep.amountSold <= 0) continue;
                            string pkey = rep.itemName.ToLowerInvariant();
                            if (!candidates.ContainsKey(pkey)) continue;
                            var m = slot(name, pkey);
                            if (isToday) m.outToday += rep.amountSold;
                            if (isYest) m.outYest += rep.amountSold;
                            if (inWeek) m.outWeek += rep.amountSold;
                        }
                    }
                }
                try
                {
                    var live = reg.unprocessedCompletedOrders;
                    if (live != null)
                    {
                        foreach (var order in live)
                        {
                            if (order == null || !order.completed || order.entries == null) continue;
                            foreach (var e in order.entries)
                            {
                                if (e == null || e.itemName == null) continue;
                                if (!e.available || !e.priceAccceptable || !e.paid) continue;
                                string pkey = e.itemName.ToLowerInvariant();
                                if (!candidates.ContainsKey(pkey)) continue;
                                var m = slot(name, pkey);
                                m.outToday += 1;
                                m.outWeek += 1;
                            }
                        }
                    }
                }
                catch { }
            }
        }
        // Store IN from direct wholesale contracts: the game keeps no per-day
        // inbound log, only calendar-week counters (reset Monday). Folded into
        // the WEEK bucket as the best available recent-inbound figure.
        // NOTE: no `enabled` filter — one-shot contracts disable themselves
        // after delivery but their counter still holds this week's receipts.
        try
        {
            var save = SaveGameManager.Current;
            if (save != null && save.DeliveryContracts != null)
            {
                foreach (var c in save.DeliveryContracts)
                {
                    if (c == null || c.items == null || c.businessAddress == null) continue;
                    if (c.businessAddress.streetName == null) continue;
                    string bname;
                    if (!byAddr.TryGetValue(StockAddrKey(c.businessAddress.streetName, c.businessAddress.streetNumber), out bname)) continue;
                    foreach (var item in c.items)
                    {
                        if (item == null || item.itemName == null || item.amountOrderedThisWeek <= 0) continue;
                        string pkey = item.itemName.ToLowerInvariant();
                        if (!candidates.ContainsKey(pkey)) continue;
                        slot(bname, pkey).inWeek += item.amountOrderedThisWeek;
                        _stockDiag += " wholesale:" + bname + "+" + item.amountOrderedThisWeek;
                    }
                }
            }
        }
        catch { }
        // Diagnostics for the log: which transfer routes were considered for
        // the searched products (sole-destination ones get attributed).
        try
        {
            foreach (var rk in transferRoutes.Keys)
            {
                int sep = rk.IndexOf('\n');
                if (sep < 0) continue;
                string rpkey = rk.Substring(sep + 1);
                if (!candidates.ContainsKey(rpkey)) continue;
                string src = rk.Substring(0, sep);
                HashSet<string> set = transferRoutes[rk];
                var dst = new List<string>(set);
                dst.Sort();
                _stockDiag += " route:" + src + "->" + string.Join(",", dst.ToArray())
                    + (set.Count == 1 ? "(=attributed)" : "(ambiguous)");
            }
        }
        catch { }
        // Exact IN via our own ledger (supersedes attribution/wholesale when
        // the buckets are covered by day-boundary snapshots):
        //   IN(window) = stock(end) - stock(start) + OUT(window).
        // Covers stores (deliveries), factories (production) and warehouses
        // (incl. wholesale receipts, which have no tx record) alike.
        try
        {
            foreach (var reg in buildings)
            {
                if (reg == null) continue;
                string bname = StockBuildingName(reg);
                string addr = StockHistory.AddrKey(reg);
                Dictionary<string, long> byProd = null;
                if (stockByName != null) stockByName.TryGetValue(bname, out byProd);
                foreach (var ck in candidates.Keys)
                {
                    long cur = 0;
                    if (byProd != null) byProd.TryGetValue(ck, out cur);
                    Dictionary<string, StockMoves> bp = null;
                    StockMoves ex = null;
                    if (moves.TryGetValue(bname, out bp) && bp != null) bp.TryGetValue(ck, out ex);
                    long outT = ex != null ? ex.outToday : 0;
                    long outY = ex != null ? ex.outYest : 0;
                    long outW = ex != null ? ex.outWeek : 0;
                    long sD, sDm1, sDm6;
                    bool hasD = StockHistory.TryGet(today, addr, ck, out sD);
                    bool hasDm1 = StockHistory.TryGet(today - 1, addr, ck, out sDm1);
                    bool hasDm6 = StockHistory.TryGet(today - 6, addr, ck, out sDm6);
                    if (!hasD && !hasDm1 && !hasDm6) continue;
                    var m = slot(bname, ck);
                    if (hasD) { m.inToday = System.Math.Max(0, cur - sD + outT); m.covT = true; }
                    if (hasD && hasDm1) { m.inYest = System.Math.Max(0, sD - sDm1 + outY); m.covY = true; }
                    if (hasDm6) { m.inWeek = System.Math.Max(0, cur - sDm6 + outW); m.covW = true; }
                }
            }
        }
        catch { }
        return moves;
    }

    static GameObject Card(GameObject parent, float top)
    {
        var card = new GameObject("Card");
        card.transform.SetParent(parent.transform, false);
        Anchor(card, 0f, 0f, 1f, 1f, 0f, 0f, 0f, top);
        var bg = card.AddComponent<Image>();
        bg.color = new Color(0.15f, 0.18f, 0.22f, 1f);
        return card;
    }

    static void PadLeft(GameObject go, float pad)
    {
        var r = Rect(go);
        r.offsetMin = new Vector2(r.offsetMin.x + pad, r.offsetMin.y);
    }

    static void PadRight(GameObject go, float pad)
    {
        var r = Rect(go);
        r.offsetMax = new Vector2(r.offsetMax.x - pad, r.offsetMax.y);
    }

    static void SelectTab(int index)
    {
        for (int i = 0; i < 6; i++)
        {
            if (_tabLabels[i] != null) _tabLabels[i].color = i == index ? TabActive : TabIdle;
            if (_tabUnderlines[i] != null) _tabUnderlines[i].SetActive(i == index);
            if (_pages[i] != null) _pages[i].SetActive(i == index);
        }
        FactoryPanel.SetVisible(index == 3);
        if (index == 3) FactoryPanel.Refresh();
        EmployeePanel.SetVisible(index == 2);
        if (index == 2) EmployeePanel.Refresh();
        BuildingsPanel.SetVisible(index == 0);
        if (index == 0) BuildingsPanel.Refresh();
        BusinessesPanel.SetVisible(index == 1);
        if (index == 1) BusinessesPanel.Refresh();
    }

    static void OpenSearchDialog(ModContext context, TMPro.TMP_Text hint)
    {
        var uis = UI.UIs.Instance;
        if (uis == null || uis.textInputUi == null) return;
        string current = _queryText != null && _queryText.text != null ? _queryText.text : "";
        uis.textInputUi.Show("thebigboard_search_hint", "common_confirm",
            (string s) =>
            {
                if (_queryText != null) _queryText.text = s != null ? s : "";
                if (hint != null) hint.gameObject.SetActive(string.IsNullOrWhiteSpace(s));
                if (!string.IsNullOrWhiteSpace(s)) Search(context);
            },
            null, current, true);
        SelectSearchInputText(uis.textInputUi);
    }

    static void Search(ModContext context)
    {
        if (_queryText == null || _salesTable == null) return;
        string raw = _queryText.text != null ? _queryText.text.Trim() : "";
        string query = raw.ToLowerInvariant();
        if (query.Length == 0) return;
        _salesTable.ShowInfo("Searching '" + raw + "'...", 32);
        if (_matchText != null) { _matchText.gameObject.SetActive(true); _matchText.text = "Searching '" + raw + "'..."; }
        PhoneButtonInstaller.RunNextFrame(() => DoSearch(context, query, raw));
    }

    static void DoSearch(ModContext context, string query, string raw)
    {
        if (_salesTable == null) return;
        try
        {
            _lastContext = context;
            _lastRaw = raw;
            var game = SaveGameManager.Current;
            if (game == null || game.BuildingRegistrations == null) return;
            _currentDay = game.Day;
            // Flat hit list: time-range pills re-aggregate without re-scanning.
            var hits = new List<SaleHit>();
            foreach (var reg in game.BuildingRegistrations)
            {
                if (reg == null || reg is Entities.Warehouse) continue;
                string storeName = !string.IsNullOrEmpty(reg.BusinessName)
                    ? reg.BusinessName
                    : (reg.StreetName + " " + reg.StreetNumber);
                if (reg.orderHistory != null)
                {
                    foreach (var entry in reg.orderHistory)
                    {
                        if (entry == null || entry.itemSales == null) continue;
                        foreach (var report in entry.itemSales)
                        {
                            if (report == null || report.itemName == null) continue;
                            if (!report.itemName.ToLowerInvariant().Contains(query)) continue;
                            if (report.amountSold == 0) continue;
                            hits.Add(new SaleHit
                            {
                                store = storeName,
                                pkey = report.itemName.ToLowerInvariant(),
                                raw = report.itemName,
                                day = entry.dayNumber,
                                amt = report.amountSold
                            });
                        }
                    }
                }
                // TODAY is never in orderHistory: the game writes each day's
                // entry at the next rollover stamped (Day - 1) (verified in
                // Helpers.BusinessHelper.ProcessDailyOrders IL). Aggregate
                // the live completed orders + factory exports as today hits.
                try
                {
                    var live = reg.unprocessedCompletedOrders;
                    if (live != null)
                    {
                        foreach (var order in live)
                        {
                            if (order == null || !order.completed || order.entries == null) continue;
                            foreach (var e in order.entries)
                            {
                                if (e == null || e.itemName == null) continue;
                                if (!e.available || !e.priceAccceptable || !e.paid) continue;
                                if (!e.itemName.ToLowerInvariant().Contains(query)) continue;
                                hits.Add(new SaleHit
                                {
                                    store = storeName,
                                    pkey = e.itemName.ToLowerInvariant(),
                                    raw = e.itemName,
                                    day = _currentDay,
                                    amt = 1
                                });
                            }
                        }
                    }
                }
                catch { }
                try
                {
                    var exports = reg.factoryExports;
                    if (exports != null)
                    {
                        foreach (var x in exports)
                        {
                            if (x == null || x.itemName == null || x.amount <= 0) continue;
                            if (!x.itemName.ToLowerInvariant().Contains(query)) continue;
                            hits.Add(new SaleHit
                            {
                                store = storeName,
                                pkey = x.itemName.ToLowerInvariant(),
                                raw = x.itemName,
                                day = _currentDay,
                                amt = x.amount
                            });
                        }
                    }
                }
                catch { }
            }
            _lastHits = hits;
            // Default to the newest range that actually has sales.
            _timeRange = 0;
            if (!RangeHasHits(0)) _timeRange = RangeHasHits(1) ? 1 : 2;
            _lastSelectedKey = null;
            BuildTimePills();
            RebuildAggregates();
            context?.Logger.Info("The Big Board sales search '" + query + "': today " + _currentDay + ", range " + _timeRange + ", " + hits.Count + " hits.");
        }
        catch (System.Exception ex)
        {
            context?.Logger.Warn("The Big Board sales search failed: " + ex.Message);
        }
    }

    static void RebuildAggregates()
    {
        var stores = new Dictionary<string, Dictionary<string, long>>();
        var products = new Dictionary<string, KeyValuePair<string, long>>();
        long total = 0;
        if (_lastHits != null)
        {
            foreach (var h in _lastHits)
            {
                if (!InTimeRange(h.day)) continue;
                Dictionary<string, long> byProduct;
                if (!stores.TryGetValue(h.store, out byProduct))
                {
                    byProduct = new Dictionary<string, long>();
                    stores[h.store] = byProduct;
                }
                long cur;
                byProduct.TryGetValue(h.pkey, out cur);
                byProduct[h.pkey] = cur + h.amt;
                KeyValuePair<string, long> found;
                if (products.TryGetValue(h.pkey, out found))
                    products[h.pkey] = new KeyValuePair<string, long>(found.Key, found.Value + h.amt);
                else
                    products[h.pkey] = new KeyValuePair<string, long>(h.raw, h.amt);
                total += h.amt;
            }
        }
        _lastStores = stores;
        _lastProducts = products;
        _lastTotal = total;
        if (products.Count == 1)
        {
            // Single hit: no ALL button, auto-select the only product.
            foreach (var only in products.Keys) { _lastSelectedKey = only; break; }
        }
        else if (_lastSelectedKey != null && !products.ContainsKey(_lastSelectedKey))
            _lastSelectedKey = null; // selected product has no sales in this range
        UpdateMatchBar(_lastRaw);
        BuildLeftList();
        ShowStoreResults();
        UpdateDayLabel();
    }

    static void ShowStoreResults()
    {
        if (_salesTable == null) return;
        _salesTable.ShowResults(_lastStores, _lastSelectedKey, _lastProducts, _lastRaw);
    }

    static bool InTimeRange(int day)
    {
        return InRange(_timeRange, day);
    }

    static bool InRange(int index, int day)
    {
        if (index == 0) return day == _currentDay;
        if (index == 1) return day == _currentDay - 1;
        return day > _currentDay - 7 && day <= _currentDay;
    }

    static bool RangeHasHits(int index)
    {
        if (_lastHits == null) return false;
        foreach (var h in _lastHits)
            if (InRange(index, h.day)) return true;
        return false;
    }

    static void AddTimePill(int index)
    {
        if (_timeBar == null) return;
        var pillGO = new GameObject("TimePill" + index);
        pillGO.transform.SetParent(_timeBar, false);
        var bg = pillGO.AddComponent<Image>();
        bg.color = new Color(0.20f, 0.23f, 0.28f, 1f); // readable before first search
        var btn = pillGO.AddComponent<Button>();
        btn.targetGraphic = bg;
        string[] names = { "TODAY", "YESTERDAY", "LAST WEEK" };
        var label = Text("TimePillLabel", names[index >= 0 && index < 3 ? index : 0], 26, TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, InkLight);
        label.transform.SetParent(pillGO.transform, false);
        Anchor(label.gameObject, 0f, 0f, 1f, 1f, 12f, 6f, -12f, -6f);
        label.overflowMode = TMPro.TextOverflowModes.Truncate;
        var size = pillGO.AddComponent<LayoutElement>();
        size.flexibleWidth = 1f;
        size.minHeight = 66f;
        _timePills.Add(new TimePill { index = index, bg = bg, label = label });
        int captured = index;
        btn.onClick.AddListener(() => SelectTimeRange(captured));
    }

    static void BuildTimePills()
    {
        if (_timePills.Count < 3) return;
        _timePills[0].label.text = "TODAY";
        _timePills[1].label.text = "YESTERDAY";
        _timePills[2].label.text = "LAST WEEK";
        RefreshTimePillStyles();
    }

    static void SelectTimeRange(int index)
    {
        if (index < 0 || index > 2) return;
        _timeRange = index;
        RefreshTimePillStyles();
        RebuildAggregates();
        if (_lastContext != null)
            _lastContext.Logger.Info("The Big Board time filter: " + (index == 0 ? "TODAY" : index == 1 ? "YESTERDAY" : "LAST WEEK"));
    }

    static void RefreshTimePillStyles()
    {
        foreach (var pill in _timePills)
        {
            if (pill == null || pill.bg == null || pill.label == null) continue;
            bool sel = pill.index == _timeRange;
            pill.bg.color = sel ? Accent : new Color(0.20f, 0.23f, 0.28f, 1f);
            pill.label.color = sel ? Color.white : InkLight;
        }
    }

    static void UpdateDayLabel()
    {
        if (_dayLabel == null) return;
        if (_timeRange == 1)
            _dayLabel.text = "DAY " + (_currentDay - 1);
        else if (_timeRange == 2)
            _dayLabel.text = "DAYS " + (_currentDay - 6) + "-" + _currentDay;
        else
            _dayLabel.text = "DAY " + _currentDay;
    }

    public static string GetProductDisplayName(string itemId)
    {
        // The order-history key ("ba:itemname_...") is a localization key;
        // quantity 1 gives the singular display name.
        try
        {
            string s = Helpers.LocalizationHelper.GetItemLabel(itemId, 1).ToString();
            return string.IsNullOrWhiteSpace(s) ? itemId : s;
        }
        catch
        {
            return itemId;
        }
    }

    static void UpdateMatchBar(string raw)
    {
        if (_matchText == null || _lastProducts == null) return;
        if (_lastProducts.Count == 0)
        {
            _matchText.gameObject.SetActive(true);
            _matchText.text = "PRODUCTS (0) - no match for '" + raw + "'.";
            return;
        }
        var sorted = new List<KeyValuePair<string, KeyValuePair<string, long>>>(_lastProducts);
        sorted.Sort((a, b) => b.Value.Value.CompareTo(a.Value.Value));
        if (sorted.Count == 1)
        {
            _matchText.text = "PRODUCT (1):";
            return;
        }
        // Several products: one tab each below, ALL is the combined view.
        _matchText.text = "PRODUCTS (" + sorted.Count + ") - tap a tab to filter:";
    }

    // TMP warmup now happens in TablePanel.RenderCached before every render.

    static void BuildLeftList()
    {
        if (_productList == null) return;
        DataGrid.ClearRows(_productList);
        WarmupStrip();
        if (_lastProducts == null || _lastProducts.Count == 0) return;
        var sorted = new List<KeyValuePair<string, KeyValuePair<string, long>>>(_lastProducts);
        sorted.Sort((a, b) => b.Value.Value.CompareTo(a.Value.Value));
        if (sorted.Count == 1)
        {
            // Single hit: no ALL tab, the tab carries the product name.
            var only = sorted[0];
            AddProductTab(only.Key, GetProductDisplayName(only.Value.Key), only.Value.Value);
        }
        else
        {
            // ALL (combined) first, then one tab per product.
            AddProductTab(null, "ALL", _lastTotal);
            foreach (var kv in sorted)
                AddProductTab(kv.Key, GetProductDisplayName(kv.Value.Key), kv.Value.Value);
        }
        PhoneButtonInstaller.RunNextFrame(LogLeftList);
    }

    static void WarmupStrip()
    {
        // Same TMP first-glyph workaround, but for the strip's own content:
        // without this the first tab can render blank (grey empty button).
        DataGrid.Warmup(_productList);
    }

    static void LogLeftList()
    {
        if (_lastContext == null || _productList == null) return;
        string info = "n=" + _productList.childCount;
        foreach (Transform child in _productList)
            info += " [" + child.name + (child.gameObject.activeSelf ? "" : "!") + "]";
        _lastContext.Logger.Info("The Big Board productstrip:" + info);
    }

    static void AddProductTab(string key, string name, long qty)
    {
        // Compact horizontal tab: "Name (qty)". Tapping filters the store
        // grid below to that product (key null = ALL products combined).
        if (_productList == null) return;
        bool sel = key == _lastSelectedKey;
        var tabGO = new GameObject(key == null ? "ProductAll" : "ProductTab");
        tabGO.transform.SetParent(_productList, false);
        var bg = tabGO.AddComponent<Image>();
        bg.color = sel ? Accent : RowBgAlt;
        var btn = tabGO.AddComponent<Button>();
        btn.targetGraphic = bg;
        var label = Text("ProductTabLabel", name + " (" + qty.ToString("N0") + ")", 26,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle,
            sel ? Color.white : InkLight);
        label.transform.SetParent(tabGO.transform, false);
        Anchor(label.gameObject, 0f, 0f, 1f, 1f, 16f, 8f, -16f, -8f);
        label.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        label.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        var size = tabGO.AddComponent<LayoutElement>();
        size.minWidth = 200f;
        size.preferredWidth = Mathf.Clamp(120f + (name != null ? name.Length : 3) * 17f, 220f, 560f);
        size.minHeight = 88f;
        string captured = key; // per-tab copy for the click closure
        btn.onClick.AddListener(() => SelectProduct(captured));
    }

    static void SelectProduct(string key)
    {
        _lastSelectedKey = key;
        RebuildAggregates();
        if (_lastContext != null)
        {
            string what = key == null ? "ALL" : GetProductDisplayName(key);
            _lastContext.Logger.Info("The Big Board product filter: " + what);
        }
    }

    // ================= SALES sub-tabs (SEARCH | BEST SELLERS) =================

    static void BuildSalesSubTabs(GameObject page)
    {
        string[] names = { "BEST SELLERS", "SEARCH" };
        for (int i = 0; i < 2; i++)
        {
            int index = i;
            var tabGO = new GameObject("SalesSubTab" + i);
            tabGO.transform.SetParent(page.transform, false);
            Anchor(tabGO, 0f, 1f, 0f, 1f,
                i == 0 ? 0f : 390f, -88f, i == 0 ? 370f : 700f, 0f);
            var bg = tabGO.AddComponent<Image>();
            bg.color = RowBgAlt;
            var btn = tabGO.AddComponent<Button>();
            btn.targetGraphic = bg;
            var label = Text("SalesSubTabLabel" + i, names[i], 28,
                TMPro.HorizontalAlignmentOptions.Center,
                TMPro.VerticalAlignmentOptions.Middle, InkLight);
            label.transform.SetParent(tabGO.transform, false);
            Anchor(label.gameObject, 0f, 0f, 1f, 1f, 16f, 8f, -16f, -8f);
            _salesSubTabBgs[i] = bg;
            _salesSubTabLabels[i] = label;
            btn.onClick.AddListener(() => SelectSalesSubTab(index));
        }
    }

    static void SelectSalesSubTab(int index)
    {
        if (index < 0 || index > 1) return;
        _salesSubTab = index;
        RefreshSalesSubTabStyles();
        if (_salesSearchView != null) _salesSearchView.SetActive(index == 1);
        if (_salesBestView != null) _salesBestView.SetActive(index == 0);
        // Lazy first scan: opening BEST SELLERS aggregates everything once;
        // pills + store tabs afterwards only re-aggregate the cached hits.
        if (index == 0 && _bestHits == null) DoBestScan(_salesContext);
        if (_lastContext != null)
            _lastContext.Logger.Info("The Big Board sales view: " + (index == 0 ? "BEST SELLERS" : "SEARCH"));
    }

    static void RefreshSalesSubTabStyles()
    {
        for (int i = 0; i < 2; i++)
        {
            if (_salesSubTabBgs[i] == null || _salesSubTabLabels[i] == null) continue;
            bool sel = i == _salesSubTab;
            _salesSubTabBgs[i].color = sel ? Accent : RowBgAlt;
            _salesSubTabLabels[i].color = sel ? Color.white : InkLight;
        }
    }

    static void BuildBestView(GameObject page, ModContext context)
    {
        _salesSubTab = 0;
        var bestGO = new GameObject("SalesBestView");
        bestGO.transform.SetParent(page.transform, false);
        Anchor(bestGO, 0f, 0f, 1f, 1f, 0f, 0f, 0f, -104f);
        bestGO.SetActive(false);
        _salesBestView = bestGO;

        // Time pills + REFRESH in one top row.
        _bestPillBar = new GameObject("BestPillBar");
        _bestPillBar.transform.SetParent(bestGO.transform, false);
        Anchor(_bestPillBar, 0f, 1f, 1f, 1f, 0f, -70f, 0f, 0f);
        var pillLayout = _bestPillBar.AddComponent<HorizontalLayoutGroup>();
        pillLayout.spacing = 12f;
        pillLayout.childControlWidth = true;
        pillLayout.childControlHeight = true;
        pillLayout.childForceExpandWidth = true;
        pillLayout.childForceExpandHeight = true;
        for (int ti = 0; ti < 3; ti++) AddBestTimePill(ti);
        var refreshGO = new GameObject("BestRefresh");
        refreshGO.transform.SetParent(_bestPillBar.transform, false);
        var refreshBg = refreshGO.AddComponent<Image>();
        refreshBg.color = Accent;
        var refreshBtn = refreshGO.AddComponent<Button>();
        refreshBtn.targetGraphic = refreshBg;
        var refreshLabel = Text("BestRefreshLabel", "REFRESH", 26,
            TMPro.HorizontalAlignmentOptions.Center,
            TMPro.VerticalAlignmentOptions.Middle, Color.white);
        refreshLabel.transform.SetParent(refreshGO.transform, false);
        Stretch(refreshLabel.gameObject);
        var refreshSize = refreshGO.AddComponent<LayoutElement>();
        refreshSize.flexibleWidth = 1f;
        refreshSize.minHeight = 60f;
        refreshBtn.onClick.AddListener(() => DoBestScan(_salesContext));

        // Store filter strip (same bordered-card pattern as the search view).
        var filterCardGO = new GameObject("BestStoreFilterCard");
        filterCardGO.transform.SetParent(bestGO.transform, false);
        Anchor(filterCardGO, 0f, 1f, 1f, 1f, 0f, -268f, 0f, -84f);
        var filterCardBg = filterCardGO.AddComponent<Image>();
        filterCardBg.color = new Color(0.12f, 0.14f, 0.18f, 1f);
        var filterCardOutline = filterCardGO.AddComponent<Outline>();
        filterCardOutline.effectColor = new Color(0.32f, 0.36f, 0.42f, 1f);
        filterCardOutline.effectDistance = new Vector2(2f, 2f);
        var filterCardLayout = filterCardGO.AddComponent<VerticalLayoutGroup>();
        filterCardLayout.padding = new RectOffset(18, 14, 18, 14);
        filterCardLayout.spacing = 8f;
        filterCardLayout.childControlWidth = true;
        filterCardLayout.childControlHeight = false;
        filterCardLayout.childForceExpandWidth = true;
        filterCardLayout.childForceExpandHeight = false;

        _bestStoreText = Text("BestStoreText", "STORES", 26, TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, InkGrey);
        _bestStoreText.transform.SetParent(filterCardGO.transform, false);
        var matchSize = _bestStoreText.gameObject.AddComponent<LayoutElement>();
        matchSize.minHeight = 44f;
        _bestStoreText.textWrappingMode = TMPro.TextWrappingModes.Normal;

        var storeScrollGO = new GameObject("BestStoreStripScroll");
        storeScrollGO.transform.SetParent(filterCardGO.transform, false);
        var storeScrollSize = storeScrollGO.AddComponent<LayoutElement>();
        storeScrollSize.minHeight = 96f;
        storeScrollSize.flexibleWidth = 1f;
        var storeScroll = storeScrollGO.AddComponent<ScrollRect>();
        storeScroll.horizontal = true;
        storeScroll.vertical = false;
        storeScroll.movementType = ScrollRect.MovementType.Clamped;

        var storeViewportGO = new GameObject("BestStoreStripViewport");
        storeViewportGO.transform.SetParent(storeScrollGO.transform, false);
        Stretch(storeViewportGO);
        storeViewportGO.AddComponent<RectMask2D>();

        var storeContentGO = new GameObject("BestStores");
        storeContentGO.transform.SetParent(storeViewportGO.transform, false);
        var storeContentRect = Rect(storeContentGO);
        storeContentRect.anchorMin = new Vector2(0f, 0f);
        storeContentRect.anchorMax = new Vector2(0f, 1f);
        storeContentRect.pivot = new Vector2(0f, 0.5f);
        storeContentRect.offsetMin = Vector2.zero;
        storeContentRect.offsetMax = Vector2.zero;
        var storeFitter = storeContentGO.AddComponent<ContentSizeFitter>();
        storeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        storeFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        var storeList = storeContentGO.AddComponent<HorizontalLayoutGroup>();
        storeList.spacing = 10f;
        storeList.childControlWidth = true;
        storeList.childControlHeight = true;
        storeList.childForceExpandWidth = false;
        storeList.childForceExpandHeight = true;
        storeScroll.viewport = storeViewportGO.GetComponent<RectTransform>();
        storeScroll.content = storeContentRect;
        _bestStoreList = storeContentGO.transform;

        _bestTable = new BestSellersTable();
        _bestTable.Init(context);
        _bestTable.BuildHeader(bestGO, "BestGridHeader", 0f, 0f, -348f, -284f);
        _bestTable.BuildRows(bestGO,
            "BestRowsScroll", "BestViewport", "BestRows", "BestScrollbar", -356f);
        _bestTable.ShowInfo("Open BEST SELLERS to scan all sales.", 30);
    }

    static void AddBestTimePill(int index)
    {
        if (_bestPillBar == null) return;
        var pillGO = new GameObject("BestTimePill" + index);
        pillGO.transform.SetParent(_bestPillBar.transform, false);
        var bg = pillGO.AddComponent<Image>();
        bg.color = new Color(0.20f, 0.23f, 0.28f, 1f);
        var btn = pillGO.AddComponent<Button>();
        btn.targetGraphic = bg;
        string[] names = { "TODAY", "YESTERDAY", "LAST WEEK" };
        var label = Text("BestTimePillLabel", names[index >= 0 && index < 3 ? index : 0], 26, TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, InkLight);
        label.transform.SetParent(pillGO.transform, false);
        Anchor(label.gameObject, 0f, 0f, 1f, 1f, 12f, 6f, -12f, -6f);
        label.overflowMode = TMPro.TextOverflowModes.Truncate;
        var size = pillGO.AddComponent<LayoutElement>();
        size.flexibleWidth = 2f;
        size.minHeight = 60f;
        _bestTimePills.Add(new TimePill { index = index, bg = bg, label = label });
        int captured = index;
        btn.onClick.AddListener(() => SelectBestRange(captured));
    }

    static void SelectBestRange(int index)
    {
        if (index < 0 || index > 2) return;
        _bestRange = index;
        RefreshBestPillStyles();
        RebuildBestAggregates();
        if (_salesContext != null)
            _salesContext.Logger.Info("The Big Board best sellers time filter: " + (index == 0 ? "TODAY" : index == 1 ? "YESTERDAY" : "LAST WEEK"));
    }

    static void RefreshBestPillStyles()
    {
        foreach (var pill in _bestTimePills)
        {
            if (pill == null || pill.bg == null || pill.label == null) continue;
            bool sel = pill.index == _bestRange;
            pill.bg.color = sel ? Accent : new Color(0.20f, 0.23f, 0.28f, 1f);
            pill.label.color = sel ? Color.white : InkLight;
        }
    }

    // Fallback unit cost for sales the game records with zero cost
    // (own-factory goods: their stock pricePerUnit is 0). Mirrors the game's
    // own repair basis: ItemHelper.GetWholesalePrice, else DefaultMarketPrice.
    static float BestFallbackUnitCost(string pkey, string rawItemId)
    {
        float cached;
        if (_bestFallbackCost.TryGetValue(pkey, out cached))
            return cached < 0 ? 0 : cached;
        float unit = 0;
        try
        {
            var item = BigAmbitions.Items.ItemsGetter.GetByName(rawItemId);
            if (item != null)
            {
                unit = ItemHelper.GetWholesalePrice(item);
                if (float.IsNaN(unit) || unit <= 0) unit = item.DefaultMarketPrice;
                if (float.IsNaN(unit) || unit < 0) unit = 0;
            }
        }
        catch { unit = 0; }
        _bestFallbackCost[pkey] = unit > 0 ? unit : -1f;
        return unit > 0 ? unit : 0;
    }

    static void DoBestScan(ModContext context)
    {
        if (_bestTable == null) return;
        _salesContext = context;
        _bestTable.ShowInfo("Scanning all sales...", 32);
        if (_bestStoreText != null) { _bestStoreText.gameObject.SetActive(true); _bestStoreText.text = "Scanning..."; }
        PhoneButtonInstaller.RunNextFrame(() => DoBestScanNow(context));
    }

    static void DoBestScanNow(ModContext context)
    {
        if (_bestTable == null) return;
        try
        {
            _salesContext = context;
            var game = SaveGameManager.Current;
            if (game == null || game.BuildingRegistrations == null) return;
            _bestDay = game.Day;
            _bestFallbackCost.Clear();
            // Factory recipe outputs: products the player's own factories can
            // make (workstation SelectedRecipe.output). Warehouse arrivals of
            // these products are the game's only day-stamped record of actual
            // production: outputs go straight to shelves, and factory history
            // only keeps consumed-ingredient costs with amount 0.
            var recipeOutputs = new HashSet<string>();
            foreach (var reg in game.BuildingRegistrations)
            {
                if (reg == null || reg.itemInstances == null) continue;
                if (!string.Equals(reg.businessTypeName, FactoryBusinessType, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var inst in reg.itemInstances.Values)
                {
                    var ws = inst as FactoryWorkstationInstance;
                    if (ws == null) continue;
                    try
                    {
                        var recipe = ws.SelectedRecipe;
                        if ((UnityEngine.Object)recipe == null) continue;
                        if (string.IsNullOrEmpty(recipe.output.item)) continue;
                        recipeOutputs.Add(recipe.output.item.ToLowerInvariant());
                    }
                    catch { }
                }
            }
            // Full unfiltered hit list: retail sales carry money (revenue +
            // cost), factory history + live exports + warehouse arrivals of
            // recipe outputs carry production.
            var hits = new List<BestHit>();
            foreach (var reg in game.BuildingRegistrations)
            {
                if (reg == null || reg is Entities.Warehouse) continue;
                string name = !string.IsNullOrEmpty(reg.BusinessName)
                    ? reg.BusinessName
                    : (reg.StreetName + " " + reg.StreetNumber);
                bool isFactory = string.Equals(reg.businessTypeName, FactoryBusinessType, StringComparison.OrdinalIgnoreCase);
                if (reg.orderHistory != null)
                {
                    foreach (var entry in reg.orderHistory)
                    {
                        if (entry == null || entry.itemSales == null) continue;
                        foreach (var report in entry.itemSales)
                        {
                            if (report == null || report.itemName == null) continue;
                            if (report.amountSold <= 0) continue;
                            if (isFactory)
                            {
                                // Factory history records production exports, not
                                // retail sales (ProcessDailyFactoryOrders).
                                hits.Add(new BestHit
                                {
                                    pkey = report.itemName.ToLowerInvariant(),
                                    raw = report.itemName,
                                    store = name,
                                    day = entry.dayNumber,
                                    amt = report.amountSold,
                                    produced = true
                                });
                            }
                            else
                            {
                                // Services and own-factory goods carry no
                                // recorded cost (wholesalePrice 0): services
                                // stay at 0, factory-recipe products fall back
                                // to the game's own wholesale basis.
                                string pkey = report.itemName.ToLowerInvariant();
                                double cost = report.totalWholesalePrice;
                                if (!(cost > 0))
                                    cost = recipeOutputs.Contains(pkey)
                                        ? BestFallbackUnitCost(pkey, report.itemName) * report.amountSold
                                        : 0;
                                hits.Add(new BestHit
                                {
                                    pkey = pkey,
                                    raw = report.itemName,
                                    store = name,
                                    day = entry.dayNumber,
                                    amt = report.amountSold,
                                    revenue = report.totalPrice,
                                    cost = cost,
                                    produced = false
                                });
                            }
                        }
                    }
                }
                if (!isFactory)
                {
                    // TODAY is never in orderHistory: aggregate the live
                    // completed paid orders as today hits (same as SEARCH).
                    try
                    {
                        var live = reg.unprocessedCompletedOrders;
                        if (live != null)
                        {
                            foreach (var order in live)
                            {
                                if (order == null || !order.completed || order.entries == null) continue;
                                foreach (var e in order.entries)
                                {
                                    if (e == null || e.itemName == null) continue;
                                    if (!e.available || !e.priceAccceptable || !e.paid) continue;
                                    string pkey = e.itemName.ToLowerInvariant();
                                    double cost = e.wholesalePrice;
                                    if (!(cost > 0))
                                        cost = recipeOutputs.Contains(pkey)
                                            ? BestFallbackUnitCost(pkey, e.itemName)
                                            : 0;
                                    hits.Add(new BestHit
                                    {
                                        pkey = pkey,
                                        raw = e.itemName,
                                        store = name,
                                        day = _bestDay,
                                        amt = 1,
                                        revenue = e.price,
                                        cost = cost,
                                        produced = false
                                    });
                                }
                            }
                        }
                    }
                    catch { }
                }
                // Today's live factory production (any building carrying
                // exports, normally only factories).
                try
                {
                    var exports = reg.factoryExports;
                    if (exports != null)
                    {
                        foreach (var x in exports)
                        {
                            if (x == null || x.itemName == null || x.amount <= 0) continue;
                            hits.Add(new BestHit
                            {
                                pkey = x.itemName.ToLowerInvariant(),
                                raw = x.itemName,
                                store = name,
                                day = _bestDay,
                                amt = x.amount,
                                produced = true
                            });
                        }
                    }
                }
                catch { }
            }
            // Production arrivals: warehouse deliveryTransactions IN of
            // factory-recipe products (day-stamped). Imports/wholesale goods
            // pass through the same transactions, so a product that is both
            // produced and bought in can read high - the game keeps no origin.
            foreach (var reg in game.BuildingRegistrations)
            {
                var wh = reg as Entities.Warehouse;
                if (wh == null || wh.deliveryTransactions == null) continue;
                string whName = !string.IsNullOrEmpty(reg.BusinessName)
                    ? reg.BusinessName
                    : (reg.StreetName + " " + reg.StreetNumber);
                foreach (var tx in wh.deliveryTransactions)
                {
                    if (tx == null || tx.deliveryItems == null) continue;
                    foreach (var di in tx.deliveryItems)
                    {
                        if (di == null || di.itemName == null || di.amountDelivered <= 0) continue;
                        string pkey = di.itemName.ToLowerInvariant();
                        if (!recipeOutputs.Contains(pkey)) continue;
                        hits.Add(new BestHit
                        {
                            pkey = pkey,
                            raw = di.itemName,
                            store = whName,
                            day = tx.dayOfDelivery,
                            amt = di.amountDelivered,
                            produced = true
                        });
                    }
                }
            }
            _bestHits = hits;
            // Default to the newest range that actually has hits.
            _bestRange = 0;
            if (!BestRangeHasHits(0)) _bestRange = BestRangeHasHits(1) ? 1 : 2;
            _bestStore = null;
            RefreshBestPillStyles();
            RebuildBestAggregates();
            int sales = 0;
            int prod = 0;
            foreach (var h in hits) { if (h.produced) prod++; else sales++; }
            context?.Logger.Info("The Big Board best sellers scan: day " + _bestDay + ", range " + _bestRange + ", " + sales + " sales hits + " + prod + " production hits (factory recipes: " + recipeOutputs.Count + ").");
        }
        catch (System.Exception ex)
        {
            context?.Logger.Warn("The Big Board best sellers scan failed: " + ex.Message);
        }
    }

    static void RebuildBestAggregates()
    {
        var byProduct = new Dictionary<string, BestAgg>();
        var storeSold = new Dictionary<string, long>();
        if (_bestHits != null)
        {
            foreach (var h in _bestHits)
            {
                if (!InBestRange(h.day)) continue;
                BestAgg agg;
                if (!byProduct.TryGetValue(h.pkey, out agg))
                {
                    agg = new BestAgg { raw = h.raw };
                    byProduct[h.pkey] = agg;
                }
                if (h.produced)
                {
                    agg.produced += h.amt;
                }
                else
                {
                    agg.sold += h.amt;
                    agg.revenue += h.revenue;
                    agg.cost += h.cost;
                    long cur;
                    agg.perStore.TryGetValue(h.store, out cur);
                    agg.perStore[h.store] = cur + h.amt;
                    double rev;
                    agg.perStoreRev.TryGetValue(h.store, out rev);
                    agg.perStoreRev[h.store] = rev + h.revenue;
                    double cst;
                    agg.perStoreCost.TryGetValue(h.store, out cst);
                    agg.perStoreCost[h.store] = cst + h.cost;
                    long ssum;
                    storeSold.TryGetValue(h.store, out ssum);
                    storeSold[h.store] = ssum + h.amt;
                }
            }
        }
        // Selected store has no sales in this range: fall back to ALL.
        if (_bestStore != null && !storeSold.ContainsKey(_bestStore))
            _bestStore = null;
        var rows = new List<BestSellersTable.BestRow>();
        foreach (var kv in byProduct)
        {
            var agg = kv.Value;
            long sold = agg.sold;
            double revenue = agg.revenue;
            double cost = agg.cost;
            if (_bestStore != null)
            {
                if (!agg.perStore.TryGetValue(_bestStore, out sold)) sold = 0;
                if (!agg.perStoreRev.TryGetValue(_bestStore, out revenue)) revenue = 0;
                if (!agg.perStoreCost.TryGetValue(_bestStore, out cost)) cost = 0;
            }
            // PRODUCED stays product-global: factories are not stores.
            if (sold == 0 && agg.produced == 0) continue;
            string top = "-";
            long topAmt = 0;
            foreach (var sp in agg.perStore)
            {
                if (sp.Value > topAmt) { topAmt = sp.Value; top = sp.Key; }
            }
            rows.Add(new BestSellersTable.BestRow
            {
                name = GetProductDisplayName(agg.raw),
                sold = sold,
                produced = agg.produced,
                revenue = revenue,
                profit = revenue - cost,
                topStore = top
            });
        }
        rows.Sort((a, b) =>
        {
            int c = b.sold.CompareTo(a.sold);
            if (c == 0) c = b.profit.CompareTo(a.profit);
            if (c == 0) c = string.Compare(a.name, b.name, StringComparison.Ordinal);
            return c;
        });
        string label = _bestStore == null
            ? ("TOTAL (" + rows.Count + " products)")
            : (_bestStore + " (" + rows.Count + " products)");
        _bestTable.ShowResults(rows, label);
        UpdateBestStoreBar(storeSold);
        BuildBestStoreTabs(storeSold);
    }

    static void UpdateBestStoreBar(Dictionary<string, long> storeSold)
    {
        if (_bestStoreText == null) return;
        int n = storeSold != null ? storeSold.Count : 0;
        string range = _bestRange == 1 ? ("DAY " + (_bestDay - 1))
            : _bestRange == 2 ? ("DAYS " + (_bestDay - 6) + "-" + _bestDay)
            : ("DAY " + _bestDay);
        if (n == 0)
        {
            _bestStoreText.gameObject.SetActive(true);
            _bestStoreText.text = "STORES (0) - no sales in " + range + ".";
            return;
        }
        _bestStoreText.text = "STORES (" + n + ") - tap a tab to filter | " + range + ":";
    }

    static void BuildBestStoreTabs(Dictionary<string, long> storeSold)
    {
        if (_bestStoreList == null) return;
        DataGrid.ClearRows(_bestStoreList);
        DataGrid.Warmup(_bestStoreList);
        if (storeSold == null || storeSold.Count == 0) return;
        var sorted = new List<KeyValuePair<string, long>>(storeSold);
        sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
        long total = 0;
        foreach (var kv in sorted) total += kv.Value;
        AddBestStoreTab(null, "ALL", total);
        foreach (var kv in sorted)
            AddBestStoreTab(kv.Key, kv.Key, kv.Value);
    }

    static void AddBestStoreTab(string key, string name, long qty)
    {
        if (_bestStoreList == null) return;
        bool sel = key == _bestStore;
        var tabGO = new GameObject(key == null ? "BestStoreAll" : "BestStoreTab");
        tabGO.transform.SetParent(_bestStoreList, false);
        var bg = tabGO.AddComponent<Image>();
        bg.color = sel ? Accent : RowBgAlt;
        var btn = tabGO.AddComponent<Button>();
        btn.targetGraphic = bg;
        var label = Text("BestStoreTabLabel", name + " (" + qty.ToString("N0") + ")", 26,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle,
            sel ? Color.white : InkLight);
        label.transform.SetParent(tabGO.transform, false);
        Anchor(label.gameObject, 0f, 0f, 1f, 1f, 16f, 8f, -16f, -8f);
        label.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        label.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        var size = tabGO.AddComponent<LayoutElement>();
        size.minWidth = 200f;
        size.preferredWidth = Mathf.Clamp(120f + (name != null ? name.Length : 3) * 17f, 220f, 560f);
        size.minHeight = 88f;
        string captured = key;
        btn.onClick.AddListener(() => SelectBestStore(captured));
    }

    static void SelectBestStore(string key)
    {
        _bestStore = key;
        RebuildBestAggregates();
        if (_salesContext != null)
            _salesContext.Logger.Info("The Big Board best sellers store filter: " + (key ?? "ALL"));
    }

    static bool InBestRange(int day)
    {
        if (_bestRange == 0) return day == _bestDay;
        if (_bestRange == 1) return day == _bestDay - 1;
        return day > _bestDay - 7 && day <= _bestDay;
    }

    static bool BestRangeHasHits(int index)
    {
        if (_bestHits == null) return false;
        foreach (var h in _bestHits)
        {
            bool inR = index == 0 ? h.day == _bestDay
                : index == 1 ? h.day == _bestDay - 1
                : h.day > _bestDay - 7 && h.day <= _bestDay;
            if (inR) return true;
        }
        return false;
    }

    // Table primitives (Text/Rect/Stretch/Anchor), row clearing, and the
    // TMP warmup come from DataGrid via "using static" above.
}
