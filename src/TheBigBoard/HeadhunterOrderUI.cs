using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BAModAPI;
using Entities;
using Localizor;
using static TheBigBoard.DataGrid;

namespace TheBigBoard;

/// <summary>Per headhunter job breaker screen. The copy/recruit flow reserves
/// a free plan per combo and presets skill + headcount, but the checkboxes
/// (deal breakers) depend on THAT headhunter's Recruitment Points, so the
/// user ticks them here: costs and the over-budget rule mirror the game's
/// own recruiting tab. Confirm starts the plan; closing skips the combo.
/// Jobs run one screen after another; onDone gets the combined notes.</summary>
public static class HeadhunterOrderUI
{
    static List<ScheduleCopy.BreakerOrder> _queue;
    static int _idx;
    static ModContext _context;
    static GameObject _page;
    static Action<string> _onDone;
    static readonly List<string> _notes = new List<string>();

    static GameObject _popup;
    static TMPro.TMP_Text _titleText;
    static TMPro.TMP_Text _infoText;
    static TMPro.TMP_Text _budgetText;
    static TMPro.TMP_Text _hintText;
    static Transform _rowsRoot;
    static readonly Dictionary<string, BreakerRow> _rowByType = new Dictionary<string, BreakerRow>();
    static readonly HashSet<string> _toggled = new HashSet<string>();
    static int _usedPoints;
    static int _availPoints;

    sealed class BreakerRow
    {
        public string type;
        public Image bg;
        public TMPro.TMP_Text label;
    }

    public static void StartOrders(List<ScheduleCopy.BreakerOrder> orders, ModContext context,
        GameObject page, Action<string> onDone)
    {
        _queue = orders != null ? new List<ScheduleCopy.BreakerOrder>(orders) : new List<ScheduleCopy.BreakerOrder>();
        _idx = 0;
        _context = context;
        _page = page;
        _onDone = onDone;
        _notes.Clear();
        if (_queue.Count == 0)
        {
            Finish();
            return;
        }
        ShowCurrent();
    }

    static void Finish()
    {
        try { if (_popup != null) UnityEngine.Object.Destroy(_popup); }
        catch { }
        _popup = null;
        string summary = string.Join(" ", _notes.ToArray());
        var done = _onDone;
        _onDone = null;
        try { if (done != null) done(summary); }
        catch (Exception ex) { _context?.Logger.Warn("The Big Board order summary failed: " + ex.Message); }
    }

    static ScheduleCopy.BreakerOrder Current()
    {
        if (_queue == null || _idx < 0 || _idx >= _queue.Count) return null;
        return _queue[_idx];
    }

    static void Advance(bool started)
    {
        _idx++;
        if (_idx >= _queue.Count)
        {
            Finish();
            return;
        }
        ShowCurrent();
    }

    static void ShowCurrent()
    {
        var order = Current();
        if (order == null || order.plan == null) { Advance(false); return; }
        if (_popup == null) BuildPopup();
        _popup.transform.SetAsLastSibling();
        _popup.SetActive(true);
        RenderOrder(order);
    }

    static void BuildPopup()
    {
        _popup = new GameObject("BizOrderPopup");
        _popup.transform.SetParent(_page.transform, false);
        Stretch(_popup);
        var dim = _popup.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.72f);
        dim.raycastTarget = true;

        var cardGO = new GameObject("BizOrderCard");
        cardGO.transform.SetParent(_popup.transform, false);
        Anchor(cardGO, 0f, 0f, 1f, 1f, 140f, 80f, -140f, -80f);
        var cardBg = cardGO.AddComponent<Image>();
        cardBg.color = new Color(0.10f, 0.12f, 0.15f, 1f);
        var cardOutline = cardGO.AddComponent<Outline>();
        cardOutline.effectColor = new Color(0.32f, 0.36f, 0.42f, 1f);
        cardOutline.effectDistance = new Vector2(2f, 2f);

        _titleText = Text("BizOrderTitle", "", 34,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, Color.white);
        _titleText.transform.SetParent(cardGO.transform, false);
        Anchor(_titleText.gameObject, 0f, 1f, 1f, 1f, 40f, -100f, -300f, -30f);

        var closeGO = new GameObject("BizOrderClose");
        closeGO.transform.SetParent(cardGO.transform, false);
        Anchor(closeGO, 1f, 1f, 1f, 1f, -260f, -94f, -40f, -36f);
        var closeBg = closeGO.AddComponent<Image>();
        closeBg.color = BtnOff;
        var closeBtn = closeGO.AddComponent<Button>();
        closeBtn.targetGraphic = closeBg;
        var closeLabel = Text("BizOrderCloseLabel", "SKIP", 28,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, InkLight);
        closeLabel.transform.SetParent(closeGO.transform, false);
        Stretch(closeLabel.gameObject);
        closeBtn.onClick.AddListener(() => SkipCurrent());

