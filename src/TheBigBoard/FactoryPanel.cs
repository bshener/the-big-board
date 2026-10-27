using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BAModAPI;
using BigAmbitions.Factories.Recipes;
using static TheBigBoard.DataGrid;

namespace TheBigBoard;

/// <summary>
/// TAB 3 - FACTORY. Left: own factory list. Right, two sub-views:
/// PRODUCTION PLAN: per-product merged rows (all lines making the same
/// product collapse into one row) with PLAN TODAY (0-24h staffed schedule)
/// and PLAN WEEK columns.
/// SUPPLY: per-ingredient NEED/WK, FACTORY stock, INCOMING
/// (importer deliveries due in the next 7 days), DAYS of coverage and
/// SHORTFALL - so the next importer order can be sized in advance.
/// Warehouse stock is not shown as a column but still counts toward DAYS
/// and SHORTFALL behind the scenes.
/// Formulas verified against in-game values: today = sum over today's shifts
/// of hours x recipe.GetScaledOutputAmount
/// (factoryworker skill); week = same over all open schedule days; material
/// need = staffed hours x recipe ingredient amount (unscaled).
/// </summary>
public static class FactoryPanel
{
    const string SkillFactoryWorker = "ba:skill_factoryworker";
    const string FactoryBusinessType = "ba:businesstype_factory";

    static ModContext _context;
    static GameObject _page;
    static int _view; // 0 = production plan, 1 = supply
    static readonly Dictionary<BuildingRegistration, FactoryData> _planCache = new Dictionary<BuildingRegistration, FactoryData>();
    static int _supplyGen; // cancels stale supply coroutines
    static bool _tabVisible;
    static int _lastFactoryCount = -1;
    static readonly List<BuildingRegistration> _factories = new List<BuildingRegistration>();
    static BuildingRegistration _selected;
    static FactoryData _data;

    static TMPro.TMP_Text _titleText;
    static TMPro.TMP_Text _snapshotText;
    static TMPro.TMP_Text _listCountText;
    static Transform _factoryListRoot;
    static GameObject _planView;
    static GameObject _supplyView;
    static PlanTable _planTable;
    static SupplyTable _supplyTable;
    static Image _planTabBg;
    static Image _supplyTabBg;
    static TMPro.TMP_Text _planTabLabel;
    static TMPro.TMP_Text _supplyTabLabel;
    static TMPro.TMP_Text _cardLines;
    static TMPro.TMP_Text _cardProducts;
    static TMPro.TMP_Text _cardToday;
    static TMPro.TMP_Text _cardWeek;
    static TMPro.TMP_Text _cardIngredients;
    static TMPro.TMP_Text _cardNeed;
    static TMPro.TMP_Text _cardIncoming;
    static TMPro.TMP_Text _cardShortfall;
    static GameObject _supplyProgGO;
    static RectTransform _supplyProgFill;
    static TMPro.TMP_Text _supplyProgLabel;

    // Palette, Text/Rect/Stretch/Anchor, ClearRows/Warmup/AddInfoRow,
    // scroll/header/row helpers: DataGrid (using static above). Only the
    // table geometry (pads/gaps/column widths) and factory logic stay here.

    sealed class FactoryData
    {
        public BuildingRegistration reg;
        public string name;
        public int day;
        public int hour;
        public int supplyDay = -1;
        public int supplyHour = -1;
        public bool supplyReady;
        public readonly Dictionary<string, long> materialNeed = new Dictionary<string, long>(StringComparer.Ordinal);
        public readonly Dictionary<string, long> factoryStock = new Dictionary<string, long>(StringComparer.Ordinal);
        public readonly List<LinePlan> lines = new List<LinePlan>();
        public readonly List<ProductRow> products = new List<ProductRow>();
        public readonly List<SupplyRow> supply = new List<SupplyRow>();
        public long totalToday;
        public long totalWeek;
        public long totalNeed;
        public long totalIncoming;
        public int shortfallItems;
    }

    sealed class LinePlan
    {
        public FactoryWorkstationInstance ws;
        public string productItem; // recipe output item id, null when no recipe
        public string productName;
        public long today;
        public long week;
    }

    public sealed class ProductRow
    {
        public string productItem;
        public string productName;
        public int lines;
        public long today;
        public long week;
        public long stock; // upstream on-hand (own factories + warehouses, stores excluded)
        public long sellToday; // retail units sold today (orderHistory + live orders)
        public long sellWeek; // retail units sold in rolling 7 days (D-6..D)
    }

    public sealed class SupplyRow
    {
        public string itemName;
        public string displayName;
        public long needWk;
        public long inFactory;
        public long inWarehouse;
        public long incoming;
        public double days; // -1 = n/a (no weekly need)
        public long shortfall;
    }

