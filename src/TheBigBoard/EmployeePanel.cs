using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BAModAPI;
using Buildings.Office.Headquarters;
using Entities;
using Entities.Employee.JobDemands;
using Entities.Employee.JobDemands.Requirements;
using static TheBigBoard.DataGrid;

namespace TheBigBoard;

/// <summary>
/// TAB 4 - EMPLOYEE. Top: a single-shot ASSIGN button. One press assigns
/// all hired-but-unassigned employees to an HR plan (vanilla recipe:
/// plan.assignedEmployees + employee.assignedHrManagerPlanId, same as the
/// BizMan HR UI). No auto mode, no periodic sweep. Plan choice: the employee's max required insurance tier
/// (from their HasHealthInsurance demands) picks the lowest satisfying tier;
/// no match falls back to the highest tier with capacity (gold down).
/// Employees without an insurance demand go to the lowest tier. Only plans
/// with a live HR manager and free capacity are used; plans without a
/// manager are skipped entirely. Warnings: employees with unfulfilled
/// demands (same check as the game's own quit-warning logic:
/// JobDemand.Fulfilled(emp, null), sorted by impact).
/// </summary>
public static class EmployeePanel
{
    static ModContext _context;
    static GameObject _page;
    static bool _tabVisible;
    static int _lastDay = -1;
    static string _status = "";
    static int _sweepAssigned;
    static int _sweepSkipped;

    static GameObject _assignBtnGO;
    static Image _assignBtnBg;
    static TMPro.TMP_Text _assignBtnLabel;
    static TMPro.TMP_Text _statusText;
    static TMPro.TMP_Text _countsText;
    static TMPro.TMP_Text _warnHeaderText;
    static Transform _rowsRoot;

    // ---- schedule copy (BASE -> TARGET) ----
    static readonly List<BuildingRegistration> _shops = new List<BuildingRegistration>();
    static BuildingRegistration _copyBase;
    static string _copyResult = "";
    static TMPro.TMP_Text _baseLabel;
    static GameObject _shopPopup;
    static TMPro.TMP_Text _shopPopupTitle;
    static ShopPickTable _shopTable;
    static TMPro.TMP_Text _copyResultText;

    static readonly List<WarnRow> _warnings = new List<WarnRow>();

    sealed class WarnRow
    {
        public string name;
        public string business;
        public string demand;
        public int extra;
        public float sat;
    }

    // Palette, Text/Rect/Anchor, Warmup, scroll/header/row helpers: DataGrid
    // (using static TheBigBoard.DataGrid above). Only employee logic stays here.
    // No auto mode: the button below runs one manual sweep per press.