        _infoText = Text("BizOrderInfo", "", 24,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, InkGrey);
        _infoText.transform.SetParent(cardGO.transform, false);
        Anchor(_infoText.gameObject, 0f, 1f, 1f, 1f, 40f, -150f, -40f, -106f);
        _infoText.textWrappingMode = TMPro.TextWrappingModes.Normal;

        _budgetText = Text("BizOrderBudget", "", 24,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, Accent);
        _budgetText.transform.SetParent(cardGO.transform, false);
        Anchor(_budgetText.gameObject, 0f, 1f, 1f, 1f, 40f, -196f, -40f, -152f);

        var scrollGO = new GameObject("BizOrderScroll");
        scrollGO.transform.SetParent(cardGO.transform, false);
        Anchor(scrollGO, 0f, 0f, 1f, 1f, 40f, 100f, -40f, -204f);
        var scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewportGO = new GameObject("BizOrderViewport");
        viewportGO.transform.SetParent(scrollGO.transform, false);
        Stretch(viewportGO);
        viewportGO.AddComponent<RectMask2D>();
        var contentGO = new GameObject("BizOrderRows");
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
        list.padding = new RectOffset(0, 8, 0, 8);
        list.childControlWidth = true;
        list.childControlHeight = true;
        list.childForceExpandWidth = true;
        list.childForceExpandHeight = false;
        scroll.viewport = viewportGO.GetComponent<RectTransform>();
        scroll.content = contentRect;
        _rowsRoot = contentGO.transform;
        Warmup(_rowsRoot);

        _hintText = Text("BizOrderHint", "", 22,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, InkGrey);
        _hintText.transform.SetParent(cardGO.transform, false);
        Anchor(_hintText.gameObject, 0f, 0f, 1f, 0f, 40f, 56f, -300f, 8f);
        _hintText.textWrappingMode = TMPro.TextWrappingModes.Normal;