    public static void BuildPage(GameObject page, ModContext context)
    {
        _page = page;
        _context = context;
        _tabVisible = false;
        _data = null;
        _supplyGen++; // abort any in-flight count tied to old UI roots

        // Left column uses pure anchors (like the right-side tables): a
        // VerticalLayoutGroup with flexible height collapsed the list
        // viewport and clipped factories out of view.
        var leftGO = new GameObject("FactoryLeft");
        leftGO.transform.SetParent(page.transform, false);
        Anchor(leftGO, 0f, 0f, 0.30f, 1f, 0f, 0f, -24f, 0f);

        _listCountText = Text("FactoryListCount", "YOUR FACTORIES", 26,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, InkGrey);
        _listCountText.transform.SetParent(leftGO.transform, false);
        Anchor(_listCountText.gameObject, 0f, 1f, 1f, 1f, 0f, -50f, 0f, 0f);

        var listScrollGO = new GameObject("FactoryListScroll");
        listScrollGO.transform.SetParent(leftGO.transform, false);
        Anchor(listScrollGO, 0f, 0f, 1f, 1f, 0f, 88f, 0f, -60f);
        var listScroll = listScrollGO.AddComponent<ScrollRect>();
        listScroll.horizontal = false;

        var listViewportGO = new GameObject("FactoryListViewport");
        listViewportGO.transform.SetParent(listScrollGO.transform, false);
        Anchor(listViewportGO, 0f, 0f, 1f, 1f, 0f, 0f, -40f, 0f);
        listViewportGO.AddComponent<RectMask2D>();

        var listContentGO = new GameObject("FactoryList");
        listContentGO.transform.SetParent(listViewportGO.transform, false);
        var listContentRect = Rect(listContentGO);
        listContentRect.anchorMin = new Vector2(0, 1);
        listContentRect.anchorMax = new Vector2(1, 1);
        listContentRect.pivot = new Vector2(0.5f, 1);
        listContentRect.offsetMin = Vector2.zero;
        listContentRect.offsetMax = Vector2.zero;
        var listFitter = listContentGO.AddComponent<ContentSizeFitter>();
        listFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        listFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var listLayout = listContentGO.AddComponent<VerticalLayoutGroup>();
        listLayout.spacing = 10f;
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = false;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;

        listScroll.viewport = listViewportGO.GetComponent<RectTransform>();
        listScroll.content = listContentRect;
        _factoryListRoot = listContentGO.transform;

        var listBarGO = new GameObject("FactoryListScrollbar");
        listBarGO.transform.SetParent(listScrollGO.transform, false);
        Anchor(listBarGO, 1f, 0f, 1f, 1f, -32f, 0f, -4f, 0f);
        var listBarImg = listBarGO.AddComponent<Image>();
        listBarImg.color = new Color(0.10f, 0.12f, 0.15f, 1f);
        var listScrollbar = listBarGO.AddComponent<Scrollbar>();
        listScrollbar.direction = Scrollbar.Direction.BottomToTop;
        var listHandleGO = new GameObject("Handle");
        listHandleGO.transform.SetParent(listBarGO.transform, false);
        Stretch(listHandleGO);
        var listHandleImg = listHandleGO.AddComponent<Image>();
        listHandleImg.color = new Color(0.55f, 0.58f, 0.62f, 1f);
        listScrollbar.handleRect = listHandleGO.GetComponent<RectTransform>();
        listScrollbar.targetGraphic = listHandleImg;
        listScroll.verticalScrollbar = listScrollbar;
        listScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        var refreshGO = new GameObject("FactoryRefresh");
        refreshGO.transform.SetParent(leftGO.transform, false);
        Anchor(refreshGO, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 78f);
        var refreshBg = refreshGO.AddComponent<Image>();
        refreshBg.color = new Color(0.20f, 0.23f, 0.28f, 1f);
        var refreshBtn = refreshGO.AddComponent<Button>();
        refreshBtn.targetGraphic = refreshBg;
        var refreshLabel = Text("FactoryRefreshLabel", "REFRESH", 30,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, InkLight);
        refreshLabel.transform.SetParent(refreshGO.transform, false);
        Stretch(refreshLabel.gameObject);
        refreshBtn.onClick.AddListener(() => Refresh());

        var rightGO = new GameObject("FactoryRight");
        rightGO.transform.SetParent(page.transform, false);
        Anchor(rightGO, 0.30f, 0f, 1f, 1f, 24f, 0f, 0f, 0f);

        _titleText = Text("FactoryTitle", "FACTORY", 44,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, Color.white);
        _titleText.transform.SetParent(rightGO.transform, false);
        Anchor(_titleText.gameObject, 0f, 1f, 0.55f, 1f, 0f, -70f, 0f, 0f);

        _snapshotText = Text("FactorySnapshot", "", 26,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, InkGrey);
        _snapshotText.transform.SetParent(rightGO.transform, false);
        Anchor(_snapshotText.gameObject, 0f, 1f, 0.55f, 1f, 0f, -120f, 0f, -70f);

        var tabRowGO = new GameObject("FactoryTabs");
        tabRowGO.transform.SetParent(rightGO.transform, false);
        Anchor(tabRowGO, 0.45f, 1f, 1f, 1f, 0f, -120f, 0f, 0f);
        var tabRow = tabRowGO.AddComponent<HorizontalLayoutGroup>();
        tabRow.spacing = 12f;
        tabRow.childControlWidth = true;
        tabRow.childControlHeight = true;
        tabRow.childForceExpandWidth = true;
        tabRow.childForceExpandHeight = true;

        _planTabBg = MakeTabButton(tabRowGO.transform, "PRODUCTION PLAN", 0, out _planTabLabel);
        _supplyTabBg = MakeTabButton(tabRowGO.transform, "SUPPLY", 1, out _supplyTabLabel);
        RefreshTabStyles();

        _planView = new GameObject("PlanView");
        _planView.transform.SetParent(rightGO.transform, false);
        Anchor(_planView, 0f, 0f, 1f, 1f, 0f, 0f, 0f, -132f);
        BuildPlanView(_planView);

        _supplyView = new GameObject("SupplyView");
        _supplyView.transform.SetParent(rightGO.transform, false);
        Anchor(_supplyView, 0f, 0f, 1f, 1f, 0f, 0f, 0f, -132f);
        BuildSupplyView(_supplyView);
        _supplyView.SetActive(false);
    }

