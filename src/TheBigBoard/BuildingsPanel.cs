using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BAModAPI;
using BigAmbitions.Rivals;
using Buildings;
using Localizor;
using Streets;
using static TheBigBoard.DataGrid;

namespace TheBigBoard;

/// <summary>
/// TAB 5 - BUILDINGS. A census of known buildings: one row per non-special
/// building registration (rentable, for sale, operating, vacant, yours).
/// No demand logic. Columns: ADDRESS | DISTRICT | BUILDING TYPE | SIZE |
/// SQM | TRAFFIC | CAPACITY | STATUS | PRICE | BUSINESS | OWNER. Filters:
/// listing pills (ALL/RENT/SALE/OPERATING/YOURS), district strip (right),
/// building-type pills (left). All headers are clickable for sorting.
/// Click a row to open the building on the city map (focuses it as if
/// clicked on the map).
/// </summary>
public static class BuildingsPanel
{
    const int MaxRows = 300;
    const string GlobalNeighborhood = "ba:neighborhood_global";
    const string SpecialBuildingType = "ba:buildingtype_special";

    static ModContext _context;
    static GameObject _page;
    static bool _tabVisible;
    static int _lastDay = -1;
    static string _selectedDistrict; // null = ALL districts
    static string _selectedBType; // null = ALL building types

    static TMPro.TMP_Text _countText;
    static TMPro.TMP_Text _dayText;
    static Transform _districtStrip;
    static TMPro.TMP_Text _districtLabel;
    static GameObject _filterCard;
    static Transform _leftCol;
    static GameObject _countGO;
    static GameObject _refreshGO;
    static TMPro.TMP_Text _btypeCountText;
    static readonly List<BTypePill> _btypePills = new List<BTypePill>();
    static readonly List<BuildingsTable.BRow> _rows = new List<BuildingsTable.BRow>();
    static readonly List<BuildingsTable.BRow> _allRows = new List<BuildingsTable.BRow>();
    static readonly Dictionary<string, int> _btypeCounts = new Dictionary<string, int>();
    static readonly Dictionary<string, int> _districtCounts = new Dictionary<string, int>();
    static int _facetAllType; // listings matching the district filter (for the left ALL pill)
    static int _facetAllDistrict; // listings matching the type filter (for the strip ALL tab)
    static readonly List<string> _districts = new List<string>();
    static readonly List<string> _btypes = new List<string>();
    static int _rentableCount;
    static int _saleCount;
    static int _operatingCount;
    static int _yoursCount;
    static int _unionCount;
    static int _listMode; // 0 = ALL, 1 = RENT, 2 = SALE, 3 = OPERATING, 4 = YOURS
    static readonly List<ListPill> _listPills = new List<ListPill>();
    static Transform _listRow;

    sealed class ListPill
    {
        public int mode;
        public Image bg;
        public TMPro.TMP_Text label;
    }

    static string SaleKey(string street, int num)
    {
        return (street != null ? street : "") + "#" + num;
    }

    sealed class BTypePill
    {
        public string key; // null = ALL
        public Image bg;
        public TMPro.TMP_Text label;
    }

    // Palette, Text/Rect/Stretch/Anchor, Warmup, scroll/header/row helpers:
    // DataGrid (using static above). Only the column geometry and building
    // logic stay here.

    // The table itself (columns, sorting, resize) is BuildingsTable, a
    // TablePanel<BRow> derivative. This panel keeps filters + data.
    static BuildingsTable _table;

