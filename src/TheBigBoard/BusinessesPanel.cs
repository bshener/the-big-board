using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using BAModAPI;
using BigAmbitions.DayNightCycle;
using BigAmbitions.Items;
using Buildings;
using Entities;
using Extensions;
using Streets;
using static TheBigBoard.DataGrid;

namespace TheBigBoard;

/// <summary>
/// TAB 6 - BUSINESSES. Left: the same business list as the vanilla BizMan
/// app (same rows, same columns: NAME | TYPE | AVG DAILY INCOME | ALERTS |
/// SATISFACTION | STATUS). Right: the selected business's requirement
/// checklist -
/// 1. open / closed right now (schedule + temporarily-closed flag),
/// 2. every planned open hour covered by an assigned employee,
/// 3. marketing campaigns active or not,
/// 4. low/empty stock items (same 25% rule the game uses for its alerts),
/// 5. uniforms assigned for all required skills.
/// </summary>
public static class BusinessesPanel
{
    static ModContext _context;
    static GameObject _page;
    static bool _tabVisible;
    static int _lastDay = -1;
    static int _lastHour = -1;

    static BusinessesTable _table;
    static readonly List<BusinessesTable.BRow> _rows = new List<BusinessesTable.BRow>();

    static Address _selected;
    static bool _hasSelection;

    static GameObject _hireBtnGO;
    static TMPro.TMP_Text _hireBtnLabel;
    static BuildingRegistration _hireReg;
    static readonly List<EmployeeInstance> _hireCandidates = new List<EmployeeInstance>();
    static GameObject _copyBtnGO;
    static TMPro.TMP_Text _copyBtnLabel;
    // Copy-shifts popup: paste another same-type shop's whole weekly plan.
    static GameObject _copyPopup;
    static TMPro.TMP_Text _copyPopupTitle;
    static ShopPickTable _copyTable;
    static string _copyResult;
    static Address _copyResultAddr;
    static bool _hasCopyResult;
    // Dead-hours popup: hourly customer breakdown of the selected shop.
    static GameObject _deadBtnGO;
    static TMPro.TMP_Text _deadBtnLabel;
    static GameObject _deadPopup;
    static TMPro.TMP_Text _deadPopupTitle;
    static TMPro.TMP_Text _deadSummary;
    static DeadHoursTable _deadTable;

    // Detail font size: readability control (+/- in the card header).
    // A global preference (all saves), persisted like the column widths.
    // FROZEN for save compat: renaming this key orphans existing PlayerPrefs.
    const string FontKey = "MyFirstMod.bizDetailFont.v1";
    const float FontMin = 0.8f;
    const float FontMax = 1.5f;
    const float FontStep = 0.1f;
    static float _fontScale = -1f;
    const int BaseTitle = 30;
    const int BaseSub = 22;
    const int BaseSec = 24;
    const int BaseBody = 22;
    const int BaseHire = 26;
    const float BaseHireH = 76f;

    static TMPro.TMP_Text _countsText;
    static TMPro.TMP_Text _detailTitle;
    static TMPro.TMP_Text _detailSub;
    static readonly TMPro.TMP_Text[] _secTitles = new TMPro.TMP_Text[5];
    static readonly TMPro.TMP_Text[] _secBodies = new TMPro.TMP_Text[5];

    static readonly string[] SecNames =
        { "1. OPEN / CLOSED", "2. STAFFING", "3. MARKETING", "4. LOW STOCK", "5. UNIFORMS" };

    public static void BuildPage(GameObject page, ModContext context)
    {
        _page = page;
        _context = context;
        _tabVisible = false;
        _hasSelection = false;
        _rows.Clear();

        var leftGO = new GameObject("BizLeft");
        leftGO.transform.SetParent(page.transform, false);
        Anchor(leftGO, 0f, 0f, 0.60f, 1f, 0f, 0f, -24f, 0f);

        var label = Text("BizLabel", "BUSINESSES (same list as BizMan)", 26,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, InkGrey);
        label.transform.SetParent(leftGO.transform, false);
        Anchor(label.gameObject, 0f, 1f, 1f, 1f, 0f, -50f, 0f, 0f);

        _countsText = Text("BizCounts", "", 26,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Top, InkGrey);
        _countsText.transform.SetParent(leftGO.transform, false);
        Anchor(_countsText.gameObject, 0f, 1f, 1f, 1f, 0f, -190f, 0f, -56f);
        _countsText.textWrappingMode = TMPro.TextWrappingModes.Normal;

        _table = new BusinessesTable();
        _table.Init(context);
        _table.OnSelect = SelectBusiness;
        _table.BuildHeader(leftGO, "BizGridHeader", 0f, 0f, -246f, -190f);
        _table.BuildRows(leftGO,
            "BizRowsScroll", "BizViewport", "BizRows", "BizScrollbar", -254f);

        var refreshGO = new GameObject("BizRefresh");
        refreshGO.transform.SetParent(leftGO.transform, false);
        Anchor(refreshGO, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 78f);
        var refreshBg = refreshGO.AddComponent<Image>();
        refreshBg.color = BtnIdle;
        var refreshBtn = refreshGO.AddComponent<Button>();
        refreshBtn.targetGraphic = refreshBg;
        var refreshLabel = Text("BizRefreshLabel", "REFRESH", 30,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, InkLight);
        refreshLabel.transform.SetParent(refreshGO.transform, false);
        Stretch(refreshLabel.gameObject);
        refreshBtn.onClick.AddListener(() => Refresh());

        // Right: requirement checklist of the selected business. Dark card
        // so the text stays readable over the blurred backdrop, and its own
        // scroll: the checklist is taller than the panel, and every entry
        // sizes itself from its wrapped text (no fixed heights, so nothing
        // can overlap).
        var rightGO = new GameObject("BizRight");
        rightGO.transform.SetParent(page.transform, false);
        Anchor(rightGO, 0.60f, 0f, 1f, 1f, 24f, 78f, 0f, 0f);
        var cardBg = rightGO.AddComponent<Image>();
        cardBg.color = new Color(0.12f, 0.14f, 0.18f, 1f);
        var cardOutline = rightGO.AddComponent<Outline>();
        cardOutline.effectColor = new Color(0.32f, 0.36f, 0.42f, 1f);
        cardOutline.effectDistance = new Vector2(2f, 2f);

        // Right card is a vertical stack: scroll (flexible) + fixed action
        // bar (auto-sized). The header floats on top, ignored by layout.
        var stack = rightGO.AddComponent<VerticalLayoutGroup>();
        stack.padding = new RectOffset(18, 18, 80, 18);
        stack.spacing = 10f;
        stack.childControlWidth = true;
        stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;
        // Slim header strip: label on the left, font -/+ on the right.
        var headGO = new GameObject("BizDetailHead");
        headGO.transform.SetParent(rightGO.transform, false);
        Anchor(headGO, 0f, 1f, 1f, 1f, 18f, -72f, -18f, -8f);
        var headIgnore = headGO.AddComponent<LayoutElement>();
        headIgnore.ignoreLayout = true;
        var headLabel = Text("BizDetailHeadLabel", "REQUIREMENTS", 24,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, InkGrey);
        headLabel.transform.SetParent(headGO.transform, false);
        Anchor(headLabel.gameObject, 0f, 0f, 1f, 1f, 6f, 0f, -140f, 0f);
        AddFontButton(headGO, "BizFontMinus", "-", -128f, () => NudgeFont(-FontStep));
        AddFontButton(headGO, "BizFontPlus", "+", -64f, () => NudgeFont(FontStep));

        var scrollRoot = BuildDetailScroll(rightGO);

        _detailTitle = Text("BizDetailTitle", "Select a business", 30,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Top, InkLight);
        _detailTitle.transform.SetParent(scrollRoot, false);
        _detailTitle.textWrappingMode = TMPro.TextWrappingModes.Normal;

        _detailSub = Text("BizDetailSub", "", 22,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Top, InkGrey);
        _detailSub.transform.SetParent(scrollRoot, false);
        _detailSub.textWrappingMode = TMPro.TextWrappingModes.Normal;

        for (int i = 0; i < 5; i++)
        {
            var t = Text("BizSec" + i + "Title", SecNames[i], 24,
                TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Top, Accent);
            t.transform.SetParent(scrollRoot, false);
            t.textWrappingMode = TMPro.TextWrappingModes.Normal;
            _secTitles[i] = t;

            var b = Text("BizSec" + i + "Body", "-", 22,
                TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Top, InkLight);
            b.transform.SetParent(scrollRoot, false);
            b.textWrappingMode = TMPro.TextWrappingModes.Normal;
            _secBodies[i] = b;
        }

        // Fixed action bar at the bottom: buttons stay visible while the
        // sections above scroll. Auto-sized: hidden buttons take no space.
        var barGO = new GameObject("BizActionBar");
        barGO.transform.SetParent(rightGO.transform, false);
        var barFitter = barGO.AddComponent<ContentSizeFitter>();
        barFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        barFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var barLayout = barGO.AddComponent<VerticalLayoutGroup>();
        barLayout.spacing = 8f;
        barLayout.childControlWidth = true;
        barLayout.childControlHeight = true;
        barLayout.childForceExpandWidth = true;
        barLayout.childForceExpandHeight = false;
        // Hire button: appears when candidates found via an agency order
        // or a headhunter plan are waiting to be assigned here. Same game
        // recipe as the MyEmployees "assign + hire" mass action.
        _hireBtnGO = new GameObject("BizHireButton");
        _hireBtnGO.transform.SetParent(barGO.transform, false);
        var hireBg = _hireBtnGO.AddComponent<Image>();
        hireBg.color = BtnIdle;
        var hireSize = _hireBtnGO.AddComponent<LayoutElement>();
        hireSize.minHeight = 76f;
        var hireBtn = _hireBtnGO.AddComponent<Button>();
        hireBtn.targetGraphic = hireBg;
        _hireBtnLabel = Text("BizHireLabel", "HIRE & ASSIGN", 26,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, Color.white);
        _hireBtnLabel.transform.SetParent(_hireBtnGO.transform, false);
        Stretch(_hireBtnLabel.gameObject);
        hireBtn.onClick.AddListener(() => HireAndAssign());
        _hireBtnGO.SetActive(false);
        // Copy: paste a same-type shop's whole shift system here.
        _copyBtnGO = new GameObject("BizCopyButton");
        _copyBtnGO.transform.SetParent(barGO.transform, false);
        var copyBg = _copyBtnGO.AddComponent<Image>();
        copyBg.color = BtnIdle;
        var copySize = _copyBtnGO.AddComponent<LayoutElement>();
        copySize.minHeight = BaseHireH;
        var copyBtn = _copyBtnGO.AddComponent<Button>();
        copyBtn.targetGraphic = copyBg;
        _copyBtnLabel = Text("BizCopyLabel", "HIRE & COPY SHIFTS FROM...", BaseHire,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, Color.white);
        _copyBtnLabel.transform.SetParent(_copyBtnGO.transform, false);
        Stretch(_copyBtnLabel.gameObject);
        copyBtn.onClick.AddListener(() => OpenCopyPopup());
        // Dead hours: hourly customer breakdown of the selected shop,
        // so dead stretches can be spotted and closed in the schedule.
        _deadBtnGO = new GameObject("BizDeadButton");
        _deadBtnGO.transform.SetParent(barGO.transform, false);
        var deadBg = _deadBtnGO.AddComponent<Image>();
        deadBg.color = BtnIdle;
        var deadSize = _deadBtnGO.AddComponent<LayoutElement>();
        deadSize.minHeight = BaseHireH;
        var deadBtn = _deadBtnGO.AddComponent<Button>();
        deadBtn.targetGraphic = deadBg;
        _deadBtnLabel = Text("BizDeadLabel", "DEAD HOURS", BaseHire,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, Color.white);
        _deadBtnLabel.transform.SetParent(_deadBtnGO.transform, false);
        Stretch(_deadBtnLabel.gameObject);
        deadBtn.onClick.AddListener(() => OpenDeadPopup());
        ApplyFontScale();
        BuildCopyPopup();
        BuildDeadPopup();
    }