    static Image MakeTabButton(Transform parent, string label, int view, out TMPro.TMP_Text outLabel)
    {
        var go = new GameObject("FactoryViewTab" + view);
        go.transform.SetParent(parent, false);
        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.20f, 0.23f, 0.28f, 1f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = bg;
        outLabel = Text("FactoryViewTabLabel" + view, label, 28,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, InkLight);
        outLabel.transform.SetParent(go.transform, false);
        Anchor(outLabel.gameObject, 0f, 0f, 1f, 1f, 16f, 8f, -16f, -8f);
        int captured = view;
        btn.onClick.AddListener(() => SelectView(captured));
        return bg;
    }

    static void BuildPlanView(GameObject view)
    {
        var cardsGO = new GameObject("PlanCards");
        cardsGO.transform.SetParent(view.transform, false);
        Anchor(cardsGO, 0f, 1f, 1f, 1f, 0f, -150f, 0f, 0f);
        var cardsLayout = cardsGO.AddComponent<HorizontalLayoutGroup>();
        cardsLayout.spacing = 16f;
        cardsLayout.childControlWidth = true;
        cardsLayout.childControlHeight = true;
        cardsLayout.childForceExpandWidth = true;
        cardsLayout.childForceExpandHeight = true;

        _cardLines = AddCard(cardsGO.transform, "PRODUCTION LINES");
        _cardProducts = AddCard(cardsGO.transform, "PRODUCTS");
        _cardToday = AddCard(cardsGO.transform, "PLAN TODAY");
        _cardWeek = AddCard(cardsGO.transform, "PLAN / WEEK");

        _planTable = new PlanTable();
        _planTable.Init(_context);
        _planTable.BuildHeader(view, "PlanHeader", 0f, 0f, -214f, -158f);
        _planTable.BuildRows(view,
            "RowsScroll", "Viewport", "Rows", "Scrollbar", -222f);
    }

    static void BuildSupplyView(GameObject view)
    {
        var cardsGO = new GameObject("SupplyCards");
        cardsGO.transform.SetParent(view.transform, false);
        Anchor(cardsGO, 0f, 1f, 1f, 1f, 0f, -150f, 0f, 0f);
        var cardsLayout = cardsGO.AddComponent<HorizontalLayoutGroup>();
        cardsLayout.spacing = 16f;
        cardsLayout.childControlWidth = true;
        cardsLayout.childControlHeight = true;
        cardsLayout.childForceExpandWidth = true;
        cardsLayout.childForceExpandHeight = true;

        _cardIngredients = AddCard(cardsGO.transform, "INGREDIENTS");
        _cardNeed = AddCard(cardsGO.transform, "NEED / WEEK");
        _cardIncoming = AddCard(cardsGO.transform, "INCOMING");
        _cardShortfall = AddCard(cardsGO.transform, "SHORTFALL ITEMS");

        _supplyTable = new SupplyTable();
        _supplyTable.Init(_context);
        _supplyTable.BuildHeader(view, "SupplyHeader", 0f, 0f, -214f, -158f);

        _supplyProgGO = new GameObject("SupplyProgress");
        _supplyProgGO.transform.SetParent(view.transform, false);
        Anchor(_supplyProgGO, 0f, 1f, 1f, 1f, 40f, -252f, -40f, -220f);
        var progBg = _supplyProgGO.AddComponent<Image>();
        progBg.color = new Color(0.10f, 0.12f, 0.15f, 1f);
        var fillGO = new GameObject("SupplyProgressFill");
        fillGO.transform.SetParent(_supplyProgGO.transform, false);
        _supplyProgFill = Rect(fillGO);
        _supplyProgFill.anchorMin = Vector2.zero;
        _supplyProgFill.anchorMax = new Vector2(0f, 1f);
        _supplyProgFill.offsetMin = Vector2.zero;
        _supplyProgFill.offsetMax = Vector2.zero;
        var fillImg = fillGO.AddComponent<Image>();
        fillImg.color = Accent;
        _supplyProgLabel = Text("SupplyProgressLabel", "", 24,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, InkLight);
        _supplyProgLabel.transform.SetParent(_supplyProgGO.transform, false);
        Anchor(_supplyProgLabel.gameObject, 0f, 0f, 1f, 1f, 12f, 4f, -12f, -4f);
        _supplyProgGO.SetActive(false);

        _supplyTable.BuildRows(view,
            "RowsScroll", "Viewport", "Rows", "Scrollbar", -260f);
    }

