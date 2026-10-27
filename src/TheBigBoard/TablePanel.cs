using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BAModAPI;
using static TheBigBoard.DataGrid;

namespace TheBigBoard;

/// <summary>
/// Real base class for every absolute-mode data table in the mod.
/// A table is declared with headers + column widths + alignments; the base
/// owns the header row (all headers clickable = sorting everywhere), the
/// draggable resize handles (widths load/save per table id), and the
/// render loop (totals, zebra stripes, empty state, row clicks).
/// Derived classes only provide their rows: CompareRows + RenderRow.
/// Panel chrome (filters, pills, tabs, data fetching) stays in the panel.
/// </summary>
public abstract class TablePanel<TRow>
{
    public string TableId { get; }
    protected ModContext Context { get; private set; }
    protected readonly string[] Headers;
    protected readonly float[] Widths;
    protected readonly float[] Defaults;
    protected readonly TMPro.HorizontalAlignmentOptions[] Aligns;
    protected readonly float PadL;
    protected readonly float PadR;
    protected readonly float Gap;
    protected readonly int HeaderFontSize;
    protected readonly DataGrid.GroupDef[] Groups;
    protected GameObject HeadRow;
    protected GameObject GroupRow;
    protected Transform RowsRoot;
    protected TMPro.TextMeshProUGUI[] HeadTexts;
    protected readonly List<TRow> Cached = new List<TRow>();
    protected int SortCol;
    protected bool SortAsc = true;

    protected TablePanel(string tableId, string[] headers, float[] defaults,
        TMPro.HorizontalAlignmentOptions[] aligns, float padL, float padR, float gap,
        int headerFontSize = 24, DataGrid.GroupDef[] groups = null,
        int defaultSortCol = -1, bool defaultAsc = true)
    {
        TableId = tableId;
        Headers = headers;
        Defaults = (float[])defaults.Clone();
        Widths = (float[])defaults.Clone();
        DataGrid.LoadWidths(tableId, Widths, Defaults);
        Aligns = aligns;
        PadL = padL;
        PadR = padR;
        Gap = gap;
        HeaderFontSize = headerFontSize;
        Groups = groups;
        SortCol = defaultSortCol;
        SortAsc = defaultAsc;
    }

    public void Init(ModContext context)
    {
        Context = context;
    }

    /// <summary>Builds the (optional) group row + header row with sort
    /// buttons and resize handles. Headers are clickable from here on.</summary>
    public void BuildHeader(GameObject parent, string name,
        float grpBottom, float grpTop, float headBottom, float headTop,
        float rightInset = -64f)
    {
        if (Groups != null && Groups.Length > 0)
        {
            GroupRow = DataGrid.AddHeaderRow(parent, name + "Group", grpBottom, grpTop, rightInset);
            DataGrid.AddNameCell(GroupRow, "GroupName", "", Widths, PadL, PadR, Gap,
                HeaderFontSize, DataGrid.HeaderInk,
                TMPro.HorizontalAlignmentOptions.Left);
            for (int g = 0; g < Groups.Length; g++)
                DataGrid.AddSpanBg(GroupRow, "GroupBg" + g, Groups[g].first, Groups[g].count,
                    Widths, PadR, Gap, GroupTint(g));
            for (int g = 0; g < Groups.Length; g++)
            {
                DataGrid.AddSpanCell(GroupRow, "Group" + g, Groups[g].title,
                    Groups[g].first, Groups[g].count, Widths, PadR, Gap,
                    HeaderFontSize, DataGrid.HeaderInk);
                var gt = GroupRow.transform.Find("Group" + g);
                if (gt != null)
                {
                    var gtxt = gt.GetComponent<TMPro.TextMeshProUGUI>();
                    if (gtxt != null) gtxt.fontStyle = TMPro.FontStyles.Bold;
                }
            }
            AddGroupDividers(GroupRow, new Color(0.30f, 0.33f, 0.37f, 1f));
        }
        HeadRow = DataGrid.AddHeaderRow(parent, name, headBottom, headTop, rightInset);
        if (Groups != null && Groups.Length > 0)
        {
            for (int g = 0; g < Groups.Length; g++)
                DataGrid.AddSpanBg(HeadRow, "HeadBg" + g, Groups[g].first, Groups[g].count,
                    Widths, PadR, Gap, GroupTint(g));
        }
        HeadTexts = new TMPro.TextMeshProUGUI[Headers.Length];
        HeadTexts[0] = DataGrid.AddNameCell(HeadRow, "HeadName", Headers[0],
            Widths, PadL, PadR, Gap, HeaderFontSize, DataGrid.HeaderInk,
            TMPro.HorizontalAlignmentOptions.Center);
        DataGrid.AddHeadButton(HeadRow, 0, Widths, PadL, PadR, Gap, SetSort);
        for (int i = 0; i < Widths.Length; i++)
        {
            // DataGrid rule: header texts are always centered, regardless of
            // how the row cells below align (left text / right numbers).
            HeadTexts[i + 1] = DataGrid.AddFixedCell(HeadRow, "Head" + i, Headers[i + 1],
                Widths, i, PadR, Gap, HeaderFontSize, DataGrid.HeaderInk,
                TMPro.HorizontalAlignmentOptions.Center);
            DataGrid.AddHeadButton(HeadRow, i + 1, Widths, PadL, PadR, Gap, SetSort);
        }
        AddGroupDividers(HeadRow, new Color(0.30f, 0.33f, 0.37f, 1f));
        UpdateHeadSort();
    }