    static void AddFontButton(GameObject parent, string goName, string glyph,
        float rightOff, Action onClick)
    {
        var go = new GameObject(goName);
        go.transform.SetParent(parent.transform, false);
        Anchor(go, 1f, 0f, 1f, 1f, rightOff, 4f, rightOff + 56f, -4f);
        var bg = go.AddComponent<Image>();
        bg.color = BtnIdle;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = bg;
        var label = Text(goName + "Label", glyph, 28,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, InkLight);
        label.transform.SetParent(go.transform, false);
        Stretch(label.gameObject);
        btn.onClick.AddListener(() => onClick());
    }

    static float FontScale()
    {
        if (_fontScale < 0)
        {
            try { _fontScale = UnityEngine.PlayerPrefs.GetFloat(FontKey, 1f); }
            catch { _fontScale = 1f; }
            if (_fontScale < FontMin || _fontScale > FontMax) _fontScale = 1f;
        }
        return _fontScale;
    }

    static void NudgeFont(float delta)
    {
        float v = FontScale() + delta;
        v = Mathf.Clamp((float)Math.Round(v, 1), FontMin, FontMax);
        _fontScale = v;
        try
        {
            UnityEngine.PlayerPrefs.SetFloat(FontKey, v);
            UnityEngine.PlayerPrefs.Save();
        }
        catch { }
        ApplyFontScale();
        _context?.Logger.Info("The Big Board businesses detail font: " + v.ToString("F1") + "x");
    }

    /// <summary>Resizes every text in the detail card. Layout is driven by
    /// TMP preferred heights, so nothing overlaps at any scale.</summary>
    static void ApplyFontScale()
    {
        float s = FontScale();
        if (_detailTitle != null) _detailTitle.fontSize = BaseTitle * s;
        if (_detailSub != null) _detailSub.fontSize = BaseSub * s;
        for (int i = 0; i < 5; i++)
        {
            if (_secTitles[i] != null) _secTitles[i].fontSize = BaseSec * s;
            if (_secBodies[i] != null) _secBodies[i].fontSize = BaseBody * s;
        }
        if (_hireBtnLabel != null) _hireBtnLabel.fontSize = BaseHire * s;
        if (_hireBtnGO != null)
        {
            var size = _hireBtnGO.GetComponent<LayoutElement>();
            if (size != null) size.minHeight = BaseHireH * s;
        }
        if (_copyBtnLabel != null) _copyBtnLabel.fontSize = BaseHire * s;
        if (_copyBtnGO != null)
        {
            var csize = _copyBtnGO.GetComponent<LayoutElement>();
            if (csize != null) csize.minHeight = BaseHireH * s;
        }
        if (_deadBtnLabel != null) _deadBtnLabel.fontSize = BaseHire * s;
        if (_deadBtnGO != null)
        {
            var dsize = _deadBtnGO.GetComponent<LayoutElement>();
            if (dsize != null) dsize.minHeight = BaseHireH * s;
        }
    }

    /// <summary>Vertical scroll whose entries size from wrapped text.
    /// Unlike DataGrid.AddRowsScroll (fixed-height table rows), this sets
    /// childControlHeight so TMP preferred heights drive the layout and
    /// entries can never pile on top of each other.</summary>
    static Transform BuildDetailScroll(GameObject parent)
    {
        var scrollGO = new GameObject("BizDetailScroll");
        scrollGO.transform.SetParent(parent.transform, false);
        // Layout-driven: fills whatever the action bar leaves over.
        var scrollSize = scrollGO.AddComponent<LayoutElement>();
        scrollSize.flexibleHeight = 1f;
        var scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        var viewportGO = new GameObject("BizDetailViewport");
        viewportGO.transform.SetParent(scrollGO.transform, false);
        Anchor(viewportGO, 0f, 0f, 1f, 1f, 0f, 0f, -56f, 0f);
        viewportGO.AddComponent<RectMask2D>();
        // Visible scrollbar (same look as the tables): the wheel worked
        // before, but nothing showed how far the list scrolls.
        var sbGO = new GameObject("BizDetailScrollbar");
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

        var contentGO = new GameObject("BizDetail");
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
        list.spacing = 10f;
        list.padding = new RectOffset(6, 6, 6, 6);
        list.childControlWidth = true;
        list.childControlHeight = true;
        list.childForceExpandWidth = true;
        list.childForceExpandHeight = false;
        scroll.viewport = viewportGO.GetComponent<RectTransform>();
        scroll.content = contentRect;
        return contentGO.transform;
    }

    public static void SetVisible(bool visible)
    {
        _tabVisible = visible;
    }