    public static void BuildPage(GameObject page, ModContext context)
    {
        _page = page;
        _context = context;
        _tabVisible = false;
        _btypePills.Clear();
        _rows.Clear();
        _districts.Clear();
        _btypes.Clear();

        var leftGO = new GameObject("BldLeft");
        leftGO.transform.SetParent(page.transform, false);
        Anchor(leftGO, 0f, 0f, 0.30f, 1f, 0f, 0f, -24f, 0f);
        var leftLayout = leftGO.AddComponent<VerticalLayoutGroup>();
        leftLayout.spacing = 14f;
        leftLayout.childControlWidth = true;
        leftLayout.childControlHeight = false;
        leftLayout.childForceExpandWidth = true;
        leftLayout.childForceExpandHeight = false;

        _btypeCountText = Text("BldTypeLabel", "BUILDING TYPE", 26,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, InkGrey);
        _btypeCountText.transform.SetParent(leftGO.transform, false);
        var btypeLabelSize = _btypeCountText.gameObject.AddComponent<LayoutElement>();
        btypeLabelSize.minHeight = 50f;

        // Type pills live directly in the left column (fixed height each -
        // same proven pattern as the old threshold pills). No nested scroll:
        // a ScrollRect inside this layout collapsed and hid the pills.
        _leftCol = leftGO.transform;

        // (No free-text search: removed per user request.)

        _countText = Text("BldCount", "", 26,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Top, InkGrey);
        _countText.transform.SetParent(leftGO.transform, false);
        var countSize = _countText.gameObject.AddComponent<LayoutElement>();
        countSize.minHeight = 170f;
        _countText.textWrappingMode = TMPro.TextWrappingModes.Normal;
        _countGO = _countText.gameObject;

        var refreshGO = new GameObject("BldRefresh");
        _refreshGO = refreshGO;
        refreshGO.transform.SetParent(leftGO.transform, false);
        var refreshBg = refreshGO.AddComponent<Image>();
        refreshBg.color = BtnIdle;
        var refreshSize = refreshGO.AddComponent<LayoutElement>();
        refreshSize.minHeight = 78f;
        var refreshBtn = refreshGO.AddComponent<Button>();
        refreshBtn.targetGraphic = refreshBg;
        var refreshLabel = Text("BldRefreshLabel", "REFRESH", 30,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle, InkLight);
        refreshLabel.transform.SetParent(refreshGO.transform, false);
        Stretch(refreshLabel.gameObject);
        refreshBtn.onClick.AddListener(() => Refresh());

        var rightGO = new GameObject("BldRight");
        rightGO.transform.SetParent(page.transform, false);
        Anchor(rightGO, 0.30f, 0f, 1f, 1f, 24f, 0f, 0f, 0f);

        var filterCardGO = new GameObject("BldFilterCard");
        _filterCard = filterCardGO;
        filterCardGO.transform.SetParent(rightGO.transform, false);
        Anchor(filterCardGO, 0f, 1f, 1f, 1f, 0f, -320f, 0f, 0f);
        var filterCardBg = filterCardGO.AddComponent<Image>();
        filterCardBg.color = new Color(0.12f, 0.14f, 0.18f, 1f);
        var filterCardOutline = filterCardGO.AddComponent<Outline>();
        filterCardOutline.effectColor = new Color(0.32f, 0.36f, 0.42f, 1f);
        filterCardOutline.effectDistance = new Vector2(2f, 2f);
        var filterCardLayout = filterCardGO.AddComponent<VerticalLayoutGroup>();
        filterCardLayout.padding = new RectOffset(18, 14, 18, 14);
        filterCardLayout.spacing = 8f;
        filterCardLayout.childControlWidth = true;
        filterCardLayout.childControlHeight = false;
        filterCardLayout.childForceExpandWidth = true;
        filterCardLayout.childForceExpandHeight = false;

        var listRowGO = new GameObject("BldListRow");
        listRowGO.transform.SetParent(filterCardGO.transform, false);
        var listRowSize = listRowGO.AddComponent<LayoutElement>();
        listRowSize.minHeight = 64f;
        var listRowLayout = listRowGO.AddComponent<HorizontalLayoutGroup>();
        listRowLayout.spacing = 10f;
        listRowLayout.childControlWidth = true;
        listRowLayout.childControlHeight = true;
        listRowLayout.childForceExpandWidth = true;
        listRowLayout.childForceExpandHeight = true;
        _listRow = listRowGO.transform;

        _districtLabel = Text("BldDistrictLabel", "DISTRICTS", 26,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, InkGrey);
        _districtLabel.transform.SetParent(filterCardGO.transform, false);
        var districtMatchSize = _districtLabel.gameObject.AddComponent<LayoutElement>();
        districtMatchSize.minHeight = 44f;
        _districtLabel.textWrappingMode = TMPro.TextWrappingModes.Normal;

        var stripScrollGO = new GameObject("BldStripScroll");
        stripScrollGO.transform.SetParent(filterCardGO.transform, false);
        var stripScrollSize = stripScrollGO.AddComponent<LayoutElement>();
        stripScrollSize.minHeight = 96f;
        stripScrollSize.flexibleWidth = 1f;
        var stripScroll = stripScrollGO.AddComponent<ScrollRect>();
        stripScroll.horizontal = true;
        stripScroll.vertical = false;
        stripScroll.movementType = ScrollRect.MovementType.Clamped;

        var stripViewportGO = new GameObject("BldStripViewport");
        stripViewportGO.transform.SetParent(stripScrollGO.transform, false);
        Stretch(stripViewportGO);
        stripViewportGO.AddComponent<RectMask2D>();

        var stripContentGO = new GameObject("BldDistricts");
        stripContentGO.transform.SetParent(stripViewportGO.transform, false);
        var stripContentRect = Rect(stripContentGO);
        stripContentRect.anchorMin = new Vector2(0f, 0f);
        stripContentRect.anchorMax = new Vector2(0f, 1f);
        stripContentRect.pivot = new Vector2(0f, 0.5f);
        stripContentRect.offsetMin = Vector2.zero;
        stripContentRect.offsetMax = Vector2.zero;
        var stripFitter = stripContentGO.AddComponent<ContentSizeFitter>();
        stripFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        stripFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        var stripList = stripContentGO.AddComponent<HorizontalLayoutGroup>();
        stripList.spacing = 10f;
        stripList.childControlWidth = true;
        stripList.childControlHeight = true;
        stripList.childForceExpandWidth = false;
        stripList.childForceExpandHeight = true;
        stripScroll.viewport = stripViewportGO.GetComponent<RectTransform>();
        stripScroll.content = stripContentRect;
        _districtStrip = stripContentGO.transform;

        _dayText = Text("BldDay", "DAY -", 24,
            TMPro.HorizontalAlignmentOptions.Left, TMPro.VerticalAlignmentOptions.Middle, InkGrey);
        _dayText.transform.SetParent(rightGO.transform, false);
        Anchor(_dayText.gameObject, 0f, 1f, 1f, 1f, 0f, -362f, 0f, -328f);

        _table = new BuildingsTable();
        _table.Init(context);
        _table.BuildHeader(rightGO, "BldGridHeader", 0f, 0f, -422f, -366f);
        _table.BuildRows(rightGO,
            "BldRowsScroll", "BldViewport", "BldRows", "BldScrollbar", -430f);
        PhoneButtonInstaller.RunNextFrame(FitCardToContent);
    }

