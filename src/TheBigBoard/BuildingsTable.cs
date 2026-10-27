using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BAModAPI;
using Buildings;
using Streets;
using static TheBigBoard.DataGrid;

namespace TheBigBoard;

/// <summary>TAB 5 table: one row per non-special building registration.
/// Columns: ADDRESS | DISTRICT | BUILDING TYPE | SIZE | SQM | TRAFFIC |
/// CAPACITY | STATUS | PRICE | BUSINESS | OWNER. All headers sort.
/// Click a row to open the building on the city map.</summary>
public sealed class BuildingsTable : TablePanel<BuildingsTable.BRow>
{
    public sealed class BRow
    {
        public BuildingRegistration reg;
        public Address mapAddress;
        public int price;
        public string address;
        public string district;
        public string districtName;
        public string btype;
        public string btypeName;
        public string size;
        public int sqm;
        public int traffic;
        public int capacity;
        public string status;
        public string business;
        public string owner;
    }

    static readonly string[] HeadNames = { "ADDRESS", "DISTRICT", "BUILDING TYPE",
        "SIZE", "SQM", "TRAFFIC", "CAPACITY", "STATUS", "PRICE", "BUSINESS", "OWNER" };

    // Header alignment matches the row cells (left for text, right for
    // numbers, center for short codes) so headers sit over their columns.
    static readonly TMPro.HorizontalAlignmentOptions[] ColAlign =
    {
        TMPro.HorizontalAlignmentOptions.Left, TMPro.HorizontalAlignmentOptions.Left,
        TMPro.HorizontalAlignmentOptions.Left, TMPro.HorizontalAlignmentOptions.Center,
        TMPro.HorizontalAlignmentOptions.Right, TMPro.HorizontalAlignmentOptions.Center,
        TMPro.HorizontalAlignmentOptions.Right, TMPro.HorizontalAlignmentOptions.Center,
        TMPro.HorizontalAlignmentOptions.Right, TMPro.HorizontalAlignmentOptions.Left,
        TMPro.HorizontalAlignmentOptions.Left,
    };

    static readonly float[] DefaultWidths = { 200f, 80f, 110f, 100f, 140f, 150f, 110f, 150f, 180f, 200f };

    public BuildingsTable()
        : base("buildings", HeadNames, DefaultWidths, ColAlign, 40f, 40f, 10f,
            22, null, 1, true)
    {
    }

    protected override int MaxRows
    {
        get { return 300; }
    }

    protected override void OnSortChanged(int col, bool asc)
    {
        Context?.Logger.Info("The Big Board buildings sort: " + Headers[col] +
            (asc ? " asc." : " desc."));
    }

    protected override int CompareRows(BRow a, BRow b, int col)
    {
        int c = 0;
        switch (col)
        {
            case 0: c = string.Compare(a.address, b.address, StringComparison.Ordinal); break;
            case 1: c = string.Compare(a.districtName, b.districtName, StringComparison.Ordinal); break;
            case 2: c = string.Compare(a.btypeName, b.btypeName, StringComparison.Ordinal); break;
            case 3: c = string.Compare(a.size, b.size, StringComparison.Ordinal); break;
            case 4: c = a.sqm.CompareTo(b.sqm); break;
            case 5: c = a.traffic.CompareTo(b.traffic); break;
            case 6: c = a.capacity.CompareTo(b.capacity); break;
            case 7: c = string.Compare(a.status, b.status, StringComparison.Ordinal); break;
            case 8: c = a.price.CompareTo(b.price); break;
            case 9: c = string.Compare(a.business, b.business, StringComparison.Ordinal); break;
            case 10: c = string.Compare(a.owner, b.owner, StringComparison.Ordinal); break;
        }
        if (c == 0) c = string.Compare(a.address, b.address, StringComparison.Ordinal);
        return c;
    }