    public static void BuildPage(GameObject page, ModContext context)
    {
        _page = page;
        _context = context;
        _tabVisible = false;
        _lastDay = -1;

        var leftGO = new GameObject("EmployeeLeft");
        leftGO.transform.SetParent(page.transform, false);
        Anchor(leftGO, 0f, 0f, 0.30f, 1f, 0f, 0f, -24f, 0f);

        _assignBtnGO = new GameObject("EmployeeAssignBtn");
        _assignBtnGO.transform.SetParent(leftGO.transform, false);
        Anchor(_assignBtnGO, 0f, 1f, 1f, 1f, 0f, -96f, 0f, 0f);
        _assignBtnBg = _assignBtnGO.AddComponent<Image>();
        _assignBtnBg.color = Accent;
        var assignBtn = _assignBtnGO.AddComponent<Button>();
        assignBtn.targetGraphic = _assignBtnBg;
        _assignBtnLabel = Text("EmployeeAssignLabel", "ASSIGN MISSING TO HR", 28,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, Color.white);
        _assignBtnLabel.transform.SetParent(_assignBtnGO.transform, false);
        Anchor(_assignBtnLabel.gameObject, 0f, 0f, 1f, 1f, 12f, 6f, -12f, -6f);
        assignBtn.onClick.AddListener(AssignMissingNow);

        _statusText = Text("EmployeeStatus", "Press the button to assign unassigned employees.", 26,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Top, InkGrey);
        _statusText.transform.SetParent(leftGO.transform, false);
        Anchor(_statusText.gameObject, 0f, 1f, 1f, 1f, 0f, -330f, 0f, -106f);
        _statusText.textWrappingMode = TMPro.TextWrappingModes.Normal;

        _countsText = Text("EmployeeCounts", "", 26,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Top, InkGrey);
        _countsText.transform.SetParent(leftGO.transform, false);
        Anchor(_countsText.gameObject, 0f, 1f, 1f, 1f, 0f, -560f, 0f, -340f);
        _countsText.textWrappingMode = TMPro.TextWrappingModes.Normal;

        BuildCopySection(leftGO);

        var refreshGO = new GameObject("EmployeeRefresh");
        refreshGO.transform.SetParent(leftGO.transform, false);
        Anchor(refreshGO, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 78f);
        var refreshBg = refreshGO.AddComponent<Image>();
        refreshBg.color = BtnOff;
        var refreshBtn = refreshGO.AddComponent<Button>();
        refreshBtn.targetGraphic = refreshBg;
        var refreshLabel = Text("EmployeeRefreshLabel", "REFRESH", 30,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, InkLight);
        refreshLabel.transform.SetParent(refreshGO.transform, false);
        Stretch(refreshLabel.gameObject);
        refreshBtn.onClick.AddListener(() => Refresh());

        var rightGO = new GameObject("EmployeeRight");
        rightGO.transform.SetParent(page.transform, false);
        Anchor(rightGO, 0.30f, 0f, 1f, 1f, 24f, 0f, 0f, 0f);

        _warnHeaderText = Text("EmployeeWarnHeader", "WARNINGS", 40,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, Color.white);
        _warnHeaderText.transform.SetParent(rightGO.transform, false);
        Anchor(_warnHeaderText.gameObject, 0f, 1f, 1f, 1f, 0f, -70f, 0f, 0f);

        var headRow = AddFlexHeader(rightGO, "EmployeeGridHeader", -130f, -78f, 40, 40, 20f);
        AddFlexHeadCell(headRow, "EMPLOYEE", 2f, 200f, TMPro.HorizontalAlignmentOptions.Center);
        AddFlexHeadCell(headRow, "BUSINESS", 2f, 200f, TMPro.HorizontalAlignmentOptions.Center);
        AddFlexHeadCell(headRow, "UNMET DEMAND", 2f, 200f, TMPro.HorizontalAlignmentOptions.Center);
        AddFlexHeadCell(headRow, "SAT", 0f, 130f, TMPro.HorizontalAlignmentOptions.Center);

        _rowsRoot = AddRowsScroll(rightGO,
            "EmployeeRowsScroll", "EmployeeViewport", "EmployeeRows", "EmployeeScrollbar",
            -138f);
    }

    public static void SetVisible(bool visible)
    {
        _tabVisible = visible;
    }

    /// <summary>~2s cadence from the bootstrap. No auto sweep anymore;
    /// refreshes the open tab on a new day.</summary>
    public static void Tick()
    {
        try
        {
            var save = SaveGameManager.Current;
            if (save == null) return;
            if (!_tabVisible || _page == null) return;
            int day = save.Day;
            if (day != _lastDay)
            {
                _lastDay = day;
                Refresh();
            }
            else
            {
                RefreshStatusLabel();
            }
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board employee tick failed: " + ex.Message);
        }
    }

    public static void Refresh()
    {
        try
        {
            var save = SaveGameManager.Current;
            if (save != null) _lastDay = save.Day;
            RebuildShops();
            if (_shopPopup != null && _shopPopup.activeSelf) RenderShopPopup();
            ComputeWarnings();
            RenderRows();
            RefreshCounts();
            RefreshStatusLabel();
            UpdateCopyLabels();
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board employee refresh failed: " + ex.Message);
        }
    }