    static string RectH(string childName)
    {
        try
        {
            var t = _filterCard != null ? _filterCard.transform.Find(childName) : null;
            if (t == null) return "?";
            var r = t.GetComponent<RectTransform>();
            return r.rect.height.ToString("F0");
        }
        catch { return "?"; }
    }

    /// <summary>Measures the filter card's real content height and resizes
    /// the card to fit, then re-anchors day/header/rows below it. Fixed
    /// offsets can't be trusted (fonts/layout vary), so the card adapts.
    /// Runs while the panel is still hidden - no visible flicker.</summary>
    static void FitCardToContent()
    {
        try
        {
            if (_filterCard == null || _dayText == null || _table == null) return;
            if (_page == null || !_page.activeInHierarchy) return;
            if (!_tabVisible) return;
            Canvas.ForceUpdateCanvases();
            float need = LayoutUtility.GetPreferredHeight(
                _filterCard.GetComponent<RectTransform>());
            if (need <= 0) return;
            float cardH = need + 16f;
            var cr = _filterCard.GetComponent<RectTransform>();
            float before = -cr.offsetMin.y;
            _context?.Logger.Warn("The Big Board cardfit: need=" + need + " before=" + before
                + " list=" + RectH("BldListRow") + " label=" + RectH("BldDistrictLabel")
                + " strip=" + RectH("BldStripScroll"));
            cr.offsetMin = new Vector2(cr.offsetMin.x, -cardH);
            cr.offsetMax = new Vector2(cr.offsetMax.x, 0f);
            float dayTop = cardH + 8f;
            Anchor(_dayText.gameObject, 0f, 1f, 1f, 1f, 0f, -(dayTop + 34f), 0f, -dayTop);
            float headTop = -(dayTop + 38f);
            _table.SetTableTop(headTop, headTop - 56f, headTop - 64f);
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board buildings card fit failed: " + ex.Message);
        }
    }

    public static void SetVisible(bool visible)
    {
        _tabVisible = visible;
    }

    /// <summary>~2s cadence from the bootstrap. The building set changes daily.</summary>
    public static void Tick()
    {
        try
        {
            var save = SaveGameManager.Current;
            if (save == null) return;
            if (!_tabVisible || _page == null) return;
            if (save.Day != _lastDay)
            {
                _lastDay = save.Day;
                Refresh();
            }
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board buildings tick failed: " + ex.Message);
        }
    }

