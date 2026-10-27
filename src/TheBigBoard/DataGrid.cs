using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheBigBoard;

/// <summary>
/// Single base for every table in the mod. There is no ready-made DataGrid:
/// each panel used to hand-roll the same skeleton (header row outside the
/// scroll area, ScrollRect + Viewport + Content + permanent scrollbar,
/// zebra rows, info row), so all of it lives here now. Two layout modes:
/// ABSOLUTE (wide tables: columns anchored to exact pixels from the right
/// edge, name column fills the rest; headers and rows share the math so
/// they cannot drift) and FLEX (narrow tables: HorizontalLayoutGroup with
/// flexible/fixed LayoutElements). Panels pull the helpers in with
/// "using static TheBigBoard.DataGrid;" and keep only their own columns.
/// </summary>
public static class DataGrid
{
    // ---------- shared palette (identical values in every panel) ----------

    public static readonly Color HeaderBg = new Color(0.93f, 0.94f, 0.95f, 1f);
    public static readonly Color HeaderInk = new Color(0.35f, 0.38f, 0.42f, 1f);
    public static readonly Color RowBg = new Color(0.17f, 0.20f, 0.25f, 1f);
    public static readonly Color RowBgAlt = new Color(0.24f, 0.28f, 0.34f, 1f);
    public static readonly Color TotalBg = new Color(0.13f, 0.22f, 0.24f, 1f);
    public static readonly Color InkLight = new Color(0.93f, 0.94f, 0.95f, 1f);
    public static readonly Color InkGrey = new Color(0.60f, 0.64f, 0.69f, 1f);
    public static readonly Color Accent = new Color(0.10f, 0.55f, 0.55f, 1f);
    public static readonly Color ProfitGreen = new Color(0.25f, 0.75f, 0.35f, 1f);
    public static readonly Color GoodGreen = new Color(0.25f, 0.75f, 0.35f, 1f);
    public static readonly Color LossRed = new Color(0.95f, 0.35f, 0.30f, 1f);
    public static readonly Color WarnRed = new Color(0.95f, 0.35f, 0.30f, 1f);
    public static readonly Color BadRed = new Color(0.95f, 0.35f, 0.30f, 1f);
    public static readonly Color BtnIdle = new Color(0.20f, 0.23f, 0.28f, 1f);
    public static readonly Color BtnOff = new Color(0.20f, 0.23f, 0.28f, 1f);
    public static readonly Color TabIdle = new Color(0.62f, 0.66f, 0.70f, 1f);
    public static readonly Color TabActive = Color.white;
    public static readonly Color CardTitle = new Color(0.60f, 0.64f, 0.69f, 1f);
    public static readonly Color ScrollTrack = new Color(0.10f, 0.12f, 0.15f, 1f);
    public static readonly Color ScrollHandle = new Color(0.55f, 0.58f, 0.62f, 1f);

    public const float RowHeight = 90f;

    // ---------- primitives (donor-font recipe shared by all tabs) ----------

    static TMPro.TMP_FontAsset _donorFont;
    static Material _donorFontMat;
    static bool _donorFontTried;

    static void EnsureDonorFont()
    {
        if (_donorFontTried) return;
        _donorFontTried = true;
        try
        {
            var donor = UnityEngine.Object.FindObjectOfType<TMPro.TextMeshProUGUI>();
            if (donor != null)
            {
                _donorFont = donor.font;
                _donorFontMat = donor.fontSharedMaterial;
            }
        }
        catch { }
    }

    public static TMPro.TextMeshProUGUI Text(string name, string value, int size,
        TMPro.HorizontalAlignmentOptions hAlign, TMPro.VerticalAlignmentOptions vAlign, Color color)
    {
        var go = new GameObject(name);
        var ui = go.AddComponent<TMPro.TextMeshProUGUI>();
        EnsureDonorFont();
        if (_donorFont != null)
        {
            if (ui.font == null) ui.font = _donorFont;
            ui.fontSharedMaterial = _donorFontMat;
        }
        ui.text = value;
        ui.fontSize = size;
        ui.horizontalAlignment = hAlign;
        ui.verticalAlignment = vAlign;
        ui.color = color;
        return ui;
    }

    public static RectTransform Rect(GameObject go)
    {
        var r = go.GetComponent<RectTransform>();
        if (r == null) r = go.AddComponent<RectTransform>();
        return r;
    }