    static void AssignMissingNow()
    {
        try
        {
            SweepAutoAssign();
            _context?.Logger.Info("The Big Board manual HR assign: " + _status);
            RefreshCounts();
            RefreshStatusLabel();
        }
        catch { }
    }

    static void RefreshStatusLabel()
    {
        if (_statusText == null) return;
        try
        {
            _statusText.text = string.IsNullOrEmpty(_status)
                ? "Press the button to assign unassigned employees."
                : _status;
        }
        catch { }
    }

    // ---------- manual assign (single-shot) ----------

    static int TierRank(HealthInsurancePlanType t)
    {
        try
        {
            string s = t.ToString();
            if (s == "Gold") return 3;
            if (s == "Silver") return 2;
            if (s == "Bronze") return 1;
        }
        catch { }
        return 0;
    }

    static int PlanTier(HrManagerPlan plan)
    {
        try
        {
            if (plan != null && plan.healthInsurancePlan != null)
                return TierRank(plan.healthInsurancePlan.planType);
        }
        catch { }
        return 0;
    }

    static string TierName(HrManagerPlan plan)
    {
        try
        {
            if (plan != null && plan.healthInsurancePlan != null)
                return plan.healthInsurancePlan.planType.ToString().ToUpperInvariant();
        }
        catch { }
        return "-";
    }

    static int RequiredTier(EmployeeInstance emp)
    {
        int req = 0;
        try
        {
            if (emp == null || emp.demands == null) return 0;
            foreach (var d in emp.demands)
            {
                if (string.IsNullOrEmpty(d)) continue;
                JobDemand jd = null;
                try { jd = JobDemandHelper.GetByName(d); }
                catch { continue; }
                var hi = jd as HasHealthInsurance;
                if (hi == null) continue;
                int r = 0;
                try { r = TierRank(hi.HealthInsurancePlan); }
                catch { continue; }
                if (r > req) req = r;
            }
        }
        catch { }
        return req;
    }

    static void SweepAutoAssign()
    {
        _sweepAssigned = 0;
        _sweepSkipped = 0;
        try
        {
            var save = SaveGameManager.Current;
            if (save == null || save.EmployeeInstances == null) return;
            var plans = save.hrManagerPlans;
            if (plans == null || plans.Count == 0)
            {
                _status = "No HR plan exists. Create one in BizMan first.";
                return;
            }
            var hrIds = new HashSet<string>();
            var live = new List<HrManagerPlan>();
            foreach (var p in plans)
            {
                if (p == null) continue;
                if (!string.IsNullOrEmpty(p.assignedEmployeeId)) hrIds.Add(p.assignedEmployeeId);
                EmployeeInstance mgr = null;
                try { mgr = p.HrManagerInstance; }
                catch { mgr = null; }
                if (mgr == null) continue; // no HR staffer: leave untouched
                live.Add(p);
            }
            if (live.Count == 0)
            {
                _status = "HR plan exists but no HR staffer assigned. Nothing assigned.";
                return;
            }
            foreach (var emp in save.EmployeeInstances)
            {
                if (emp == null || emp.IsCandidate || string.IsNullOrEmpty(emp.id)) continue;
                if (!string.IsNullOrEmpty(emp.assignedHrManagerPlanId)) continue;
                if (hrIds.Contains(emp.id)) continue; // is an HR manager itself
                HrManagerPlan pick = PickPlan(emp, live);
                if (pick == null)
                {
                    _sweepSkipped++;
                    continue;
                }
                try
                {
                    if (!pick.assignedEmployees.Contains(emp.id))
                        pick.assignedEmployees.Add(emp.id);
                    emp.assignedHrManagerPlanId = pick.id;
                    _sweepAssigned++;
                    _context?.Logger.Info("The Big Board HR assign: " + EmpName(emp) + " -> " +
                        HrName(pick) + " (" + TierName(pick) + ").");
                }
                catch
                {
                    _sweepSkipped++;
                }
            }
            _status = "Last sweep: +" + _sweepAssigned + " assigned, " + _sweepSkipped +
                " skipped (full). Plans: " + live.Count + ".";
        }
        catch (Exception ex)
        {
            _status = "Sweep failed: " + ex.Message;
        }
    }

