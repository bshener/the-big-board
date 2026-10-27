using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BAModAPI;
using BigAmbitions.Items;
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
/// DELIVER NOW runs the plan immediately (same code as the game's run,
/// destinations in order). EXPORT EXCESS (factory sources only) first
/// delivers every warehouse/store destination, then sells everything left
/// in the factory via an import/export destination - so production
/// leftovers never pile up. Destination add/remove stays in the vanilla
/// BizMan logistics screen (HQ); amounts, runs and exports live here.
/// </summary>
public static class TransferPanel
{
    const string FactoryBusinessType = "ba:businesstype_factory";
    const string ImportExportBusinessType = "ba:businesstype_importexport";
    const int MaxTarget = 9999999;

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
    static GameObject _deliverBtnGO;
    static TMPro.TMP_Text _deliverBtnLabel;
    static GameObject _exportBtnGO;
    static TMPro.TMP_Text _exportBtnLabel;
    static GameObject _createBtnGO;

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

        _refreshBtnGO = MakeBarButton(barGO.transform, "REFRESH", BtnIdle, () => Refresh());
        _deliverBtnGO = MakeBarButton(barGO.transform, "DELIVER NOW", Accent, () => DeliverNow());
        _deliverBtnLabel = _deliverBtnGO.GetComponentInChildren<TMPro.TMP_Text>(true);
        _exportBtnGO = MakeBarButton(barGO.transform, "EXPORT EXCESS", BtnIdle, () => ExportExcess());
        _exportBtnLabel = _exportBtnGO.GetComponentInChildren<TMPro.TMP_Text>(true);
        _createBtnGO = MakeBarButton(barGO.transform, "CREATE PLAN", Accent, () => CreatePlan());
        _refreshBtnGO.SetActive(false);
        _deliverBtnGO.SetActive(false);
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