    public static void Stretch(GameObject go)
    {
        var r = Rect(go);
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero;
        r.offsetMax = Vector2.zero;
    }

    public static void Anchor(GameObject go, float minX, float minY, float maxX, float maxY,
        float offLeft, float offBottom, float offRight, float offTop)
    {
        var r = Rect(go);
        r.anchorMin = new Vector2(minX, minY);
        r.anchorMax = new Vector2(maxX, maxY);
        r.offsetMin = new Vector2(offLeft, offBottom);
        r.offsetMax = new Vector2(offRight, offTop);
    }

    // ---------- rows root ----------

    public static void ClearRows(Transform root)
    {
        if (root == null) return;
        foreach (Transform child in root)
            UnityEngine.Object.Destroy(child.gameObject);
    }

    public static void Warmup(Transform root)
    {
        if (root == null) return;
        var dummy = Text("Warmup", ".", 10,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Top, InkGrey);
        dummy.transform.SetParent(root, false);
        UnityEngine.Object.Destroy(dummy.gameObject);
    }

    public static void AddInfoRow(Transform rowsRoot, string message, int fontSize = 30,
        TMPro.VerticalAlignmentOptions vAlign = TMPro.VerticalAlignmentOptions.Middle,
        float minHeight = RowHeight)
    {
        var info = Text("Info", message, fontSize,
            TMPro.HorizontalAlignmentOptions.Left, vAlign, InkGrey);
        info.transform.SetParent(rowsRoot, false);
        var size = info.gameObject.AddComponent<LayoutElement>();
        size.minHeight = minHeight;
    }

    // ---------- scroll area (header stays outside, rows scroll inside) ----------

    public static Transform AddRowsScroll(GameObject parent,
        string scrollName, string viewportName, string contentName, string scrollbarName,
        float top, float spacing = 3f, float viewportRightInset = -64f,
        bool autoHideScrollbar = false, float barLeft = -52f, float barRight = -12f)
    {
        var scrollGO = new GameObject(scrollName);
        scrollGO.transform.SetParent(parent.transform, false);
        Anchor(scrollGO, 0f, 0f, 1f, 1f, 0f, 0f, 0f, top);
        var scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal = false;

        var viewportGO = new GameObject(viewportName);
        viewportGO.transform.SetParent(scrollGO.transform, false);
        Anchor(viewportGO, 0f, 0f, 1f, 1f, 0f, 0f, viewportRightInset, 0f);
        viewportGO.AddComponent<RectMask2D>();

        var contentGO = new GameObject(contentName);
        contentGO.transform.SetParent(viewportGO.transform, false);
        var contentRect = Rect(contentGO);
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;
        var fitter = contentGO.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var list = contentGO.AddComponent<VerticalLayoutGroup>();
        list.spacing = spacing;
        list.childControlWidth = true;
        list.childControlHeight = false;
        list.childForceExpandWidth = true;
        list.childForceExpandHeight = false;

        scroll.viewport = viewportGO.GetComponent<RectTransform>();
        scroll.content = contentRect;

        var sbGO = new GameObject(scrollbarName);
        sbGO.transform.SetParent(scrollGO.transform, false);
        Anchor(sbGO, 1f, 0f, 1f, 1f, barLeft, 0f, barRight, 0f);
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
        scroll.verticalScrollbarVisibility = autoHideScrollbar
            ? ScrollRect.ScrollbarVisibility.AutoHide
            : ScrollRect.ScrollbarVisibility.Permanent;
        return contentGO.transform;
    }

    // ---------- absolute mode (wide tables, pixel-exact columns) ----------

    public static GameObject AddHeaderRow(GameObject parent, string name,
        float offBottom, float offTop, float rightInset = -64f)
    {
        var headRow = new GameObject(name);
        headRow.transform.SetParent(parent.transform, false);
        Anchor(headRow, 0f, 1f, 1f, 1f, 0f, offBottom, rightInset, offTop);
        var headBg = headRow.AddComponent<Image>();
        headBg.color = HeaderBg;
        return headRow;
    }

    public static float FixedSpan(float[] widths, float gap)
    {
        float t = 0f;
        for (int i = 0; i < widths.Length; i++) t += widths[i];
        return t + gap * widths.Length;
    }