    static HrManagerPlan PickPlan(EmployeeInstance emp, List<HrManagerPlan> live)
    {
        var free = new List<HrManagerPlan>();
        foreach (var p in live)
        {
            try
            {
                if (p.NumberOfAssignedEmployees < p.MaxEmployees) free.Add(p);
            }
            catch { }
        }
        if (free.Count == 0) return null;
        int req = RequiredTier(emp);
        if (req > 0)
        {
            // Lowest tier that still satisfies the demand...
            HrManagerPlan best = null;
            int bestTier = int.MaxValue;
            foreach (var p in free)
            {
                int t = PlanTier(p);
                if (t >= req && t < bestTier) { best = p; bestTier = t; }
            }
            if (best != null) return best;
            // ...else the highest tier available (gold down).
            HrManagerPlan top = null;
            int topTier = -1;
            foreach (var p in free)
            {
                int t = PlanTier(p);
                if (t > topTier) { top = p; topTier = t; }
            }
            return top;
        }
        // No insurance demand: cheapest coverage first (none/bronze up).
        HrManagerPlan cheap = null;
        int cheapTier = int.MaxValue;
        foreach (var p in free)
        {
            int t = PlanTier(p);
            if (t < cheapTier) { cheap = p; cheapTier = t; }
        }
        return cheap;
    }

    static string EmpName(EmployeeInstance emp)
    {
        try { return emp != null && emp.characterData != null ? emp.characterData.name ?? emp.id : "?"; }
        catch { return "?"; }
    }

    static string HrName(HrManagerPlan plan)
    {
        try
        {
            var mgr = plan.HrManagerInstance;
            if (mgr != null && mgr.characterData != null && !string.IsNullOrEmpty(mgr.characterData.name))
                return mgr.characterData.name;
        }
        catch { }
        return "HR plan";
    }

    // ---------- warnings ----------

    static void ComputeWarnings()
    {
        _warnings.Clear();
        try
        {
            var save = SaveGameManager.Current;
            if (save == null || save.EmployeeInstances == null) return;
            var byAddr = new Dictionary<string, string>();
            try
            {
                var own = Helpers.BuildingHelper.GetPlayerBuildingRegistrations(null, null);
                if (own != null)
                    foreach (var reg in own)
                    {
                        if (reg == null) continue;
                        byAddr[(reg.StreetName != null ? reg.StreetName : "") + "#" + reg.StreetNumber] =
                            !string.IsNullOrEmpty(reg.BusinessName)
                                ? reg.BusinessName
                                : (reg.StreetName + " " + reg.StreetNumber);
                    }
            }
            catch { }
            foreach (var emp in save.EmployeeInstances)
            {
                if (emp == null || emp.IsCandidate) continue;
                List<JobDemand> unmet = null;
                try
                {
                    if (emp.demands == null) continue;
                    foreach (var d in emp.demands)
                    {
                        if (string.IsNullOrEmpty(d)) continue;
                        JobDemand jd = null;
                        try { jd = JobDemandHelper.GetByName(d); }
                        catch { continue; }
                        if (jd == null) continue;
                        bool ok = true;
                        try { ok = jd.Fulfilled(emp, null); }
                        catch { continue; }
                        if (!ok)
                        {
                            if (unmet == null) unmet = new List<JobDemand>();
                            unmet.Add(jd);
                        }
                    }
                }
                catch { continue; }
                if (unmet == null || unmet.Count == 0) continue;
                unmet.Sort((a, b) => ImpactOf(b).CompareTo(ImpactOf(a)));
                string label = DemandLabel(unmet[0]);
                string biz = "?";
                try
                {
                    if (emp.assignedAddress != null && emp.assignedAddress.streetName != null)
                    {
                        string b;
                        string key = emp.assignedAddress.streetName + "#" + emp.assignedAddress.streetNumber;
                        biz = byAddr.TryGetValue(key, out b) ? b : emp.assignedAddress.streetName + " " + emp.assignedAddress.streetNumber;
                    }
                    else
                    {
                        biz = "unassigned";
                    }
                }
                catch { }
                float sat = 0f;
                try { sat = emp.satisfaction; }
                catch { }
                _warnings.Add(new WarnRow
                {
                    name = EmpName(emp),
                    business = biz,
                    demand = label,
                    extra = unmet.Count - 1,
                    sat = sat
                });
            }
            _warnings.Sort((a, b) => a.sat.CompareTo(b.sat));
        }
        catch { }
    }

