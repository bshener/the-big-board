using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BAModAPI;
using static TheBigBoard.DataGrid;

namespace TheBigBoard;

/// <summary>FACTORY production-plan table: one row per product (merged lines)
/// with LINES / PLAN TODAY / PLAN WEEK / STOCK (upstream, stores excluded) /
/// SELL TODAY / SELL WEEK / PROD TODAY / PROD YDAY / PROD WEEK (manufactured
/// in THIS factory via the ledger identity; "..." while unattributable) /
/// TODAY +/- % / YDAY +/- % / WEEK +/- % (manufactured vs sold: over/under
/// production, 0% = balanced, + = surplus, - = shortfall).
/// All headers sort.</summary>
public sealed class PlanTable : TablePanel<FactoryPanel.ProductRow>
{
    static readonly string[] HeadNames = { "PRODUCT", "LINES", "PLAN TODAY", "PLAN WEEK", "STOCK", "SELL TODAY", "SELL WEEK", "PROD TODAY", "PROD YDAY", "PROD WEEK", "TODAY \u00B1%", "YDAY \u00B1%", "WEEK \u00B1%" };

    static readonly TMPro.HorizontalAlignmentOptions[] ColAlign =
    {
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
    };

    static readonly float[] DefaultWidths = { 130f, 220f, 220f, 160f, 160f, 160f, 160f, 160f, 160f, 120f, 120f, 120f }; // LINES, TODAY, WEEK, STOCK, SELL-T, SELL-W, PROD-T, PROD-Y, PROD-W, DIFF-T, DIFF-Y, DIFF-W

    static readonly DataGrid.GroupDef[] GroupDefs =
    {
        new DataGrid.GroupDef("PROD vs SELL", 9, 3),
    };