    public static TMPro.TextMeshProUGUI AddNameCell(GameObject parent, string goName, string value,
        float[] widths, float padL, float padR, float gap,
        int fontSize, Color color,
        TMPro.HorizontalAlignmentOptions align = TMPro.HorizontalAlignmentOptions.Left)
    {
        var cell = Text(goName, value, fontSize, align,
            TMPro.VerticalAlignmentOptions.Middle, color);
        cell.transform.SetParent(parent.transform, false);
        var r = Rect(cell.gameObject);
        r.anchorMin = new Vector2(0f, 0f);
        r.anchorMax = new Vector2(1f, 1f);
        r.offsetMin = new Vector2(padL, 0f);
        r.offsetMax = new Vector2(-(padR + FixedSpan(widths, gap)), 0f);
        cell.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        cell.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        TagCell(cell.gameObject, ColKind.Name, -1, 0, 0, widths, padL, padR, gap);
        return cell;
    }

    public static TMPro.TextMeshProUGUI AddFixedCell(GameObject parent, string goName, string value,
        float[] widths, int index, float padR, float gap,
        int fontSize, Color color, TMPro.HorizontalAlignmentOptions align)
    {
        var cell = Text(goName, value, fontSize, align,
            TMPro.VerticalAlignmentOptions.Middle, color);
        cell.transform.SetParent(parent.transform, false);
        float right = padR;
        for (int j = index + 1; j < widths.Length; j++) right += widths[j] + gap;
        var r = Rect(cell.gameObject);
        r.anchorMin = new Vector2(1f, 0f);
        r.anchorMax = new Vector2(1f, 1f);
        r.offsetMin = new Vector2(-(right + widths[index]), 0f);
        r.offsetMax = new Vector2(-right, 0f);
        cell.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        cell.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        TagCell(cell.gameObject, ColKind.Fixed, index, 0, 0, widths, 0f, padR, gap);
        return cell;
    }

    public static void AddHeadCells(GameObject headRow, string[] headers, float[] widths,
        float padL, float padR, float gap, int fontSize, Color color,
        TMPro.HorizontalAlignmentOptions fixedAlign = TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions[] fixedAligns = null)
    {
        AddNameCell(headRow, "HeadName", headers[0], widths, padL, padR, gap,
            fontSize, color, TMPro.HorizontalAlignmentOptions.Left);
        for (int i = 0; i < widths.Length; i++)
        {
            var align = fixedAligns != null && i < fixedAligns.Length ? fixedAligns[i] : fixedAlign;
            AddFixedCell(headRow, "Head" + i, headers[i + 1], widths, i, padR, gap,
                fontSize, color, align);
        }
    }

    public static void AddSpanCell(GameObject parent, string goName, string value,
        int first, int count, float[] widths, float padR, float gap, int fontSize, Color color)
    {
        var cell = Text(goName, value, fontSize, TMPro.HorizontalAlignmentOptions.Center,
            TMPro.VerticalAlignmentOptions.Middle, color);
        cell.transform.SetParent(parent.transform, false);
        float right = padR;
        for (int j = first + count; j < widths.Length; j++) right += widths[j] + gap;
        float width = 0f;
        for (int j = first; j < first + count; j++) width += widths[j];
        width += gap * (count - 1);
        var r = Rect(cell.gameObject);
        r.anchorMin = new Vector2(1f, 0f);
        r.anchorMax = new Vector2(1f, 1f);
        r.offsetMin = new Vector2(-(right + width), 0f);
        r.offsetMax = new Vector2(-right, 0f);
        TagCell(cell.gameObject, ColKind.Span, -1, first, count, widths, 0f, padR, gap);
    }

    public static GameObject AddRowRoot(Transform rowsRoot, string name, Color bg,
        float minHeight = RowHeight)
    {
        var rowGO = new GameObject(name);
        rowGO.transform.SetParent(rowsRoot, false);
        var bgImg = rowGO.AddComponent<Image>();
        bgImg.color = bg;
        var rowSize = rowGO.AddComponent<LayoutElement>();
        rowSize.minHeight = minHeight;
        return rowGO;
    }

    public static void AddRowClick(GameObject rowGO, Action onClick)
    {
        var bgImg = rowGO.GetComponent<Image>();
        var btn = rowGO.AddComponent<Button>();
        btn.targetGraphic = bgImg;
        btn.onClick.AddListener(() => onClick());
    }

