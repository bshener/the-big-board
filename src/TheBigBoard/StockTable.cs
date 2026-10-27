using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BAModAPI;
using static TheBigBoard.DataGrid;

namespace TheBigBoard;

/// <summary>STOCK table: current per-building stock for a product search,
/// plus TODAY / YESTERDAY / LAST WEEK grouped columns with IN / OUT / NET
/// flows. Warehouses track IN via deliveryTransactions; other buildings show
/// numbers once the ledger covers them, "..." while history accumulates.</summary>
public sealed class StockTable : TablePanel<StockTable.StockRowData>
{
    public sealed class StockRowData
    {
        public string name;
        public long stock;
        public ModPanel.StockMoves moves;
        public bool warehouse;
    }

    static readonly string[] HeadNames = { "LOCATION", "STOCK",
        "IN", "OUT", "NET", "IN", "OUT", "NET", "IN", "OUT", "NET" };

    // LOCATION left, STOCK right, flow triples centered under their groups.
    static readonly TMPro.HorizontalAlignmentOptions[] ColAlign =
    {
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Center, TMPro.HorizontalAlignmentOptions.Center,
        TMPro.HorizontalAlignmentOptions.Center, TMPro.HorizontalAlignmentOptions.Center,
        TMPro.HorizontalAlignmentOptions.Center, TMPro.HorizontalAlignmentOptions.Center,
        TMPro.HorizontalAlignmentOptions.Center, TMPro.HorizontalAlignmentOptions.Center,
        TMPro.HorizontalAlignmentOptions.Center,
    };

    static readonly float[] DefaultWidths = { 130f, 100f, 100f, 100f, 100f, 100f, 100f, 100f, 100f, 100f };

    static readonly DataGrid.GroupDef[] GroupDefs =
    {
        new DataGrid.GroupDef("TODAY", 1, 3),
        new DataGrid.GroupDef("YESTERDAY", 4, 3),
        new DataGrid.GroupDef("LAST WEEK", 7, 3),
    };

    string _totalLabel = "";
    long _totalStock;
    ModPanel.StockMoves _totalMoves = new ModPanel.StockMoves();

    public StockTable()
        : base("stock", HeadNames, DefaultWidths, ColAlign, 40f, 40f, 10f,
            24, GroupDefs, -1, true)
    {
    }