    static float ImpactOf(JobDemand jd)
    {
        try { return jd != null ? jd.Impact : 0f; }
        catch { return 0f; }
    }

    static string DemandLabel(JobDemand jd)
    {
        try
        {
            if (jd == null) return "?";
            string s = jd.GetLocalization();
            return string.IsNullOrWhiteSpace(s) ? jd.demandName : s;
        }
        catch
        {
            try { return jd.demandName; }
            catch { return "?"; }
        }
    }

    static void RefreshCounts()
    {
        if (_countsText == null) return;
        try
        {
            var save = SaveGameManager.Current;
            if (save == null) return;
            int hired = 0;
            int unassigned = 0;
            if (save.EmployeeInstances != null)
            {
                foreach (var emp in save.EmployeeInstances)
                {
                    if (emp == null || emp.IsCandidate) continue;
                    hired++;
                    if (string.IsNullOrEmpty(emp.assignedHrManagerPlanId)) unassigned++;
                }
            }
            var parts = new List<string>();
            if (save.hrManagerPlans != null)
            {
                foreach (var p in save.hrManagerPlans)
                {
                    if (p == null) continue;
                    int n = 0, max = 0;
                    try { n = p.NumberOfAssignedEmployees; max = p.MaxEmployees; }
                    catch { }
                    parts.Add(TierName(p) + ": " + n + "/" + max);
                }
            }
            _countsText.text = "Employees: " + hired + " hired, " + unassigned + " without HR.\n" +
                "HR plans (" + parts.Count + "): " + (parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "-") + ".";
        }
        catch { }
    }

    static void RenderRows()
    {
        if (_rowsRoot == null) return;
        ClearRows(_rowsRoot);
        if (_warnHeaderText != null)
            _warnHeaderText.text = _warnings.Count == 0
                ? "WARNINGS (0) - ALL DEMANDS MET"
                : "WARNINGS (" + _warnings.Count + ") - UNMET DEMANDS";
        Warmup(_rowsRoot);
        if (_warnings.Count == 0)
        {
            AddInfoRow(_rowsRoot, "No unmet demands. Employees are covered.");
            return;
        }
        int stripe = 0;
        foreach (var w in _warnings)
            AddWarnRow(w, stripe++ % 2 == 0 ? RowBg : RowBgAlt);
    }

    static void AddWarnRow(WarnRow w, Color bg)
    {
        var rowGO = AddFlexRow(_rowsRoot, "EmpWarnRow", bg, 40, 40, 20f);

        AddFlexCell(rowGO, w.name, 2f, 200f, TMPro.HorizontalAlignmentOptions.Left, InkLight);
        AddFlexCell(rowGO, w.business, 2f, 200f, TMPro.HorizontalAlignmentOptions.Left, InkLight);
        AddFlexCell(rowGO, w.extra > 0 ? (w.demand + " (+" + w.extra + ")") : w.demand, 2f, 200f,
            TMPro.HorizontalAlignmentOptions.Left, InkLight);
        Color satColor = w.sat >= 80f ? GoodGreen : w.sat >= 50f ? InkLight : BadRed;
        AddFlexCell(rowGO, w.sat.ToString("F0") + "%", 0f, 130f,
            TMPro.HorizontalAlignmentOptions.Right, satColor);
    }