    public static void AddHeadButton(GameObject parent, int sortCol,
        float[] widths, float padL, float padR, float gap, Action<int> onClick)
    {
        var go = new GameObject("Sort" + sortCol);
        go.transform.SetParent(parent.transform, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var r = Rect(go);
        if (sortCol == 0)
        {
            r.anchorMin = new Vector2(0f, 0f);
            r.anchorMax = new Vector2(1f, 1f);
            r.offsetMin = new Vector2(padL, 0f);
            r.offsetMax = new Vector2(-(padR + FixedSpan(widths, gap)), 0f);
        }
        else
        {
            int index = sortCol - 1;
            float right = padR;
            for (int j = index + 1; j < widths.Length; j++) right += widths[j] + gap;
            r.anchorMin = new Vector2(1f, 0f);
            r.anchorMax = new Vector2(1f, 1f);
            r.offsetMin = new Vector2(-(right + widths[index]), 0f);
            r.offsetMax = new Vector2(-right, 0f);
        }
        int captured = sortCol;
        btn.onClick.AddListener(() => onClick(captured));
        TagCell(go, ColKind.HeadButton, sortCol - 1, 0, 0, widths, padL, padR, gap);
    }

    // ---------- draggable column widths ----------
    // In absolute tables every fixed column has a 16px grip on its LEFT edge.
    // Dragging the left edge grows/shrinks THAT column; the right side stays
    // fixed and the NAME column absorbs the difference. Double-click restores
    // the default width. Widths persist per table in PlayerPrefs.

    public enum ColKind { Name, Fixed, Span, HeadButton, Handle }

    public struct GroupDef
    {
        public string title;
        public int first;
        public int count;
        public GroupDef(string title, int first, int count)
        {
            this.title = title;
            this.first = first;
            this.count = count;
        }
    }

    public sealed class ColTag : MonoBehaviour
    {
        public ColKind kind;
        public int col;
        public int spanFirst;
        public int spanCount;
        public float[] widths;
        public float padL;
        public float padR;
        public float gap;
    }

    static void TagCell(GameObject go, ColKind kind, int col, int spanFirst, int spanCount,
        float[] widths, float padL, float padR, float gap)
    {
        var tag = go.AddComponent<ColTag>();
        tag.kind = kind;
        tag.col = col;
        tag.spanFirst = spanFirst;
        tag.spanCount = spanCount;
        tag.widths = widths;
        tag.padL = padL;
        tag.padR = padR;
        tag.gap = gap;
    }

    public const float ColMinWidth = 60f;
    public const float ColMaxWidth = 600f;
    const float HandleWidth = 16f;

    public static void LoadWidths(string tableId, float[] widths, float[] defaults)
    {
        try
        {
            for (int i = 0; i < widths.Length && i < defaults.Length; i++)
                widths[i] = defaults[i];
            // FROZEN prefix for save compat: renaming orphans saved column widths.
            string key = "MyFirstMod.cols." + tableId;
            string saved;
            try { saved = UnityEngine.PlayerPrefs.GetString(key, ""); }
            catch { return; }
            if (string.IsNullOrEmpty(saved)) return;
            var parts = saved.Split(',');
            if (parts.Length != widths.Length) return;
            for (int i = 0; i < parts.Length; i++)
            {
                float v;
                if (float.TryParse(parts[i],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v))
                    widths[i] = Mathf.Clamp(v, ColMinWidth, ColMaxWidth);
            }
        }
        catch { }
    }

    public static void SaveWidths(string tableId, float[] widths)
    {
        try
        {
            var parts = new string[widths.Length];
            for (int i = 0; i < widths.Length; i++)
                parts[i] = widths[i].ToString("F0",
                    System.Globalization.CultureInfo.InvariantCulture);
            // FROZEN prefix, see LoadWidths.
            UnityEngine.PlayerPrefs.SetString("MyFirstMod.cols." + tableId, string.Join(",", parts));
            try { UnityEngine.PlayerPrefs.Save(); }
            catch { }
        }
        catch { }
    }

    static float HandleFromRight(float[] widths, int col, float padR, float gap)
    {
        float fromRight = padR;
        for (int j = col + 1; j < widths.Length; j++) fromRight += widths[j] + gap;
        return fromRight + widths[col];
    }

    public static void AddResizeHandles(GameObject headRow, float[] widths, float[] defaults,
        float padL, float padR, float gap, Transform rowsRoot, string tableId,
        GameObject groupRow = null)
    {
        if (headRow == null || widths == null || widths.Length == 0) return;
        for (int i = 0; i < widths.Length; i++)
        {
            int captured = i;
            var go = new GameObject("Resize" + i);
            go.transform.SetParent(headRow.transform, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.10f, 0.12f, 0.15f, 0.0f);
            var gripImg = MakeGripLine(go.transform, -1f, 1f);
            Image gripImg2 = null;
            PositionHandle(go, widths, captured, padR, gap);
            var tag = go.AddComponent<ColTag>();
            tag.kind = ColKind.Handle;
            tag.col = captured;
            tag.widths = widths;
            tag.padL = padL;
            tag.padR = padR;
            tag.gap = gap;
            var drag = go.AddComponent<ColumnDrag>();
            drag.col = captured;
            drag.widths = widths;
            drag.defaults = defaults;
            drag.padL = padL;
            drag.padR = padR;
            drag.gap = gap;
            drag.headRow = headRow;
            drag.groupRow = groupRow;
            drag.rowsRoot = rowsRoot;
            drag.tableId = tableId;
            drag.handleImg = img;
            drag.gripImg = gripImg;
            drag.gripImg2 = gripImg2;
        }
    }

    static void PositionHandle(GameObject go, float[] widths, int col, float padR, float gap)
    {
        float center = HandleFromRight(widths, col, padR, gap);
        var r = Rect(go);
        r.anchorMin = new Vector2(1f, 0f);
        r.anchorMax = new Vector2(1f, 1f);
        r.offsetMin = new Vector2(-(center + HandleWidth * 0.5f), 0f);
        r.offsetMax = new Vector2(-(center - HandleWidth * 0.5f), 0f);
    }

    static Image MakeGripLine(Transform parent, float x0, float x1)
    {
        var line = new GameObject("Grip");
        line.transform.SetParent(parent, false);
        var r = Rect(line);
        r.anchorMin = new Vector2(0.5f, 0f);
        r.anchorMax = new Vector2(0.5f, 1f);
        r.offsetMin = new Vector2(x0, 0f);
        r.offsetMax = new Vector2(x1, 0f);
        var img = line.AddComponent<Image>();
        img.color = new Color(0.32f, 0.35f, 0.39f, 0.75f);
        img.raycastTarget = false;
        return img;
    }

    // ---------- hover cursor (two-way arrow) ----------
    static Texture2D _resizeCursor;

    static void DragLine(Texture2D tex, int x0, int y0, int x1, int y1, int thick, Color c)
    {
        int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
        int r = thick / 2;
        for (int i = 0; i <= steps; i++)
        {
            float t = steps == 0 ? 0f : (float)i / steps;
            int x = Mathf.RoundToInt(x0 + (x1 - x0) * t);
            int y = Mathf.RoundToInt(y0 + (y1 - y0) * t);
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int px = x + dx, py = y + dy;
                if (px >= 0 && py >= 0 && px < tex.width && py < tex.height)
                    tex.SetPixel(px, py, c);
            }
        }
    }