    // Pale rotating tints so each column group reads as one block.
    // Same tint on the group title and its sub-headers links them.
    static Color GroupTint(int g)
    {
        switch (g % 3)
        {
            case 1: return new Color(0.93f, 0.90f, 0.82f, 1f);
            case 2: return new Color(0.84f, 0.88f, 0.93f, 1f);
            default: return new Color(0.78f, 0.86f, 0.87f, 1f);
        }
    }

    // Vertical dividers at group boundaries. Tables opt in via
    // GroupDividerCols (fixed-column indices = each group's first column).
    protected virtual int[] GroupDividerCols()
    {
        return null;
    }

    protected void AddGroupDividers(GameObject rowGO, Color color)
    {
        var cols = GroupDividerCols();
        if (cols == null || rowGO == null) return;
        foreach (int f in cols)
        {
            if (f <= 0 || f >= Widths.Length) continue;
            DataGrid.AddDivider(rowGO, "GrpDiv" + f, Widths, f, PadR, Gap, color);
        }
    }

    public void BuildRows(GameObject parent,
        string scrollName, string viewportName, string contentName, string scrollbarName,
        float top)
    {
        var scrollGO = DataGrid.AddRowsScroll(parent,
            scrollName, viewportName, contentName, scrollbarName, top);
        _scrollGO = scrollGO.gameObject;
        RowsRoot = scrollGO.transform;
        DataGrid.AddResizeHandles(HeadRow, Widths, Defaults,
            PadL, PadR, Gap, RowsRoot, TableId, GroupRow);
    }

    /// <summary>Push new rows in: sorts (unless SortCol is -1 = query order)
    /// and renders. Called by the owning panel after fetching data.</summary>
    public void SetRows(List<TRow> rows)
    {
        Cached.Clear();
        if (rows != null) Cached.AddRange(rows);
        SortCached();
        if (MaxRows > 0 && Cached.Count > MaxRows)
        {
            Truncated = Cached.Count - MaxRows;
            Cached.RemoveRange(MaxRows, Truncated);
        }
        else Truncated = 0;
        RenderCached();
    }

    public int Truncated { get; private set; }
    public int RowCount
    {
        get { return Cached.Count; }
    }

    protected virtual int MaxRows
    {
        get { return -1; }
    }

    GameObject _scrollGO;

    /// <summary>Re-anchors header (+group) and rows below a new top offset.
    /// Used when the panel above the table changes height at runtime.</summary>
    public void SetTableTop(float headTop, float headBottom, float rowsTop)
    {
        if (HeadRow != null)
        {
            var h = Rect(HeadRow);
            float delta = headTop - h.offsetMax.y;
            h.offsetMin = new Vector2(h.offsetMin.x, headBottom);
            h.offsetMax = new Vector2(h.offsetMax.x, headTop);
            if (GroupRow != null)
            {
                var g = Rect(GroupRow);
                g.offsetMin = new Vector2(g.offsetMin.x, g.offsetMin.y + delta);
                g.offsetMax = new Vector2(g.offsetMax.x, g.offsetMax.y + delta);
            }
        }
        if (_scrollGO != null)
        {
            var s = Rect(_scrollGO);
            s.offsetMax = new Vector2(s.offsetMax.x, rowsTop);
        }
    }
    public void ShowInfo(string message, int fontSize = 30)
    {
        if (RowsRoot == null) return;
        DataGrid.ClearRows(RowsRoot);
        DataGrid.Warmup(RowsRoot);
        DataGrid.AddInfoRow(RowsRoot, message, fontSize,
            TMPro.VerticalAlignmentOptions.Top);
    }