    // ---------- schedule copy UI (BASE -> TARGET) ----------

    static void BuildCopySection(GameObject leftGO)
    {
        var header = Text("CopyHeader", "RECRUIT CREW (HEADHUNTER)", 26,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, InkGrey);
        header.transform.SetParent(leftGO.transform, false);
        Anchor(header.gameObject, 0f, 1f, 1f, 1f, 0f, -610f, 0f, -566f);

        _baseLabel = BuildShopButton(leftGO, "CopyBaseRow", "BASE", -620f, () => OpenShopPopup());
        BuildShopPopup();

        // Single action: the BASE crew's profile goes straight to the
        // headhunter. No TARGET needed; seating happens in Businesses.
        // Agency orders need a target address: order those from the
        // Businesses tab COPY instead.
        var recGO = new GameObject("RecruitButton");
        recGO.transform.SetParent(leftGO.transform, false);
        Anchor(recGO, 0f, 1f, 1f, 1f, 0f, -762f, 0f, -700f);
        var recBg = recGO.AddComponent<Image>();
        recBg.color = Accent;
        var recBtn = recGO.AddComponent<Button>();
        recBtn.targetGraphic = recBg;
        var recLabel = Text("RecruitButtonLabel", "RECRUIT CREW", 28,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, Color.white);
        recLabel.transform.SetParent(recGO.transform, false);
        Stretch(recLabel.gameObject);
        recBtn.onClick.AddListener(DoRecruit);

        _copyResultText = Text("CopyResult", "", 24,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Top, InkGrey);
        _copyResultText.transform.SetParent(leftGO.transform, false);
        Anchor(_copyResultText.gameObject, 0f, 0f, 1f, 1f, 0f, 90f, 0f, -770f);
        _copyResultText.textWrappingMode = TMPro.TextWrappingModes.Normal;

        RebuildShops();
        UpdateCopyLabels();
        try
        {
            var names = new List<string>();
            foreach (var s in _shops) names.Add(ScheduleCopy.ShopName(s));
            _context?.Logger.Info("The Big Board schedule copy ready: " + _shops.Count +
                " eligible shops (" + string.Join(", ", names.ToArray()) + ").");
        }
        catch { }
    }

    // Full-width tappable selector: opens the shop picker popup.
    static TMPro.TMP_Text BuildShopButton(GameObject parent, string name, string tag,
        float yTop, Action onClick)
    {
        float yBot = yTop - 64f;
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        Anchor(go, 0f, 1f, 1f, 1f, 0f, yBot, 0f, yTop);
        var bg = go.AddComponent<Image>();
        bg.color = BtnOff;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = bg;
        var label = Text(name + "Label", tag + ": -", 24,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, InkLight);
        label.transform.SetParent(go.transform, false);
        Anchor(label.gameObject, 0f, 0f, 1f, 1f, 14f, 6f, -14f, -6f);
        label.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        label.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        btn.onClick.AddListener(() => { try { onClick(); } catch { } });
        return label;
    }

    static string ShopKey(BuildingRegistration reg)
    {
        try { return (reg.StreetName ?? "") + "#" + reg.StreetNumber; }
        catch { return ""; }
    }

    static void RebuildShops()
    {
        string baseKey = _copyBase != null ? ShopKey(_copyBase) : null;
        _shops.Clear();
        try { _shops.AddRange(ScheduleCopy.PlayerShops()); }
        catch { }
        _copyBase = null;
        foreach (var s in _shops)
        {
            if (_copyBase == null && ShopKey(s) == baseKey) _copyBase = s;
        }
        if (_copyBase == null && _shops.Count > 0) _copyBase = _shops[0];
    }