    /// <summary>Rebuilds rows from the search result dicts (owned by the
    /// panel), then sorts + renders via the base.</summary>
    public void ShowResults(int day,
        Dictionary<string, Dictionary<string, long>> warehouses,
        Dictionary<string, Dictionary<string, ModPanel.StockMoves>> moves,
        Dictionary<string, HashSet<string>> relevant,
        HashSet<string> isWarehouse,
        string selectedKey,
        Dictionary<string, KeyValuePair<string, long>> products,
        string raw)
    {
        var dayCell = GroupNameCell();
        if (dayCell != null)
        {
            int since = StockHistory.FirstDay;
            dayCell.text = since >= 0
                ? ("DAY " + day + " | TRACKING SINCE " + since)
                : ("DAY " + day + " | TRACKING STARTS TONIGHT");
        }
        if (warehouses == null || warehouses.Count == 0)
        {
            ShowInfo("No own buildings found.", 32);
            return;
        }
        var rows = new List<StockRowData>();
        long totalStock = 0;
        var totalMove = new ModPanel.StockMoves();
        // Union with movement-only buildings: a store that received and sold
        // everything (stock 0) still shows its flows. Plus raw-relevant
        // buildings: a factory needing fabric with empty shelves still shows
        // its zero row for the selected product (or ALL when it needs any).
        var allNames = new HashSet<string>();
        foreach (var k in warehouses.Keys) allNames.Add(k);
        if (moves != null)
            foreach (var k in moves.Keys) allNames.Add(k);
        if (relevant != null)
        {
            if (selectedKey != null)
            {
                HashSet<string> rel;
                if (relevant.TryGetValue(selectedKey, out rel) && rel != null)
                    foreach (var n in rel) allNames.Add(n);
            }
            else
            {
                foreach (var rel in relevant.Values)
                {
                    if (rel == null) continue;
                    foreach (var n in rel) allNames.Add(n);
                }
            }
        }
        if (allNames.Count == 0)
        {
            ShowInfo("No stock found for '" + raw + "'.", 32);
            return;
        }
        foreach (var bname in allNames)
        {
            long stock = 0;
            var agg = new ModPanel.StockMoves();
            bool isWh = isWarehouse != null && isWarehouse.Contains(bname);
            Dictionary<string, long> byProd = null;
            Dictionary<string, ModPanel.StockMoves> byMove = null;
            bool hasProd = warehouses.TryGetValue(bname, out byProd) && byProd != null;
            bool hasMove = moves != null && moves.TryGetValue(bname, out byMove) && byMove != null;
            if (selectedKey == null)
            {
                if (hasProd) foreach (var p in byProd) stock += p.Value;
                if (hasMove) foreach (var m in byMove.Values) ModPanel.AddMoves(agg, m);
            }
            else
            {
                if (hasProd && !byProd.TryGetValue(selectedKey, out stock)) stock = 0;
                if (hasMove)
                {
                    ModPanel.StockMoves m;
                    if (byMove.TryGetValue(selectedKey, out m)) ModPanel.AddMoves(agg, m);
                }
            }
            if (stock == 0 && agg.inToday == 0 && agg.outToday == 0
                && agg.inYest == 0 && agg.outYest == 0
                && agg.inWeek == 0 && agg.outWeek == 0
                && !IsRelevantRow(relevant, selectedKey, bname)) continue;
            rows.Add(new StockRowData { name = bname, stock = stock, moves = agg, warehouse = isWh });
            totalStock += stock;
            ModPanel.AddMoves(totalMove, agg);
        }
        string totalLabel;
        if (selectedKey == null)
        {
            totalLabel = "TOTAL (" + rows.Count + " locations)";
        }
        else
        {
            string rawKey = selectedKey;
            if (products != null)
            {
                KeyValuePair<string, long> prod;
                if (products.TryGetValue(selectedKey, out prod)) rawKey = prod.Key;
            }
            totalLabel = ModPanel.GetProductDisplayName(rawKey) + " (" + rows.Count + " locations)";
        }
        SetTotals(totalLabel, totalStock, totalMove);
        SetRows(rows);
    }

    static bool IsRelevantRow(Dictionary<string, HashSet<string>> relevant,
        string selectedKey, string bname)
    {
        if (relevant == null || string.IsNullOrEmpty(bname)) return false;
        try
        {
            if (selectedKey != null)
            {
                HashSet<string> rel;
                return relevant.TryGetValue(selectedKey, out rel) && rel != null && rel.Contains(bname);
            }
            foreach (var rel in relevant.Values)
            {
                if (rel != null && rel.Contains(bname)) return true;
            }
        }
        catch { }
        return false;
    }

    void SetTotals(string label, long stock, ModPanel.StockMoves moves)
    {
        _totalLabel = label;
        _totalStock = stock;
        _totalMoves = moves ?? new ModPanel.StockMoves();
    }

    static long Net(long inV, long outV, bool tracked)
    {
        return (tracked ? inV : 0) - outV;
    }

    protected override int CompareRows(StockRowData a, StockRowData b, int col)
    {
        int c = 0;
        switch (col)
        {
            case 0: c = string.Compare(a.name, b.name, StringComparison.Ordinal); break;
            case 1: c = a.stock.CompareTo(b.stock); break;
            case 2: c = a.moves.inToday.CompareTo(b.moves.inToday); break;
            case 3: c = a.moves.outToday.CompareTo(b.moves.outToday); break;
            case 4: c = Net(a.moves.inToday, a.moves.outToday, a.moves.covT).CompareTo(Net(b.moves.inToday, b.moves.outToday, b.moves.covT)); break;
            case 5: c = a.moves.inYest.CompareTo(b.moves.inYest); break;
            case 6: c = a.moves.outYest.CompareTo(b.moves.outYest); break;
            case 7: c = Net(a.moves.inYest, a.moves.outYest, a.moves.covY).CompareTo(Net(b.moves.inYest, b.moves.outYest, b.moves.covY)); break;
            case 8: c = a.moves.inWeek.CompareTo(b.moves.inWeek); break;
            case 9: c = a.moves.outWeek.CompareTo(b.moves.outWeek); break;
            case 10: c = Net(a.moves.inWeek, a.moves.outWeek, a.moves.covW).CompareTo(Net(b.moves.inWeek, b.moves.outWeek, b.moves.covW)); break;
        }
        if (c == 0) c = string.Compare(a.name, b.name, StringComparison.Ordinal);
        return c;
    }