    protected override void RenderTotalRow(List<BRow> rows)
    {
        var rowGO = DataGrid.AddRowRoot(RowsRoot, "BldTotalRow", DataGrid.TotalBg);
        Cell(rowGO, "BldTotalName", "TOP (" + rows.Count + " rows)", 26, DataGrid.ProfitGreen);
        Fixed(rowGO, "BldTotalD", "-", 0, 26, DataGrid.ProfitGreen, ColAlign[1]);
        Fixed(rowGO, "BldTotalT", "-", 1, 26, DataGrid.ProfitGreen, ColAlign[2]);
        Fixed(rowGO, "BldTotalSz", "-", 2, 26, DataGrid.ProfitGreen, ColAlign[3]);
        Fixed(rowGO, "BldTotalSq", "-", 3, 26, DataGrid.ProfitGreen, ColAlign[4]);
        Fixed(rowGO, "BldTotalTr", "-", 4, 26, DataGrid.ProfitGreen, ColAlign[5]);
        Fixed(rowGO, "BldTotalCap", "-", 5, 26, DataGrid.ProfitGreen, ColAlign[6]);
        Fixed(rowGO, "BldTotalStatus", "-", 6, 26, DataGrid.ProfitGreen, ColAlign[7]);
        Fixed(rowGO, "BldTotalPrice", "-", 7, 26, DataGrid.ProfitGreen, ColAlign[8]);
        Fixed(rowGO, "BldTotalBiz", "-", 8, 26, DataGrid.ProfitGreen, ColAlign[9]);
        Fixed(rowGO, "BldTotalO", "-", 9, 26, DataGrid.ProfitGreen, ColAlign[10]);
    }

    protected override void RenderRow(GameObject rowGO, BRow r)
    {
        Cell(rowGO, "BldRowAddr", r.address, 24, DataGrid.InkLight);
        Fixed(rowGO, "BldRowDist", r.districtName, 0, 24, DataGrid.InkGrey, ColAlign[1]);
        Fixed(rowGO, "BldRowType", r.btypeName, 1, 24, DataGrid.InkLight, ColAlign[2]);
        Fixed(rowGO, "BldRowSize", r.size, 2, 24, DataGrid.InkLight, ColAlign[3]);
        Fixed(rowGO, "BldRowSqm", r.sqm > 0 ? r.sqm.ToString("N0") : "-", 3, 24, DataGrid.InkLight, ColAlign[4]);
        Fixed(rowGO, "BldRowTr", r.traffic.ToString(), 4, 24, DataGrid.InkLight, ColAlign[5]);
        Fixed(rowGO, "BldRowCap", r.capacity > 0 ? r.capacity.ToString("N0") : "-", 5, 24, DataGrid.InkLight, ColAlign[6]);
        Fixed(rowGO, "BldRowStatus", r.status, 6, 24, DataGrid.InkLight, ColAlign[7]);
        Fixed(rowGO, "BldRowPrice", r.price > 0 ? r.price.ToString("N0") : "-", 7, 24, DataGrid.InkLight, ColAlign[8]);
        Fixed(rowGO, "BldRowBiz", r.business, 8, 24, DataGrid.InkLight, ColAlign[9]);
        Fixed(rowGO, "BldRowOwner", r.owner, 9, 24, DataGrid.InkLight, ColAlign[10]);
    }

    TMPro.TextMeshProUGUI Cell(GameObject parent, string goName, string value,
        int fontSize, Color color)
    {
        return DataGrid.AddNameCell(parent, goName, value, Widths,
            PadL, PadR, Gap, fontSize, color);
    }

    TMPro.TextMeshProUGUI Fixed(GameObject parent, string goName, string value, int index,
        int fontSize, Color color, TMPro.HorizontalAlignmentOptions align)
    {
        return DataGrid.AddFixedCell(parent, goName, value, Widths, index,
            PadR, Gap, fontSize, color, align);
    }

    protected override string EmptyText
    {
        get
        {
            return "No known buildings. Open BizMan / Market Insider once, then refresh." +
                " / No match for the current filters.";
        }
    }

    protected override bool RowClickable(BRow row)
    {
        return true;
    }

    protected override void OnRowClicked(BRow r)
    {
        GoToBuilding(r.mapAddress, r.address);
    }

    /// <summary>Closes the phone and opens the city map focused on the
    /// building, as if the player clicked it on the map: the camera moves
    /// to it and its card is selected (CityMap.Toggle handles a set
    /// buildingToFocus exactly this way).</summary>
    void GoToBuilding(Address addr, string label)
    {
        try
        {
            if (addr == null || addr.IsUndefined()) return;
            var cityMgr = InstanceBehavior<CityManager>.Instance;
            if (cityMgr == null || cityMgr.cityMap == null) return;
            var controller = cityMgr.FindCityBuildingController(addr);
            if (controller == null)
            {
                Context?.Logger.Warn("The Big Board buildings go-to: no scene controller for " + label);
                return;
            }
            var uis = UI.UIs.Instance;
            if (uis != null && uis.fullMenu != null) uis.fullMenu.Toggle(false);
            var map = cityMgr.cityMap;
            map.buildingToFocus = controller;
            if (!CityMap.IsOpen) map.Toggle();
            Context?.Logger.Info("The Big Board buildings go-to map: " + label);
        }
        catch (Exception ex)
        {
            Context?.Logger.Warn("The Big Board buildings go-to failed: " + ex.Message);
        }
    }
}
