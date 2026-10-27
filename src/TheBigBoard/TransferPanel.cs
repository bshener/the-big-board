using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BAModAPI;
using BigAmbitions.Factories.Recipes;
using BigAmbitions.Items;
using Buildings;
using Buildings.Office.Headquarters;
using Entities;
using Extensions;
using Streets;
using static TheBigBoard.DataGrid;

namespace TheBigBoard;

/// <summary>
/// TAB 7 - TRANSFER. All inter-warehouse logistics plans in one place.
/// Left: every own factory + warehouse (sources), with route counts.
/// Right: the selected source's plan as a (destination x product) table:
/// DEST STOCK (units at the target now), MIN STOCK (the plan target -
/// click a row to edit it, 0 removes the product), TO SEND (what the next
/// logistics run will move: target minus dest stock, clamped by source
/// stock). The game's own factory screens label this "deliver", but the
/// engine already tops the destination up to the target - so entering
/// 1000 with 600 at the destination sends 400.
/// Nothing here delivers instantly: the tab only edits the plan (amounts,
/// destinations, products, export setup) and the game runs it on its own
/// logistics schedule. ADD DESTINATION / ADD PRODUCT add routes (same
/// building rules as the vanilla BizMan logistics screen), MIN STOCK tops
/// the destination up, and EXPORT EXCESS (factory sources only) pins an
/// import/export destination LAST with SELL ALL on every finished good -
/// warehouses fill first, leftovers sell each run, nothing piles up.
/// Editing is gated behind an opt-in toggle (default OFF, persisted per
/// machine): while disabled the tab is read-only and the game keeps its
/// own distribution untouched.
/// </summary>
public static class TransferPanel
{
    const string FactoryBusinessType = "ba:businesstype_factory";
    const string ImportExportBusinessType = "ba:businesstype_importexport";
    const int MaxTarget = 9999999;
    // Editing opt-in (default OFF), persisted like the column widths.
    // FROZEN for save compat: renaming this key orphans the preference.
    const string EditingKey = "TheBigBoard.transferEditing.v1";

    static ModContext _context;
    static GameObject _page;
    static bool _tabVisible;
    static int _lastDay = -1;
    static int _lastHour = -1;

    sealed class SourceEntry
    {
        public BuildingRegistration reg;
        public string name;
        public bool isFactory;
        public LogisticsManagerPlan plan;
    }

    static readonly List<SourceEntry> _sources = new List<SourceEntry>();
    static SourceEntry _selected;

    static TMPro.TMP_Text _titleText;
    static TMPro.TMP_Text _snapshotText;
    static TMPro.TMP_Text _listCountText;
    static Transform _sourceListRoot;
    static GameObject _detailView;
    static TransferTable _table;
    static readonly List<TransferTable.TransferRow> _rows = new List<TransferTable.TransferRow>();
    static GameObject _refreshBtnGO;
    static GameObject _addDestBtnGO;
    static GameObject _addProdBtnGO;
    static GameObject _exportBtnGO;
    static GameObject _createBtnGO;
    static GameObject _editToggleBtnGO;
    static GameObject _rowMenu;
    static GameObject _modal;

    static bool EditingEnabled()
    {
        try { return UnityEngine.PlayerPrefs.GetInt(EditingKey, 0) != 0; }
        catch { return false; }
    }

    static void ToggleEditing()
    {
        bool next = !EditingEnabled();
        try
        {
            UnityEngine.PlayerPrefs.SetInt(EditingKey, next ? 1 : 0);
            UnityEngine.PlayerPrefs.Save();
        }
        catch { }
        _context?.Logger.Info("The Big Board transfer editing "
            + (next ? "ENABLED." : "DISABLED - game runs its own distribution."));
        Refresh();
    }

    static string EditingDisabledText()
    {
        return "TRANSFER management is DISABLED."
            + "\nThe game keeps running its own distribution - nothing here can change it."
            + "\nPress ENABLE EDITING below to allow edits (amounts, destinations, exports)."
            + "\nThe game still executes everything on its own 02:00 schedule.";
    }

    static void PaintEditToggle(bool editing)
    {
        if (_editToggleBtnGO == null) return;
        var label = _editToggleBtnGO.GetComponentInChildren<TMPro.TMP_Text>(true);
        if (label != null) label.text = editing ? "EDITING: ON" : "ENABLE EDITING";
        var img = _editToggleBtnGO.GetComponent<Image>();
        if (img != null) img.color = editing ? BtnIdle : Accent;
    }