    protected void SetSort(int col)
    {
        if (col < 0 || col >= Headers.Length) return;
        if (col == SortCol) SortAsc = !SortAsc;
        else { SortCol = col; SortAsc = true; }
        UpdateHeadSort();
        SortCached();
        RenderCached();
        OnSortChanged(col, SortAsc);
    }

    protected virtual void OnSortChanged(int col, bool asc)
    {
    }

    void UpdateHeadSort()
    {
        if (HeadTexts == null || Headers == null) return;
        for (int i = 0; i < HeadTexts.Length && i < Headers.Length; i++)
        {
            var t = HeadTexts[i];
            if (t == null) continue;
            bool sel = i == SortCol;
            t.text = Headers[i] + (sel ? (SortAsc ? " \u25B2" : " \u25BC") : "");
            t.color = sel ? DataGrid.Accent : DataGrid.HeaderInk;
        }
    }

    void SortCached()
    {
        if (SortCol < 0 || Cached.Count < 2) return;
        Cached.Sort((a, b) =>
        {
            int c = CompareRows(a, b, SortCol);
            return SortAsc ? c : -c;
        });
    }

    void RenderCached()
    {
        if (RowsRoot == null) return;
        DataGrid.ClearRows(RowsRoot);
        DataGrid.Warmup(RowsRoot);
        if (Cached.Count == 0)
        {
            DataGrid.AddInfoRow(RowsRoot, EmptyText, InfoFontSize,
                TMPro.VerticalAlignmentOptions.Top);
            return;
        }
        int totalBefore = RowsRoot.childCount;
        RenderTotalRow(Cached);
        if (RowsRoot.childCount > totalBefore)
            AddGroupDividers(RowsRoot.GetChild(RowsRoot.childCount - 1).gameObject,
                new Color(0.60f, 0.63f, 0.67f, 1f));
        int stripe = 0;
        foreach (var r in Cached)
        {
            var rowGO = DataGrid.AddRowRoot(RowsRoot, "Row",
                stripe % 2 == 0 ? DataGrid.RowBg : DataGrid.RowBgAlt);
            RenderRow(rowGO, r);
            AddGroupDividers(rowGO, new Color(0.60f, 0.63f, 0.67f, 1f));
            if (RowClickable(r))
            {
                var captured = r;
                DataGrid.AddRowClick(rowGO, () => OnRowClicked(captured));
            }
            stripe++;
        }
    }

    protected TMPro.TextMeshProUGUI GroupNameCell()
    {
        if (GroupRow == null) return null;
        var t = GroupRow.transform.Find("GroupName");
        return t != null ? t.GetComponent<TMPro.TextMeshProUGUI>() : null;
    }

    /// <summary>Shared row-cell helpers (were copy-pasted in every table):
    /// name text left, fixed-width numbers right, 30pt. Tables with their
    /// own alignment needs keep local overloads.</summary>
    protected TMPro.TextMeshProUGUI NameCell(GameObject parent, string goName, string value, Color color)
    {
        return DataGrid.AddNameCell(parent, goName, value, Widths,
            PadL, PadR, Gap, 30, color, TMPro.HorizontalAlignmentOptions.Left);
    }

    protected TMPro.TextMeshProUGUI Fixed(GameObject parent, string goName, string value, int index, Color color)
    {
        return DataGrid.AddFixedCell(parent, goName, value, Widths, index,
            PadR, Gap, 30, color, TMPro.HorizontalAlignmentOptions.Right);
    }

    // ---------- to be provided by each table ----------

    protected abstract int CompareRows(TRow a, TRow b, int col);
    protected abstract void RenderRow(GameObject rowGO, TRow row);

    protected virtual void RenderTotalRow(List<TRow> rows)
    {
    }

    protected virtual string EmptyText
    {
        get { return "No rows."; }
    }

    protected virtual int InfoFontSize
    {
        get { return 30; }
    }

    protected virtual bool RowClickable(TRow row)
    {
        return false;
    }

    protected virtual void OnRowClicked(TRow row)
    {
    }
}