    // ---------- shop picker popup (datagrid) ----------

    static void BuildShopPopup()
    {
        if (_page == null) return;
        // Dim layer: full-page VISUAL ONLY, darkens the tab behind so the
        // window pops. No click-to-close here: the scrollbar sits right at
        // the card edge, so grabs aimed at it often land on the dim and
        // used to kill the popup mid-scroll. CLOSE button is the way out.
        _shopPopup = new GameObject("ShopPickerPopup");
        _shopPopup.transform.SetParent(_page.transform, false);
        Stretch(_shopPopup);
        var dim = _shopPopup.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.72f);
        dim.raycastTarget = true;

        // Window card floating above the dim layer.
        var cardGO = new GameObject("ShopPickerCard");
        cardGO.transform.SetParent(_shopPopup.transform, false);
        Anchor(cardGO, 0f, 0f, 1f, 1f, 120f, 60f, -120f, -60f);
        var cardBg = cardGO.AddComponent<Image>();
        cardBg.color = new Color(0.10f, 0.12f, 0.15f, 1f);
        var cardOutline = cardGO.AddComponent<Outline>();
        cardOutline.effectColor = new Color(0.32f, 0.36f, 0.42f, 1f);
        cardOutline.effectDistance = new Vector2(2f, 2f);

        // Title bar: caption left, CLOSE right, separator line below.
        _shopPopupTitle = Text("ShopPickerTitle", "SELECT SHOP", 40,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, Color.white);
        _shopPopupTitle.transform.SetParent(cardGO.transform, false);
        Anchor(_shopPopupTitle.gameObject, 0f, 1f, 1f, 1f, 40f, -110f, -300f, -30f);

        var closeGO = new GameObject("ShopPickerClose");
        closeGO.transform.SetParent(cardGO.transform, false);
        Anchor(closeGO, 1f, 1f, 1f, 1f, -260f, -104f, -40f, -36f);
        var closeBg = closeGO.AddComponent<Image>();
        closeBg.color = BtnOff;
        var closeBtn = closeGO.AddComponent<Button>();
        closeBtn.targetGraphic = closeBg;
        var closeLabel = Text("ShopPickerCloseLabel", "CLOSE", 28,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, InkLight);
        closeLabel.transform.SetParent(closeGO.transform, false);
        Stretch(closeLabel.gameObject);
        closeBtn.onClick.AddListener(CloseShopPopup);

        var sepGO = new GameObject("ShopPickerSep");
        sepGO.transform.SetParent(cardGO.transform, false);
        Anchor(sepGO, 0f, 1f, 1f, 1f, 40f, -120f, -40f, -116f);
        var sepBg = sepGO.AddComponent<Image>();
        sepBg.color = new Color(0.32f, 0.36f, 0.42f, 1f);

        _shopTable = new ShopPickTable(PickShop);
        _shopTable.Init(_context);
        _shopTable.BuildHeader(cardGO, "ShopPickerHead", 0f, 0f, -184f, -130f);
        _shopTable.BuildRows(cardGO,
            "ShopPickerScroll", "ShopPickerViewport", "ShopPickerRows", "ShopPickerScrollbar",
            -194f);