    protected override void RenderTotalRow(List<StockRowData> rows)
    {
        var rowGO = DataGrid.AddRowRoot(RowsRoot, "StockTotalRow", DataGrid.TotalBg);
        AddCells(rowGO, _totalLabel, _totalStock, _totalMoves, true, true,
            DataGrid.ProfitGreen, DataGrid.ProfitGreen);
    }

    protected override void RenderRow(GameObject rowGO, StockRowData r)
    {
        AddCells(rowGO, r.name, r.stock, r.moves, r.warehouse, false,
            DataGrid.InkLight, DataGrid.InkLight);
    }

    void AddCells(GameObject rowGO, string name, long stock, ModPanel.StockMoves moves,
        bool isWarehouse, bool total, Color nameColor, Color numColor)
    {
        var right = TMPro.HorizontalAlignmentOptions.Right;
        var center = TMPro.HorizontalAlignmentOptions.Center;
        DataGrid.AddNameCell(rowGO, "StockRowName", name, Widths,
            PadL, PadR, Gap, 30, nameColor,
            TMPro.HorizontalAlignmentOptions.Left);
        Fixed(rowGO, "StockRowQty", stock.ToString("N0"), 0, 30, numColor, right);
        // IN states: exact number (ledger / unique route / wholesale) or
        // "..." while our ledger is still accumulating history for the bucket.
        // NET is always computable (OUT is tracked everywhere).
        // Group columns (IN / OUT / NET) are centered like their sub-headers;
        // only LOCATION + STOCK stay left/right aligned.
        Color inColor = total ? numColor : DataGrid.ProfitGreen;
        bool covT = total || isWarehouse || moves.covT;
        bool covY = total || isWarehouse || moves.covY;
        bool covW = total || isWarehouse || moves.covW;
        MoveCell(rowGO, "StockRowInT", 1, moves.inToday, covT, inColor, center);
        MoveCell(rowGO, "StockRowOutT", 2, moves.outToday, true, numColor, center);
        NetCell(rowGO, "StockRowNetT", 3, moves.inToday, moves.outToday, covT, numColor);
        MoveCell(rowGO, "StockRowInY", 4, moves.inYest, covY, inColor, center);
        MoveCell(rowGO, "StockRowOutY", 5, moves.outYest, true, numColor, center);
        NetCell(rowGO, "StockRowNetY", 6, moves.inYest, moves.outYest, covY, numColor);
        MoveCell(rowGO, "StockRowInW", 7, moves.inWeek, covW, inColor, center);
        MoveCell(rowGO, "StockRowOutW", 8, moves.outWeek, true, numColor, center);
        NetCell(rowGO, "StockRowNetW", 9, moves.inWeek, moves.outWeek, covW, numColor);
    }

    TMPro.TextMeshProUGUI Fixed(GameObject parent, string goName, string value, int index,
        int fontSize, Color color, TMPro.HorizontalAlignmentOptions align)
    {
        return DataGrid.AddFixedCell(parent, goName, value, Widths, index,
            PadR, Gap, fontSize, color, align);
    }

    void MoveCell(GameObject rowGO, string goName, int column, long value,
        bool tracked, Color color, TMPro.HorizontalAlignmentOptions align)
    {
        if (!tracked)
        {
            Fixed(rowGO, goName, "...", column, 28, DataGrid.InkGrey, align);
            return;
        }
        Fixed(rowGO, goName, value.ToString("N0"), column, 28, color, align);
    }

    void NetCell(GameObject rowGO, string goName, int column,
        long inV, long outV, bool inTracked, Color numColor)
    {
        long net = (inTracked ? inV : 0) - outV;
        Color c = net > 0 ? DataGrid.ProfitGreen : net < 0 ? DataGrid.LossRed : numColor;
        string text = (net > 0 ? "+" : "") + net.ToString("N0");
        Fixed(rowGO, goName, text, column, 28, c, TMPro.HorizontalAlignmentOptions.Center);
    }

    protected override string EmptyText
    {
        get { return "No stock found."; }
    }

    protected override int InfoFontSize
    {
        get { return 32; }
    }
}
