using System;
using System.Collections.Generic;
using UnityEngine;
using static TheBigBoard.DataGrid;

namespace TheBigBoard;

/// <summary>Shop picker table (schedule copy popup). Derives from
/// TablePanel, so all headers sort and columns are draggable for free.
/// A row click picks the shop as copy BASE or TARGET (via callback).</summary>
public sealed class ShopPickTable : TablePanel<ShopPickTable.Row>
{
    public sealed class Row
    {
        public string name;
        public string type;
        public int openDays;
        public int shifts;
        public int staff;
        public bool selected;
        public BuildingRegistration reg;
    }

    static readonly string[] HeadNames = { "SHOP", "TYPE", "OPEN", "SHIFTS", "STAFF" };

    static readonly TMPro.HorizontalAlignmentOptions[] ColAlign =
    {
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
    };

    // TYPE, OPEN, SHIFTS, STAFF fixed widths; SHOP flexes into the rest.
    static readonly float[] DefaultWidths = { 260f, 110f, 140f, 130f };

    readonly Action<BuildingRegistration> _onPick;

    public ShopPickTable(Action<BuildingRegistration> onPick)
        : base("copyShopPick", HeadNames, DefaultWidths, ColAlign, 40f, 40f, 20f,
            26, null, 0, true)
    {
        _onPick = onPick;
    }

    protected override int CompareRows(Row a, Row b, int col)
    {
        int c = 0;
        switch (col)
        {
            case 0: c = string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase); break;
            case 1: c = string.Compare(a.type, b.type, StringComparison.OrdinalIgnoreCase); break;
            case 2: c = a.openDays.CompareTo(b.openDays); break;
            case 3: c = a.shifts.CompareTo(b.shifts); break;
            case 4: c = a.staff.CompareTo(b.staff); break;
        }
        if (c == 0) c = string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
        return c;
    }

    protected override void RenderRow(GameObject rowGO, Row r)
    {
        Color ink = r.selected ? Color.white : InkLight;
        string name = r.selected ? ">> " + r.name : r.name;
        NameCell(rowGO, "RowName", name, ink);
        Fixed(rowGO, "RowType", r.type, 0, ink);
        Fixed(rowGO, "RowOpen", r.openDays + "/7", 1, ink);
        Fixed(rowGO, "RowShifts", r.shifts.ToString("N0"), 2, ink);
        Fixed(rowGO, "RowStaff", r.staff.ToString("N0"), 3, ink);
    }

    protected override bool RowClickable(Row row)
    {
        return true;
    }

    protected override void OnRowClicked(Row row)
    {
        try { if (_onPick != null && row != null) _onPick(row.reg); }
        catch { }
    }

    protected override string EmptyText
    {
        get { return "No shops found."; }
    }

    TMPro.TextMeshProUGUI NameCell(GameObject parent, string goName, string value, Color color)
    {
        return DataGrid.AddNameCell(parent, goName, value, Widths,
            PadL, PadR, Gap, 30, color, TMPro.HorizontalAlignmentOptions.Left);
    }

    TMPro.TextMeshProUGUI Fixed(GameObject parent, string goName, string value, int index, Color color)
    {
        // Row cells align like the rest of the mod: text left, numbers right.
        var align = index == 0
            ? TMPro.HorizontalAlignmentOptions.Left
            : TMPro.HorizontalAlignmentOptions.Right;
        return DataGrid.AddFixedCell(parent, goName, value, Widths, index,
            PadR, Gap, 30, color, align);
    }
}