    public static void Refresh()
    {
        try
        {
            var save = SaveGameManager.Current;
            if (save != null) _lastDay = save.Day;
            ComputeRows();
            BuildListPills();
            BuildBTypePills();
            BuildDistrictTabs();
            RenderRows();
            RenderCounts();
            if (_dayText != null && save != null)
                _dayText.text = "DAY " + save.Day + " | " + _unionCount + " KNOWN (" + _rentableCount + " RENT / " + _saleCount + " SALE / " + _operatingCount + " OP)";
            _context?.Logger.Info("The Big Board buildings refreshed: " + _unionCount + " known (" +
                _rentableCount + " rent / " + _saleCount + " sale / " + _operatingCount +
                " operating / " + _yoursCount + " yours), " + _table.RowCount + " shown.");
            PhoneButtonInstaller.RunNextFrame(FitCardToContent);
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board buildings refresh failed: " + ex.Message);
        }
    }

    static void RenderCounts()
    {
        if (_countText == null) return;
        string d = _selectedDistrict == null ? "all districts" : DistrictDisplay(_selectedDistrict);
        string t = _selectedBType == null ? "all types" : BTypeDisplay(_selectedBType);
        string l = _listMode == 0 ? "all" : _listMode == 1 ? "rent"
            : _listMode == 2 ? "sale" : _listMode == 3 ? "operating" : "yours";
        _countText.text = _table.RowCount + " shown (" + d + ", " + t + ", " + l + ").\n" +
            _unionCount + " known: " + _rentableCount + " rent / " + _saleCount
            + " sale / " + _operatingCount + " operating / " + _yoursCount + " yours.\n" +
            (_table.Truncated > 0 ? ("+" + _table.Truncated + " more hidden (top 300).") : "Showing all rows.");
    }

    // ---------- data ----------

    static void ComputeRows()
    {
        _rows.Clear();
        _allRows.Clear();
        _districts.Clear();
        _btypes.Clear();
        _rentableCount = 0;
        _saleCount = 0;
        _operatingCount = 0;
        _yoursCount = 0;
        _unionCount = 0;
        ReadSalePrices();
        try
        {
            var save = SaveGameManager.Current;
            if (save == null || save.BuildingRegistrations == null)
                return;

            var districtSet = new HashSet<string>();
            var btypeSet = new HashSet<string>();
            foreach (var reg in save.BuildingRegistrations)
            {
                if (reg == null) continue;
                Building cached = null;
                try { cached = reg.BuildingCached; }
                catch { continue; }
                if (cached == null) continue;
                string district;
                string btype;
                try
                {
                    district = reg.Neighborhood;
                    btype = cached.BuildingType;
                }
                catch { continue; }
                if (string.IsNullOrEmpty(district) || district == GlobalNeighborhood) continue;
                if (string.IsNullOrEmpty(btype) || btype == SpecialBuildingType) continue;
                bool isRent = reg.AvailableForRent;
                bool yours = reg.RentedByPlayer;
                try { if (!yours) yours = reg.BuildingOwnedByPlayer; }
                catch { }
                bool hasBiz = false;
                string bizName = "-";
                try
                {
                    hasBiz = !string.IsNullOrEmpty(reg.businessTypeName)
                        && reg.businessTypeName != "ba:businesstype_empty";
                    if (!string.IsNullOrEmpty(reg.BusinessName)) bizName = reg.BusinessName;
                }
                catch { }
                bool operating = hasBiz && !isRent && !yours;
                float salePrice = 0f;
                bool isSale = _salePrices.TryGetValue(SaleKey(reg.StreetName, reg.StreetNumber), out salePrice);
                if (isRent) _rentableCount++;
                if (operating) _operatingCount++;
                if (yours) _yoursCount++;
                if (isSale) _seenSale.Add(SaleKey(reg.StreetName, reg.StreetNumber));
                _unionCount++;
                districtSet.Add(district);
                btypeSet.Add(btype);
                if (_listMode == 1 && !isRent) continue;
                if (_listMode == 2 && !isSale) continue;
                if (_listMode == 3 && !operating) continue;
                if (_listMode == 4 && !yours) continue;
                // Facet filters are applied below from _allRows so the pills
                // can show per-option counts; only the listing mode applies here.
                string status = yours ? "Yours"
                    : isRent ? (isSale ? "Rent+Sale" : "For rent")
                    : isSale ? (operating ? "Op+Sale" : "For sale")
                    : operating ? "Operating" : "Vacant";
                var row = new BuildingsTable.BRow
                {
                    reg = reg,
                    mapAddress = reg.Address,
                    address = BuildingAddress(reg),
                    district = district,
                    districtName = DistrictDisplay(district),
                    btype = btype,
                    btypeName = BTypeDisplay(btype),
                    size = BuildingSizeDisplay(reg),
                    sqm = BuildingSqm(reg),
                    traffic = BuildingTraffic(reg),
                    capacity = BuildingCapacity(reg),
                    status = status,
                    business = bizName,
                    owner = OwnerName(reg),
                    price = salePrice > 0f ? Mathf.CeilToInt(salePrice) : 0,
                };
                _allRows.Add(row);
            }

            AddSaleOnlyRows(districtSet, btypeSet);
            _districts.AddRange(districtSet);
            _districts.Sort((a, b) => string.Compare(DistrictDisplay(a), DistrictDisplay(b), StringComparison.Ordinal));
            if (_selectedDistrict != null && !districtSet.Contains(_selectedDistrict))
                _selectedDistrict = null;
            _btypes.AddRange(btypeSet);
            _btypes.Sort((a, b) => string.Compare(BTypeDisplay(a), BTypeDisplay(b), StringComparison.Ordinal));
            if (_selectedBType != null && !btypeSet.Contains(_selectedBType))
                _selectedBType = null;
            // Faceted counts: each pill respects the OTHER filters, so the
            // ALL options never parrot the already-filtered row count.
            _btypeCounts.Clear();
            _districtCounts.Clear();
            foreach (var b in _btypes) _btypeCounts[b] = 0;
            foreach (var d in _districts) _districtCounts[d] = 0;
            _facetAllType = 0;
            _facetAllDistrict = 0;
            _rows.Clear();
            foreach (var r in _allRows)
            {
                bool dOk = _selectedDistrict == null || r.district == _selectedDistrict;
                bool tOk = _selectedBType == null || r.btype == _selectedBType;
                if (dOk)
                {
                    _facetAllType++;
                    int c;
                    _btypeCounts.TryGetValue(r.btype, out c);
                    _btypeCounts[r.btype] = c + 1;
                }
                if (tOk)
                {
                    _facetAllDistrict++;
                    int c;
                    _districtCounts.TryGetValue(r.district, out c);
                    _districtCounts[r.district] = c + 1;
                }
                if (dOk && tOk) _rows.Add(r);
            }
        }
        catch (Exception ex)
        {
            _context?.Logger.Warn("The Big Board buildings compute failed: " + ex.Message);
        }
    }

