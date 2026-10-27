using System;
using System.Collections.Generic;
using UnityEngine;
using BAModAPI;
using Buildings.Office.Headquarters;
using Entities;
using static TheBigBoard.DataGrid;

namespace TheBigBoard;

/// <summary>TRANSFER table: one row per (destination x product) route in the
/// selected logistics plan. DESTINATION + PRODUCT on the left, DEST STOCK
/// (current units at the target), MIN STOCK (the plan target: click a row
/// to edit it, 0 removes the product), TO SEND (what the next run - or
/// DELIVER NOW - will move: max(0, target - destStock), clamped by source
/// stock; for exporter destinations min(target, sourceStock) since export
/// sells a fixed amount), SRC STOCK (units available at the source) and
/// EXPORT $ (expected revenue for exporter rows).</summary>
public sealed class TransferTable : TablePanel<TransferTable.TransferRow>
{
    public sealed class TransferRow
    {
        public string destName;
        public string productName;
        public string itemName;
        public long destStock;
        public long target;
        public long toSend;
        public long srcStock;
        public bool exporter;
        public float exportPrice;
        public LogisticsManagerPlanDestination dest;
    }

    static readonly string[] HeadNames = { "DESTINATION", "PRODUCT",
        "DEST STOCK", "MIN STOCK", "TO SEND", "SRC STOCK", "EXPORT $" };

    static readonly TMPro.HorizontalAlignmentOptions[] ColAlign =
    {
        TMPro.HorizontalAlignmentOptions.Left,
        TMPro.HorizontalAlignmentOptions.Right, TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right, TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
    };

    static readonly float[] DefaultWidths = { 170f, 150f, 105f, 105f, 105f, 105f };

    public Action<TransferRow> OnEdit;

    string _totalLabel = "";
    long _totalSend;
    double _totalExportValue;

    public TransferTable()
        : base("transfer", HeadNames, DefaultWidths, ColAlign, 40f, 40f, 10f,
            24, null, 0, true)
    {
    }

    public void SetTotals(string label, long send, double exportValue)
    {
        _totalLabel = label;
        _totalSend = send;
        _totalExportValue = exportValue;
    }

    protected override int CompareRows(TransferRow a, TransferRow b, int col)
    {
        int c = 0;
        switch (col)
        {
            case 0: c = string.Compare(a.destName, b.destName, StringComparison.Ordinal); break;
            case 1: c = string.Compare(a.productName, b.productName, StringComparison.Ordinal); break;
            case 2: c = a.destStock.CompareTo(b.destStock); break;
            case 3: c = a.target.CompareTo(b.target); break;
            case 4: c = a.toSend.CompareTo(b.toSend); break;
            case 5: c = a.srcStock.CompareTo(b.srcStock); break;
            case 6: c = ExportValue(a).CompareTo(ExportValue(b)); break;
        }
        if (c == 0) c = string.Compare(a.destName, b.destName, StringComparison.Ordinal);
        if (c == 0) c = string.Compare(a.productName, b.productName, StringComparison.Ordinal);
        return c;
    }

    static double ExportValue(TransferRow r)
    {
        if (r == null || !r.exporter) return 0;
        return (double)r.toSend * r.exportPrice;
    }

    protected override void RenderTotalRow(List<TransferRow> rows)
    {
        var rowGO = DataGrid.AddRowRoot(RowsRoot, "TransferTotalRow", DataGrid.TotalBg);
        AddCells(rowGO, _totalLabel, "", "", "", _totalSend.ToString("N0"), "",
            _totalExportValue > 0 ? _totalExportValue.ToString("N0") : "",
            DataGrid.ProfitGreen, DataGrid.ProfitGreen);
    }

    protected override void RenderRow(GameObject rowGO, TransferRow r)
    {
        string exportText = r.exporter
            ? (ExportValue(r) > 0 ? ExportValue(r).ToString("N0") : "0")
            : "-";
        Color sendColor = r.toSend > 0 ? DataGrid.ProfitGreen : DataGrid.InkGrey;
        AddCells(rowGO, r.destName, r.productName,
            r.destStock.ToString("N0"), r.target.ToString("N0"),
            r.toSend.ToString("N0"), r.srcStock.ToString("N0"), exportText,
            DataGrid.InkLight, sendColor);
    }

    void AddCells(GameObject rowGO, string dest, string product,
        string destStock, string target, string toSend, string srcStock,
        string exportText, Color nameColor, Color numColor)
    {
        var right = TMPro.HorizontalAlignmentOptions.Right;
        DataGrid.AddNameCell(rowGO, "TransferRowDest", dest, Widths,
            PadL, PadR, Gap, 30, nameColor,
            TMPro.HorizontalAlignmentOptions.Left);
        Fixed(rowGO, "TransferRowProduct", product, 0, 30,
            TMPro.HorizontalAlignmentOptions.Left);
        Fixed(rowGO, "TransferRowDestStock", destStock, 1, 30, numColor, right);
        Fixed(rowGO, "TransferRowTarget", target, 2, 30, numColor, right);
        Fixed(rowGO, "TransferRowToSend", toSend, 3, 30, numColor, right);
        Fixed(rowGO, "TransferRowSrcStock", srcStock, 4, 30, numColor, right);
        Fixed(rowGO, "TransferRowExport", exportText, 5, 30, numColor, right);
    }

    TMPro.TextMeshProUGUI Fixed(GameObject parent, string goName, string value, int index,
        int fontSize, TMPro.HorizontalAlignmentOptions align)
    {
        return DataGrid.AddFixedCell(parent, goName, value, Widths, index,
            PadR, Gap, fontSize, DataGrid.InkLight, align);
    }

    TMPro.TextMeshProUGUI Fixed(GameObject parent, string goName, string value, int index,
        int fontSize, Color color, TMPro.HorizontalAlignmentOptions align)
    {
        return DataGrid.AddFixedCell(parent, goName, value, Widths, index,
            PadR, Gap, fontSize, color, align);
    }

    protected override string EmptyText
    {
        get { return "No routes."; }
    }

    protected override int InfoFontSize
    {
        get { return 32; }
    }

    protected override bool RowClickable(TransferRow row)
    {
        return true;
    }

    protected override void OnRowClicked(TransferRow row)
    {
        try
        {
            if (OnEdit != null) OnEdit(row);
        }
        catch { }
    }
}