    public PlanTable()
        : base("factoryPlan", HeadNames, DefaultWidths, ColAlign, 40f, 40f, 12f,
            26, GroupDefs, -1, true)
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
            case 4: c = a.stock.CompareTo(b.stock); break;
            case 5: c = a.sellToday.CompareTo(b.sellToday); break;
            case 6: c = a.sellWeek.CompareTo(b.sellWeek); break;
            case 7: c = a.prodToday.CompareTo(b.prodToday); break;
            case 8: c = a.prodYday.CompareTo(b.prodYday); break;
            case 9: c = a.prodWeek.CompareTo(b.prodWeek); break;
            case 10: c = DiffValue(a.prodToday, a.prodTodayExact, a.sellToday).CompareTo(DiffValue(b.prodToday, b.prodTodayExact, b.sellToday)); break;
            case 11: c = DiffValue(a.prodYday, a.prodYdayExact, a.sellYday).CompareTo(DiffValue(b.prodYday, b.prodYdayExact, b.sellYday)); break;
            case 12: c = DiffValue(a.prodWeek, a.prodWeekExact, a.sellWeek).CompareTo(DiffValue(b.prodWeek, b.prodWeekExact, b.sellWeek)); break;
        }
        if (c == 0) c = string.Compare(a.productName, b.productName, StringComparison.Ordinal);
        return c;
    }

    protected override void RenderTotalRow(List<FactoryPanel.ProductRow> rows)
    {
        long lines = 0, today = 0, week = 0, stock = 0, sellT = 0, sellY = 0, sellW = 0, prodT = 0, prodW = 0, prodY = 0;
        bool prodPartial = false;
        foreach (var p in rows)
        {
            lines += p.lines;
            today += p.today;
            week += p.week;
            stock += p.stock;
            sellT += p.sellToday;
            sellY += p.sellYday;
            sellW += p.sellWeek;
            if (p.prodTodayExact) prodT += p.prodToday;
            else prodPartial = true;
            if (p.prodWeekExact) prodW += p.prodWeek;
            else prodPartial = true;
            if (p.prodYdayExact) prodY += p.prodYday;
            else prodPartial = true;
        }
        var rowGO = DataGrid.AddRowRoot(RowsRoot, "TotalRow", DataGrid.TotalBg);
        NameCell(rowGO, "TotalName", "TOTAL (" + rows.Count + " products)", DataGrid.ProfitGreen);
        Fixed(rowGO, "TotalLines", lines.ToString("N0"), 0, DataGrid.ProfitGreen);
        Fixed(rowGO, "TotalToday", today.ToString("N0"), 1, DataGrid.ProfitGreen);
        Fixed(rowGO, "TotalWeek", week.ToString("N0"), 2, DataGrid.ProfitGreen);
        Fixed(rowGO, "TotalStock", stock.ToString("N0"), 3, DataGrid.ProfitGreen);
        Fixed(rowGO, "TotalSellT", sellT.ToString("N0"), 4, DataGrid.ProfitGreen);
        Fixed(rowGO, "TotalSellW", sellW.ToString("N0"), 5, DataGrid.ProfitGreen);
        Fixed(rowGO, "TotalProdT", prodT.ToString("N0") + (prodPartial ? "+" : ""), 6, DataGrid.ProfitGreen);
        Fixed(rowGO, "TotalProdY", prodY.ToString("N0") + (prodPartial ? "+" : ""), 7, DataGrid.ProfitGreen);
        Fixed(rowGO, "TotalProdW", prodW.ToString("N0") + (prodPartial ? "+" : ""), 8, DataGrid.ProfitGreen);
        Color diffT, diffY, diffW;
        bool totExact = !prodPartial;
        Fixed(rowGO, "TotalDiffT", DiffText(prodT, totExact, sellT, out diffT), 9, diffT);
        Fixed(rowGO, "TotalDiffY", DiffText(prodY, totExact, sellY, out diffY), 10, diffY);
        Fixed(rowGO, "TotalDiffW", DiffText(prodW, totExact, sellW, out diffW), 11, diffW);
    }

    // Over/under production: (manufactured - sold) / sold. NaN = no verdict
    // (unattributable production, or nothing made and nothing sold) and
    // sorts first; +Infinity = produced with zero sales (pure stock build).
    static double DiffValue(long prod, bool exact, long sell)
    {
        if (!exact) return double.NaN;
        if (sell == 0) return prod == 0 ? double.NaN : double.PositiveInfinity;
        return (prod - sell) * 100.0 / sell;
    }

    static string DiffText(long prod, bool exact, long sell, out Color color)
    {
        if (!exact) { color = DataGrid.InkGrey; return "..."; }
        if (sell == 0)
        {
            if (prod == 0) { color = DataGrid.InkGrey; return "-"; }
            color = DataGrid.ProfitGreen;
            return "+\u221E";
        }
        double v = (prod - sell) * 100.0 / sell;
        if (v > 0) color = DataGrid.ProfitGreen;
        else if (v < 0) color = DataGrid.WarnRed;
        else color = DataGrid.InkLight;
        long r = (long)Math.Round(v);
        return (r >= 0 ? "+" : "") + r.ToString("N0") + "%";
    }

    protected override void RenderRow(GameObject rowGO, FactoryPanel.ProductRow p)
    {
        NameCell(rowGO, "RowName", p.productName, DataGrid.InkLight);
        Fixed(rowGO, "RowLines", p.lines.ToString("N0"), 0, DataGrid.InkLight);
        Fixed(rowGO, "RowToday", p.today.ToString("N0"), 1, DataGrid.InkLight);
        Fixed(rowGO, "RowWeek", p.week.ToString("N0"), 2, DataGrid.InkLight);
        Fixed(rowGO, "RowStock", p.stock.ToString("N0"), 3, DataGrid.InkLight);
        Fixed(rowGO, "RowSellT", p.sellToday.ToString("N0"), 4, DataGrid.InkLight);
        Fixed(rowGO, "RowSellW", p.sellWeek.ToString("N0"), 5, DataGrid.InkLight);
        if (p.prodTodayExact)
            Fixed(rowGO, "RowProdT", p.prodToday.ToString("N0"), 6, DataGrid.InkLight);
        else
            Fixed(rowGO, "RowProdT", "...", 6, DataGrid.InkGrey);
        if (p.prodYdayExact)
            Fixed(rowGO, "RowProdY", p.prodYday.ToString("N0"), 7, DataGrid.InkLight);
        else
            Fixed(rowGO, "RowProdY", "...", 7, DataGrid.InkGrey);
        if (p.prodWeekExact)
            Fixed(rowGO, "RowProdW", p.prodWeek.ToString("N0"), 8, DataGrid.InkLight);
        else
            Fixed(rowGO, "RowProdW", "...", 8, DataGrid.InkGrey);
        Color diffT, diffY, diffW;
        Fixed(rowGO, "RowDiffT", DiffText(p.prodToday, p.prodTodayExact, p.sellToday, out diffT), 9, diffT);
        Fixed(rowGO, "RowDiffY", DiffText(p.prodYday, p.prodYdayExact, p.sellYday, out diffY), 10, diffY);
        Fixed(rowGO, "RowDiffW", DiffText(p.prodWeek, p.prodWeekExact, p.sellWeek, out diffW), 11, diffW);
    }

    // Row cells come from the TablePanel base (shared NameCell/Fixed).
}