    public static bool IsSelected(BusinessesTable.BRow r)
    {
        try
        {
            return _hasSelection && r != null && r.reg != null && r.reg.Address == _selected;
        }
        catch { return false; }
    }

    static void SelectBusiness(BusinessesTable.BRow r)
    {
        try
        {
            if (r == null || r.reg == null) return;
            _selected = r.reg.Address;
            _hasSelection = true;
            _hasCopyResult = false;
            _copyResult = null;
            _table.SetRows(_rows); // repaint selection highlight
            RenderDetail(r.reg);
            _context?.Logger.Info("The Big Board businesses selected: " + r.name);
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board businesses select failed: " + ex.Message);
        }
    }

    /// <summary>Day or hour change recomputes (open status is hourly). After
    /// an auto-fill run, refreshes once the solver had time to finish.</summary>
    public static void Tick()
    {
        try
        {
            var save = SaveGameManager.Current;
            if (save == null) return;
            if (!_tabVisible || _page == null) return;
            if (save.Day != _lastDay || save.Hour != _lastHour)
            {
                _lastDay = save.Day;
                _lastHour = save.Hour;
                Refresh();
                return;
            }
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board businesses tick failed: " + ex.Message);
        }
    }

    public static void Refresh()
    {
        try
        {
            var save = SaveGameManager.Current;
            if (save != null)
            {
                _lastDay = save.Day;
                try { _lastHour = save.Hour; } catch { }
            }
            ComputeRows();
            // Keep the selection if the business is still listed.
            BuildingRegistration sel = null;
            if (_hasSelection)
            {
                foreach (var r in _rows)
                {
                    try
                    {
                        if (r.reg != null && r.reg.Address == _selected) { sel = r.reg; break; }
                    }
                    catch { }
                }
                if (sel == null) _hasSelection = false;
            }
            if (sel == null && _rows.Count > 0)
            {
                sel = _rows[0].reg;
                if (sel != null)
                {
                    try { _selected = sel.Address; _hasSelection = true; }
                    catch { _hasSelection = false; sel = null; }
                }
            }
            _table.SetRows(_rows);
            int open = 0;
            foreach (var r in _rows) if (r.statusRank == 0) open++;
            if (_countsText != null)
                _countsText.text = _rows.Count + " businesses (" + open + " open now).\n"
                    + "Click a row to see its requirements.";
            if (sel != null) RenderDetail(sel);
            else RenderEmpty();
            _context?.Logger.Info("The Big Board businesses refreshed: " + _rows.Count + " rows.");
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board businesses refresh failed: " + ex.Message);
        }
    }

    // ---------- data ----------

    static void ComputeRows()
    {
        _rows.Clear();
        try
        {
            var save = SaveGameManager.Current;
            if (save == null || save.BuildingRegistrations == null) return;
            foreach (var reg in save.BuildingRegistrations)
            {
                if (reg == null) continue;
                bool rented = false;
                try { rented = reg.RentedByPlayer; }
                catch { continue; }
                if (rented && IsVisibleBusiness(reg))
                {
                    _rows.Add(MakeRow(reg, false));
                }
                else
                {
                    bool owned = false;
                    try { owned = reg.BuildingOwnedByPlayer && !reg.BuildingCached.IsHamptonsHouse(); }
                    catch { owned = false; }
                    if (owned) _rows.Add(MakeRow(reg, true));
                }
            }
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board businesses compute failed: " + ex.Message);
        }
    }

    /// <summary>Same visibility rule as the vanilla BizMan list.</summary>
    static bool IsVisibleBusiness(BuildingRegistration reg)
    {
        try
        {
            var bt = Helpers.BusinessTypeHelper.GetData(reg);
            if (bt != null && bt.HasTag(BigAmbitions.Tags.TagRef.Businesstag.generatesrevenue))
                return true;
            string btype = null;
            try { btype = reg.businessTypeName; } catch { }
            if (btype == "ba:businesstype_empty")
            {
                var btd = BuildingTypeHelper.GetData(reg);
                if (btd != null && btd.HasTag(BigAmbitions.Tags.TagRef.Buildingtypetag.showinbusinesslist))
                    return true;
            }
            return false;
        }
        catch
        {
            try { return !string.IsNullOrEmpty(reg.BusinessName); }
            catch { return false; }
        }
    }

    static BusinessesTable.BRow MakeRow(BuildingRegistration reg, bool isRealEstate)
    {
        var row = new BusinessesTable.BRow { reg = reg, isRealEstate = isRealEstate };
        string name = null;
        try { name = reg.BusinessName; } catch { }
        row.hasBusiness = !string.IsNullOrEmpty(name);
        if (row.hasBusiness) row.name = name;
        else
        {
            try
            {
                var addr = reg.Address;
                row.name = (addr != null && !addr.IsUndefined())
                    ? addr.ToFormattedString() : (reg.StreetName + " " + reg.StreetNumber);
            }
            catch { row.name = (reg.StreetName + " " + reg.StreetNumber); }
        }
        try { row.typeName = LocalizeOrClean(reg.businessTypeName, "ba:businesstype_"); }
        catch { row.typeName = "-"; }
        if (isRealEstate) row.typeName = "Real estate";
        try { row.income = isRealEstate ? reg.RealEstate.DailyIncome : reg.GetAvgDailyIncome(7); }
        catch { row.income = 0f; }
        try { row.alerts = reg.Alerts; }
        catch { row.alerts = 0; }
        row.alertsText = (!row.hasBusiness && !isRealEstate) ? "-" : row.alerts.ToString();
        int sat = -1;
        try { if (reg.satisfaction != null) sat = reg.satisfaction.overall; } catch { }
        row.satisfaction = sat;
        row.satText = sat < 0 ? "-" : (sat + "%");
        if (isRealEstate)
        {
            float occ = 0f;
            try { occ = reg.RealEstate.OccupancyPercentage; } catch { }
            row.occupancy = occ;
            row.statusText = occ.ToString("F0") + "% rented";
            row.statusRank = occ >= 70 ? 0 : (occ >= 30 ? 1 : 3);
        }
        else if (!row.hasBusiness)
        {
            row.statusText = "-";
            row.statusRank = 4;
        }
        else
        {
            bool open = false;
            try { open = Helpers.BusinessHelper.IsBusinessOpen(reg); } catch { }
            bool paused = false;
            try { paused = reg.temporarilyClosed; } catch { }
            row.statusText = paused ? "CLOSED (paused)" : (open ? "OPEN" : "CLOSED");
            row.statusRank = open ? 0 : (paused ? 3 : 2);
        }
        return row;
    }

    // ---------- detail ----------

    static void RenderEmpty()
    {
        if (_detailTitle != null) _detailTitle.text = "Select a business";
        if (_detailSub != null) _detailSub.text = "";
        for (int i = 0; i < 5; i++) SetSection(i, Accent, "-");
        _hireCandidates.Clear();
        UpdateHireButton(null);
    }

    static void RenderDetail(BuildingRegistration reg)
    {
        string name = "?";
        try { name = !string.IsNullOrEmpty(reg.BusinessName) ? reg.BusinessName : reg.Address.ToFormattedString(); }
        catch { }
        if (_detailTitle != null) _detailTitle.text = name;
        string type = "";
        try { type = LocalizeOrClean(reg.businessTypeName, "ba:businesstype_"); } catch { }
        if (_detailSub != null)
        {
            string addr = "";
            try { addr = reg.Address.ToFormattedString(); } catch { }
            _detailSub.text = type + "  |  " + addr;
        }
        RenderOpenSection(reg);
        RenderStaffingSection(reg);
        RenderMarketingSection(reg);
        RenderStockSection(reg);
        RenderUniformSection(reg);
    }

    static void SetSection(int i, Color titleColor, string body)
    {
        if (_secTitles[i] != null) _secTitles[i].color = titleColor;
        if (_secBodies[i] != null) _secBodies[i].text = body;
    }