    // Sale addresses (street#number -> price), rebuilt per refresh from
    // SaveGameManager.buildingsForSale. BuildingForSale.BuildingRegistration
    // is deliberately NOT used: that helper CREATES and saves a junk
    // registration for unknown addresses.
    static readonly Dictionary<string, float> _salePrices = new Dictionary<string, float>();
    static readonly HashSet<string> _seenSale = new HashSet<string>();

    static void ReadSalePrices()
    {
        _salePrices.Clear();
        _seenSale.Clear();
        _saleCount = 0;
        try
        {
            var save = SaveGameManager.Current;
            if (save == null || save.buildingsForSale == null) return;
            foreach (var s in save.buildingsForSale)
            {
                if (s == null || s.address == null || s.address.streetName == null) continue;
                if (s.buildingPrice <= 0f) continue;
                _salePrices[SaleKey(s.address.streetName, s.address.streetNumber)] = s.buildingPrice;
            }
            _saleCount = _salePrices.Count;
        }
        catch { }
    }

    // Sale listings whose building has no registration yet: synthesize a
    // display row from static Building data (read-only, nothing is written
    // to the save).
    static void AddSaleOnlyRows(HashSet<string> districtSet, HashSet<string> btypeSet)
    {
        if (_listMode == 1 || _listMode == 3 || _listMode == 4) return;
        try
        {
            var save = SaveGameManager.Current;
            if (save == null || save.buildingsForSale == null) return;
            foreach (var s in save.buildingsForSale)
            {
                if (s == null || s.address == null || s.address.streetName == null) continue;
                if (s.buildingPrice <= 0f) continue;
                string key = SaleKey(s.address.streetName, s.address.streetNumber);
                if (_seenSale.Contains(key)) continue;
                Building b = null;
                try { b = Helpers.BuildingHelper.GetBuilding(s.address); }
                catch { continue; }
                if (b == null) continue;
                if (string.IsNullOrEmpty(b.Neighbourhood) || b.Neighbourhood == GlobalNeighborhood) continue;
                if (string.IsNullOrEmpty(b.BuildingType) || b.BuildingType == SpecialBuildingType) continue;
                _unionCount++;
                districtSet.Add(b.Neighbourhood);
                btypeSet.Add(b.BuildingType);
                // Facet filters are applied below from _allRows; only the
                // listing mode applies here (RENT mode returns early above).
                string size = "-";
                try
                {
                    string bs = b.BuildingSize;
                    if (!string.IsNullOrEmpty(bs))
                    {
                        if (bs.StartsWith("ba:buildingsize_", StringComparison.OrdinalIgnoreCase))
                            bs = bs.Substring("ba:buildingsize_".Length);
                        bs = bs.ToUpperInvariant();
                        size = b.BuildingVersion > 0 ? bs + b.BuildingVersion : bs;
                    }
                }
                catch { }
                int cap = 0;
                try
                {
                    var sd = Buildings.BuildingSizeHelper.GetData(b.BuildingSize);
                    if (sd != null) cap = sd.GetCustomerCapacity(b.BuildingType, b.BuildingVersion);
                }
                catch { }
                string addr = "?";
                try { addr = s.address.ToFormattedString(); }
                catch { }
                if (string.IsNullOrWhiteSpace(addr) || addr.IndexOf("ba:", StringComparison.OrdinalIgnoreCase) >= 0)
                    addr = s.address.streetNumber + " " + s.address.streetName;
                _allRows.Add(new BuildingsTable.BRow
                {
                    reg = null,
                    mapAddress = s.address,
                    address = addr,
                    district = b.Neighbourhood,
                    districtName = DistrictDisplay(b.Neighbourhood),
                    btype = b.BuildingType,
                    btypeName = BTypeDisplay(b.BuildingType),
                    size = size,
                    sqm = Mathf.Max(0, b.totalSqm),
                    traffic = Mathf.Max(0, b.trafficIndex),
                    capacity = Mathf.Max(0, cap),
                    status = "For sale",
                    business = "-",
                    owner = "-",
                    price = Mathf.CeilToInt(s.buildingPrice),
                });
            }
        }
        catch { }
    }