    static TMPro.TMP_Text AddCard(Transform parent, string title)
    {
        var card = new GameObject("Card" + title);
        card.transform.SetParent(parent, false);
        var bg = card.AddComponent<Image>();
        bg.color = new Color(0.93f, 0.94f, 0.95f, 1f);
        var t = Text("CardTitle", title, 24,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Top, CardTitle);
        t.transform.SetParent(card.transform, false);
        Anchor(t.gameObject, 0f, 1f, 1f, 1f, 24f, -64f, -24f, -16f);
        var v = Text("CardValue", "-", 52,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Top, Color.black);
        v.transform.SetParent(card.transform, false);
        Anchor(v.gameObject, 0f, 1f, 1f, 1f, 24f, -142f, -24f, -60f);
        return v;
    }

    // Table geometry, sorting and resize live in PlanTable / SupplyTable
    // (TablePanel derivatives). Only factory logic stays here.

    // Table geometry / sorting / resize live in PlanTable / SupplyTable
    // (TablePanel derivatives). Only factory logic stays here.

    // Tab switches and factory selection render instantly (cached or fast
    // plan-only compute). The slow warehouse scan runs chunked in the
    // background and only for the SUPPLY view.
    public static void Refresh()
    {
        ShowTab(true);
    }

    public static void SetVisible(bool visible)
    {
        _tabVisible = visible;
    }

    /// <summary>Clears computed plans (panel reopened or day changed).</summary>
    public static void InvalidatePlanCache()
    {
        _planCache.Clear();
        _supplyGen++; // abort any in-flight warehouse count
    }

    /// <summary>Called every ~2s from the bootstrap. A new day recomputes
    /// the plan; a new hour re-runs the warehouse count while SUPPLY is open.
    /// Also picks up factories bought while the panel is open.</summary>
    public static void Tick()
    {
        try
        {
            if (!_tabVisible || _page == null) return;
            var save = SaveGameManager.Current;
            if (save == null) return;
            int day = save.Day;
            int hour = save.Hour;
            if (_data == null)
            {
                ReadFactories();
                if (_factories.Count == _lastFactoryCount) return;
                ShowTab(false);
                return;
            }
            if (day != _data.day)
            {
                ShowTab(true);
                return;
            }
            if (_view == 1 && _data.supplyReady && (day != _data.supplyDay || hour != _data.supplyHour))
                StartSupplyCompute();
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board factory tick failed: " + ex.Message);
        }
    }

    static void ShowTab(bool force)
    {
        if (_page == null) return;
        try
        {
            ReadFactories();
            _lastFactoryCount = _factories.Count;
            if (_selected == null || !_factories.Contains(_selected))
                _selected = _factories.Count > 0 ? _factories[0] : null;
            BuildFactoryList();
            if (_selected == null)
            {
                _data = null;
                RenderAll();
                return;
            }
            FactoryData cached;
            if (!force && _planCache.TryGetValue(_selected, out cached) && cached != null)
            {
                _data = cached;
                RenderAll();
                if (_view == 1 && !_data.supplyReady) StartSupplyCompute();
                return;
            }
            float planStart = Time.realtimeSinceStartup;
            _data = ComputePlan(_selected);
            _planCache[_selected] = _data;
            RenderAll();
            if (_view == 1) StartSupplyCompute();
            _context?.Logger.Info("The Big Board factory refresh: " + _factories.Count + " factories, " + _data.lines.Count + " lines in " + (int)((Time.realtimeSinceStartup - planStart) * 1000) + "ms.");
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board factory refresh failed: " + ex.Message);
        }
    }

    static void SelectFactory(BuildingRegistration reg)
    {
        _selected = reg;
        ShowTab(false);
    }

    static void SelectView(int view)
    {
        _view = view;
        RefreshTabStyles();
        RenderAll();
        if (view == 1 && _data != null && !_data.supplyReady) StartSupplyCompute();
    }

    static void StartSupplyCompute()
    {
        if (_data == null || _data.supplyReady) return;
        _supplyGen++;
        RenderSupply(); // shows the loading placeholder
        PhoneButtonInstaller.RunJob(SupplyCoroutine(_data, _supplyGen));
    }

    static void UpdateSupplyProgress(int done, int total)
    {
        if (_supplyProgGO == null) return;
        _supplyProgGO.SetActive(true);
        if (_supplyProgFill != null)
        {
            float p = total > 0 ? Mathf.Clamp01((float)done / total) : 0f;
            _supplyProgFill.anchorMax = new Vector2(p, 1f);
        }
        if (_supplyProgLabel != null)
            _supplyProgLabel.text = total > 0 ? (done + " / " + total + " ingredients") : "...";
    }