    public static void BuildPage(GameObject page, ModContext context)
    {
        _page = page;
        _context = context;
        _tabVisible = false;
        _selected = null;
        _sources.Clear();
        _rows.Clear();

        var leftGO = new GameObject("TransferLeft");
        leftGO.transform.SetParent(page.transform, false);
        Anchor(leftGO, 0f, 0f, 0.30f, 1f, 0f, 0f, -24f, 0f);

        var listHeadBgGO = new GameObject("TransferListHeadBg");
        listHeadBgGO.transform.SetParent(leftGO.transform, false);
        Anchor(listHeadBgGO, 0f, 1f, 1f, 1f, 0f, -58f, 0f, 0f);
        var listHeadBg = listHeadBgGO.AddComponent<Image>();
        listHeadBg.color = new Color(0.08f, 0.10f, 0.13f, 0.78f);
        _listCountText = Text("TransferListCount", "SOURCES", 26,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, InkLight);
        _listCountText.transform.SetParent(leftGO.transform, false);
        Anchor(_listCountText.gameObject, 0f, 1f, 1f, 1f, 0f, -50f, 0f, 0f);

        var listScrollGO = new GameObject("TransferListScroll");
        listScrollGO.transform.SetParent(leftGO.transform, false);
        Anchor(listScrollGO, 0f, 0f, 1f, 1f, 0f, 0f, 0f, -60f);
        var listScroll = listScrollGO.AddComponent<ScrollRect>();
        listScroll.horizontal = false;

        var listViewportGO = new GameObject("TransferListViewport");
        listViewportGO.transform.SetParent(listScrollGO.transform, false);
        Anchor(listViewportGO, 0f, 0f, 1f, 1f, 0f, 0f, -40f, 0f);
        listViewportGO.AddComponent<RectMask2D>();

        var listContentGO = new GameObject("TransferList");
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
        _sourceListRoot = listContentGO.transform;

        var listBarGO = new GameObject("TransferListScrollbar");
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

        var rightGO = new GameObject("TransferRight");
        rightGO.transform.SetParent(page.transform, false);
        Anchor(rightGO, 0.30f, 0f, 1f, 1f, 24f, 0f, 0f, 0f);

        var headBgGO = new GameObject("TransferHeadBg");
        headBgGO.transform.SetParent(rightGO.transform, false);
        Anchor(headBgGO, 0f, 1f, 1f, 1f, 0f, -128f, 0f, 0f);
        var headBg = headBgGO.AddComponent<Image>();
        headBg.color = new Color(0.08f, 0.10f, 0.13f, 0.78f);
        _titleText = Text("TransferTitle", "TRANSFER", 40,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, Color.white);
        _titleText.transform.SetParent(rightGO.transform, false);
        Anchor(_titleText.gameObject, 0f, 1f, 0.62f, 1f, 0f, -64f, 0f, 0f);

        _snapshotText = Text("TransferSnapshot", "", 24,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, InkLight);
        _snapshotText.transform.SetParent(rightGO.transform, false);
        Anchor(_snapshotText.gameObject, 0f, 1f, 1f, 1f, 0f, -122f, 0f, -64f);
        _snapshotText.textWrappingMode = TMPro.TextWrappingModes.Normal;

        _detailView = new GameObject("TransferDetailView");
        _detailView.transform.SetParent(rightGO.transform, false);
        Anchor(_detailView, 0f, 0f, 1f, 1f, 0f, 86f, 0f, -132f);
        _table = new TransferTable();
        _table.Init(context);
        _table.OnEdit = EditTarget;
        _table.OnMenu = OpenRowMenu;
        _table.BuildHeader(_detailView, "TransferHeader", -52f, 0f, -108f, -52f);
        _table.BuildRows(_detailView,
            "TransferRowsScroll", "TransferViewport", "TransferRows", "TransferScrollbar", -116f);
        _table.ShowInfo("Select a source.");

        var barGO = new GameObject("TransferActionBar");
        barGO.transform.SetParent(rightGO.transform, false);
        Anchor(barGO, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 78f);
        var bar = barGO.AddComponent<HorizontalLayoutGroup>();
        bar.spacing = 12f;
        bar.childControlWidth = true;
        bar.childControlHeight = true;
        bar.childForceExpandWidth = true;
        bar.childForceExpandHeight = true;

        _editToggleBtnGO = MakeBarButton(barGO.transform, "ENABLE EDITING", Accent, () => ToggleEditing());
        _refreshBtnGO = MakeBarButton(barGO.transform, "REFRESH", BtnIdle, () => Refresh());
        _addDestBtnGO = MakeBarButton(barGO.transform, "ADD DEST", BtnIdle, () => OpenDestinationPicker());
        _addProdBtnGO = MakeBarButton(barGO.transform, "ADD PRODUCT", BtnIdle, () => OpenAddProductFlow());
        _exportBtnGO = MakeBarButton(barGO.transform, "EXPORT EXCESS", BtnIdle, () => ExportExcess());
        _createBtnGO = MakeBarButton(barGO.transform, "CREATE PLAN", Accent, () => CreatePlan());
        _editToggleBtnGO.SetActive(false);
        _refreshBtnGO.SetActive(false);
        _addDestBtnGO.SetActive(false);
        _addProdBtnGO.SetActive(false);
        _exportBtnGO.SetActive(false);
        _createBtnGO.SetActive(false);
    }

    static GameObject MakeBarButton(Transform parent, string label, Color bg, Action onClick)
    {
        var go = new GameObject("TransferBar_" + label);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = bg;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var text = Text("TransferBarLabel_" + label, label, 26,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, Color.white);
        text.transform.SetParent(go.transform, false);
        Stretch(text.gameObject);
        btn.onClick.AddListener(() => onClick());
        return go;
    }

    public static void SetVisible(bool visible)
    {
        _tabVisible = visible;
    }

    /// <summary>Day/hour change restocks everything: refresh while visible.</summary>
    public static void Tick()
    {
        try
        {
            if (!_tabVisible || _page == null) return;
            var save = SaveGameManager.Current;
            if (save == null) return;
            if (save.Day != _lastDay || save.Hour != _lastHour)
                Refresh();
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board transfer tick failed: " + ex.Message);
        }
    }

    public static void Refresh()
    {
        if (_page == null) return;
        try
        {
            var save = SaveGameManager.Current;
            if (save == null) return;
            _lastDay = save.Day;
            try { _lastHour = save.Hour; } catch { }
            ReadSources(save);
            if (_selected == null || !_sources.Contains(_selected))
                _selected = _sources.Count > 0 ? _sources[0] : null;
            BuildSourceList();
            RenderDetail();
            int plans = 0;
            foreach (var s in _sources) if (s.plan != null) plans++;
            _context?.Logger.Info("The Big Board transfer refresh: " + _sources.Count
                + " sources, " + plans + " plans, day " + _lastDay + ".");
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board transfer refresh failed: " + ex.Message);
        }
    }

    static void SelectSource(SourceEntry entry)
    {
        _selected = entry;
        BuildSourceList();
        RenderDetail();
        try
        {
            _context?.Logger.Info("The Big Board transfer selected: "
                + (entry != null ? entry.name : "-"));
        }
        catch { }
    }

    // ---------- data ----------

    static void ReadSources(GameInstance save)
    {
        _sources.Clear();
        if (save.BuildingRegistrations == null) return;
        foreach (var reg in save.BuildingRegistrations)
        {
            if (reg == null) continue;
            bool rented = false;
            try { rented = reg.RentedByPlayer; } catch { continue; }
            if (!rented) continue;
            bool isFactory = false;
            try { isFactory = string.Equals(reg.businessTypeName, FactoryBusinessType, StringComparison.OrdinalIgnoreCase); }
            catch { continue; }
            bool isWarehouse = reg is Entities.Warehouse;
            if (!isFactory && !isWarehouse) continue;
            var entry = new SourceEntry
            {
                reg = reg,
                isFactory = isFactory,
                name = SourceName(reg),
            };
            entry.plan = FindPlan(save, reg);
            _sources.Add(entry);
        }
        _sources.Sort((a, b) =>
        {
            int c = (b.plan != null).CompareTo(a.plan != null);
            if (c != 0) return c;
            return string.Compare(a.name, b.name, StringComparison.Ordinal);
        });
    }