    static string LocalizeOrClean(string key, string prefix)
    {
        if (string.IsNullOrEmpty(key)) return "-";
        try
        {
            if (LocalizorManager.IsLocalizedKey(key))
            {
                string s = LocalizorManager.GetLocalization(key, null);
                if (!string.IsNullOrWhiteSpace(s) && !string.Equals(s, key, StringComparison.Ordinal))
                    return s;
            }
        }
        catch { }
        string t = key;
        if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            t = t.Substring(prefix.Length);
        t = t.Replace("_", " ").Trim();
        if (t.Length == 0) return key;
        return char.ToUpperInvariant(t[0]) + t.Substring(1);
    }

    static string DistrictDisplay(string district)
    {
        return LocalizeOrClean(district, "ba:neighborhood_");
    }

    static string BTypeDisplay(string btype)
    {
        return LocalizeOrClean(btype, "ba:buildingtype_");
    }

    // Familiar "A1"/"B2" labels: size letter + BuildingVersion.
    static string BuildingSizeDisplay(BuildingRegistration reg)
    {
        try
        {
            var cached = reg.BuildingCached;
            string s = cached.BuildingSize;
            if (string.IsNullOrEmpty(s)) return "-";
            if (s.StartsWith("ba:buildingsize_", StringComparison.OrdinalIgnoreCase))
                s = s.Substring("ba:buildingsize_".Length);
            s = s.ToUpperInvariant();
            int ver = 0;
            try { ver = cached.BuildingVersion; }
            catch { ver = 0; }
            return ver > 0 ? s + ver : s;
        }
        catch { return "-"; }
    }

    static int BuildingSqm(BuildingRegistration reg)
    {
        try { return Math.Max(0, reg.BuildingCached.totalSqm); }
        catch { return 0; }
    }

    static int BuildingTraffic(BuildingRegistration reg)
    {
        try { return Mathf.Max(0, reg.BuildingCached.trafficIndex); }
        catch { return 0; }
    }

    static int BuildingCapacity(BuildingRegistration reg)
    {
        // Shell maximum for the size-type first: reg.customerCapacity is the
        // min() with the previous tenant's furniture limits and can be
        // stale-low for a vacant building.
        try
        {
            var cached = reg.BuildingCached;
            if (cached != null)
            {
                int cap = cached.GetCustomerCapacity;
                if (cap > 0) return cap;
            }
        }
        catch { }
        try
        {
            if (reg.customerCapacity > 0) return reg.customerCapacity;
        }
        catch { }
        return 0;
    }