    static IEnumerator SupplyCoroutine(FactoryData data, int gen)
    {
        float start = Time.realtimeSinceStartup;
        var warehouse = new Dictionary<string, long>(StringComparer.Ordinal);
        var keys = new List<string>(data.materialNeed.Keys);
        int done = 0;
        foreach (var item in keys)
        {
            // Abort if the user switched factory/tab or forced a refresh.
            if (gen != _supplyGen || _data != data) yield break;
            try
            {
                var save = SaveGameManager.Current;
                if (save != null && save.BuildingRegistrations != null)
                {
                    foreach (var reg in save.BuildingRegistrations)
                    {
                        if (reg == null || !(reg is Entities.Warehouse)) continue;
                        int amt = 0;
                        try { amt = Helpers.BuildingHelper.CountTotalResourcesInStock(reg, item); }
                        catch { amt = 0; }
                        if (amt > 0) AddAmount(warehouse, item, amt);
                    }
                }
            }
            catch { }
            done++;
            if (_data == data) UpdateSupplyProgress(done, keys.Count);
            // One ingredient per frame: the game stays responsive.
            yield return null;
        }
        if (gen != _supplyGen || _data != data) yield break;
        try
        {
            var incoming = ReadIncoming(data.materialNeed, data.day);
            data.supply.Clear();
            data.totalNeed = 0;
            data.totalIncoming = 0;
            data.shortfallItems = 0;
            var needKeys = new List<string>(data.materialNeed.Keys);
            needKeys.Sort();
            foreach (var item in needKeys)
            {
                long need = Math.Max(0, data.materialNeed[item]);
                long f = Amount(data.factoryStock, item);
                long w = Amount(warehouse, item);
                long inc = Amount(incoming, item);
                long shortfall = Math.Max(0, need - f - w - inc);
                double days = need > 0 ? (f + w + inc) / (need / 7.0) : -1;
                data.supply.Add(new SupplyRow
                {
                    itemName = item,
                    displayName = GetProductDisplayName(item),
                    needWk = need,
                    inFactory = f,
                    inWarehouse = w,
                    incoming = inc,
                    days = days,
                    shortfall = shortfall
                });
                data.totalNeed += need;
                data.totalIncoming += inc;
                if (shortfall > 0) data.shortfallItems++;
            }
            data.supply.Sort((a, b) =>
            {
                int c = b.shortfall.CompareTo(a.shortfall);
                if (c != 0) return c;
                return b.needWk.CompareTo(a.needWk);
            });
            var saveNow = SaveGameManager.Current;
            data.supplyDay = saveNow != null ? saveNow.Day : data.day;
            data.supplyHour = saveNow != null ? saveNow.Hour : data.hour;
            data.supplyReady = true;
            if (_data == data) RenderSupply();
            _context?.Logger.Info("The Big Board factory supply ready: " + data.supply.Count + " ingredients in " + (int)((Time.realtimeSinceStartup - start) * 1000) + "ms.");
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board factory supply failed: " + ex.Message);
        }
    }

    static void RefreshTabStyles()
    {
        if (_planTabBg != null)
        {
            bool sel = _view == 0;
            _planTabBg.color = sel ? Accent : new Color(0.20f, 0.23f, 0.28f, 1f);
            if (_planTabLabel != null) _planTabLabel.color = sel ? Color.white : InkLight;
        }
        if (_supplyTabBg != null)
        {
            bool sel = _view == 1;
            _supplyTabBg.color = sel ? Accent : new Color(0.20f, 0.23f, 0.28f, 1f);
            if (_supplyTabLabel != null) _supplyTabLabel.color = sel ? Color.white : InkLight;
        }
        if (_planView != null) _planView.SetActive(_view == 0);
        if (_supplyView != null) _supplyView.SetActive(_view == 1);
    }

    static void RenderAll()
    {
        if (_data == null)
        {
            if (_titleText != null) _titleText.text = "FACTORY";
            if (_snapshotText != null) _snapshotText.text = _factories.Count == 0
                ? "No factories found. Buy a factory first."
                : "Select a factory.";
            _planTable.ShowInfo("No factory selected.");
            _supplyTable.ShowInfo("No factory selected.");
            SetCardsEmpty();
            return;
        }
        if (_titleText != null) _titleText.text = _data.name;
        if (_snapshotText != null)
            _snapshotText.text = "Snapshot: Day " + _data.day + " - " + _data.hour.ToString("00") + ":00";
        RenderPlan();
        RenderSupply();
    }

    static void SetCardsEmpty()
    {
        if (_cardLines != null) _cardLines.text = "-";
        if (_cardProducts != null) _cardProducts.text = "-";
        if (_cardToday != null) _cardToday.text = "-";
        if (_cardWeek != null) _cardWeek.text = "-";
        if (_cardIngredients != null) _cardIngredients.text = "-";
        if (_cardNeed != null) _cardNeed.text = "-";
        if (_cardIncoming != null) _cardIncoming.text = "-";
        if (_cardShortfall != null) _cardShortfall.text = "-";
    }

    static void RenderPlan()
    {
        if (_cardLines != null) _cardLines.text = _data.lines.Count.ToString("N0");
        if (_cardProducts != null) _cardProducts.text = _data.products.Count.ToString("N0");
        if (_cardToday != null) _cardToday.text = _data.totalToday.ToString("N0");
        if (_cardWeek != null) _cardWeek.text = _data.totalWeek.ToString("N0");
        _planTable.SetRows(_data.products);
        // Per-product audit line: plan vs upstream stock vs retail sales.
        // Lets plan/actual gaps (e.g. popcorn) be diagnosed from Player.log
        // alone, without asking the player for the on-screen numbers.
        try
        {
            if (_context != null && _data.products != null)
            {
                foreach (var p in _data.products)
                {
                    if (p == null) continue;
                    _context.Logger.Info("The Big Board factory plan [" + _data.name + "]: "
                        + p.productName + " lines=" + p.lines
                        + " planT=" + p.today + " planW=" + p.week
                        + " stock=" + p.stock + " sellT=" + p.sellToday + " sellW=" + p.sellWeek);
                }
            }
        }
        catch { }
    }