    static Texture2D ResizeCursorTexture()
    {
        if (_resizeCursor != null) return _resizeCursor;
        try
        {
            const int S = 32;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
                tex.SetPixel(x, y, clear);
            var halo = Color.white;
            var core = new Color(0.1f, 0.1f, 0.1f, 1f);
            // shaft + two chevrons, white halo under dark core (visible on light and dark rows)
            DragLine(tex, 11, 16, 21, 16, 5, halo);
            DragLine(tex, 11, 16, 16, 10, 5, halo);
            DragLine(tex, 11, 16, 16, 22, 5, halo);
            DragLine(tex, 21, 16, 16, 10, 5, halo);
            DragLine(tex, 21, 16, 16, 22, 5, halo);
            DragLine(tex, 11, 16, 21, 16, 2, core);
            DragLine(tex, 11, 16, 16, 10, 2, core);
            DragLine(tex, 11, 16, 16, 22, 2, core);
            DragLine(tex, 21, 16, 16, 10, 2, core);
            DragLine(tex, 21, 16, 16, 22, 2, core);
            tex.Apply();
            _resizeCursor = tex;
        }
        catch { _resizeCursor = null; }
        return _resizeCursor;
    }

    static void SetResizeCursor(bool on)
    {
        try
        {
            if (on)
            {
                var t = ResizeCursorTexture();
                if (t != null) Cursor.SetCursor(t, new Vector2(16f, 16f), CursorMode.Auto);
            }
            else Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
        catch { }
    }

    public static void RelayoutRow(GameObject row)
    {
        if (row == null) return;
        foreach (Transform child in row.transform)
        {
            var tag = child.GetComponent<ColTag>();
            if (tag == null || tag.widths == null) continue;
            var r = child.GetComponent<RectTransform>();
            if (r == null) continue;
            var w = tag.widths;
            switch (tag.kind)
            {
                case ColKind.Name:
                    r.anchorMin = new Vector2(0f, 0f);
                    r.anchorMax = new Vector2(1f, 1f);
                    r.offsetMin = new Vector2(tag.padL, 0f);
                    r.offsetMax = new Vector2(-(tag.padR + FixedSpan(w, tag.gap)), 0f);
                    break;
                case ColKind.HeadButton:
                    if (tag.col < 0)
                    {
                        r.anchorMin = new Vector2(0f, 0f);
                        r.anchorMax = new Vector2(1f, 1f);
                        r.offsetMin = new Vector2(tag.padL, 0f);
                        r.offsetMax = new Vector2(-(tag.padR + FixedSpan(w, tag.gap)), 0f);
                    }
                    else
                    {
                        int idx = tag.col;
                        float right = tag.padR;
                        for (int j = idx + 1; j < w.Length; j++) right += w[j] + tag.gap;
                        r.anchorMin = new Vector2(1f, 0f);
                        r.anchorMax = new Vector2(1f, 1f);
                        r.offsetMin = new Vector2(-(right + w[idx]), 0f);
                        r.offsetMax = new Vector2(-right, 0f);
                    }
                    break;
                case ColKind.Fixed:
                    {
                        int idx = tag.col;
                        if (idx < 0 || idx >= w.Length) break;
                        float right = tag.padR;
                        for (int j = idx + 1; j < w.Length; j++) right += w[j] + tag.gap;
                        r.anchorMin = new Vector2(1f, 0f);
                        r.anchorMax = new Vector2(1f, 1f);
                        r.offsetMin = new Vector2(-(right + w[idx]), 0f);
                        r.offsetMax = new Vector2(-right, 0f);
                    }
                    break;
                case ColKind.Span:
                    {
                        float right = tag.padR;
                        for (int j = tag.spanFirst + tag.spanCount; j < w.Length; j++)
                            right += w[j] + tag.gap;
                        float width = 0f;
                        for (int j = tag.spanFirst; j < tag.spanFirst + tag.spanCount; j++)
                            width += w[j];
                        width += tag.gap * (tag.spanCount - 1);
                        r.anchorMin = new Vector2(1f, 0f);
                        r.anchorMax = new Vector2(1f, 1f);
                        r.offsetMin = new Vector2(-(right + width), 0f);
                        r.offsetMax = new Vector2(-right, 0f);
                    }
                    break;
                case ColKind.Handle:
                    PositionHandle(child.gameObject, w, tag.col, tag.padR, tag.gap);
                    break;
            }
        }
    }

    public static void RelayoutTable(GameObject headRow, Transform rowsRoot, GameObject groupRow = null)
    {
        RelayoutRow(headRow);
        RelayoutRow(groupRow);
        if (rowsRoot == null) return;
        foreach (Transform row in rowsRoot)
            RelayoutRow(row.gameObject);
    }

    public sealed class ColumnDrag : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public int col;
        public float[] widths;
        public float[] defaults;
        public float padL;
        public float padR;
        public float gap;
        public GameObject headRow;
        public GameObject groupRow;
        public Transform rowsRoot;
        public string tableId;
        public Image handleImg;
        public Image gripImg;
        public Image gripImg2;
        bool _dragging;

        float CanvasScale()
        {
            try
            {
                var c = GetComponentInParent<Canvas>();
                if (c != null && c.scaleFactor > 0f) return c.scaleFactor;
            }
            catch { }
            return 1f;
        }

        void Paint(bool hover)
        {
            try
            {
                if (handleImg != null)
                    handleImg.color = _dragging ? new Color(0.10f, 0.55f, 0.55f, 0.55f)
                        : hover ? new Color(0.10f, 0.55f, 0.55f, 0.25f)
                        : new Color(0.10f, 0.12f, 0.15f, 0.0f);
                var gripColor = _dragging ? new Color(1f, 1f, 1f, 0.95f)
                    : hover ? new Color(0.10f, 0.55f, 0.55f, 1f)
                    : new Color(0.32f, 0.35f, 0.39f, 0.75f);
                if (gripImg != null) gripImg.color = gripColor;
                if (gripImg2 != null) gripImg2.color = gripColor;
            }
            catch { }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _dragging = true;
            Paint(true);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (widths == null || col < 0 || col >= widths.Length) return;
            try
            {
                float dx = eventData.delta.x / CanvasScale();
                // Grip on the column's LEFT edge: drag right = shrink, left = widen.
                float next = Mathf.Clamp(widths[col] - dx, ColMinWidth, ColMaxWidth);
                if (Mathf.Approximately(next, widths[col])) return;
                widths[col] = next;
                RelayoutTable(headRow, rowsRoot, groupRow);
            }
            catch { }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _dragging = false;
            Paint(false);
            SetResizeCursor(false);
            try { SaveWidths(tableId, widths); }
            catch { }
        }

        public void OnPointerEnter(PointerEventData eventData) { Paint(true); SetResizeCursor(true); }
        public void OnPointerExit(PointerEventData eventData) { if (!_dragging) Paint(false); if (!_dragging) SetResizeCursor(false); }

        public void OnPointerClick(PointerEventData eventData)
        {
            try
            {
                if (eventData == null || eventData.clickCount != 2) return;
                if (widths == null || defaults == null) return;
                if (col < 0 || col >= widths.Length || col >= defaults.Length) return;
                widths[col] = Mathf.Clamp(defaults[col], ColMinWidth, ColMaxWidth);
                RelayoutTable(headRow, rowsRoot, groupRow);
                SaveWidths(tableId, widths);
            }
            catch { }
        }
    }