    static string BuildingAddress(BuildingRegistration reg)
    {
        // Same recipe as the vanilla Market Insider table: the Address
        // extension resolves the localized street name.
        try
        {
            var addr = reg.Address;
            if (addr != null && !addr.IsUndefined())
            {
                string s = null;
                try { s = addr.ToFormattedString(); }
                catch { s = null; }
                if (!string.IsNullOrWhiteSpace(s)
                    && s.IndexOf("ba:", StringComparison.OrdinalIgnoreCase) < 0)
                    return s;
            }
        }
        catch { }
        try
        {
            string street = reg.StreetName ?? "";
            if (reg.StreetNumber > 0 && !string.IsNullOrWhiteSpace(street))
                return reg.StreetNumber + " " + street;
            if (!string.IsNullOrWhiteSpace(street)) return street;
            if (reg.StreetNumber > 0) return reg.StreetNumber.ToString();
        }
        catch { }
        return "?";
    }

    static string OwnerName(BuildingRegistration reg)
    {
        try
        {
            if (reg.RentedByPlayer || reg.BuildingOwnedByPlayer) return "YOU";
            string id = !string.IsNullOrEmpty(reg.buildingOwnerRivalId)
                ? reg.buildingOwnerRivalId
                : reg.businessOwnerRivalId;
            if (string.IsNullOrEmpty(id)) return "-";
            try
            {
                var data = RivalsHelper.GetRivalData(id);
                if (data != null && !string.IsNullOrEmpty(data.rivalName))
                    return data.rivalName;
            }
            catch { }
            return LocalizeOrClean(id, "ba:rival_");
        }
        catch { return "-"; }
    }

    // ---------- building-type pills (left) ----------

    static void BuildBTypePills()
    {
        if (_leftCol == null) return;
        // Keep the label / counts / refresh, drop stale pills.
        foreach (Transform child in _leftCol)
        {
            if (_countGO != null && child.gameObject == _countGO) continue;
            if (_refreshGO != null && child.gameObject == _refreshGO) continue;
            if (_btypeCountText != null && child.gameObject == _btypeCountText.gameObject) continue;
            UnityEngine.Object.Destroy(child.gameObject);
        }
        Warmup(_leftCol);
        _btypePills.Clear();
        if (_btypeCountText != null)
            _btypeCountText.text = "BUILDING TYPE (" + _btypes.Count + ")";
        AddBTypePill(null, "ALL (" + _facetAllType + ")");
        foreach (var b in _btypes)
        {
            int c;
            _btypeCounts.TryGetValue(b, out c);
            AddBTypePill(b, BTypeDisplay(b) + " (" + c + ")");
        }
        // Pills append after counts/refresh: move those back to the bottom.
        if (_countGO != null) _countGO.transform.SetAsLastSibling();
        if (_refreshGO != null) _refreshGO.transform.SetAsLastSibling();
    }

    static void AddBTypePill(string key, string name)
    {
        if (_leftCol == null) return;
        bool sel = key == _selectedBType;
        var pillGO = new GameObject(key == null ? "BldTypeAll" : "BldTypePill");
        pillGO.transform.SetParent(_leftCol, false);
        var bg = pillGO.AddComponent<Image>();
        bg.color = sel ? Accent : BtnIdle;
        var btn = pillGO.AddComponent<Button>();
        btn.targetGraphic = bg;
        var label = Text("BldTypePillLabel", name, 26,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle,
            sel ? Color.white : InkLight);
        label.transform.SetParent(pillGO.transform, false);
        Anchor(label.gameObject, 0f, 0f, 1f, 1f, 12f, 6f, -12f, -6f);
        label.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        label.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        var size = pillGO.AddComponent<LayoutElement>();
        size.minHeight = 66f;
        string captured = key;
        btn.onClick.AddListener(() => SelectBType(captured));
        _btypePills.Add(new BTypePill { key = key, bg = bg, label = label });
    }

    static void SelectBType(string key)
    {
        _selectedBType = key;
        ComputeRows();
        BuildListPills();
        BuildBTypePills();
        BuildDistrictTabs();
        RenderRows();
        RenderCounts();
        PhoneButtonInstaller.RunNextFrame(FitCardToContent);
        if (_context != null)
            _context.Logger.Info("The Big Board buildings type: " + (key == null ? "ALL" : BTypeDisplay(key)));
    }

    // ---------- listing pills (ALL / RENT / SALE / OPERATING / YOURS) ----------