    static void RenderSupply()
    {
        if (_data == null) return;
        if (!_data.supplyReady)
        {
            if (_cardIngredients != null) _cardIngredients.text = _data.materialNeed.Count.ToString("N0");
            if (_cardNeed != null) _cardNeed.text = "...";
            if (_cardIncoming != null) _cardIncoming.text = "...";
            if (_cardShortfall != null)
            {
                _cardShortfall.text = "...";
                _cardShortfall.color = Color.black;
            }
            if (_supplyProgGO != null) _supplyProgGO.SetActive(true);
            _supplyTable.ShowInfo("Counting warehouses...");
            return;
        }
        if (_supplyProgGO != null) _supplyProgGO.SetActive(false);
        if (_cardIngredients != null) _cardIngredients.text = _data.supply.Count.ToString("N0");
        if (_cardNeed != null) _cardNeed.text = _data.totalNeed.ToString("N0");
        if (_cardIncoming != null) _cardIncoming.text = _data.totalIncoming.ToString("N0");
        if (_cardShortfall != null)
        {
            _cardShortfall.text = _data.shortfallItems.ToString("N0");
            _cardShortfall.color = _data.shortfallItems > 0 ? WarnRed : ProfitGreen;
        }
        _supplyTable.SetRows(_data.supply);
        // Shortfall audit lines: only items actually short, so the log stays
        // small while material starvation is diagnosable from Player.log.
        try
        {
            if (_context != null && _data.supply != null)
            {
                foreach (var s in _data.supply)
                {
                    if (s == null || s.shortfall <= 0) continue;
                    _context.Logger.Info("The Big Board factory shortfall [" + _data.name + "]: "
                        + s.displayName + " needWk=" + s.needWk
                        + " inFactory=" + s.inFactory + " incoming=" + s.incoming
                        + " days=" + s.days.ToString("F1") + " shortfall=" + s.shortfall);
                }
            }
        }
        catch { }
    }

    // ---------- data ----------

    static void ReadFactories()
    {
        _factories.Clear();
        var save = SaveGameManager.Current;
        if (save == null || save.BuildingRegistrations == null) return;
        foreach (var reg in save.BuildingRegistrations)
        {
            if (reg == null || !reg.RentedByPlayer) continue;
            if (!string.Equals(reg.businessTypeName, FactoryBusinessType, StringComparison.OrdinalIgnoreCase))
                continue;
            _factories.Add(reg);
        }
        _factories.Sort((a, b) => string.Compare(FactoryName(a), FactoryName(b), StringComparison.Ordinal));
    }

    static string FactoryName(BuildingRegistration reg)
    {
        if (reg == null) return "?";
        if (!string.IsNullOrEmpty(reg.BusinessName)) return reg.BusinessName;
        return reg.StreetName + " " + reg.StreetNumber;
    }

    // Fast part: lines, merged products, material need, factory stock.
    // The slow warehouse scan happens later in SupplyCoroutine.
    static FactoryData ComputePlan(BuildingRegistration reg)
    {
        var data = new FactoryData { reg = reg, name = FactoryName(reg) };
        var save = SaveGameManager.Current;
        data.day = save != null ? save.Day : 0;
        data.hour = save != null ? save.Hour : 0;

        if (reg.itemInstances == null) return data;
        var workstations = new List<FactoryWorkstationInstance>();
        foreach (var inst in reg.itemInstances.Values)
        {
            var ws = inst as FactoryWorkstationInstance;
            if (ws == null) continue;
            if (string.IsNullOrEmpty(ws.workstationType)) continue;
            workstations.Add(ws);
        }

        var materialNeed = new Dictionary<string, long>(StringComparer.Ordinal);
        var productToday = new Dictionary<string, long>(StringComparer.Ordinal);
        var productWeek = new Dictionary<string, long>(StringComparer.Ordinal);
        var productNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var productLines = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var ws in workstations)
        {
            var line = new LinePlan { ws = ws };
            Recipe recipe = null;
            try { recipe = ws.SelectedRecipe; }
            catch { recipe = null; }
            if ((UnityEngine.Object)recipe != null)
            {
                string outputItem = null;
                try { outputItem = recipe.output.item; }
                catch { outputItem = null; }
                if (!string.IsNullOrEmpty(outputItem))
                {
                    line.productItem = outputItem;
                    string created = null;
                    try { created = ws.CreatedItemName; }
                    catch { created = null; }
                    line.productName = GetProductDisplayName(string.IsNullOrEmpty(created) ? outputItem : created);
                    line.today = PlanToday(ws, recipe);
                    line.week = PlanWeek(reg, ws, recipe, materialNeed);
                }
            }
            if (line.productItem == null)
            {
                line.productItem = "__NORECIPE__";
                line.productName = "-- no recipe --";
            }
            data.lines.Add(line);
            data.totalToday += line.today;
            data.totalWeek += line.week;
            AddAmount(productToday, line.productItem, line.today);
            AddAmount(productWeek, line.productItem, line.week);
            if (!productNames.ContainsKey(line.productItem))
                productNames[line.productItem] = line.productName;
            int n;
            productLines.TryGetValue(line.productItem, out n);
            productLines[line.productItem] = n + 1;
        }