        _shopPopup.SetActive(false);
    }

    static void OpenShopPopup()
    {
        try
        {
            if (_shopPopup == null) return;
            RebuildShops();
            RenderShopPopup();
            // Siblings added after the popup (right column) would paint
            // over the dim layer: bring the window back to front.
            _shopPopup.transform.SetAsLastSibling();
            _shopPopup.SetActive(true);
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board shop picker open failed: " + ex.Message);
        }
    }

    static void CloseShopPopup()
    {
        try { if (_shopPopup != null) _shopPopup.SetActive(false); }
        catch { }
    }

    static List<BuildingRegistration> PopupShops()
    {
        return new List<BuildingRegistration>(_shops);
    }

    static void RenderShopPopup()
    {
        if (_shopPopup == null || _shopTable == null) return;
        var list = PopupShops();
        if (_shopPopupTitle != null)
            _shopPopupTitle.text = "SELECT BASE SHOP (" + list.Count + ")";
        var rows = new List<ShopPickTable.Row>();
        var current = _copyBase;
        foreach (var shop in list)
        {
            if (shop == null) continue;
            int openDays, shifts, staff;
            ShopStats(shop, out openDays, out shifts, out staff);
            rows.Add(new ShopPickTable.Row
            {
                name = ScheduleCopy.ShopName(shop),
                type = ScheduleCopy.ShortType(shop.businessTypeName),
                openDays = openDays,
                shifts = shifts,
                staff = staff,
                selected = shop == current,
                reg = shop
            });
        }
        if (rows.Count == 0)
            _shopTable.ShowInfo("No eligible shops found.");
        else
            _shopTable.SetRows(rows);
    }

    static void ShopStats(BuildingRegistration shop, out int openDays, out int shifts, out int staff)
    {
        openDays = 0;
        shifts = 0;
        staff = 0;
        try
        {
            if (shop != null && shop.scheduleDays != null)
            {
                foreach (var day in shop.scheduleDays)
                {
                    if (day == null) continue;
                    if (day.isOpen) openDays++;
                    if (day.workShifts != null) shifts += day.workShifts.Count;
                }
            }
            var save = SaveGameManager.Current;
            if (save != null && save.EmployeeInstances != null && shop != null && shop.Address != null)
            {
                foreach (var emp in save.EmployeeInstances)
                {
                    if (emp == null || emp.IsCandidate) continue;
                    if (emp.assignedAddress != null && emp.assignedAddress.Equals(shop.Address))
                        staff++;
                }
            }
        }
        catch { }
    }

    static void PickShop(BuildingRegistration shop)
    {
        try
        {
            _copyBase = shop;
            CloseShopPopup();
            UpdateCopyLabels();
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board shop pick failed: " + ex.Message);
        }
    }

    static void UpdateCopyLabels()
    {
        try
        {
            if (_baseLabel != null)
                _baseLabel.text = "BASE: " + (_copyBase != null ? ScheduleCopy.ShopName(_copyBase) : "- tap to pick -");
            if (_copyResultText != null) _copyResultText.text = _copyResult;
        }
        catch { }
    }

    /// <summary>Recruit-only: the BASE crew's profile (skill + hour demand
    /// per head) goes straight to the headhunter. No TARGET needed; seating
    /// happens later in the Businesses tab. Agency orders need a target
    /// address: order those from the Businesses tab COPY instead.</summary>
    static void DoRecruit()
    {
        try
        {
            RebuildShops();
            if (_copyBase == null)
            {
                _copyResult = "Pick a BASE shop first.";
            }
            else
            {
                var batch = ScheduleCopy.RecruitCrew(_copyBase, _context);
                if (batch.blocked != null)
                {
                    _copyResult = batch.blocked;
                }
                else if (batch.orders.Count == 0)
                {
                    _copyResult = batch.summary + " " + string.Join(" ", batch.notes.ToArray());
                }
                else
                {
                    HeadhunterOrderUI.StartOrders(batch.orders, _context, _page, summary =>
                    {
                        _copyResult = batch.summary + " " +
                            string.Join(" ", batch.notes.ToArray()) + " " + summary;
                        UpdateCopyLabels();
                        try { RefreshCounts(); }
                        catch { }
                    });
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _copyResult = "Recruit failed: " + ex.Message;
        }
        UpdateCopyLabels();
        try { RefreshCounts(); }
        catch { }
    }
}