    static void BuildListPills()
    {
        if (_listRow == null) return;
        foreach (Transform child in _listRow)
            UnityEngine.Object.Destroy(child.gameObject);
        _listPills.Clear();
        AddListPill(0, "ALL (" + _unionCount + ")");
        AddListPill(1, "RENT (" + _rentableCount + ")");
        AddListPill(2, "SALE (" + _saleCount + ")");
        AddListPill(3, "OPERATING (" + _operatingCount + ")");
        AddListPill(4, "YOURS (" + _yoursCount + ")");
    }

    static void AddListPill(int mode, string name)
    {
        if (_listRow == null) return;
        bool sel = mode == _listMode;
        var pillGO = new GameObject("BldListPill" + mode);
        pillGO.transform.SetParent(_listRow, false);
        var bg = pillGO.AddComponent<Image>();
        bg.color = sel ? Accent : BtnIdle;
        var btn = pillGO.AddComponent<Button>();
        btn.targetGraphic = bg;
        var label = Text("BldListPillLabel" + mode, name, 26,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle,
            sel ? Color.white : InkLight);
        label.transform.SetParent(pillGO.transform, false);
        Anchor(label.gameObject, 0f, 0f, 1f, 1f, 12f, 6f, -12f, -6f);
        label.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        label.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        int captured = mode;
        btn.onClick.AddListener(() => SelectListing(captured));
        _listPills.Add(new ListPill { mode = mode, bg = bg, label = label });
    }

    static void SelectListing(int mode)
    {
        _listMode = mode;
        ComputeRows();
        BuildListPills();
        BuildBTypePills();
        BuildDistrictTabs();
        RenderRows();
        RenderCounts();
        PhoneButtonInstaller.RunNextFrame(FitCardToContent);
        if (_context != null)
            _context.Logger.Info("The Big Board buildings listing: " +
                (mode == 0 ? "ALL" : mode == 1 ? "RENT" : mode == 2 ? "SALE"
                : mode == 3 ? "OPERATING" : "YOURS"));
    }

    // ---------- district tabs (right) ----------

    static void BuildDistrictTabs()
    {
        if (_districtStrip == null) return;
        foreach (Transform child in _districtStrip)
            UnityEngine.Object.Destroy(child.gameObject);
        Warmup(_districtStrip);
        AddDistrictTab(null, "ALL (" + _facetAllDistrict + ")");
        foreach (var d in _districts)
        {
            int c;
            _districtCounts.TryGetValue(d, out c);
            AddDistrictTab(d, DistrictDisplay(d) + " (" + c + ")");
        }
        if (_districtLabel != null)
            _districtLabel.text = _districts.Count == 0
                ? "DISTRICTS (0) - no known buildings."
                : "DISTRICTS (" + _districts.Count + ") - tap a tab to filter:";
    }

    static void AddDistrictTab(string key, string name)
    {
        if (_districtStrip == null) return;
        bool sel = key == _selectedDistrict;
        var tabGO = new GameObject(key == null ? "BldDistrictAll" : "BldDistrictTab");
        tabGO.transform.SetParent(_districtStrip, false);
        var bg = tabGO.AddComponent<Image>();
        bg.color = sel ? Accent : RowBgAlt;
        var btn = tabGO.AddComponent<Button>();
        btn.targetGraphic = bg;
        var label = Text("BldDistrictTabLabel", name, 26,
            TMPro.HorizontalAlignmentOptions.Center, TMPro.VerticalAlignmentOptions.Middle,
            sel ? Color.white : InkLight);
        label.transform.SetParent(tabGO.transform, false);
        Anchor(label.gameObject, 0f, 0f, 1f, 1f, 16f, 8f, -16f, -8f);
        label.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        label.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        var size = tabGO.AddComponent<LayoutElement>();
        size.minWidth = 200f;
        size.preferredWidth = Mathf.Clamp(120f + (name != null ? name.Length : 3) * 17f, 220f, 560f);
        size.minHeight = 88f;
        string captured = key;
        btn.onClick.AddListener(() => SelectDistrict(captured));
    }

    static void SelectDistrict(string key)
    {
        _selectedDistrict = key;
        ComputeRows();
        BuildListPills();
        BuildBTypePills();
        BuildDistrictTabs();
        RenderRows();
        RenderCounts();
        PhoneButtonInstaller.RunNextFrame(FitCardToContent);
        if (_context != null)
            _context.Logger.Info("The Big Board buildings district: " + (key == null ? "ALL" : DistrictDisplay(key)));
    }

    static void Warmup(Transform root)
    {
        DataGrid.Warmup(root);
    }

    // ---------- table: sorting, resize and rendering live in BuildingsTable ----------

    static void RenderRows()
    {
        if (_table != null) _table.SetRows(_rows);
    }

}