    static void RenderOpenSection(BuildingRegistration reg)
    {
        bool hasBiz = false;
        try { hasBiz = !string.IsNullOrEmpty(reg.BusinessName); } catch { }
        if (!hasBiz) { SetSection(0, InkGrey, "- (no business set up)"); return; }
        bool open = false;
        try { open = Helpers.BusinessHelper.IsBusinessOpen(reg); } catch { }
        bool paused = false;
        try { paused = reg.temporarilyClosed; } catch { }
        ScheduleDay today = null;
        try { today = Helpers.BuildingHelper.GetTodaySchedule(reg); } catch { }
        string hours = "closed today";
        if (today != null && today.isOpen && today.openingHourSlots != null && today.openingHourSlots.Count > 0)
        {
            var parts = new List<string>();
            foreach (var s in today.openingHourSlots)
            {
                if (s == null) continue;
                parts.Add(s.startingHour.ToString("D2") + ":00-" + s.endingHour.ToString("D2") + ":00");
            }
            if (parts.Count > 0) hours = string.Join(", ", parts.ToArray());
        }
        int openDays = 0;
        try
        {
            if (reg.scheduleDays != null)
                foreach (var d in reg.scheduleDays)
                    if (d != null && d.isOpen) openDays++;
        }
        catch { }
        string reason = paused ? " (temporarily closed by you)"
            : (today == null || !today.isOpen) ? " (no hours today)"
            : (open ? "" : " (outside open hours)");
        string body = (open ? "OPEN NOW" : "CLOSED NOW") + reason
            + "\nToday: " + hours + "\nOpen " + openDays + "/7 days";
        SetSection(0, open ? ProfitGreen : LossRed, body);
    }

