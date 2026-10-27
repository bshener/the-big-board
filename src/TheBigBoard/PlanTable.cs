using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BAModAPI;
using static TheBigBoard.DataGrid;

namespace TheBigBoard;

/// <summary>FACTORY production-plan table: one row per product (merged lines)
/// with LINES / PLAN TODAY / PLAN WEEK columns. All headers sort.</summary>
public sealed class PlanTable : TablePanel<FactoryPanel.ProductRow>
{
    static readonly string[] HeadNames = { "PRODUCT", "LINES", "PLAN TODAY", "PLAN WEEK" };

    static readonly TMPro.HorizontalAlignmentOptions[] ColAlign =
    {
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
    };

    static readonly float[] DefaultWidths = { 130f, 230f, 230f }; // LINES, TODAY, WEEK

    public PlanTable()
        : base("factoryPlan", HeadNames, DefaultWidths, ColAlign, 40f, 40f, 12f,
            26, null, -1, true)
    {
    }

    protected override int CompareRows(FactoryPanel.ProductRow a, FactoryPanel.ProductRow b, int col)
    {
        int c = 0;
        switch (col)
        {
            case 0: c = string.Compare(a.productName, b.productName, StringComparison.Ordinal); break;
            case 1: c = a.lines.CompareTo(b.lines); break;
            case 2: c = a.today.CompareTo(b.today); break;
            case 3: c = a.week.CompareTo(b.week); break;
        }
        if (c == 0) c = string.Compare(a.productName, b.productName, StringComparison.Ordinal);
        return c;
    }

    protected override void RenderTotalRow(List<FactoryPanel.ProductRow> rows)
    {
        long lines = 0, today = 0, week = 0;
        foreach (var p in rows)
        {
            lines += p.lines;
            today += p.today;
            week += p.week;
        }
        var rowGO = DataGrid.AddRowRoot(RowsRoot, "TotalRow", DataGrid.TotalBg);
        NameCell(rowGO, "TotalName", "TOTAL (" + rows.Count + " products)", DataGrid.ProfitGreen);
        Fixed(rowGO, "TotalLines", lines.ToString("N0"), 0, DataGrid.ProfitGreen);
        Fixed(rowGO, "TotalToday", today.ToString("N0"), 1, DataGrid.ProfitGreen);
        Fixed(rowGO, "TotalWeek", week.ToString("N0"), 2, DataGrid.ProfitGreen);
    }

    protected override void RenderRow(GameObject rowGO, FactoryPanel.ProductRow p)
    {
        NameCell(rowGO, "RowName", p.productName, DataGrid.InkLight);
        Fixed(rowGO, "RowLines", p.lines.ToString("N0"), 0, DataGrid.InkLight);
        Fixed(rowGO, "RowToday", p.today.ToString("N0"), 1, DataGrid.InkLight);
        Fixed(rowGO, "RowWeek", p.week.ToString("N0"), 2, DataGrid.InkLight);
    }

    TMPro.TextMeshProUGUI NameCell(GameObject parent, string goName, string value, Color color)
    {
        return DataGrid.AddNameCell(parent, goName, value, Widths,
            PadL, PadR, Gap, 30, color, TMPro.HorizontalAlignmentOptions.Left);
    }

    TMPro.TextMeshProUGUI Fixed(GameObject parent, string goName, string value, int index, Color color)
    {
        return DataGrid.AddFixedCell(parent, goName, value, Widths, index,
            PadR, Gap, 30, color, TMPro.HorizontalAlignmentOptions.Right);
    }
}