    /// <summary>Distinct stocked product ids at the source (any cargo with
    /// amount &gt; 0), the same pool the game's export branch withdraws.</summary>
    static List<string> ReadSourceProducts(BuildingRegistration reg)
    {
        var list = new List<string>();
        var seen = new HashSet<string>();
        try
        {
            if (reg == null || reg.itemInstances == null) return list;
            foreach (var inst in reg.itemInstances.Values)
            {
                if (inst == null || inst.cargoInstances == null) continue;
                foreach (var c in inst.cargoInstances)
                {
                    if (c == null || string.IsNullOrEmpty(c.itemName)) continue;
                    int amt = 0;
                    try { amt = c.amount; } catch { continue; }
                    if (amt <= 0 || seen.Contains(c.itemName)) continue;
                    seen.Add(c.itemName);
                    list.Add(c.itemName);
                }
            }
        }
        catch { }
        list.Sort(StringComparer.Ordinal);
        return list;
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
        if (_refreshBtnGO != null) _refreshBtnGO.SetActive(hasSource);
        if (_deliverBtnGO != null) _deliverBtnGO.SetActive(hasPlan);
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
        if (_deliverBtnLabel != null)
            _deliverBtnLabel.text = "DELIVER NOW (" + totalSend.ToString("N0") + ")";
        if (_exportBtnLabel != null)
            _exportBtnLabel.text = "EXPORT EXCESS";
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
        string auto = (assigned && max > 0)
            ? "AUTO ON (" + routes + "/" + max + ")"
            : "AUTO PAUSED (needs manager + trucks in BizMan)";
        string line = "Day " + day + " - " + hour.ToString("00") + ":00  |  "
            + auto + "  |  TO SEND " + totalSend.ToString("N0");
        if (totalExport > 0)
            line += "  |  EXPORT ~" + totalExport.ToString("N0");
        line += "\nMIN STOCK tops the destination up (1000 target - 600 stock = 400 sent)."
            + " Click a row to edit it, 0 removes the product.";
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
            var uis = UI.UIs.Instance;
            if (uis == null || uis.textInputUi == null) return;
            string kind = row.exporter ? "SELL amount" : "MIN STOCK";
            string current = row.target.ToString();
            uis.textInputUi.Show("thebigboard_search_hint", "common_confirm",
                (string s) =>
                {
                    int v;
                    if (!int.TryParse(s != null ? s.Trim() : "", out v)) return;
                    v = Mathf.Clamp(v, 0, MaxTarget);
                    ApplyTarget(row, v, kind);
                },
                null, current, true);
            SelectInputText(uis.textInputUi);
            _context?.Logger.Info("The Big Board transfer edit opened: "
                + row.destName + " / " + row.productName + " (" + kind + ", now " + current + ").");
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board transfer edit failed: " + ex.Message);
        }
    }

    static void ApplyTarget(TransferTable.TransferRow row, int value, string kind)
    {
        try
        {
            var plan = _selected != null ? _selected.plan : null;
            if (plan == null || row.dest == null) return;
            if (plan.destinations == null || !plan.destinations.Contains(row.dest)) return;
            if (row.dest.stockTargets == null) return;
            ItemAmountTarget found = null;
            foreach (var t in row.dest.stockTargets)
            {
                if (t == null || t.itemName == null) continue;
                if (string.Equals(t.itemName, row.itemName, StringComparison.OrdinalIgnoreCase))
                { found = t; break; }
            }
            if (value <= 0)
            {
                if (found != null) row.dest.stockTargets.Remove(found);
            }
            else if (found != null)
            {
                found.targetAmount = value;
            }
            else
            {
                row.dest.stockTargets.Add(new ItemAmountTarget(row.itemName, value));
            }
            try { SaveGameManager.MarkChange(); } catch { }
            Refresh();
            _context?.Logger.Info("The Big Board transfer target set: "
                + row.destName + " / " + row.productName + " " + kind + " = "
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

    /// <summary>Runs the whole plan now, destinations in order - the same
    /// DeliverDestination the game's own run calls, then flushes alerts and
    /// export payments via OnFinishedDeliveries.</summary>
    static void DeliverNow()
    {
        try
        {
            if (_selected == null || _selected.plan == null) return;
            var plan = _selected.plan;
            if (plan.destinations == null || plan.destinations.Count == 0) return;
            int done = 0;
            int failed = 0;
            foreach (var dest in new List<LogisticsManagerPlanDestination>(plan.destinations))
            {
                if (dest == null) continue;
                try
                {
                    plan.DeliverDestination(dest);
                    done++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _context?.Logger.Warn("The Big Board transfer deliver failed for a destination: "
                        + ex.GetType().Name + ": " + ex.Message);
                }
            }
            try { LogisticsManagerPlan.OnFinishedDeliveries(); } catch { }
            try { SaveGameManager.MarkChange(); } catch { }
            _context?.Logger.Info("The Big Board DELIVER NOW [" + _selected.name + "]: "
                + done + " destinations run, " + failed + " failed.");
            Refresh();
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board deliver-now failed: " + ex.Message);
        }
    }

    /// <summary>Factory leftovers outlet: delivers every store/warehouse
    /// destination first, then sells whatever is left in the factory via an
    /// import/export destination (created when missing). Export targets are
    /// overwritten with the remaining stock snapshot, so nothing piles up.</summary>
    static void ExportExcess()
    {
        try
        {
            if (_selected == null || _selected.plan == null || !_selected.isFactory) return;
            var plan = _selected.plan;
            if (plan.destinations == null) return;
            int delivered = 0;
            foreach (var dest in new List<LogisticsManagerPlanDestination>(plan.destinations))
            {
                if (dest == null) continue;
                bool exporter = false;
                try { exporter = dest.IsTargetExporter; } catch { }
                if (exporter) continue;
                try
                {
                    plan.DeliverDestination(dest);
                    delivered++;
                }
                catch (Exception ex)
                {
                    _context?.Logger.Warn("The Big Board export-excess deliver failed: "
                        + ex.GetType().Name + ": " + ex.Message);
                }
            }
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
                int max = 0;
                try { max = plan.MaxDestinations; } catch { }
                exp = new LogisticsManagerPlanDestination { deliveryTargetAddress = ieAddr };
                plan.destinations.Add(exp);
                _context?.Logger.Info("The Big Board export-excess: export destination added"
                    + (max > 0 && plan.destinations.Count > max
                        ? " (over truck capacity " + max + ": manual runs still work, auto runs only serve the first " + max + ")"
                        : "") + ".");
            }
            if (exp.stockTargets == null)
                exp.stockTargets = new List<ItemAmountTarget>();
            var products = ReadSourceProducts(_selected.reg);
            long totalUnits = 0;
            double totalValue = 0;
            var lines = new List<string>();
            foreach (var item in products)
            {
                int remaining = SafeStock(_selected.reg, item);
                ItemAmountTarget found = null;
                foreach (var t in exp.stockTargets)
                {
                    if (t == null || t.itemName == null) continue;
                    if (string.Equals(t.itemName, item, StringComparison.OrdinalIgnoreCase))
                    { found = t; break; }
                }
                if (remaining <= 0)
                {
                    if (found != null) exp.stockTargets.Remove(found);
                    continue;
                }
                if (found != null) found.targetAmount = remaining;
                else exp.stockTargets.Add(new ItemAmountTarget(item, remaining));
                float price = 0f;
                try { price = Helpers.ProductMarketHelper.GetProductExportPrice(item); }
                catch { }
                totalUnits += remaining;
                totalValue += (double)remaining * price;
                string display;
                try { display = ModPanel.GetProductDisplayName(item); }
                catch { display = item; }
                lines.Add(display + " x" + remaining.ToString("N0"));
            }
            // Drop export targets for products that are gone entirely.
            for (int i = exp.stockTargets.Count - 1; i >= 0; i--)
            {
                var t = exp.stockTargets[i];
                if (t == null || string.IsNullOrEmpty(t.itemName)) continue;
                if (SafeStock(_selected.reg, t.itemName) <= 0)
                    exp.stockTargets.RemoveAt(i);
            }
            if (totalUnits > 0)
            {
                try
                {
                    plan.DeliverDestination(exp);
                }
                catch (Exception ex)
                {
                    _context?.Logger.Warn("The Big Board export-excess sale failed: "
                        + ex.GetType().Name + ": " + ex.Message);
                }
            }
            try { LogisticsManagerPlan.OnFinishedDeliveries(); } catch { }
            try { SaveGameManager.MarkChange(); } catch { }
            _context?.Logger.Info("The Big Board EXPORT EXCESS [" + _selected.name + "]: "
                + delivered + " warehouse/store runs, then " + totalUnits.ToString("N0")
                + " units (~" + totalValue.ToString("N0") + ") exported"
                + (lines.Count > 0 ? ": " + string.Join(", ", lines.ToArray()) : "."));
            Refresh();
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board export-excess failed: " + ex.Message);
        }
    }

    static void CreatePlan()
    {
        try
        {
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