    static void RenderStaffingSection(BuildingRegistration reg)
    {
        bool hasBiz = false;
        try { hasBiz = !string.IsNullOrEmpty(reg.BusinessName); } catch { }
        if (!hasBiz)
        {
            SetSection(1, InkGrey, "- (no business set up)");
            UpdateHireButton(null);
            return;
        }
        var missing = new List<string>();
        int shiftsTotal = 0;
        int shiftsBare = 0;
        try
        {
            if (reg.scheduleDays != null)
            {
                foreach (var day in reg.scheduleDays)
                {
                    if (day == null || !day.isOpen) continue;
                    if (day.openingHourSlots == null) continue;
                    string dayName = DayName(day.day);
                    foreach (var slot in day.openingHourSlots)
                    {
                        if (slot == null) continue;
                        int runStart = -1;
                        for (int h = slot.startingHour; h < slot.endingHour; h++)
                        {
                            bool covered = false;
                            if (day.workShifts != null)
                            {
                                foreach (var ws in day.workShifts)
                                {
                                    if (ws == null || string.IsNullOrEmpty(ws.employeeId)) continue;
                                    if (h >= ws.startingHour && h < ws.endingHour) { covered = true; break; }
                                }
                            }
                            if (!covered)
                            {
                                if (runStart < 0) runStart = h;
                            }
                            else if (runStart >= 0)
                            {
                                missing.Add(dayName + " " + runStart.ToString("D2") + ":00-"
                                    + h.ToString("D2") + ":00 nobody assigned");
                                runStart = -1;
                            }
                        }
                        if (runStart >= 0)
                            missing.Add(dayName + " " + runStart.ToString("D2") + ":00-"
                                + slot.endingHour.ToString("D2") + ":00 nobody assigned");
                    }
                    if (day.workShifts != null)
                    {
                        foreach (var ws in day.workShifts)
                        {
                            if (ws == null) continue;
                            shiftsTotal++;
                            if (string.IsNullOrEmpty(ws.employeeId)) shiftsBare++;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SetSection(1, LossRed, "check failed: " + ex.Message);
            return;
        }
        if (shiftsBare > 0)
            missing.Insert(0, shiftsBare + " shift(s) with no employee at all");
        string coverBody;
        bool coverBad;
        if (missing.Count == 0)
        {
            coverBody = "OK - " + shiftsTotal + " assigned shift(s) cover all open hours";
            coverBad = false;
        }
        else
        {
            int show = Math.Min(6, missing.Count);
            coverBody = "MISSING (" + missing.Count + "):\n- "
                + string.Join("\n- ", missing.GetRange(0, show).ToArray());
            if (missing.Count > show) coverBody += "\n+ " + (missing.Count - show) + " more";
            coverBad = true;
        }
        RenderHiringBlock(reg, coverBody, coverBad);
    }

    // ---------- hiring: agency / headhunter orders + assign button ----------

    /// <summary>Staffing part 2: which of the required skills has no assigned
    /// employee, whether an agency campaign or a headhunter plan already
    /// covers them, and which found candidates wait for assignment. Waiting
    /// candidates arm the HIRE &amp; ASSIGN button (same recipe as the game's
    /// own assign+hires mass action).</summary>
    static void RenderHiringBlock(BuildingRegistration reg, string coverBody, bool coverBad)
    {
        var valid = ValidBusinessSkills(reg);
        var covered = CoveredSkills(reg);
        int assignedHere = 0;
        try
        {
            var qc = default(EmployeeInstancesQueryInfo);
            qc.withAssignedAddress = reg.Address;
            var el = Helpers.EmployeeHelper.GetEmployeeInstances(qc);
            if (el != null) assignedHere = el.Count;
        }
        catch { }
        var need = new List<string>();
        foreach (var s in valid)
            if (!covered.Contains(s)) need.Add(s);
        var lines = new List<string>();
        lines.Add(coverBody);
        lines.Add("Assigned here: " + assignedHere + " employee(s)");
        int stations = -1;
        try
        {
            var ai = new List<ItemInstance>();
            reg.GetAssignableItems(ai);
            stations = ai.Count;
        }
        catch { }
        if (stations < 0) lines.Add("Workstations: ?");
        else if (stations == 0) lines.Add("Workstations: NONE - place furniture inside first");
        else lines.Add("Workstations: " + stations);
        var waiting = new List<EmployeeInstance>();
        try
        {
            // Agency orders for this business.
            var campaigns = SaveGameManager.Current.RecruitmentCampaigns;
            if (campaigns != null)
            {
                foreach (var c in campaigns)
                {
                    if (c == null || c.finished) continue;
                    try { if (c.businessAddress != reg.Address) continue; }
                    catch { continue; }
                    string skill = null;
                    try { skill = c.skillRequirement != null ? c.skillRequirement.skillName : null; }
                    catch { }
                    if (string.IsNullOrEmpty(skill) || !need.Contains(skill)) continue;
                    string agency = "?";
                    try
                    {
                        var a = Helpers.BuildingHelper.GetBuildingRegistration(c.agencyAddress);
                        if (a != null && !string.IsNullOrEmpty(a.BusinessName)) agency = a.BusinessName;
                    }
                    catch { }
                    int found = 0;
                    int want = 0;
                    try { found = c.candidatesFound; want = c.amountOfCandidates; } catch { }
                    lines.Add("Agency '" + agency + "': recruiting "
                        + LocalizeOrClean(skill, "ba:skill_")
                        + " (" + found + "/" + want + " found)");
                }
            }
            // Headhunter plans recruiting a skill this business needs.
            var plans = SaveGameManager.Current.headhunterPlans;
            var matchingPlans = new List<Buildings.Office.Headquarters.HeadhunterPlan>();
            if (plans != null)
            {
                foreach (var p in plans)
                {
                    if (p == null || !p.isRecruiting) continue;
                    string skill = null;
                    try { skill = p.skillRecruiting; } catch { }
                    if (string.IsNullOrEmpty(skill) || !need.Contains(skill)) continue;
                    matchingPlans.Add(p);
                    string hh = "?";
                    try
                    {
                        var inst = p.HeadhunterInstance;
                        if (inst != null && inst.characterData != null
                            && !string.IsNullOrEmpty(inst.characterData.name))
                            hh = inst.characterData.name;
                    }
                    catch { }
                    bool linked = false;
                    try { linked = PlanLinkedToBusiness(p, reg.Address); } catch { }
                    lines.Add("Headhunter " + hh + ": recruiting "
                        + LocalizeOrClean(skill, "ba:skill_")
                        + (linked ? " (for this business)" : ""));
                }
            }
            // Candidates already found, waiting for assignment here.
            var all = SaveGameManager.Current.CandidateEmployeeInstances;
            if (all != null)
            {
                foreach (var cand in all)
                {
                    if (cand == null || cand.candidateInfo == null) continue;
                    bool ours = false;
                    try { ours = cand.assignedAddress == reg.Address; } catch { }
                    if (!ours && !string.IsNullOrEmpty(cand.candidateInfo.sourceHeadhunterId))
                    {
                        foreach (var p in matchingPlans)
                        {
                            string pid = null;
                            try { pid = p.id; } catch { }
                            if (!string.IsNullOrEmpty(pid)
                                && pid == cand.candidateInfo.sourceHeadhunterId)
                            { ours = true; break; }
                        }
                    }
                    if (!ours) continue;
                    if (!CandidateFits(cand, valid)) continue;
                    waiting.Add(cand);
                }
            }
            if (need.Count > 0)
            {
                var labels = new List<string>();
                foreach (var s in need) labels.Add(LocalizeOrClean(s, "ba:skill_"));
                // Orders covering every missing skill -> no complaint line.
                bool allCovered = true;
                foreach (var s in need)
                {
                    if (!SkillOrdered(s, campaigns, matchingPlans, reg)) { allCovered = false; break; }
                }
                if (!allCovered)
                    lines.Add("No order for: " + string.Join(", ", labels.ToArray()));
            }
            if (waiting.Count > 0)
            {
                int show = Math.Min(4, waiting.Count);
                var names = new List<string>();
                for (int i = 0; i < show; i++) names.Add(CandidateLabel(waiting[i]));
                string w = "Ready to hire: " + string.Join(", ", names.ToArray());
                if (waiting.Count > show) w += " + " + (waiting.Count - show) + " more";
                lines.Add(w);
            }
        }
        catch (Exception ex)
        {
            lines.Add("hiring check failed: " + ex.Message);
        }
        if (_hasCopyResult && _copyResult != null)
        {
            bool same = false;
            try { same = _copyResultAddr == reg.Address; } catch { }
            if (same) lines.Add(_copyResult);
        }
        bool bad = coverBad || waiting.Count > 0 || need.Count > 0;
        SetSection(1, bad ? LossRed : ProfitGreen, string.Join("\n", lines.ToArray()));
        _hireCandidates.Clear();
        if (waiting.Count > 0) _hireCandidates.AddRange(waiting);
        UpdateHireButton(waiting.Count > 0 ? reg : null);
    }

    static bool SkillOrdered(string skill, List<RecruitmentCampaign> campaigns,
        List<Buildings.Office.Headquarters.HeadhunterPlan> plans, BuildingRegistration reg)
    {
        try
        {
            if (campaigns != null)
                foreach (var c in campaigns)
                {
                    if (c == null || c.finished) continue;
                    try { if (c.businessAddress != reg.Address) continue; }
                    catch { continue; }
                    string s = null;
                    try { s = c.skillRequirement != null ? c.skillRequirement.skillName : null; }
                    catch { }
                    if (s == skill) return true;
                }
            if (plans != null)
                foreach (var p in plans)
                {
                    if (p == null) continue;
                    string s = null;
                    try { s = p.skillRecruiting; } catch { }
                    if (s == skill) return true;
                }
        }
        catch { }
        return false;
    }

    /// <summary>Same skill set the game's own assign+hires mass action
    /// accepts for a business.</summary>
    static List<string> ValidBusinessSkills(BuildingRegistration reg)
    {
        var list = new List<string>();
        try
        {
            var bt = Helpers.BusinessTypeHelper.GetData(reg);
            if (bt != null && bt.employeePrimarySkills != null)
                foreach (var s in bt.employeePrimarySkills)
                    if (!string.IsNullOrEmpty(s) && !list.Contains(s)) list.Add(s);
            var btd = BuildingTypeHelper.GetData(reg);
            if (btd != null)
            {
                if (btd.requiredBuildingSkills != null)
                    foreach (var s in btd.requiredBuildingSkills)
                        if (!string.IsNullOrEmpty(s) && !list.Contains(s)) list.Add(s);
                if (btd.NeedsCleaning && !list.Contains("ba:skill_cleaning"))
                    list.Add("ba:skill_cleaning");
                if (bt != null && bt.HasTag(BigAmbitions.Tags.TagRef.Businesstag.allowtheft)
                    && !list.Contains("ba:skill_securityguard"))
                    list.Add("ba:skill_securityguard");
                if (btd.requiredBuildingSkills != null
                    && btd.requiredBuildingSkills.Contains("ba:skill_deliverydriver")
                    && !list.Contains("ba:skill_deliverydriver"))
                    list.Add("ba:skill_deliverydriver");
            }
        }
        catch { }
        return list;
    }

    static HashSet<string> CoveredSkills(BuildingRegistration reg)
    {
        var set = new HashSet<string>();
        try
        {
            var q = default(EmployeeInstancesQueryInfo);
            q.withAssignedAddress = reg.Address;
            var emps = Helpers.EmployeeHelper.GetEmployeeInstances(q);
            if (emps != null)
                foreach (var e in emps)
                {
                    if (e == null || e.characterData == null || e.characterData.skills == null) continue;
                    foreach (var sk in e.characterData.skills)
                        if (sk != null && !string.IsNullOrEmpty(sk.name)) set.Add(sk.name);
                }
        }
        catch { }
        return set;
    }

    /// <summary>Headhunter plan reaches this business when one of its HR
    /// plans manages an employee assigned here.</summary>
    static bool PlanLinkedToBusiness(Buildings.Office.Headquarters.HeadhunterPlan plan,
        Address addr)
    {
        if (plan.assignedHrPlans == null) return false;
        var hrPlans = SaveGameManager.Current.hrManagerPlans;
        if (hrPlans == null) return false;
        foreach (var hrId in plan.assignedHrPlans)
        {
            if (string.IsNullOrEmpty(hrId)) continue;
            Buildings.Office.Headquarters.HrManagerPlan hr = null;
            foreach (var h in hrPlans)
            {
                if (h == null) continue;
                string hid = null;
                try { hid = h.id; } catch { }
                if (hid == hrId) { hr = h; break; }
            }
            if (hr == null || hr.assignedEmployees == null) continue;
            foreach (var empId in hr.assignedEmployees)
            {
                if (string.IsNullOrEmpty(empId)) continue;
                EmployeeInstance emp = null;
                try { emp = Helpers.EmployeeHelper.GetEmployeeById(empId, false); }
                catch { }
                if (emp == null) continue;
                try { if (emp.assignedAddress == addr) return true; }
                catch { }
            }
        }
        return false;
    }

    static bool CandidateFits(EmployeeInstance cand, List<string> valid)
    {
        try
        {
            if (cand.characterData == null || cand.characterData.skills == null) return false;
            foreach (var sk in cand.characterData.skills)
                if (sk != null && valid.Contains(sk.name)) return true;
        }
        catch { }
        return false;
    }

    static string CandidateLabel(EmployeeInstance cand)
    {
        try
        {
            string name = cand.characterData != null ? cand.characterData.name : "?";
            string skill = null;
            float val = 0f;
            try
            {
                skill = cand.GetPrimarySkill();
                if (!string.IsNullOrEmpty(skill)) val = cand.GetSkillValue(skill);
            }
            catch { }
            string sl = string.IsNullOrEmpty(skill) ? "?" : LocalizeOrClean(skill, "ba:skill_");
            return (name ?? "?") + " (" + sl + " " + val.ToString("F0") + ")";
        }
        catch { return "?"; }
    }

    static void UpdateHireButton(BuildingRegistration reg)
    {
        _hireReg = reg;
        if (_hireBtnGO == null) return;
        bool show = reg != null && _hireCandidates.Count > 0;
        if (_hireBtnLabel != null && show)
            _hireBtnLabel.text = "HIRE & ASSIGN (" + _hireCandidates.Count + ") TO THIS BUSINESS";
        _hireBtnGO.SetActive(show);
    }

    /// <summary>Hires every waiting candidate into the selected business
    /// (assignedAddress + HireCandidate, exactly like the game's own
    /// "assign to business and hire" mass action) and then seats them into
    /// the business's empty shift slots, matched by station skill.</summary>
    static void HireAndAssign()
    {
        try
        {
            if (_hireReg == null || _hireCandidates.Count == 0) return;
            int done = 0;
            var hired = new List<EmployeeInstance>();
            foreach (var cand in new List<EmployeeInstance>(_hireCandidates))
            {
                if (cand == null) continue;
                try
                {
                    cand.assignedAddress = _hireReg.Address;
                    Helpers.EmployeeHelper.HireCandidate(cand);
                    done++;
                    hired.Add(cand);
                }
                catch (Exception ex)
                {
                    _context?.Logger.Warn("The Big Board hire failed for candidate: " + ex.Message);
                }
            }
            int filled = 0;
            int empty = 0;
            try { filled = FillEmptyShifts(_hireReg, hired, out empty); }
            catch (Exception ex)
            {
                _context?.Logger.Warn("The Big Board shift fill failed: " + ex.Message);
            }
            _context?.Logger.Info("The Big Board hired " + done + "/" + _hireCandidates.Count
                + " waiting candidates into "
                + (_hireReg.BusinessName ?? _hireReg.Address.ToFormattedString())
                + "; seated " + filled + "/" + empty + " empty shifts");
            _hireCandidates.Clear();
            try { ScheduleCopy.RefreshScheduleUI(_hireReg); }
            catch { }
            Refresh();
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board hire-and-assign failed: " + ex.Message);
        }
    }

    /// <summary>Seats fresh hires 1:1 into the recorded copy pattern: all
    /// empty slots that belonged to base person N go to hire N (same order
    /// both sides, skill match first, then any free hand). One hire takes a
    /// whole weekly set, exactly like the source. Without a recorded
    /// pattern (older copy), empty shifts pair to hires round-robin.</summary>
    static int FillEmptyShifts(BuildingRegistration reg, List<EmployeeInstance> hires,
        out int emptyTotal)
    {
        emptyTotal = 0;
        int filled = 0;
        if (reg == null || hires == null || hires.Count == 0) return 0;
        if (reg.scheduleDays == null) return 0;
        var pool = new List<EmployeeInstance>();
        foreach (var h in hires)
            if (h != null && !string.IsNullOrEmpty(h.id) && !pool.Contains(h)) pool.Add(h);
        pool.Sort(ScheduleCopy.EmpOrder);
        var used = new HashSet<string>();
        var touched = new HashSet<string>();
        var wants = ScheduleCopy.GetWants(reg);
        if (wants != null && wants.Count > 0)
        {
            var groups = new List<List<ScheduleCopy.SlotWant>>();
            var groupSkill = new List<string>();
            var groupKey = new List<string>();
            foreach (var w in new List<ScheduleCopy.SlotWant>(wants))
            {
                if (w == null || w.shift == null) { wants.Remove(w); continue; }
                if (!string.IsNullOrEmpty(w.shift.employeeId)) { wants.Remove(w); continue; }
                emptyTotal++;
                string key = w.baseEmp ?? ("slot" + groups.Count);
                int gi = groupKey.IndexOf(key);
                if (gi < 0)
                {
                    gi = groups.Count;
                    groupKey.Add(key);
                    groups.Add(new List<ScheduleCopy.SlotWant>());
                    groupSkill.Add(w.skill);
                }
                groups[gi].Add(w);
            }
            for (int pass = 0; pass < 2; pass++)
            {
                bool anyFree = false;
                foreach (var h in pool) if (!used.Contains(h.id)) { anyFree = true; break; }
                if (!anyFree) break;
                for (int gi = 0; gi < groups.Count; gi++)
                {
                    if (groups[gi].Count == 0) continue;
                    if (groups[gi][0].shift != null
                        && !string.IsNullOrEmpty(groups[gi][0].shift.employeeId)) continue;
                    EmployeeInstance pick = null;
                    if (pass == 0 && !string.IsNullOrEmpty(groupSkill[gi]))
                        pick = pool.Find(h => !used.Contains(h.id)
                            && PrimaryIs(h, groupSkill[gi]));
                    else if (pass == 1)
                        pick = pool.Find(h => !used.Contains(h.id));
                    if (pick == null) continue;
                    used.Add(pick.id);
                    touched.Add(pick.id);
                    foreach (var w in groups[gi])
                    {
                        try { w.shift.employeeId = pick.id; filled++; }
                        catch { }
                        wants.Remove(w);
                    }
                    groups[gi].Clear();
                }
            }
        }
        else
        {
            // No recorded pattern (older copy): pair empty shifts to hires
            // in plain day order, round-robin. Even, deterministic.
            var empties = new List<WorkShift>();
            foreach (var day in reg.scheduleDays)
            {
                if (day == null || day.workShifts == null) continue;
                foreach (var sh in day.workShifts)
                    if (sh != null && string.IsNullOrEmpty(sh.employeeId)) empties.Add(sh);
            }
            emptyTotal = empties.Count;
            for (int i = 0; i < empties.Count && pool.Count > 0; i++)
            {
                var pick = pool[i % pool.Count];
                try { empties[i].employeeId = pick.id; touched.Add(pick.id); filled++; }
                catch { }
            }
        }
        foreach (var empId in touched)
        {
            try
            {
                var emp = Helpers.EmployeeHelper.GetEmployeeById(empId, false);
                if (emp == null) continue;
                emp.UpdateWeeklyHoursAndDays(reg.scheduleDays);
                emp.UpdateAssignedWorkStationItems();
            }
            catch { }
        }
        if (filled > 0)
        {
            try { SaveGameManager.MarkChange(); }
            catch { }
        }
        return filled;
    }

    static bool PrimaryIs(EmployeeInstance emp, string skill)
    {
        try { return emp != null && emp.GetPrimarySkill() == skill; }
        catch { return false; }
    }

    static string DayName(DayOfWeekOrdered day)
    {
        try { return day.ToString().Substring(0, 3); }
        catch { return "?"; }
    }

    static void RenderMarketingSection(BuildingRegistration reg)
    {
        bool hasBiz = false;
        try { hasBiz = !string.IsNullOrEmpty(reg.BusinessName); } catch { }
        if (!hasBiz) { SetSection(2, InkGrey, "- (no business set up)"); return; }
        int active = 0;
        try
        {
            if (reg.marketingCampaigns != null)
                foreach (var c in reg.marketingCampaigns)
                    if (c != null && c.enabled) active++;
        }
        catch { }
        if (active == 0)
        {
            SetSection(2, LossRed, "NONE - no active campaigns");
            return;
        }
        float cost = 0f;
        float reach = 0f;
        try { cost = reg.GetDailyMarketingExpenses(); } catch { }
        try { reach = reg.GetMarketingEfficiency(); } catch { }
        SetSection(2, ProfitGreen,
            "ACTIVE - " + active + " campaign(s)\n"
            + FormatMoney(cost) + "/day  |  reach " + reach.ToString("F0") + "%");
    }

    static void RenderStockSection(BuildingRegistration reg)
    {
        bool hasBiz = false;
        try { hasBiz = !string.IsNullOrEmpty(reg.BusinessName); } catch { }
        if (!hasBiz) { SetSection(3, InkGrey, "- (no business set up)"); return; }
        var empty = new List<string>();
        var low = new List<string>();
        int stocked = 0;
        bool anyTrackable = false;
        try
        {
            bool paperBags = false;
            try
            {
                var bt = Helpers.BusinessTypeHelper.GetData(reg);
                paperBags = bt != null && bt.HasTag(BigAmbitions.Tags.TagRef.Businesstag.customersneedpaperbags);
            }
            catch { }
            if (reg.itemInstances != null)
            {
                foreach (var item in reg.itemInstances.Values)
                {
                    if (item == null) continue;
                    bool restockable = false;
                    try { restockable = ItemHelper.CanRestockItem(item); } catch { continue; }
                    if (!restockable) continue;
                    try
                    {
                        if (item.ItemCached != null
                            && (item.ItemCached.type & ItemType.PointOfSale) != 0
                            && !paperBags)
                            continue;
                    }
                    catch { }
                    anyTrackable = true;
                    stocked++;
                    CargoInstance stock = null;
                    try { stock = item.GetStockInstance(); } catch { continue; }
                    if (stock == null || stock.itemName == null) continue;
                    int max = 0;
                    try { max = stock.GetMaxStockCapacity(item); } catch { continue; }
                    if (max <= 0) continue;
                    int num = 0;
                    try
                    {
                        num = stock.amount + Helpers.BuildingHelper.CountTotalResourcesInStockCached(
                            reg, stock.itemName, false, false);
                    }
                    catch { continue; }
                    string label = ItemLabel(stock.itemName) + " (" + num + "/" + max + ")";
                    if (num <= 0) empty.Add(label);
                    else if ((float)num <= (float)max * 0.25f) low.Add(label);
                }
            }
        }
        catch (Exception ex)
        {
            SetSection(3, LossRed, "check failed: " + ex.Message);
            return;
        }
        if (!anyTrackable) { SetSection(3, InkGrey, "- (no stockable items)"); return; }
        if (empty.Count == 0 && low.Count == 0)
        {
            SetSection(3, ProfitGreen, "OK - " + stocked + " stocked item(s), none low");
            return;
        }
        var lines = new List<string>();
        foreach (var e in empty) lines.Add("EMPTY: " + e);
        foreach (var l in low) lines.Add("LOW: " + l);
        int total = lines.Count;
        int show = Math.Min(10, total);
        string body = string.Join("\n", lines.GetRange(0, show).ToArray());
        if (total > show) body += "\n+ " + (total - show) + " more";
        SetSection(3, LossRed, body);
    }

    static void RenderUniformSection(BuildingRegistration reg)
    {
        bool hasBiz = false;
        try { hasBiz = !string.IsNullOrEmpty(reg.BusinessName); } catch { }
        if (!hasBiz) { SetSection(4, InkGrey, "- (no business set up)"); return; }
        var required = new List<string>();
        try
        {
            var bt = Helpers.BusinessTypeHelper.GetData(reg.businessTypeName);
            if (bt != null && bt.employeePrimarySkills != null)
                foreach (var s in bt.employeePrimarySkills)
                    if (!string.IsNullOrEmpty(s) && !required.Contains(s)) required.Add(s);
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board businesses uniform skills failed: " + ex.Message);
        }
        try
        {
            var btd = BuildingTypeHelper.GetData(reg);
            if (btd != null && btd.requiredBuildingSkills != null)
                foreach (var s in btd.requiredBuildingSkills)
                    if (!string.IsNullOrEmpty(s) && !required.Contains(s)) required.Add(s);
        }
        catch { }
        if (required.Count == 0) { SetSection(4, InkGrey, "- (no skills required)"); return; }
        var missing = new List<string>();
        try
        {
            var uni = reg.uniformsBySkill;
            foreach (var s in required)
                if (uni == null || !uni.ContainsKey(s))
                    missing.Add(LocalizeOrClean(s, "ba:skill_"));
        }
        catch { }
        if (missing.Count == 0)
        {
            SetSection(4, ProfitGreen, "OK - " + required.Count + " skill(s) uniformed");
            return;
        }
        SetSection(4, LossRed, "MISSING (" + missing.Count + "):\n- "
            + string.Join("\n- ", missing.ToArray()));
    }

    // ---------- copy shifts from another shop ----------

    static BuildingRegistration SelectedReg()
    {
        if (!_hasSelection) return null;
        foreach (var r in _rows)
        {
            try
            {
                if (r != null && r.reg != null && r.reg.Address == _selected) return r.reg;
            }
            catch { }
        }
        return null;
    }

    static void BuildCopyPopup()
    {
        if (_page == null) return;
        _copyPopup = new GameObject("BizCopyPopup");
        _copyPopup.transform.SetParent(_page.transform, false);
        Stretch(_copyPopup);
        var dim = _copyPopup.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.72f);
        dim.raycastTarget = true;
        var cardGO = new GameObject("BizCopyCard");
        cardGO.transform.SetParent(_copyPopup.transform, false);
        Anchor(cardGO, 0f, 0f, 1f, 1f, 120f, 60f, -120f, -60f);
        var cardBg = cardGO.AddComponent<Image>();
        cardBg.color = new Color(0.10f, 0.12f, 0.15f, 1f);
        var cardOutline = cardGO.AddComponent<Outline>();
        cardOutline.effectColor = new Color(0.32f, 0.36f, 0.42f, 1f);
        cardOutline.effectDistance = new Vector2(2f, 2f);
        _copyPopupTitle = Text("BizCopyTitle", "COPY SHIFTS FROM", 40,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, Color.white);
        _copyPopupTitle.transform.SetParent(cardGO.transform, false);
        Anchor(_copyPopupTitle.gameObject, 0f, 1f, 1f, 1f, 40f, -110f, -300f, -30f);
        var closeGO = new GameObject("BizCopyClose");
        closeGO.transform.SetParent(cardGO.transform, false);
        Anchor(closeGO, 1f, 1f, 1f, 1f, -260f, -104f, -40f, -36f);
        var closeBg = closeGO.AddComponent<Image>();
        closeBg.color = BtnOff;
        var closeBtn = closeGO.AddComponent<Button>();
        closeBtn.targetGraphic = closeBg;
        var closeLabel = Text("BizCopyCloseLabel", "CLOSE", 28,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, InkLight);
        closeLabel.transform.SetParent(closeGO.transform, false);
        Stretch(closeLabel.gameObject);
        closeBtn.onClick.AddListener(CloseCopyPopup);
        // Missing staff is always recruited via headhunter (agency finds
        // whoever is around; most would never fit the pattern).
        var sepGO = new GameObject("BizCopySep");
        sepGO.transform.SetParent(cardGO.transform, false);
        Anchor(sepGO, 0f, 1f, 1f, 1f, 40f, -124f, -40f, -120f);
        var sepBg = sepGO.AddComponent<Image>();
        sepBg.color = new Color(0.32f, 0.36f, 0.42f, 1f);
        _copyTable = new ShopPickTable(PickCopySource);
        _copyTable.Init(_context);
        _copyTable.BuildHeader(cardGO, "BizCopyHead", 0f, 0f, -188f, -134f);
        _copyTable.BuildRows(cardGO,
            "BizCopyScroll", "BizCopyViewport", "BizCopyRows", "BizCopyScrollbar",
            -196f);
        _copyPopup.SetActive(false);
    }

    static void OpenCopyPopup()
    {
        try
        {
            if (_copyPopup == null) return;
            var target = SelectedReg();
            if (target == null) return;
            RenderCopyPopup();
            _copyPopup.transform.SetAsLastSibling();
            _copyPopup.SetActive(true);
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board copy popup open failed: " + ex.Message);
        }
    }

    static void CloseCopyPopup()
    {
        try { if (_copyPopup != null) _copyPopup.SetActive(false); }
        catch { }
    }

    static void RenderCopyPopup()
    {
        if (_copyPopup == null || _copyTable == null) return;
        var target = SelectedReg();
        string bt = "";
        try { bt = target != null ? target.businessTypeName ?? "" : ""; } catch { }
        var rows = new List<ShopPickTable.Row>();
        foreach (var shop in ScheduleCopy.PlayerShops())
        {
            if (shop == null || (shop.businessTypeName ?? "") != bt) continue;
            bool self = false;
            try { self = target != null && shop.Address == target.Address; } catch { }
            if (self) continue;
            int openDays = 0;
            int shifts = 0;
            int staff = 0;
            try
            {
                if (shop.scheduleDays != null)
                    foreach (var d in shop.scheduleDays)
                    {
                        if (d == null) continue;
                        if (d.isOpen) openDays++;
                        if (d.workShifts != null) shifts += d.workShifts.Count;
                    }
            }
            catch { }
            try
            {
                var save = SaveGameManager.Current;
                if (save != null && save.EmployeeInstances != null && shop.Address != null)
                    foreach (var emp in save.EmployeeInstances)
                    {
                        if (emp == null || emp.IsCandidate) continue;
                        if (emp.assignedAddress != null && emp.assignedAddress.Equals(shop.Address))
                            staff++;
                    }
            }
            catch { }
            rows.Add(new ShopPickTable.Row
            {
                name = ScheduleCopy.ShopName(shop),
                type = ScheduleCopy.ShortType(shop.businessTypeName),
                openDays = openDays,
                shifts = shifts,
                staff = staff,
                selected = false,
                reg = shop
            });
        }
        string tname = "?";
        try { tname = target != null ? ScheduleCopy.ShopName(target) : "?"; } catch { }
        if (_copyPopupTitle != null)
            _copyPopupTitle.text = "COPY SHIFTS -> " + tname + " (" + rows.Count + ")";
        if (rows.Count == 0)
            _copyTable.ShowInfo("No same-type shop found to copy from.");
        else
            _copyTable.SetRows(rows);
    }

    static void PickCopySource(BuildingRegistration source)
    {
        try
        {
            var target = SelectedReg();
            if (source == null || target == null) return;
            // Structure only: missing heads are ordered from the Employee
            // tab, never from here.
            var res = ScheduleCopy.Copy(source, target, _context);
            try
            {
                _copyResultAddr = target.Address;
                _hasCopyResult = true;
                _copyResult = res != null ? res.message : "Copy failed.";
            }
            catch { }
            CloseCopyPopup();
            Refresh();
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board copy pick failed: " + ex.Message);
        }
    }

    // ---------- dead hours: which open hours have no customers ----------

    /// <summary>Hourly customer breakdown of the selected shop from
    /// orderHistory.hourReports (per-day, per-hour customer counts written
    /// at rollover). Missing hours on a recorded day count as 0. Averages
    /// divide by recorded days, so they stay comparable as history grows.
    /// OPEN comes from the current weekly plan (not history). Verdicts:
    /// DEAD = avg &lt; 1/day, WEAK = avg &lt; 3/day, else OK.</summary>
    static void BuildDeadPopup()
    {
        if (_page == null) return;
        _deadPopup = new GameObject("BizDeadPopup");
        _deadPopup.transform.SetParent(_page.transform, false);
        Stretch(_deadPopup);
        var dim = _deadPopup.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.72f);
        dim.raycastTarget = true;
        var cardGO = new GameObject("BizDeadCard");
        cardGO.transform.SetParent(_deadPopup.transform, false);
        Anchor(cardGO, 0f, 0f, 1f, 1f, 120f, 60f, -120f, -60f);
        var cardBg = cardGO.AddComponent<Image>();
        cardBg.color = new Color(0.10f, 0.12f, 0.15f, 1f);
        var cardOutline = cardGO.AddComponent<Outline>();
        cardOutline.effectColor = new Color(0.32f, 0.36f, 0.42f, 1f);
        cardOutline.effectDistance = new Vector2(2f, 2f);
        _deadPopupTitle = Text("BizDeadTitle", "DEAD HOURS", 40,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, Color.white);
        _deadPopupTitle.transform.SetParent(cardGO.transform, false);
        Anchor(_deadPopupTitle.gameObject, 0f, 1f, 1f, 1f, 40f, -110f, -300f, -30f);
        var closeGO = new GameObject("BizDeadClose");
        closeGO.transform.SetParent(cardGO.transform, false);
        Anchor(closeGO, 1f, 1f, 1f, 1f, -260f, -104f, -40f, -36f);
        var closeBg = closeGO.AddComponent<Image>();
        closeBg.color = BtnOff;
        var closeBtn = closeGO.AddComponent<Button>();
        closeBtn.targetGraphic = closeBg;
        var closeLabel = Text("BizDeadCloseLabel", "CLOSE", 28,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, InkLight);
        closeLabel.transform.SetParent(closeGO.transform, false);
        Stretch(closeLabel.gameObject);
        closeBtn.onClick.AddListener(CloseDeadPopup);
        _deadSummary = Text("BizDeadSummary", "", 26,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Top, InkLight);
        _deadSummary.transform.SetParent(cardGO.transform, false);
        Anchor(_deadSummary.gameObject, 0f, 1f, 1f, 1f, 40f, -250f, -40f, -120f);
        _deadSummary.textWrappingMode = TMPro.TextWrappingModes.Normal;
        _deadTable = new DeadHoursTable();
        _deadTable.Init(_context);
        _deadTable.BuildHeader(cardGO, "BizDeadHead", 0f, 0f, -318f, -264f);
        _deadTable.BuildRows(cardGO,
            "BizDeadScroll", "BizDeadViewport", "BizDeadRows", "BizDeadScrollbar",
            -326f);
        _deadPopup.SetActive(false);
    }

    static void OpenDeadPopup()
    {
        try
        {
            if (_deadPopup == null) return;
            var target = SelectedReg();
            if (target == null) return;
            RenderDeadPopup(target);
            _deadPopup.transform.SetAsLastSibling();
            _deadPopup.SetActive(true);
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board dead hours open failed: " + ex.Message);
        }
    }

    static void CloseDeadPopup()
    {
        try { if (_deadPopup != null) _deadPopup.SetActive(false); }
        catch { }
    }

    static void RenderDeadPopup(BuildingRegistration target)
    {
        if (_deadPopupTitle == null || _deadSummary == null || _deadTable == null) return;
        string tname = "?";
        try { tname = ScheduleCopy.ShopName(target); }
        catch { }
        _deadPopupTitle.text = "DEAD HOURS -> " + tname;
        var rows = new List<DeadHoursTable.Row>();
        try
        {
            var perHour = new long[24];
            int days = 0;
            long totalCust = 0;
            if (target.orderHistory != null)
            {
                var entries = new List<Entities.OrderHistoryEntry>(target.orderHistory);
                entries.Sort((a, b) => a.dayNumber.CompareTo(b.dayNumber));
                // Recent history only: old weeks ran under a different
                // schedule and would blur today's dead stretches.
                if (entries.Count > 14) entries.RemoveRange(0, entries.Count - 14);
                days = entries.Count;
                foreach (var e in entries)
                {
                    if (e == null) continue;
                    totalCust += Math.Max(0, e.totalCustomers);
                    if (e.hourReports == null) continue;
                    foreach (var h in e.hourReports)
                    {
                        if (h == null || h.hour < 0 || h.hour > 23) continue;
                        perHour[h.hour] += Math.Max(0, h.customers);
                    }
                }
            }
            var openDays = new int[24];
            try
            {
                if (target.scheduleDays != null)
                    foreach (var day in target.scheduleDays)
                    {
                        if (day == null || !day.isOpen || day.openingHourSlots == null) continue;
                        for (int h = 0; h < 24; h++)
                        {
                            foreach (var slot in day.openingHourSlots)
                            {
                                if (slot == null) continue;
                                if (h >= slot.startingHour && h < slot.endingHour) { openDays[h]++; break; }
                            }
                        }
                    }
            }
            catch { }
            if (days == 0)
            {
                _deadSummary.text = "No sales history yet - keep the shop open a few days first.";
                _deadTable.ShowInfo("No history.");
                return;
            }
            var deadOpen = new List<string>();
            var weakOpen = new List<string>();
            for (int h = 0; h < 24; h++)
            {
                float avg = (float)perHour[h] / Math.Max(1, days);
                string verdict;
                int rank;
                if (openDays[h] == 0) { verdict = "CLOSED"; rank = 3; }
                else if (avg < 1f) { verdict = "DEAD - consider closing"; rank = 0; }
                else if (avg < 3f) { verdict = "WEAK"; rank = 1; }
                else { verdict = "OK"; rank = 2; }
                if (rank == 0) deadOpen.Add(h.ToString("D2"));
                else if (rank == 1) weakOpen.Add(h.ToString("D2"));
                rows.Add(new DeadHoursTable.Row
                {
                    hour = h,
                    hourLabel = h.ToString("D2") + ":00",
                    customers = perHour[h],
                    avg = avg,
                    openDays = openDays[h],
                    verdict = verdict,
                    verdictRank = rank,
                });
            }
            string head = days + " day(s), " + totalCust.ToString("N0") + " customers. ";
            if (deadOpen.Count > 0)
                head += "DEAD but open: " + string.Join(", ", deadOpen.ToArray()) + ". ";
            else
                head += "No dead open hour. ";
            if (weakOpen.Count > 0)
                head += "WEAK but open: " + string.Join(", ", weakOpen.ToArray()) + ".";
            _deadSummary.text = head;
            _deadTable.SetRows(rows);
            _context?.Logger.Info("The Big Board dead hours: " + tname + " over "
                + days + " days, dead-open: " + string.Join(",", deadOpen.ToArray()));
        }
        catch (Exception ex)
        {
            _deadSummary.text = "check failed: " + ex.Message;
            _context?.Logger.Warn("The Big Board dead hours render failed: " + ex.Message);
        }
    }

    // ---------- formatting helpers (also used by the table) ----------

    public static string FormatMoney(float v)
    {
        try { return v.ToShortCurrencyFormat(); }
        catch { return v.ToString("N0"); }
    }

    static string ItemLabel(string itemId)
    {
        try
        {
            string s = Helpers.LocalizationHelper.GetItemLabel(itemId, 1).ToString();
            return string.IsNullOrWhiteSpace(s) ? itemId : s;
        }
        catch { return itemId; }
    }

    public static string LocalizeOrClean(string key, string prefix)
    {
        if (string.IsNullOrEmpty(key)) return "-";
        try
        {
            if (Localizor.LocalizorManager.IsLocalizedKey(key))
            {
                string s = Localizor.LocalizorManager.GetLocalization(key, null);
                if (!string.IsNullOrWhiteSpace(s) && !string.Equals(s, key, StringComparison.Ordinal))
                    return s;
            }
        }
        catch { }
        string t = key;
        if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            t = t.Substring(prefix.Length);
        t = t.Replace("_", " ").Trim();
        if (t.Length == 0) return key;
        return char.ToUpperInvariant(t[0]) + t.Substring(1);
    }

}