        var confirmGO = new GameObject("BizOrderConfirm");
        confirmGO.transform.SetParent(cardGO.transform, false);
        Anchor(confirmGO, 1f, 0f, 1f, 0f, -260f, 16f, -40f, 84f);
        var confirmBg = confirmGO.AddComponent<Image>();
        confirmBg.color = Accent;
        var confirmBtn = confirmGO.AddComponent<Button>();
        confirmBtn.targetGraphic = confirmBg;
        var confirmLabel = Text("BizOrderConfirmLabel", "START SEARCH", 28,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, Color.white);
        confirmLabel.transform.SetParent(confirmGO.transform, false);
        Stretch(confirmLabel.gameObject);
        confirmBtn.onClick.AddListener(() => ConfirmCurrent());
    }

    static void RenderOrder(ScheduleCopy.BreakerOrder order)
    {
        ClearRows(_rowsRoot);
        Warmup(_rowsRoot);
        _rowByType.Clear();
        _toggled.Clear();
        _usedPoints = 0;
        _availPoints = 0;
        string hhName = "?";
        int hhSkill = 0;
        try
        {
            var inst = order.plan.HeadhunterInstance;
            if (inst != null && inst.characterData != null) hhName = inst.characterData.name ?? "?";
            hhSkill = Mathf.RoundToInt(order.plan.HeadhunterSkillValue);
        }
        catch { }
        try { _availPoints = order.plan.AvailableDealBreakersPoints; }
        catch { _availPoints = hhSkill; }
        string skillName = order.skill ?? "?";
        try { skillName = BusinessesPanel.LocalizeOrClean(order.skill, "ba:skill_"); }
        catch { }
        if (_titleText != null)
            _titleText.text = "ORDER " + (_idx + 1) + "/" + _queue.Count + ": " + skillName + " x" + order.count;
        if (_infoText != null)
            _infoText.text = "Headhunter: " + hhName + " (" + hhSkill + "%)  |  Suggested: " +
                (order.hours == "part" ? "PART-TIME" : order.hours == "full" ? "FULL-TIME" : "no hours breaker");
        // Fresh start: stale plan breakers are ignored. Hours first:
        // the part/full suggestion is ticked before anything else, then
        // remaining points are spent bottom-up (day/shift breakers at the
        // list bottom first) until the budget is full. Nothing above
        // budget is ever ticked.
        string hint = "";
        string suggested = null;
        var displayOrder = new List<string>();
        try
        {
            if (order.hours == "part")
                suggested = ScheduleCopy.BreakerFullTime;
            else if (order.hours == "full")
                suggested = ScheduleCopy.BreakerPartTime;
            var allGroups = Buildings.Office.Headquarters.HeadhunterHelper.GetDealBreakersForSkill(order.skill);
            if (allGroups != null)
                foreach (var grp in allGroups)
                {
                    if (grp == null) continue;
                    foreach (var bt in grp)
                    {
                        if (string.IsNullOrEmpty(bt)) continue;
                        if (!displayOrder.Contains(bt)) displayOrder.Add(bt);
                    }
                }
        }
        catch { suggested = null; }
        RecalcUsed();
        int hoursTicked = 0;
        try
        {
            if (suggested != null)
            {
                if (Fits(suggested))
                {
                    _toggled.Add(suggested);
                    _usedPoints += CostOf(suggested);
                    hoursTicked = 1;
                }
                else
                    hint = "Suggested " + (order.hours == "part" ? "Part Time" : "Full Time") +
                        " breaker did not fit this headhunter's points.";
            }
        }
        catch { }
        int flexTicked = 0;
        try
        {
            for (int di = displayOrder.Count - 1; di >= 0; di--)
            {
                string bt = displayOrder[di];
                if (_toggled.Contains(bt)) continue;
                if (!Fits(bt)) continue;
                _toggled.Add(bt);
                _usedPoints += CostOf(bt);
                flexTicked++;
            }
        }
        catch { }
        RecalcUsed();
        try
        {
            int scost = suggested != null ? CostOf(suggested) : 0;
            _context?.Logger.Info("The Big Board order " + SkillName(order.skill) + " x" + order.count +
                ": HH skill " + hhSkill + "% = " + _availPoints + " RP, used " + _usedPoints +
                (suggested != null ? ", suggested costs " + scost + " RP" : ", no suggestion") +
                ", hours ticked: " + hoursTicked + ", bottom-up auto-ticked: " + flexTicked);
        }
        catch { }
        RecalcUsed();
        if (_hintText != null) _hintText.text = hint;
        UpdateBudget();
        // Breaker rows, grouped like the game's own tab.
        try
        {
            var groups = Buildings.Office.Headquarters.HeadhunterHelper.GetDealBreakersForSkill(order.skill);
            if (groups == null || groups.Length == 0)
            {
                var t = Text("BizOrderEmpty", "No deal breakers for this skill.", 24,
                    TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Top, InkGrey);
                t.transform.SetParent(_rowsRoot, false);
                SizeFor(t, 1);
                return;
            }
            foreach (var group in groups)
            {
                if (group == null) continue;
                foreach (var type in group)
                {
                    if (string.IsNullOrEmpty(type)) continue;
                    AddBreakerRow(type);
                }
            }
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board breaker list failed: " + ex.Message);
        }
    }

    static bool Fits(string type)
    {
        try
        {
            if (_toggled.Contains(type)) return true;
            int cost = CostOf(type);
            return _usedPoints + cost <= _availPoints;
        }
        catch { return false; }
    }

    static int CostOf(string type)
    {
        try { return Buildings.Office.Headquarters.HeadhunterHelper.GetData(type).recruitmentPointCost; }
        catch { return 0; }
    }

    static void RecalcUsed()
    {
        _usedPoints = 0;
        foreach (var t in _toggled) _usedPoints += CostOf(t);
    }

    static void UpdateBudget()
    {
        if (_budgetText == null) return;
        _budgetText.text = "Recruitment Points: " + _usedPoints + " / " + _availPoints + " used";
        _budgetText.color = _usedPoints > _availPoints ? LossRed : Accent;
    }

    static void AddBreakerRow(string type)
    {
        var rowGO = new GameObject("BizOrderRow");
        rowGO.transform.SetParent(_rowsRoot, false);
        var bg = rowGO.AddComponent<Image>();
        bool on = _toggled.Contains(type);
        bg.color = on ? new Color(0.13f, 0.35f, 0.38f, 1f) : RowBgAlt;
        var size = rowGO.AddComponent<LayoutElement>();
        size.minHeight = 64f;
        var btn = rowGO.AddComponent<Button>();
        btn.targetGraphic = bg;
        string label = BreakerLabel(type);
        var tmp = Text("BizOrderRowLabel", (on ? "[x] " : "[  ] ") + label, 24,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle,
            on ? Color.white : InkLight);
        tmp.transform.SetParent(rowGO.transform, false);
        Anchor(tmp.gameObject, 0f, 0f, 1f, 1f, 20f, 6f, -20f, -6f);
        tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        tmp.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        var row = new BreakerRow { type = type, bg = bg, label = tmp };
        _rowByType[type] = row;
        string captured = type;
        btn.onClick.AddListener(() => ToggleRow(captured));
    }

    static string BreakerLabel(string type)
    {
        // The game's own labels name the EXCLUDED type, which reads
        // backwards (ticking "Full Time" hires part-timers). Spell out
        // the effect for the two hours breakers so nobody misreads it.
        string effect = "";
        try
        {
            if (type == ScheduleCopy.BreakerFullTime) effect = " \u2192 part-time only";
            else if (type == ScheduleCopy.BreakerPartTime) effect = " \u2192 full-time only";
        }
        catch { }
        try
        {
            int cost = CostOf(type);
            string s = null;
            try
            {
                s = "headhunter_deal_breaker_group_label".Localize(
                    new { dealBreakerType = type, rpCost = cost }).ToString();
            }
            catch { s = null; }
            if (!string.IsNullOrWhiteSpace(s)) return s + effect;
        }
        catch { }
        return BusinessesPanel.LocalizeOrClean(type, "ba:headhuntersdealbreaker_") + " (" + CostOf(type) + " RP)" + effect;
    }

    static void ToggleRow(string type)
    {
        try
        {
            BreakerRow row;
            if (!_rowByType.TryGetValue(type, out row) || row == null) return;
            if (_toggled.Contains(type))
            {
                _toggled.Remove(type);
                if (_hintText != null && _hintText.text.StartsWith("Suggested breaker exceeds"))
                    _hintText.text = "";
            }
            else
            {
                if (!Fits(type))
                {
                    if (_hintText != null)
                        _hintText.text = "Not enough Recruitment Points for that breaker (" +
                            _availPoints + " max).";
                    return;
                }
                _toggled.Add(type);
            }
            RecalcUsed();
            UpdateBudget();
            bool on = _toggled.Contains(type);
            row.bg.color = on ? new Color(0.13f, 0.35f, 0.38f, 1f) : RowBgAlt;
            string label = BreakerLabel(type);
            row.label.text = (on ? "[x] " : "[  ] ") + label;
            row.label.color = on ? Color.white : InkLight;
            if (_hintText != null && _hintText.text == "Not enough Recruitment Points for that breaker.")
                _hintText.text = "";
        }
        catch { }
    }

    static void SizeFor(TMPro.TMP_Text t, int lines)
    {
        var size = t.gameObject.AddComponent<LayoutElement>();
        size.minHeight = 40f * lines;
    }

    static void SkipCurrent()
    {
        var order = Current();
        if (order != null)
            _notes.Add("Skipped " + SkillName(order.skill) + " x" + order.count + " (no order placed).");
        Advance(false);
    }

    static void ConfirmCurrent()
    {
        var order = Current();
        if (order == null || order.plan == null) { Advance(false); return; }
        try
        {
            var plan = order.plan;
            EmployeeInstance inst = null;
            try { inst = plan.HeadhunterInstance; } catch { }
            if (inst == null)
            {
                _notes.Add("No headhunter assigned for " + SkillName(order.skill) + " x" + order.count + " (skipped).");
                Advance(false);
                return;
            }
            if (plan.isRecruiting)
            {
                _notes.Add(SkillName(order.skill) + " x" + order.count + " already recruiting (left as is).");
                Advance(false);
                return;
            }
            plan.dealBreakerTypes = new List<string>(_toggled);
            plan.skillRecruiting = order.skill;
            plan.amountOfCandidatesToRecruitPreference = order.count;
            plan.StartRecruiting();
            try { SaveGameManager.MarkChange(); } catch { }
            string hours = order.hours == "part" ? " (part-time)" : order.hours == "full" ? " (full-time)" : "";
            _notes.Add("Headhunter: recruiting " + SkillName(order.skill) + " x" + order.count + hours + ".");
            _context?.Logger.Info("The Big Board headhunter order started: " + SkillName(order.skill) +
                " x" + order.count + hours + ".");
        }
        catch (Exception ex)
        {
            _notes.Add("Order failed for " + SkillName(order.skill) + ": " + ex.Message);
        }
        Advance(true);
    }

    static string SkillName(string skill)
    {
        try { return BusinessesPanel.LocalizeOrClean(skill, "ba:skill_"); }
        catch { return skill ?? "?"; }
    }
}