    // ---------- flex mode (narrow tables, layout-group columns) ----------

    public static GameObject AddFlexHeader(GameObject parent, string name,
        float offBottom, float offTop, int padL, int padR, float spacing, float rightInset = -64f)
    {
        var headRow = new GameObject(name);
        headRow.transform.SetParent(parent.transform, false);
        Anchor(headRow, 0f, 1f, 1f, 1f, 0f, offBottom, rightInset, offTop);
        var headBg = headRow.AddComponent<Image>();
        headBg.color = HeaderBg;
        var headLayout = headRow.AddComponent<HorizontalLayoutGroup>();
        headLayout.padding = new RectOffset(padL, padR, 0, 0);
        headLayout.spacing = spacing;
        headLayout.childControlWidth = true;
        headLayout.childControlHeight = true;
        headLayout.childForceExpandHeight = true;
        return headRow;
    }

    public static void AddFlexHeadCell(GameObject parent, string value,
        float flex, float fixedW, TMPro.HorizontalAlignmentOptions align, int fontSize = 26)
    {
        var cell = Text("Head", value, fontSize, align,
            TMPro.VerticalAlignmentOptions.Middle, HeaderInk);
        cell.transform.SetParent(parent.transform, false);
        var size = cell.gameObject.AddComponent<LayoutElement>();
        if (flex > 0)
        {
            size.flexibleWidth = flex;
            size.minWidth = fixedW;
        }
        else
        {
            size.preferredWidth = fixedW;
        }
    }

    public static GameObject AddFlexRow(Transform rowsRoot, string name, Color bg,
        int padL, int padR, float spacing, float minHeight = RowHeight)
    {
        var rowGO = new GameObject(name);
        rowGO.transform.SetParent(rowsRoot, false);
        var bgImg = rowGO.AddComponent<Image>();
        bgImg.color = bg;
        var rowSize = rowGO.AddComponent<LayoutElement>();
        rowSize.minHeight = minHeight;
        var layout = rowGO.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(padL, padR, 0, 0);
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = true;
        return rowGO;
    }

    public static void AddFlexCell(GameObject parent, string value,
        float flex, float fixedW, TMPro.HorizontalAlignmentOptions align, Color color, int fontSize = 30)
    {
        var cell = Text("Cell", value, fontSize, align,
            TMPro.VerticalAlignmentOptions.Middle, color);
        cell.transform.SetParent(parent.transform, false);
        var size = cell.gameObject.AddComponent<LayoutElement>();
        if (flex > 0)
        {
            size.flexibleWidth = flex;
            size.minWidth = fixedW;
        }
        else
        {
            size.preferredWidth = fixedW;
        }
        cell.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        cell.overflowMode = TMPro.TextOverflowModes.Ellipsis;
    }
}