    static LogisticsManagerPlan FindPlan(GameInstance save, BuildingRegistration reg)
    {
        try
        {
            if (save.logisticsManagerPlans == null || reg == null) return null;
            Address addr = null;
            try { addr = reg.Address; } catch { return null; }
            if (addr == null || addr.IsUndefined()) return null;
            foreach (var plan in save.logisticsManagerPlans)
            {
                if (plan == null || plan.targetAddress == null) continue;
                try
                {
                    if (plan.targetAddress.IsUndefined()) continue;
                    if (plan.targetAddress == addr) return plan;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    static string SourceName(BuildingRegistration reg)
    {
        if (reg == null) return "?";
        try
        {
            if (!string.IsNullOrEmpty(reg.BusinessName)) return reg.BusinessName;
        }
        catch { }
        try { return reg.StreetName + " " + reg.StreetNumber; }
        catch { return "?"; }
    }

    static string ResolveDestName(GameInstance save, Address addr)
    {
        if (addr == null) return "(unassigned)";
        try
        {
            if (addr.IsUndefined()) return "(unassigned)";
        }
        catch { return "(unassigned)"; }
        try
        {
            if (save.BuildingRegistrations != null)
            {
                foreach (var reg in save.BuildingRegistrations)
                {
                    if (reg == null) continue;
                    try
                    {
                        if (reg.Address == addr)
                            return SourceName(reg);
                    }
                    catch { }
                }
            }
        }
        catch { }
        try
        {
            var importExports = Helpers.BuildingHelper.GetImportExportBuildingRegistrations();
            if (importExports != null)
            {
                foreach (var reg in importExports)
                {
                    if (reg == null) continue;
                    try
                    {
                        if (reg.Address == addr)
                            return SourceName(reg) + " (export)";
                    }
                    catch { }
                }
            }
        }
        catch { }
        try { return addr.ToFormattedString(); }
        catch { return "?"; }
    }

    static int SafeStock(BuildingRegistration reg, string item)
    {
        try { return Helpers.BuildingHelper.CountTotalResourcesInStock(reg, item); }
        catch { return 0; }
    }

    static int SafeStock(Address addr, string item)
    {
        try
        {
            if (addr == null || addr.IsUndefined()) return 0;
            return Helpers.BuildingHelper.CountTotalResourcesInStock(addr, item);
        }
        catch { return 0; }
    }

    // ---------- render ----------

    static void BuildSourceList()
    {
        if (_sourceListRoot == null) return;
        foreach (Transform child in _sourceListRoot)
            UnityEngine.Object.Destroy(child.gameObject);
        Warmup(_sourceListRoot);
        if (_listCountText != null)
            _listCountText.text = "SOURCES (" + _sources.Count + ")";
        foreach (var entry in _sources)
        {
            var go = new GameObject("TransferSource");
            go.transform.SetParent(_sourceListRoot, false);
            var bg = go.AddComponent<Image>();
            bool sel = entry == _selected;
            bg.color = sel ? Accent : RowBgAlt;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            string kind = entry.isFactory ? "FACTORY" : "WAREHOUSE";
            string routes = entry.plan != null
                ? (entry.plan.destinations != null ? entry.plan.destinations.Count.ToString() : "0") + " routes"
                : "no plan";
            var label = Text("TransferSourceLabel",
                entry.name + "\n" + kind + "  |  " + routes, 26,
                TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle,
                sel ? Color.white : InkLight);
            label.transform.SetParent(go.transform, false);
            Anchor(label.gameObject, 0f, 0f, 1f, 1f, 16f, 8f, -16f, -8f);
            label.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            label.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            var size = go.AddComponent<LayoutElement>();
            size.minHeight = 110f;
            var captured = entry;
            btn.onClick.AddListener(() => SelectSource(captured));
        }
        if (_sources.Count == 0)
            AddInfoRow(_sourceListRoot, "No factories or warehouses found.");
    }

    static void RenderDetail()
    {
        if (_table == null) return;
        bool hasSource = _selected != null;
        bool hasPlan = hasSource && _selected.plan != null;
        bool editing = EditingEnabled();
        if (_editToggleBtnGO != null) _editToggleBtnGO.SetActive(true);
        PaintEditToggle(editing);
        if (!editing)
        {
            if (_refreshBtnGO != null) _refreshBtnGO.SetActive(false);
            if (_addDestBtnGO != null) _addDestBtnGO.SetActive(false);
            if (_addProdBtnGO != null) _addProdBtnGO.SetActive(false);
            if (_exportBtnGO != null) _exportBtnGO.SetActive(false);
            if (_createBtnGO != null) _createBtnGO.SetActive(false);
            if (_titleText != null)
                _titleText.text = hasSource
                    ? (_selected.name.ToUpperInvariant()
                        + (_selected.isFactory ? " (FACTORY)" : " (WAREHOUSE)"))
                    : "TRANSFER";
            if (_snapshotText != null) _snapshotText.text = "";
            _table.ShowInfo(EditingDisabledText(), 30);
            return;
        }
        if (_refreshBtnGO != null) _refreshBtnGO.SetActive(hasSource);
        if (_addDestBtnGO != null) _addDestBtnGO.SetActive(hasPlan);
        if (_addProdBtnGO != null) _addProdBtnGO.SetActive(hasPlan);
        if (_exportBtnGO != null)
            _exportBtnGO.SetActive(hasPlan && _selected.isFactory);
        if (_createBtnGO != null) _createBtnGO.SetActive(hasSource && !hasPlan);
        if (!hasSource)
        {
            if (_titleText != null) _titleText.text = "TRANSFER";
            if (_snapshotText != null)
                _snapshotText.text = "Buy or rent a factory or warehouse first.";
            _table.ShowInfo("No factories or warehouses found.", 32);
            return;
        }
        if (_titleText != null)
            _titleText.text = _selected.name.ToUpperInvariant()
                + (_selected.isFactory ? " (FACTORY)" : " (WAREHOUSE)");
        if (!hasPlan)
        {
            if (_snapshotText != null)
                _snapshotText.text = "No logistics plan for this source yet."
                    + " Create one below, then add destinations in the phone BizMan logistics screen (HQ)."
                    + " Amounts, runs and exports are managed here.";
            _table.ShowInfo("No logistics plan for this source.", 32);
            return;
        }
        var plan = _selected.plan;
        int routeCount = 0;
        int productCount = 0;
        try { routeCount = plan.destinations != null ? plan.destinations.Count : 0; } catch { }
        ComputeRows(plan, _selected.reg);
        foreach (var r in _rows) if (r != null) productCount++;
        long totalSend = 0;
        double totalExport = 0;
        foreach (var r in _rows)
        {
            if (r == null) continue;
            totalSend += r.toSend;
            if (r.exporter) totalExport += (double)r.toSend * r.exportPrice;
        }
        _table.SetTotals("TOTAL (" + productCount + " lines)", totalSend, totalExport);
        _table.SetRows(_rows);
        if (_snapshotText != null)
            _snapshotText.text = SnapshotLine(plan, routeCount, totalSend, totalExport);
        try
        {
            _context?.Logger.Info("The Big Board transfer plan [" + _selected.name + "]: "
                + routeCount + " routes, " + productCount + " lines, toSend="
                + totalSend.ToString("N0") + ".");
        }
        catch { }
    }

    static string SnapshotLine(LogisticsManagerPlan plan, int routes, long totalSend, double totalExport)
    {
        var save = SaveGameManager.Current;
        int day = save != null ? save.Day : 0;
        int hour = 0;
        try { hour = save != null ? save.Hour : 0; } catch { }
        bool assigned = false;
        int max = 0;
        try { assigned = !string.IsNullOrEmpty(plan.assignedEmployeeId); } catch { }
        try { max = plan.MaxDestinations; } catch { }
        string auto;
        if (assigned && max > 0)
            auto = routes <= max
                ? "AUTO ON (" + routes + "/" + max + ")"
                : "AUTO PARTIAL (first " + max + " of " + routes + " routes)";
        else
            auto = "AUTO PAUSED (needs manager + trucks in BizMan)";
        string line = "Day " + day + " - " + hour.ToString("00") + ":00  |  "
            + auto + "  |  NEXT RUN SENDS " + totalSend.ToString("N0");
        if (totalExport > 0)
            line += "  |  EXPORT ~" + totalExport.ToString("N0");
        line += "\nMIN STOCK tops the destination up (1000 target - 600 stock = 400 sent)."
            + " Click a row to edit, right-click for more (add/remove products & destinations).";
        return line;
    }

    static void ComputeRows(LogisticsManagerPlan plan, BuildingRegistration source)
    {
        _rows.Clear();
        if (plan == null || plan.destinations == null) return;
        var save = SaveGameManager.Current;
        foreach (var dest in plan.destinations)
        {
            if (dest == null) continue;
            Address addr = null;
            try { addr = dest.deliveryTargetAddress; } catch { continue; }
            bool exporter = false;
            try { exporter = dest.IsTargetExporter; } catch { }
            string destName = ResolveDestName(save, addr);
            if (exporter && !destName.EndsWith(" (export)"))
                destName += " (export)";
            if (dest.stockTargets == null) continue;
            foreach (var target in dest.stockTargets)
            {
                if (target == null || string.IsNullOrEmpty(target.itemName)) continue;
                int amount = 0;
                try { amount = target.targetAmount; } catch { continue; }
                if (amount <= 0) continue;
                int destStock = exporter ? 0 : SafeStock(addr, target.itemName);
                int srcStock = SafeStock(source, target.itemName);
                long need = exporter
                    ? Math.Min((long)amount, (long)Math.Max(0, srcStock))
                    : Math.Max(0, (long)amount - destStock);
                long toSend = Math.Min(need, (long)Math.Max(0, srcStock));
                float price = 0f;
                if (exporter)
                {
                    try { price = Helpers.ProductMarketHelper.GetProductExportPrice(target.itemName); }
                    catch { price = 0f; }
                }
                string display;
                try { display = ModPanel.GetProductDisplayName(target.itemName); }
                catch { display = target.itemName; }
                _rows.Add(new TransferTable.TransferRow
                {
                    destName = destName,
                    productName = display,
                    itemName = target.itemName,
                    destStock = destStock,
                    target = amount,
                    toSend = toSend,
                    srcStock = srcStock,
                    exporter = exporter,
                    exportPrice = price,
                    dest = dest,
                });
            }
        }
    }

    // ---------- actions ----------

    static void EditTarget(TransferTable.TransferRow row)
    {
        try
        {
            if (row == null || row.dest == null || string.IsNullOrEmpty(row.itemName)) return;
            if (_selected == null || _selected.plan == null) return;
            if (row.dest.stockTargets == null) return;
            string kind = row.exporter ? "SELL amount" : "MIN STOCK";
            EditAmount(row.dest, row.itemName, (int)Math.Min(row.target, MaxTarget),
                kind, row.destName, row.productName);
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board transfer edit failed: " + ex.Message);
        }
    }

    static void EditAmount(LogisticsManagerPlanDestination dest, string itemName,
        int current, string kind, string destName, string productName)
    {
        try
        {
            if (!EditingEnabled()) return;
            if (dest == null || string.IsNullOrEmpty(itemName)) return;
            if (_selected == null || _selected.plan == null) return;
            var uis = UI.UIs.Instance;
            if (uis == null || uis.textInputUi == null) return;
            string currentText = current.ToString();
            uis.textInputUi.Show("thebigboard_amount_hint", "common_confirm",
                (string s) =>
                {
                    int v;
                    if (!int.TryParse(s != null ? s.Trim() : "", out v)) return;
                    v = Mathf.Clamp(v, 0, MaxTarget);
                    ApplyTarget(dest, itemName, v, kind, destName, productName);
                },
                null, currentText, true);
            SelectInputText(uis.textInputUi);
            _context?.Logger.Info("The Big Board transfer edit opened: "
                + destName + " / " + productName + " (" + kind + ", now " + currentText + ").");
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board transfer edit failed: " + ex.Message);
        }
    }

    static void ApplyTarget(LogisticsManagerPlanDestination dest, string itemName,
        int value, string kind, string destName, string productName)
    {
        try
        {
            if (!EditingEnabled()) return;
            var plan = _selected != null ? _selected.plan : null;
            if (plan == null || dest == null || string.IsNullOrEmpty(itemName)) return;
            if (plan.destinations == null || !plan.destinations.Contains(dest)) return;
            if (dest.stockTargets == null)
                dest.stockTargets = new List<ItemAmountTarget>();
            ItemAmountTarget found = null;
            foreach (var t in dest.stockTargets)
            {
                if (t == null || t.itemName == null) continue;
                if (string.Equals(t.itemName, itemName, StringComparison.OrdinalIgnoreCase))
                { found = t; break; }
            }
            if (value <= 0)
            {
                if (found != null) dest.stockTargets.Remove(found);
            }
            else if (found != null)
            {
                found.targetAmount = value;
            }
            else
            {
                dest.stockTargets.Add(new ItemAmountTarget(itemName, value));
            }
            try { SaveGameManager.MarkChange(); } catch { }
            Refresh();
            _context?.Logger.Info("The Big Board transfer target set: "
                + destName + " / " + productName + " " + kind + " = "
                + value.ToString("N0") + ".");
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board transfer target save failed: " + ex.Message);
        }
    }

    static void SelectInputText(MonoBehaviour ui)
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

    /// <summary>Factory leftovers outlet, config-only: pins an import/export
    /// destination LAST in the plan and sets every finished good (factory
    /// recipe outputs - raw materials are never touched) to SELL ALL, so each
    /// logistics run tops the warehouses up first and sells the rest. SELL ALL
    /// withdraws whatever is there (the engine caps at source stock), so it
    /// never goes stale. Nothing runs now; the game executes on schedule.</summary>
    static void ExportExcess()
    {
        try
        {
            if (!EditingEnabled()) return;
            if (_selected == null || _selected.plan == null || !_selected.isFactory) return;
            var plan = _selected.plan;
            if (plan.destinations == null)
                plan.destinations = new List<LogisticsManagerPlanDestination>();
            LogisticsManagerPlanDestination exp = null;
            foreach (var dest in plan.destinations)
            {
                if (dest == null) continue;
                bool exporter = false;
                try { exporter = dest.IsTargetExporter; } catch { }
                if (exporter) { exp = dest; break; }
            }
            if (exp == null)
            {
                BuildingRegistration ie = null;
                try
                {
                    var list = Helpers.BuildingHelper.GetImportExportBuildingRegistrations();
                    if (list != null && list.Count > 0) ie = list[0];
                }
                catch { }
                Address ieAddr = null;
                try { ieAddr = ie != null ? ie.Address : null; } catch { }
                bool usable = false;
                try { usable = ieAddr != null && !ieAddr.IsUndefined(); } catch { }
                if (!usable)
                {
                    _context?.Logger.Warn("The Big Board export-excess: no import/export business in the city.");
                    _table.ShowInfo("No import/export business in the city.", 30);
                    return;
                }
                exp = new LogisticsManagerPlanDestination { deliveryTargetAddress = ieAddr };
                plan.destinations.Add(exp);
                _context?.Logger.Info("The Big Board export-excess: export destination added to "
                    + _selected.name + ".");
            }
            else
            {
                plan.destinations.Remove(exp);
                plan.destinations.Add(exp);
            }
            if (exp.stockTargets == null)
                exp.stockTargets = new List<ItemAmountTarget>();
            var outputs = FactoryOutputs(_selected.reg);
            if (outputs.Count == 0)
            {
                _context?.Logger.Warn("The Big Board export-excess [" + _selected.name
                    + "]: no finished goods found (no workstation recipes).");
                return;
            }
            int touched = 0;
            var names = new List<string>();
            foreach (var item in outputs)
            {
                ItemAmountTarget found = null;
                foreach (var t in exp.stockTargets)
                {
                    if (t == null || t.itemName == null) continue;
                    if (string.Equals(t.itemName, item, StringComparison.OrdinalIgnoreCase))
                    { found = t; break; }
                }
                if (found != null)
                {
                    if (found.targetAmount != MaxTarget)
                    {
                        found.targetAmount = MaxTarget;
                        touched++;
                    }
                }
                else
                {
                    exp.stockTargets.Add(new ItemAmountTarget(item, MaxTarget));
                    touched++;
                }
                try { names.Add(ModPanel.GetProductDisplayName(item)); }
                catch { names.Add(item); }
            }
            try { SaveGameManager.MarkChange(); } catch { }
            Refresh();
            _context?.Logger.Info("The Big Board EXPORT EXCESS [" + _selected.name + "]: "
                + touched + " finished goods set to SELL ALL (export runs last): "
                + string.Join(", ", names.ToArray()) + ". Raw materials untouched.");
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board export-excess failed: " + ex.Message);
        }
    }

    // ---------- overlays: row menu + modals ----------

    public static void CloseOverlays()
    {
        if (_rowMenu != null)
        {
            try { UnityEngine.Object.Destroy(_rowMenu); } catch { }
            _rowMenu = null;
        }
        if (_modal != null)
        {
            try { UnityEngine.Object.Destroy(_modal); } catch { }
            _modal = null;
        }
    }

    static void OpenRowMenu(TransferTable.TransferRow row, GameObject rowGO, Vector2 cursorPos)
    {
        if (!EditingEnabled()) return;
        if (row == null || rowGO == null || row.dest == null || _page == null) return;
        if (_selected == null || _selected.plan == null) return;
        CloseOverlays();
        var items = new List<KeyValuePair<string, Action>>();
        string kind = row.exporter ? "SELL amount" : "MIN STOCK";
        var captured = row;
        items.Add(new KeyValuePair<string, Action>("Edit " + kind,
            () => EditTarget(captured)));
        items.Add(new KeyValuePair<string, Action>("Add product to this destination",
            () => OpenProductPicker(captured.dest)));
        items.Add(new KeyValuePair<string, Action>("Remove product '" + captured.productName + "'",
            () => ApplyTarget(captured.dest, captured.itemName, 0, kind, captured.destName, captured.productName)));
        items.Add(new KeyValuePair<string, Action>(
            "Remove destination '" + captured.destName + "' (" + CountDestLines(captured.dest) + ")",
            () => ConfirmRemoveDestination(captured.dest, captured.destName)));
        _rowMenu = DataGrid.ShowContextMenu(_page.transform, rowGO,
            captured.productName, items, () => { _rowMenu = null; }, cursorPos);
        _context?.Logger.Info("The Big Board transfer menu: '" + captured.destName
            + " / " + captured.productName + "'.");
    }

    static int CountDestLines(LogisticsManagerPlanDestination dest)
    {
        int n = 0;
        try
        {
            if (dest == null || dest.stockTargets == null) return 0;
            foreach (var t in dest.stockTargets)
            {
                if (t == null) continue;
                int amt = 0;
                try { amt = t.targetAmount; } catch { }
                if (amt > 0) n++;
            }
        }
        catch { }
        return n;
    }

    static void ConfirmRemoveDestination(LogisticsManagerPlanDestination dest, string destName)
    {
        if (!EditingEnabled()) return;
        CloseOverlays();
        ShowConfirm("REMOVE DESTINATION?",
            "Remove '" + destName + "' with all its products from this plan?"
            + "\nThe game will stop serving it on the next run.",
            () => RemoveDestination(dest, destName));
    }

    static void RemoveDestination(LogisticsManagerPlanDestination dest, string destName)
    {
        try
        {
            if (!EditingEnabled()) return;
            var plan = _selected != null ? _selected.plan : null;
            if (plan == null || plan.destinations == null || dest == null) return;
            if (!plan.destinations.Contains(dest)) return;
            plan.destinations.Remove(dest);
            try { SaveGameManager.MarkChange(); } catch { }
            Refresh();
            _context?.Logger.Info("The Big Board transfer destination removed: '" + destName + "'.");
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board remove-destination failed: " + ex.Message);
        }
    }

    // ---------- modal framework: dim + card + (scroll list | body + buttons) ----------

    static Transform BuildModalCard(string title, out GameObject root, float height)
    {
        root = new GameObject("TransferModal");
        root.transform.SetParent(_page.transform, false);
        Stretch(root);
        var dimGO = new GameObject("TransferModalDim");
        dimGO.transform.SetParent(root.transform, false);
        Stretch(dimGO);
        var dimBg = dimGO.AddComponent<Image>();
        dimBg.color = new Color(0, 0, 0, 0.55f);
        var dimBtn = dimGO.AddComponent<Button>();
        dimBtn.targetGraphic = dimBg;
        dimBtn.onClick.AddListener(() => CloseOverlays());

        var cardGO = new GameObject("TransferModalCard");
        cardGO.transform.SetParent(root.transform, false);
        var cardRect = Rect(cardGO);
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(1000f, height);
        var cardBg = cardGO.AddComponent<Image>();
        cardBg.color = new Color(0.12f, 0.14f, 0.18f, 1f);
        var outline = cardGO.AddComponent<Outline>();
        outline.effectColor = new Color(0.32f, 0.36f, 0.42f, 1f);
        outline.effectDistance = new Vector2(2f, 2f);
        var layout = cardGO.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 20, 20);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var titleText = Text("TransferModalTitle", title, 34,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, Color.white);
        titleText.transform.SetParent(cardGO.transform, false);
        titleText.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        titleText.overflowMode = TMPro.TextOverflowModes.Ellipsis;

        _modal = root;
        root.transform.SetAsLastSibling();
        return cardGO.transform;
    }

    static GameObject ModalButton(Transform parent, string label, Color bg, Action onClick, int fontSize = 28)
    {
        var go = new GameObject("TransferModalBtn_" + label);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = bg;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var size = go.AddComponent<LayoutElement>();
        size.minHeight = 84f;
        var text = Text("TransferModalBtnLabel", label, fontSize,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, Color.white);
        text.transform.SetParent(go.transform, false);
        Stretch(text.gameObject);
        text.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        text.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        btn.onClick.AddListener(() => onClick());
        return go;
    }

    static void ShowListModal(string title, string emptyText, List<KeyValuePair<string, Action>> options)
    {
        if (_page == null) return;
        CloseOverlays();
        GameObject root;
        var card = BuildModalCard(title, out root, 1200f);
        if (options == null || options.Count == 0)
        {
            var t = Text("TransferModalEmpty", emptyText != null ? emptyText : "Nothing to pick.", 28,
                TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Top, InkLight);
            t.transform.SetParent(card, false);
            t.textWrappingMode = TMPro.TextWrappingModes.Normal;
        }
        else
        {
            var scrollGO = new GameObject("TransferModalScroll");
            scrollGO.transform.SetParent(card, false);
            var scrollSize = scrollGO.AddComponent<LayoutElement>();
            scrollSize.flexibleHeight = 1f;
            scrollSize.minHeight = 200f;
            var scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewportGO = new GameObject("TransferModalViewport");
            viewportGO.transform.SetParent(scrollGO.transform, false);
            Anchor(viewportGO, 0f, 0f, 1f, 1f, 0f, 0f, -56f, 0f);
            viewportGO.AddComponent<RectMask2D>();

            var sbGO = new GameObject("TransferModalScrollbar");
            sbGO.transform.SetParent(scrollGO.transform, false);
            Anchor(sbGO, 1f, 0f, 1f, 1f, -48f, 0f, -12f, 0f);
            var sbImg = sbGO.AddComponent<Image>();
            sbImg.color = ScrollTrack;
            var scrollbar = sbGO.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            var handleGO = new GameObject("Handle");
            handleGO.transform.SetParent(sbGO.transform, false);
            Stretch(handleGO);
            var handleImg = handleGO.AddComponent<Image>();
            handleImg.color = ScrollHandle;
            scrollbar.handleRect = handleGO.GetComponent<RectTransform>();
            scrollbar.targetGraphic = handleImg;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            var contentGO = new GameObject("TransferModalList");
            contentGO.transform.SetParent(viewportGO.transform, false);
            var contentRect = Rect(contentGO);
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;
            var fitter = contentGO.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var list = contentGO.AddComponent<VerticalLayoutGroup>();
            list.spacing = 8f;
            list.padding = new RectOffset(6, 6, 6, 6);
            list.childControlWidth = true;
            list.childControlHeight = true;
            list.childForceExpandWidth = true;
            list.childForceExpandHeight = false;
            scroll.viewport = viewportGO.GetComponent<RectTransform>();
            scroll.content = contentRect;

            foreach (var opt in options)
            {
                var action = opt.Value;
                var btnGO = new GameObject("TransferModalOption");
                btnGO.transform.SetParent(contentGO.transform, false);
                var bg = btnGO.AddComponent<Image>();
                bg.color = RowBgAlt;
                var btn = btnGO.AddComponent<Button>();
                btn.targetGraphic = bg;
                var size = btnGO.AddComponent<LayoutElement>();
                size.minHeight = 84f;
                var label = Text("TransferModalOptionLabel", opt.Key, 28,
                    TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, InkLight);
                label.transform.SetParent(btnGO.transform, false);
                Anchor(label.gameObject, 0f, 0f, 1f, 1f, 20f, 6f, -20f, -6f);
                label.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                label.overflowMode = TMPro.TextOverflowModes.Ellipsis;
                btn.onClick.AddListener(() => { CloseOverlays(); try { action(); } catch { } });
            }
        }
        ModalButton(card, "CLOSE", BtnIdle, () => CloseOverlays());
    }

    static void ShowConfirm(string title, string body, Action onYes)
    {
        if (_page == null) return;
        GameObject root;
        var card = BuildModalCard(title, out root, 520f);
        var bodyText = Text("TransferModalBody", body, 28,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Top, InkLight);
        bodyText.transform.SetParent(card, false);
        bodyText.textWrappingMode = TMPro.TextWrappingModes.Normal;
        var barGO = new GameObject("TransferModalConfirmBar");
        barGO.transform.SetParent(card, false);
        var barSize = barGO.AddComponent<LayoutElement>();
        barSize.minHeight = 84f;
        var bar = barGO.AddComponent<HorizontalLayoutGroup>();
        bar.spacing = 12f;
        bar.childControlWidth = true;
        bar.childControlHeight = true;
        bar.childForceExpandWidth = true;
        bar.childForceExpandHeight = true;
        ModalButton(barGO.transform, "YES, REMOVE", LossRed,
            () => { CloseOverlays(); try { if (onYes != null) onYes(); } catch { } });
        ModalButton(barGO.transform, "CANCEL", BtnIdle, () => CloseOverlays());
    }

    // ---------- destinations ----------

    static void OpenDestinationPicker()
    {
        try
        {
            if (!EditingEnabled()) return;
            if (_selected == null || _selected.plan == null) return;
            var save = SaveGameManager.Current;
            if (save == null) return;
            var plan = _selected.plan;
            var cands = DestinationCandidates(save, plan);
            var options = new List<KeyValuePair<string, Action>>();
            foreach (var reg in cands)
            {
                var captured = reg;
                string label = SourceName(reg);
                try
                {
                    if (string.Equals(reg.businessTypeName, ImportExportBusinessType,
                        StringComparison.OrdinalIgnoreCase))
                        label += " (export)";
                }
                catch { }
                options.Add(new KeyValuePair<string, Action>(label,
                    () => AddDestination(plan, captured)));
            }
            ShowListModal("ADD DESTINATION",
                "Every available building is already a destination.", options);
            _context?.Logger.Info("The Big Board transfer destination picker: "
                + cands.Count + " candidates.");
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board destination picker failed: " + ex.Message);
        }
    }

    /// <summary>Same building rules as the vanilla logistics screen:
    /// rented non-empty buildings carrying the route tag (warehouse routes
    /// vs factory routes), plus city import/export businesses for factory
    /// plans. Already-routed buildings are excluded.</summary>
    static List<BuildingRegistration> DestinationCandidates(GameInstance save, LogisticsManagerPlan plan)
    {
        var list = new List<BuildingRegistration>();
        try
        {
            if (save == null || save.BuildingRegistrations == null || plan == null) return list;
            var existing = new HashSet<Address>();
            if (plan.destinations != null)
            {
                foreach (var d in plan.destinations)
                {
                    if (d == null) continue;
                    try
                    {
                        if (d.deliveryTargetAddress != null)
                            existing.Add(d.deliveryTargetAddress);
                    }
                    catch { }
                }
            }
            try
            {
                if (plan.targetAddress != null)
                    existing.Add(plan.targetAddress);
            }
            catch { }
            foreach (var reg in save.BuildingRegistrations)
            {
                if (reg == null) continue;
                bool rented = false;
                try { rented = reg.RentedByPlayer; } catch { continue; }
                if (!rented) continue;
                try
                {
                    if (reg.businessTypeName == "ba:businesstype_empty") continue;
                }
                catch { continue; }
                bool excluded = false;
                try { excluded = reg.Address != null && existing.Contains(reg.Address); }
                catch { }
                if (excluded) continue;
                bool ok = false;
                try
                {
                    var data = BuildingTypeHelper.GetData(reg);
                    if (data != null)
                        ok = data.HasTag(plan.isFactory
                            ? BigAmbitions.Tags.TagRef.Buildingtypetag.isfactorydestination
                            : BigAmbitions.Tags.TagRef.Buildingtypetag.iswarehousedestination);
                }
                catch { }
                if (ok) list.Add(reg);
            }
            if (plan.isFactory)
            {
                try
                {
                    var ies = Helpers.BuildingHelper.GetImportExportBuildingRegistrations();
                    if (ies != null)
                    {
                        foreach (var reg in ies)
                        {
                            if (reg == null) continue;
                            bool excluded = false;
                            try { excluded = reg.Address != null && existing.Contains(reg.Address); }
                            catch { }
                            if (!excluded) list.Add(reg);
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
        list.Sort((a, b) => string.Compare(SourceName(a), SourceName(b), StringComparison.Ordinal));
        return list;
    }

    static void AddDestination(LogisticsManagerPlan plan, BuildingRegistration reg)
    {
        try
        {
            if (!EditingEnabled()) return;
            if (plan == null || reg == null) return;
            Address addr = null;
            try { addr = reg.Address; } catch { return; }
            if (addr == null || addr.IsUndefined()) return;
            if (plan.destinations == null)
                plan.destinations = new List<LogisticsManagerPlanDestination>();
            foreach (var d in plan.destinations)
            {
                if (d == null) continue;
                try
                {
                    if (d.deliveryTargetAddress == addr) return;
                }
                catch { }
            }
            plan.destinations.Add(new LogisticsManagerPlanDestination { deliveryTargetAddress = addr });
            try { SaveGameManager.MarkChange(); } catch { }
            Refresh();
            _context?.Logger.Info("The Big Board transfer destination added: '"
                + SourceName(reg) + "'. Add its products with ADD PRODUCT.");
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board add-destination failed: " + ex.Message);
        }
    }

    // ---------- products ----------

    static void OpenAddProductFlow()
    {
        try
        {
            if (!EditingEnabled()) return;
            if (_selected == null || _selected.plan == null) return;
            var plan = _selected.plan;
            if (plan.destinations == null || plan.destinations.Count == 0)
            {
                _context?.Logger.Warn("The Big Board add-product: add a destination first.");
                return;
            }
            var save = SaveGameManager.Current;
            var options = new List<KeyValuePair<string, Action>>();
            foreach (var dest in plan.destinations)
            {
                if (dest == null) continue;
                var captured = dest;
                Address addr = null;
                try { addr = dest.deliveryTargetAddress; } catch { continue; }
                string label = ResolveDestName(save, addr) + " (" + CountDestLines(dest) + ")";
                options.Add(new KeyValuePair<string, Action>(label,
                    () => OpenProductPicker(captured)));
            }
            ShowListModal("PICK DESTINATION", "No destinations yet.", options);
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board add-product flow failed: " + ex.Message);
        }
    }

    static void OpenProductPicker(LogisticsManagerPlanDestination dest)
    {
        try
        {
            if (!EditingEnabled()) return;
            if (_selected == null || _selected.plan == null || dest == null) return;
            var plan = _selected.plan;
            if (plan.destinations == null || !plan.destinations.Contains(dest)) return;
            var save = SaveGameManager.Current;
            Address addr = null;
            try { addr = dest.deliveryTargetAddress; } catch { return; }
            string destName = ResolveDestName(save, addr);
            bool exporter = false;
            try { exporter = dest.IsTargetExporter; } catch { }
            string kind = exporter ? "SELL amount" : "MIN STOCK";
            var known = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (dest.stockTargets != null)
            {
                foreach (var t in dest.stockTargets)
                {
                    if (t == null || t.itemName == null) continue;
                    int amt = 0;
                    try { amt = t.targetAmount; } catch { }
                    if (!known.ContainsKey(t.itemName)) known[t.itemName] = amt;
                }
            }
            var avail = AvailableProducts(plan, dest);
            var labels = new List<KeyValuePair<string, string>>();
            foreach (var item in avail)
            {
                if (string.IsNullOrEmpty(item)) continue;
                string display;
                try { display = ModPanel.GetProductDisplayName(item); }
                catch { display = item; }
                int amt = 0;
                bool has = known.TryGetValue(item, out amt);
                labels.Add(new KeyValuePair<string, string>(
                    display + (has ? " (now " + amt.ToString("N0") + ")" : ""), item));
            }
            labels.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal));
            var options = new List<KeyValuePair<string, Action>>();
            foreach (var kv in labels)
            {
                string item = kv.Value;
                string display = kv.Key;
                int cur = 0;
                known.TryGetValue(item, out cur);
                var capturedDest = dest;
                string capturedName = destName;
                options.Add(new KeyValuePair<string, Action>(display,
                    () => EditAmount(capturedDest, item, cur, kind, capturedName, display)));
            }
            ShowListModal("ADD PRODUCT - " + destName.ToUpperInvariant(),
                "No products available for this destination.", options);
            _context?.Logger.Info("The Big Board transfer product picker: '"
                + destName + "' (" + options.Count + " products).");
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board product picker failed: " + ex.Message);
        }
    }

    /// <summary>Same product rules as the vanilla logistics screen:
    /// warehouse targets come from the source warehouse's own products +
    /// assigned imports, exporter targets from the import/export catalogue,
    /// store targets from the business's primary products + shelf items.</summary>
    static List<string> AvailableProducts(LogisticsManagerPlan plan, LogisticsManagerPlanDestination dest)
    {
        var list = new List<string>();
        try
        {
            var save = SaveGameManager.Current;
            if (save == null || plan == null || dest == null) return list;
            Address addr = null;
            try { addr = dest.deliveryTargetAddress; } catch { return list; }
            var breg = FindRegByAddress(save, addr);
            if (breg == null) { AddExistingTargets(dest, list); return list; }
            string btype = null;
            try { btype = breg.GetBuildingType(); } catch { }
            if (btype == "ba:buildingtype_warehouse")
            {
                try
                {
                    if (plan.targetAddress != null)
                    {
                        var srcReg = FindRegByAddress(save, plan.targetAddress);
                        var wh = srcReg as Entities.Warehouse;
                        if (wh != null)
                        {
                            foreach (var p in wh.GetProducts())
                                if (!string.IsNullOrEmpty(p) && !list.Contains(p)) list.Add(p);
                        }
                        AddImportProducts(plan, list);
                    }
                }
                catch { }
                AddExistingTargets(dest, list);
            }
            else
            {
                bool isIE = false;
                try
                {
                    var bc = breg.BuildingCached;
                    var ss = bc != null ? bc.SpecialService : null;
                    isIE = ss != null && ss.settings is ImportExportSettings;
                }
                catch { }
                if (isIE)
                {
                    try
                    {
                        var bc = breg.BuildingCached;
                        var ies = (ImportExportSettings)bc.SpecialService.settings;
                        foreach (var item in ies.GetItemsAvailable(save, true))
                            if (!string.IsNullOrEmpty(item) && CanProductBeDelivered(item) && !list.Contains(item))
                                list.Add(item);
                    }
                    catch { }
                }
                else
                {
                    try
                    {
                        var bt = Helpers.BusinessTypeHelper.GetData(breg);
                        if (bt != null)
                        {
                            foreach (var p in bt.GetPrimaryProducts())
                                if (!string.IsNullOrEmpty(p) && CanProductBeDelivered(p) && !list.Contains(p))
                                    list.Add(p);
                            if (breg.itemInstances != null)
                            {
                                foreach (var inst in breg.itemInstances.Values)
                                {
                                    if (inst == null) continue;
                                    bool shelf = false;
                                    try { shelf = (inst.ItemCached.type & ItemType.ShowcaseShelf) != 0; }
                                    catch { continue; }
                                    if (!shelf) continue;
                                    string sn = null;
                                    try
                                    {
                                        var si = inst.GetStockInstance();
                                        if (si != null) sn = si.itemName;
                                    }
                                    catch { }
                                    if (!string.IsNullOrEmpty(sn) && !list.Contains(sn)) list.Add(sn);
                                }
                            }
                            bool needBags = false;
                            try { needBags = bt.HasTag(BigAmbitions.Tags.TagRef.Businesstag.customersneedpaperbags); }
                            catch { }
                            if (needBags && !list.Contains("ba:itemname_paperbag"))
                                list.Add("ba:itemname_paperbag");
                        }
                    }
                    catch { }
                    AddExistingTargets(dest, list);
                    var dedup = new List<string>();
                    var seen = new HashSet<string>();
                    foreach (var p in list)
                        if (!string.IsNullOrEmpty(p) && seen.Add(p)) dedup.Add(p);
                    list = dedup;
                }
            }
        }
        catch { }
        return list;
    }

    static void AddImportProducts(LogisticsManagerPlan plan, List<string> list)
    {
        try
        {
            var save = SaveGameManager.Current;
            if (save == null || save.importPartnerships == null || plan == null || list == null) return;
            foreach (var ps in save.importPartnerships)
            {
                if (ps == null || ps.products == null) continue;
                foreach (var product in ps.products)
                {
                    if (product == null || string.IsNullOrEmpty(product.itemName)) continue;
                    bool ours = false;
                    try { ours = product.assignedWarehouse == plan.targetAddress; } catch { }
                    int amt = 0;
                    try { amt = product.amount; } catch { }
                    if (ours && amt > 0 && !list.Contains(product.itemName))
                        list.Add(product.itemName);
                }
            }
        }
        catch { }
    }

    static void AddExistingTargets(LogisticsManagerPlanDestination dest, List<string> list)
    {
        if (dest == null || dest.stockTargets == null || list == null) return;
        foreach (var t in dest.stockTargets)
        {
            if (t == null || string.IsNullOrEmpty(t.itemName)) continue;
            int amt = 0;
            try { amt = t.targetAmount; } catch { }
            if (amt > 0 && !list.Contains(t.itemName)) list.Add(t.itemName);
        }
    }

    static bool CanProductBeDelivered(string itemName)
    {
        try { return (ItemsGetter.GetByName(itemName).type & ItemType.ServiceProduct) == 0; }
        catch { return false; }
    }

    /// <summary>Address lookup that never creates registrations (unlike
    /// BuildingHelper.GetBuildingRegistration for unknown addresses).</summary>
    static BuildingRegistration FindRegByAddress(GameInstance save, Address addr)
    {
        if (save == null || addr == null) return null;
        try
        {
            if (save.BuildingRegistrations != null)
            {
                foreach (var reg in save.BuildingRegistrations)
                {
                    if (reg == null) continue;
                    try { if (reg.Address == addr) return reg; } catch { }
                }
            }
        }
        catch { }
        try
        {
            var ies = Helpers.BuildingHelper.GetImportExportBuildingRegistrations();
            if (ies != null)
            {
                foreach (var reg in ies)
                {
                    if (reg == null) continue;
                    try { if (reg.Address == addr) return reg; } catch { }
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>Finished goods made by this factory (workstation recipe
    /// outputs). Raw materials are excluded, so EXPORT EXCESS can never
    /// flush production ingredients.</summary>
    static HashSet<string> FactoryOutputs(BuildingRegistration reg)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (reg == null || reg.itemInstances == null) return set;
            foreach (var inst in reg.itemInstances.Values)
            {
                var ws = inst as FactoryWorkstationInstance;
                if (ws == null) continue;
                bool hasType = false;
                try { hasType = !string.IsNullOrEmpty(ws.workstationType); } catch { continue; }
                if (!hasType) continue;
                Recipe recipe = null;
                try { recipe = ws.SelectedRecipe; } catch { continue; }
                if ((UnityEngine.Object)recipe == null) continue;
                string output = null;
                try { output = recipe.output.item; } catch { continue; }
                if (!string.IsNullOrEmpty(output)) set.Add(output);
            }
        }
        catch { }
        return set;
    }

    static void CreatePlan()
    {
        try
        {
            if (!EditingEnabled()) return;
            if (_selected == null || _selected.plan != null) return;
            var save = SaveGameManager.Current;
            if (save == null) return;
            Address addr = null;
            try { addr = _selected.reg.Address; } catch { return; }
            if (addr == null || addr.IsUndefined()) return;
            var plan = new LogisticsManagerPlan
            {
                isFactory = _selected.isFactory,
                targetAddress = addr,
            };
            if (save.logisticsManagerPlans == null)
                save.logisticsManagerPlans = new List<LogisticsManagerPlan>();
            save.logisticsManagerPlans.Add(plan);
            _selected.plan = plan;
            try { SaveGameManager.MarkChange(); } catch { }
            _context?.Logger.Info("The Big Board transfer plan created for " + _selected.name
                + " (assign a manager + trucks and add destinations in BizMan logistics).");
            BuildSourceList();
            RenderDetail();
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board create-plan failed: " + ex.Message);
        }
    }
}