        var sortedKeys = new List<string>(productWeek.Keys);
        sortedKeys.Sort((a, b) =>
        {
            int c = productWeek[b].CompareTo(productWeek[a]);
            if (c != 0) return c;
            return string.Compare(
                productNames.ContainsKey(a) ? productNames[a] : a,
                productNames.ContainsKey(b) ? productNames[b] : b, StringComparison.Ordinal);
        });
        foreach (var key in sortedKeys)
        {
            long t;
            productToday.TryGetValue(key, out t);
            data.products.Add(new ProductRow
            {
                productItem = key,
                productName = productNames.ContainsKey(key) ? productNames[key] : key,
                lines = productLines.ContainsKey(key) ? productLines[key] : 0,
                today = t,
                week = productWeek[key]
            });
        }

        // Factory stock is cheap to read (one building); warehouse stock is
        // slow and runs later in SupplyCoroutine, only for the SUPPLY view.
        foreach (var kv in materialNeed) data.materialNeed[kv.Key] = kv.Value;
        foreach (var kv in ReadFactoryStorage(reg)) data.factoryStock[kv.Key] = kv.Value;
        data.supplyReady = false;
        FillProductSalesAndStock(data);
        return data;
    }

    // Product-level STOCK (own factories + warehouses, stores excluded) and
    // retail SELL TODAY / SELL WEEK (own stores, factories excluded: their
    // history is production exports, not retail). Same buckets as the SALES
    // tab: TODAY = day D (+ live paid orders), WEEK = rolling D-6..D.
    static void FillProductSalesAndStock(FactoryData data)
    {
        if (data == null || data.products.Count == 0) return;
        var save = SaveGameManager.Current;
        if (save == null || save.BuildingRegistrations == null) return;
        int today = data.day;
        var byPkey = new Dictionary<string, ProductRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in data.products)
        {
            if (p == null || string.IsNullOrEmpty(p.productItem)) continue;
            if (p.productItem == "__NORECIPE__") continue;
            if (!byPkey.ContainsKey(p.productItem)) byPkey[p.productItem] = p;
        }
        if (byPkey.Count == 0) return;
        var upstream = new List<BuildingRegistration>();
        var retail = new List<BuildingRegistration>();
        foreach (var reg in save.BuildingRegistrations)
        {
            if (reg == null || !reg.RentedByPlayer) continue;
            if (reg is Entities.Warehouse) { upstream.Add(reg); continue; }
            bool isFactory = string.Equals(reg.businessTypeName, FactoryBusinessType, StringComparison.OrdinalIgnoreCase);
            if (isFactory) { upstream.Add(reg); continue; }
            retail.Add(reg);
        }
        // STOCK: authoritative helper per product x upstream building.
        foreach (var kv in byPkey)
        {
            long sum = 0;
            foreach (var reg in upstream)
            {
                try { sum += Helpers.BuildingHelper.CountTotalResourcesInStock(reg, kv.Key); }
                catch { }
            }
            kv.Value.stock = Math.Max(0, sum);
        }
        // SELL: retail orderHistory + today's live paid orders.
        foreach (var reg in retail)
        {
            try
            {
                if (reg.orderHistory != null)
                {
                    foreach (var entry in reg.orderHistory)
                    {
                        if (entry == null || entry.itemSales == null) continue;
                        bool isToday = entry.dayNumber == today;
                        bool inWeek = entry.dayNumber > today - 7 && entry.dayNumber <= today;
                        if (!isToday && !inWeek) continue;
                        foreach (var rep in entry.itemSales)
                        {
                            if (rep == null || rep.itemName == null || rep.amountSold <= 0) continue;
                            ProductRow row;
                            if (!byPkey.TryGetValue(rep.itemName, out row)) continue;
                            if (isToday) row.sellToday += rep.amountSold;
                            if (inWeek) row.sellWeek += rep.amountSold;
                        }
                    }
                }
            }
            catch { }
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
                            ProductRow row;
                            if (!byPkey.TryGetValue(e.itemName, out row)) continue;
                            row.sellToday += 1;
                            row.sellWeek += 1;
                        }
                    }
                }
            }
            catch { }
        }
    }

    static long PlanToday(FactoryWorkstationInstance ws, Recipe recipe)
    {
        long planned = 0;
        List<WorkShift> shifts;
        try { shifts = ItemHelper.GetTodayWorkShifts(ws); }
        catch { return 0; }
        if (shifts == null) return 0;
        foreach (var shift in shifts)
        {
            if (shift == null) continue;
            if (string.IsNullOrEmpty(shift.employeeId)) continue;
            Entities.EmployeeInstance emp = null;
            try { emp = Helpers.EmployeeHelper.GetEmployeeById(shift.employeeId, false); }
            catch { emp = null; }
            if (emp == null) continue;
            int hours = 0;
            try { hours = Math.Max(0, shift.DurationInHours); }
            catch { continue; }
            int scaled = 0;
            try { scaled = Math.Max(0, recipe.GetScaledOutputAmount(emp.GetSkillValue(SkillFactoryWorker))); }
            catch { continue; }
            planned += (long)hours * scaled;
        }
        return planned;
    }

    static long PlanWeek(BuildingRegistration reg, FactoryWorkstationInstance ws,
        Recipe recipe, Dictionary<string, long> materialNeed)
    {
        long planned = 0;
        if (reg.scheduleDays == null) return 0;
        string wsId = null;
        try { wsId = ws.id; }
        catch { return 0; }
        foreach (var day in reg.scheduleDays)
        {
            if (day == null || !day.isOpen || day.workShifts == null) continue;
            foreach (var shift in day.workShifts)
            {
                if (shift == null) continue;
                if (!string.Equals(shift.itemInstanceId, wsId, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(shift.employeeId)) continue;
                int hours = 0;
                try { hours = Math.Max(0, shift.DurationInHours); }
                catch { continue; }
                if (hours <= 0) continue;
                Entities.EmployeeInstance emp = null;
                try { emp = Helpers.EmployeeHelper.GetEmployeeById(shift.employeeId, false); }
                catch { emp = null; }
                if (emp == null) continue;
                int scaled = 0;
                try { scaled = Math.Max(0, recipe.GetScaledOutputAmount(emp.GetSkillValue(SkillFactoryWorker))); }
                catch { continue; }
                planned += (long)hours * scaled;
                try
                {
                    if (recipe.ingredients != null)
                    {
                        foreach (var ing in recipe.ingredients)
                        {
                            if (ing.item == null || ing.amount <= 0) continue;
                            AddAmount(materialNeed, ing.item, (long)hours * ing.amount);
                        }
                    }
                }
                catch { }
            }
        }
        return planned;
    }

    static Dictionary<string, long> ReadFactoryStorage(BuildingRegistration reg)
    {
        var stock = new Dictionary<string, long>(StringComparer.Ordinal);
        try
        {
            if (reg.itemInstances == null) return stock;
            int tag = BigAmbitions.Tags.TagRef.Itemtag.iswarehousestorage;
            foreach (var inst in reg.itemInstances.Values)
            {
                if (inst == null) continue;
                BigAmbitions.Items.Item cached = null;
                try { cached = inst.ItemCached; }
                catch { continue; }
                if ((UnityEngine.Object)cached == null) continue;
                bool tagged = false;
                try { tagged = cached.HasTag(tag); }
                catch { continue; }
                if (!tagged) continue;
                if (inst.cargoInstances == null) continue;
                foreach (var c in inst.cargoInstances)
                {
                    if (c == null || string.IsNullOrEmpty(c.itemName) || c.amount <= 0) continue;
                    AddAmount(stock, c.itemName, c.amount);
                }
            }
        }
        catch { }
        return stock;
    }

    static Dictionary<string, long> ReadIncoming(Dictionary<string, long> materialNeed, int today)
    {
        var incoming = new Dictionary<string, long>(StringComparer.Ordinal);
        if (materialNeed == null || materialNeed.Count == 0) return incoming;
        try
        {
            var save = SaveGameManager.Current;
            if (save == null || save.importPartnerships == null) return incoming;
            foreach (var partnership in save.importPartnerships)
            {
                if (partnership == null || !partnership.isActive) continue;
                if (partnership.nextDeliveryDay < today || partnership.nextDeliveryDay >= today + 7) continue;
                if (partnership.products == null) continue;
                foreach (var product in partnership.products)
                {
                    if (product == null || string.IsNullOrEmpty(product.itemName)) continue;
                    if (!materialNeed.ContainsKey(product.itemName)) continue;
                    if (product.amount > 0) AddAmount(incoming, product.itemName, product.amount);
                }
            }
        }
        catch { }
        return incoming;
    }

    static void AddAmount(Dictionary<string, long> dict, string key, long amount)
    {
        if (dict == null || string.IsNullOrEmpty(key) || amount == 0) return;
        long cur;
        dict.TryGetValue(key, out cur);
        dict[key] = cur + amount;
    }

    static long Amount(Dictionary<string, long> dict, string key)
    {
        if (dict == null) return 0;
        long v;
        return dict.TryGetValue(key, out v) ? v : 0;
    }

    static string GetProductDisplayName(string itemId)
    {
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

    // ---------- UI rendering ----------

    static void BuildFactoryList()
    {
        if (_factoryListRoot == null) return;
        foreach (Transform child in _factoryListRoot)
            UnityEngine.Object.Destroy(child.gameObject);
        Warmup(_factoryListRoot);
        if (_listCountText != null)
            _listCountText.text = "YOUR FACTORIES (" + _factories.Count + ")";
        foreach (var reg in _factories)
        {
            var go = new GameObject("FactoryTab");
            go.transform.SetParent(_factoryListRoot, false);
            var bg = go.AddComponent<Image>();
            bool sel = reg == _selected;
            bg.color = sel ? Accent : RowBgAlt;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            var label = Text("FactoryTabLabel", FactoryName(reg), 30,
                TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle,
                sel ? Color.white : InkLight);
            label.transform.SetParent(go.transform, false);
            Anchor(label.gameObject, 0f, 0f, 1f, 1f, 16f, 8f, -16f, -8f);
            label.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            label.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            var size = go.AddComponent<LayoutElement>();
            size.minHeight = 96f;
            var captured = reg;
            btn.onClick.AddListener(() => SelectFactory(captured));
        }
        if (_factories.Count == 0)
            AddInfoRow(_factoryListRoot, "No factories found.");
    }

}
