using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BAModAPI;
using static TheBigBoard.DataGrid;

namespace TheBigBoard;

/// <summary>BEST SELLERS table: one row per product sold in the selected time
/// range - units sold (retail), units produced (factories), revenue, profit
/// (revenue minus wholesale cost) and the top-selling store. Like every other
/// table it derives from TablePanel, so header sorting + resize come from the
/// base; the owning panel (ModPanel) keeps only chrome + data fetching.</summary>
public sealed class BestSellersTable : TablePanel<BestSellersTable.BestRow>
{
    public sealed class BestRow
    {
        public string name;
        public long sold;
        public long produced;
        public double revenue;
        public double profit;
        public string topStore;
    }

    static readonly string[] HeadNames =
        { "PRODUCT", "SOLD", "PRODUCED", "REVENUE", "PROFIT", "TOP STORE" };

    static readonly TMPro.HorizontalAlignmentOptions[] ColAlign =
    {
        TMPro.HorizontalAlignmentOptions.Right, // SOLD
        TMPro.HorizontalAlignmentOptions.Right, // PRODUCED
        TMPro.HorizontalAlignmentOptions.Right, // REVENUE
        TMPro.HorizontalAlignmentOptions.Right, // PROFIT
        TMPro.HorizontalAlignmentOptions.Left, // TOP STORE (text)
    };

    static readonly float[] DefaultWidths = { 150f, 160f, 180f, 180f, 300f };

    string _totalLabel = "";
    long _totalSold;
    long _totalProduced;
    double _totalRevenue;
    double _totalProfit;

    public BestSellersTable()
        : base("bestsellers", HeadNames, DefaultWidths, ColAlign, 80f, 80f, 10f,
            28, null, -1, true)
    {
    }

    /// <summary>Pushes pre-aggregated product rows in (panel owns the scan).
    /// Totals are summed here so the total row stays consistent.</summary>
    public void ShowResults(List<BestRow> rows, string totalLabel)
    {
        long sold = 0;
        long produced = 0;
        double revenue = 0;
        double profit = 0;
        if (rows != null)
        {
            foreach (var r in rows)
            {
                if (r == null) continue;
                sold += r.sold;
                produced += r.produced;
                revenue += r.revenue;
                profit += r.profit;
            }
        }
        _totalLabel = totalLabel ?? "";
        _totalSold = sold;
        _totalProduced = produced;
        _totalRevenue = revenue;
        _totalProfit = profit;
        SetRows(rows);
    }

    protected override int CompareRows(BestRow a, BestRow b, int col)
    {
        int c;
        switch (col)
        {
            case 1: c = a.sold.CompareTo(b.sold); break;
            case 2: c = a.produced.CompareTo(b.produced); break;
            case 3: c = a.revenue.CompareTo(b.revenue); break;
            case 4: c = a.profit.CompareTo(b.profit); break;
            case 5: c = string.Compare(a.topStore, b.topStore, StringComparison.Ordinal); break;
            default: c = string.Compare(a.name, b.name, StringComparison.Ordinal); break;
        }
        if (c == 0) c = string.Compare(a.name, b.name, StringComparison.Ordinal);
        return c;
    }

    protected override void RenderTotalRow(List<BestRow> rows)
    {
        var rowGO = DataGrid.AddRowRoot(RowsRoot, "BestTotalRow", DataGrid.TotalBg);
        AddCells(rowGO, _totalLabel, _totalSold, _totalProduced,
            _totalRevenue, _totalProfit, "-",
            DataGrid.ProfitGreen, DataGrid.ProfitGreen);
    }

    protected override void RenderRow(GameObject rowGO, BestRow r)
    {
        Color profitColor = r.profit > 0 ? DataGrid.ProfitGreen
            : r.profit < 0 ? DataGrid.LossRed : DataGrid.InkLight;
        var right = TMPro.HorizontalAlignmentOptions.Right;
        DataGrid.AddNameCell(rowGO, "BestRowName", r.name, Widths,
            PadL, PadR, Gap, 30, DataGrid.InkLight,
            TMPro.HorizontalAlignmentOptions.Left);
        DataGrid.AddFixedCell(rowGO, "BestRowSold", r.sold.ToString("N0"), Widths, 0,
            PadR, Gap, 30, DataGrid.InkLight, right);
        DataGrid.AddFixedCell(rowGO, "BestRowProduced", r.produced.ToString("N0"), Widths, 1,
            PadR, Gap, 30, DataGrid.InkLight, right);
        DataGrid.AddFixedCell(rowGO, "BestRowRevenue", Money(r.revenue), Widths, 2,
            PadR, Gap, 30, DataGrid.InkLight, right);
        DataGrid.AddFixedCell(rowGO, "BestRowProfit", Money(r.profit), Widths, 3,
            PadR, Gap, 30, profitColor, right);
        DataGrid.AddFixedCell(rowGO, "BestRowTopStore", r.topStore ?? "-", Widths, 4,
            PadR, Gap, 28, DataGrid.InkLight,
            TMPro.HorizontalAlignmentOptions.Left);
    }

    void AddCells(GameObject rowGO, string name, long sold, long produced,
        double revenue, double profit, string topStore,
        Color nameColor, Color numColor)
    {
        var right = TMPro.HorizontalAlignmentOptions.Right;
        DataGrid.AddNameCell(rowGO, "BestTotalName", name, Widths,
            PadL, PadR, Gap, 30, nameColor,
            TMPro.HorizontalAlignmentOptions.Left);
        DataGrid.AddFixedCell(rowGO, "BestTotalSold", sold.ToString("N0"), Widths, 0,
            PadR, Gap, 30, numColor, right);
        DataGrid.AddFixedCell(rowGO, "BestTotalProduced", produced.ToString("N0"), Widths, 1,
            PadR, Gap, 30, numColor, right);
        DataGrid.AddFixedCell(rowGO, "BestTotalRevenue", Money(revenue), Widths, 2,
            PadR, Gap, 30, numColor, right);
        DataGrid.AddFixedCell(rowGO, "BestTotalProfit", Money(profit), Widths, 3,
            PadR, Gap, 30, numColor, right);
        DataGrid.AddFixedCell(rowGO, "BestTotalTopStore", topStore, Widths, 4,
            PadR, Gap, 28, numColor,
            TMPro.HorizontalAlignmentOptions.Left);
    }

    static string Money(double v)
    {
        return BusinessesPanel.FormatMoney((float)v);
    }

    protected override string EmptyText
    {
        get { return "No sales found."; }
    }

    protected override int InfoFontSize
    {
        get { return 32; }
    }
}
