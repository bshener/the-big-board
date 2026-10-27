using System;
using System.Collections.Generic;
using BAModAPI;
using BigAmbitions.Factories.Recipes;

namespace TheBigBoard;

/// <summary>
/// Manufactured quantities from the conservation identity, not from shipment
/// proxies. The game keeps NO manufactured counter: factory orderHistory only
/// archives logistics shipments (ProcessDailyFactoryOrders archives the live
/// factoryExports list, which the logistics Deliver flow fills), and unsold
/// goods sitting in the factory are invisible to every history. What the game
/// DOES keep lets us reconstruct manufactured exactly:
///
///   manufactured(P, [A..D]) = stockLive(P) - stockStartOf(A, P)
///                           + soldRetail(P, A..D)
///                           - imported(P, A..D) - wholesaleIn(P, A..D)
///
/// Exactness gates (else fallback to the shipped proxy, never a fake exact
/// number): the stock ledger must cover day A (StockHistory.FirstDay), and
/// the product must not be imported, wholesaled in, or routed to an
/// exporter in the window (those flows share the same warehouse transaction
/// records as internal transfers, so they cannot be split by day).
/// Player hand-carry and spoilage/theft are the known residual bias (same
/// class as the StockHistory ledger bias).
/// Per-factory variant: manufactured(F, P, day) = histExports(F, P, day)
/// + warehouse arrivals of P on [day..today] attributed to F when F is the
/// sole own producer of P + (liveFactoryStock(F, P) - ledgerStock(F, P, day)).
/// LangVersion 10: no file-scoped namespaces, no fancy syntax.
/// </summary>
public static class Manufactured
{
    const string FactoryBusinessType = "ba:businesstype_factory";
    const string ImportExportBusinessType = "ba:businesstype_importexport";

    public struct Result
    {
        public long Value;
        public bool Exact;
        public string Basis;
        public long S0; // start-boundary stock
        public long S1; // end-boundary stock (live for open windows)
        public long Hist; // factory history exports in window
        public long Wh; // warehouse arrivals in window (attributed)
        public long LiveE; // live factory exports
    }

    // ---------- building set (same recipe as the stock search) ----------

    static List<BuildingRegistration> StockBuildings()
    {
        var buildings = new List<BuildingRegistration>();
        try
        {
            var own = Helpers.BuildingHelper.GetPlayerBuildingRegistrations(null, null);
            if (own != null)
                foreach (var reg in own)
                    if (reg != null && !buildings.Contains(reg)) buildings.Add(reg);
        }
        catch { }
        try
        {
            var all = SaveGameManager.Current != null ? SaveGameManager.Current.BuildingRegistrations : null;
            if (all != null)
                foreach (var reg in all)
                {
                    if (reg == null || !(reg is Entities.Warehouse)) continue;
                    if (!buildings.Contains(reg)) buildings.Add(reg);
                }
        }
        catch { }
        return buildings;
    }

    // ---------- gate sets (built once per refresh, shared by a batch) ----------

    public sealed class Gates
    {
        public readonly HashSet<string> Imported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Wholesaled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> ExportRouted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public static Gates BuildGates()
    {
        var gates = new Gates();
        try
        {
            var save = SaveGameManager.Current;
            if (save == null) return gates;
            if (save.importPartnerships != null)
            {
                foreach (var partnership in save.importPartnerships)
                {
                    if (partnership == null || !partnership.isActive) continue;
                    if (partnership.products == null) continue;
                    foreach (var product in partnership.products)
                    {
                        if (product == null || string.IsNullOrEmpty(product.itemName)) continue;
                        gates.Imported.Add(product.itemName);
                    }
                }
            }
            if (save.DeliveryContracts != null)
            {
                foreach (var c in save.DeliveryContracts)
                {
                    if (c == null || c.items == null) continue;
                    foreach (var item in c.items)
                    {
                        if (item == null || string.IsNullOrEmpty(item.itemName)) continue;
                        gates.Wholesaled.Add(item.itemName);
                    }
                }
            }
            if (save.logisticsManagerPlans != null)
            {
                var byAddr = new Dictionary<string, string>();
                try
                {
                    foreach (var reg in save.BuildingRegistrations)
                    {
                        if (reg == null) continue;
                        byAddr[(reg.StreetName != null ? reg.StreetName : "") + "#" + reg.StreetNumber] =
                            !string.IsNullOrEmpty(reg.BusinessName)
                                ? reg.BusinessName
                                : (reg.StreetName + " " + reg.StreetNumber);
                    }
                }
                catch { }
                foreach (var plan in save.logisticsManagerPlans)
                {
                    if (plan == null || plan.destinations == null) continue;
                    foreach (var dest in plan.destinations)
                    {
                        if (dest == null || dest.stockTargets == null) continue;
                        if (dest.deliveryTargetAddress == null) continue;
                        if (dest.deliveryTargetAddress.streetName == null) continue;
                        string dstName;
                        if (!byAddr.TryGetValue(
                            (dest.deliveryTargetAddress.streetName != null ? dest.deliveryTargetAddress.streetName : "")
                            + "#" + dest.deliveryTargetAddress.streetNumber, out dstName)) continue;
                        BuildingRegistration dstReg = null;
                        try
                        {
                            foreach (var reg in save.BuildingRegistrations)
                            {
                                if (reg == null) continue;
                                string n = !string.IsNullOrEmpty(reg.BusinessName)
                                    ? reg.BusinessName
                                    : (reg.StreetName + " " + reg.StreetNumber);
                                if (n == dstName) { dstReg = reg; break; }
                            }
                        }
                        catch { }
                        if (dstReg == null) continue;
                        bool isExporter = false;
                        try
                        {
                            isExporter = string.Equals(dstReg.businessTypeName, ImportExportBusinessType,
                                StringComparison.OrdinalIgnoreCase);
                        }
                        catch { continue; }
                        if (!isExporter) continue;
                        foreach (var st in dest.stockTargets)
                        {
                            if (st == null || string.IsNullOrEmpty(st.itemName)) continue;
                            gates.ExportRouted.Add(st.itemName);
                        }
                    }
                }
            }
        }
        catch { }
        return gates;
    }

    // ---------- retail sales per day (single pass over histories) ----------

    static Dictionary<string, Dictionary<int, long>> SoldMap(int fromDay, int toDay, bool includeLive)
    {
        var map = new Dictionary<string, Dictionary<int, long>>(StringComparer.OrdinalIgnoreCase);
        var save = SaveGameManager.Current;
        if (save == null || save.BuildingRegistrations == null) return map;
        foreach (var reg in save.BuildingRegistrations)
        {
            if (reg == null || !reg.RentedByPlayer) continue;
            if (reg is Entities.Warehouse) continue;
            try
            {
                if (string.Equals(reg.businessTypeName, FactoryBusinessType, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            catch { continue; }
            try
            {
                if (reg.orderHistory != null)
                {
                    foreach (var entry in reg.orderHistory)
                    {
                        if (entry == null || entry.itemSales == null) continue;
                        if (entry.dayNumber < fromDay || entry.dayNumber > toDay) continue;
                        foreach (var rep in entry.itemSales)
                        {
                            if (rep == null || string.IsNullOrEmpty(rep.itemName) || rep.amountSold <= 0) continue;
                            Dictionary<int, long> perDay;
                            if (!map.TryGetValue(rep.itemName, out perDay))
                            {
                                perDay = new Dictionary<int, long>();
                                map[rep.itemName] = perDay;
                            }
                            long cur;
                            perDay.TryGetValue(entry.dayNumber, out cur);
                            perDay[entry.dayNumber] = cur + rep.amountSold;
                        }
                    }
                }
            }
            catch { }
            if (includeLive)
            {
                try
                {
                    var live = reg.unprocessedCompletedOrders;
                    if (live != null)
                    {
                        foreach (var order in live)
                        {
                            if (order == null || !order.completed || order.entries == null) continue;
                            foreach (var e in order.entries)
                            {
                                if (e == null || string.IsNullOrEmpty(e.itemName)) continue;
                                if (!e.available || !e.priceAccceptable || !e.paid) continue;
                                Dictionary<int, long> perDay;
                                if (!map.TryGetValue(e.itemName, out perDay))
                                {
                                    perDay = new Dictionary<int, long>();
                                    map[e.itemName] = perDay;
                                }
                                long cur;
                                perDay.TryGetValue(toDay, out cur);
                                perDay[toDay] = cur + 1;
                            }
                        }
                    }
                }
                catch { }
            }
        }
        return map;
    }

    static long SoldInWindow(Dictionary<string, Dictionary<int, long>> soldMap, string pkey, int fromDay, int toDay)
    {
        Dictionary<int, long> perDay;
        if (soldMap == null || !soldMap.TryGetValue(pkey, out perDay) || perDay == null) return 0;
        long sum = 0;
        foreach (var kv in perDay)
            if (kv.Key >= fromDay && kv.Key <= toDay) sum += kv.Value;
        return sum;
    }

    // ---------- ledger + live stock ----------

    static long LedgerStock(List<BuildingRegistration> buildings, int day, string pkey)
    {
        long sum = 0;
        if (buildings == null) return 0;
        foreach (var reg in buildings)
        {
            if (reg == null) continue;
            try
            {
                long v;
                if (StockHistory.TryGet(day, StockHistory.AddrKey(reg), pkey, out v)) sum += v;
            }
            catch { }
        }
        return sum;
    }

    static long LiveStock(List<BuildingRegistration> buildings, string pkey, string rawItem,
        Dictionary<string, long> liveTotals)
    {
        if (liveTotals != null)
        {
            long v;
            if (liveTotals.TryGetValue(pkey, out v)) return v;
            return 0;
        }
        long sum = 0;
        if (buildings == null || string.IsNullOrEmpty(rawItem)) return 0;
        foreach (var reg in buildings)
        {
            if (reg == null) continue;
            try { sum += Helpers.BuildingHelper.CountTotalResourcesInStock(reg, rawItem); }
            catch { }
        }
        return sum;
    }

    // ---------- global identity: manufactured(P, [fromDay..today]) ----------

    /// <summary>Batch global manufactured for several products over the same
    /// window. Open windows (toDay == today) end live; closed past windows
    /// end at the next day's snapshot. One history pass for all products.
    /// Exact only with completed-snapshot boundaries and clean gates; else
    /// Value=0 and Exact=false (caller keeps the shipped proxy).</summary>
    public static Dictionary<string, Result> ForGlobalBatch(List<KeyValuePair<string, string>> products,
        int fromDay, int toDay, int today, Dictionary<string, long> liveTotals)
    {
        var out_ = new Dictionary<string, Result>(StringComparer.OrdinalIgnoreCase);
        if (products == null || products.Count == 0) return out_;
        var gates = BuildGates();
        bool open = toDay >= today;
        // Pair-missing reads as 0, so boundaries require COMPLETED sweeps:
        // HasDay, not FirstDay (a mid-day load leaves the load day unswept).
        bool covered = StockHistory.HasDay(fromDay) && (open || StockHistory.HasDay(toDay + 1));
        var buildings = covered ? StockBuildings() : null;
        var soldMap = covered ? SoldMap(fromDay, toDay, open) : null;
        foreach (var kv in products)
        {
            string pkey = kv.Key;
            string raw = kv.Value;
            var r = new Result { Value = 0, Exact = false, Basis = "shipped-proxy" };
            if (string.IsNullOrEmpty(pkey))
            {
                out_[pkey] = r;
                continue;
            }
            if (!covered)
            {
                r.Basis = "fallback:ledger-uncovered";
            }
            else if (gates.Imported.Contains(pkey))
            {
                r.Basis = "fallback:imported";
            }
            else if (gates.Wholesaled.Contains(pkey))
            {
                r.Basis = "fallback:wholesaled";
            }
            else if (gates.ExportRouted.Contains(pkey))
            {
                r.Basis = "fallback:exported";
            }
            else
            {
                long end = open
                    ? LiveStock(buildings, pkey, raw, liveTotals)
                    : LedgerStock(buildings, toDay + 1, pkey);
                long start = LedgerStock(buildings, fromDay, pkey);
                long sold = SoldInWindow(soldMap, pkey, fromDay, toDay);
                r.S1 = end;
                r.S0 = start;
                r.Value = Math.Max(0, end - start + sold);
                r.Exact = true;
                r.Basis = open ? "ledger" : "ledger-closed";
            }
            out_[pkey] = r;
        }
        return out_;
    }

    // ---------- per-factory identity: manufactured(F, P, day) ----------

    static int ProducerCount(string pkey)
    {
        int n = 0;
        try
        {
            var save = SaveGameManager.Current;
            if (save == null || save.BuildingRegistrations == null) return 0;
            foreach (var reg in save.BuildingRegistrations)
            {
                if (reg == null || !reg.RentedByPlayer) continue;
                try
                {
                    if (!string.Equals(reg.businessTypeName, FactoryBusinessType,
                        StringComparison.OrdinalIgnoreCase)) continue;
                }
                catch { continue; }
                if (reg.itemInstances == null) continue;
                bool makes = false;
                foreach (var inst in reg.itemInstances.Values)
                {
                    var ws = inst as FactoryWorkstationInstance;
                    if (ws == null) continue;
                    Recipe recipe = null;
                    try { recipe = ws.SelectedRecipe; }
                    catch { continue; }
                    if ((UnityEngine.Object)recipe == null) continue;
                    string outputItem = null;
                    try { outputItem = recipe.output.item; }
                    catch { continue; }
                    if (!string.IsNullOrEmpty(outputItem)
                        && string.Equals(outputItem, pkey, StringComparison.OrdinalIgnoreCase))
                    {
                        makes = true;
                        break;
                    }
                }
                if (makes) n++;
            }
        }
        catch { }
        return n;
    }

    static long WarehouseArrivals(string pkey, int fromDay, int toDay)
    {
        long sum = 0;
        try
        {
            var save = SaveGameManager.Current;
            if (save == null || save.BuildingRegistrations == null) return 0;
            foreach (var reg in save.BuildingRegistrations)
            {
                var wh = reg as Entities.Warehouse;
                if (wh == null || wh.deliveryTransactions == null) continue;
                foreach (var tx in wh.deliveryTransactions)
                {
                    if (tx == null || tx.deliveryItems == null) continue;
                    if (tx.dayOfDelivery < fromDay || tx.dayOfDelivery > toDay) continue;
                    foreach (var di in tx.deliveryItems)
                    {
                        if (di == null || string.IsNullOrEmpty(di.itemName) || di.amountDelivered <= 0) continue;
                        if (!string.Equals(di.itemName, pkey, StringComparison.OrdinalIgnoreCase)) continue;
                        sum += di.amountDelivered;
                    }
                }
            }
        }
        catch { }
        return sum;
    }

    static long FactoryHistoryExports(BuildingRegistration factoryReg, string pkey, int day)
    {
        long sum = 0;
        try
        {
            if (factoryReg == null || factoryReg.orderHistory == null) return 0;
            foreach (var entry in factoryReg.orderHistory)
            {
                if (entry == null || entry.itemSales == null) continue;
                if (entry.dayNumber != day) continue;
                foreach (var rep in entry.itemSales)
                {
                    if (rep == null || string.IsNullOrEmpty(rep.itemName) || rep.amountSold <= 0) continue;
                    if (!string.Equals(rep.itemName, pkey, StringComparison.OrdinalIgnoreCase)) continue;
                    sum += rep.amountSold;
                }
            }
        }
        catch { }
        return sum;
    }

    static long FactoryLiveExports(BuildingRegistration factoryReg, string pkey)
    {
        long sum = 0;
        try
        {
            var exports = factoryReg != null ? factoryReg.factoryExports : null;
            if (exports == null) return 0;
            foreach (var x in exports)
            {
                if (x == null || string.IsNullOrEmpty(x.itemName) || x.amount <= 0) continue;
                if (!string.Equals(x.itemName, pkey, StringComparison.OrdinalIgnoreCase)) continue;
                sum += x.amount;
            }
        }
        catch { }
        return sum;
    }

    /// <summary>Manufactured in one factory over [fromDay..today] (live end):
    /// (liveFactoryStock - ledgerFactoryStock(fromDay)) + histExports +
    /// warehouse arrivals (only when the factory is the sole own producer of
    /// the product, else unattributable) + live exports. Exact with a
    /// completed start snapshot and clean attribution; else "…" upstream.
    /// Imports/wholesale hit warehouses/stores, never factory stock, so no
    /// gates needed.</summary>
    public static Result ForFactory(BuildingRegistration factoryReg, string pkey, string rawItem,
        int fromDay, int today, bool soleProducer)
    {
        var r = new Result { Value = 0, Exact = false, Basis = "shipped-proxy" };
        if (factoryReg == null || string.IsNullOrEmpty(pkey)) return r;
        if (!StockHistory.HasDay(fromDay))
        {
            r.Basis = "fallback:ledger-uncovered";
            return r;
        }
        long whTotal = WarehouseArrivals(pkey, fromDay, today);
        if (whTotal > 0 && !soleProducer)
        {
            r.Basis = "fallback:shared-warehouse-flow";
            return r;
        }
        long sLive = 0;
        try
        {
            if (!string.IsNullOrEmpty(rawItem))
                sLive = Helpers.BuildingHelper.CountTotalResourcesInStock(factoryReg, rawItem);
        }
        catch { }
        long sDay = 0;
        try
        {
            long v;
            if (StockHistory.TryGet(fromDay, StockHistory.AddrKey(factoryReg), pkey, out v)) sDay = v;
        }
        catch { }
        long hist = 0;
        for (int d = fromDay; d <= today; d++) hist += FactoryHistoryExports(factoryReg, pkey, d);
        long liveExp = FactoryLiveExports(factoryReg, pkey);
        r.S0 = sDay;
        r.S1 = sLive;
        r.Hist = hist;
        r.Wh = whTotal;
        r.LiveE = liveExp;
        r.Value = Math.Max(0, (sLive - sDay) + hist + whTotal + liveExp);
        r.Exact = true;
        r.Basis = "ledger";
        return r;
    }

    /// <summary>Own factories (besides F excluded by caller if needed) making
    /// the product. Feed the result into ForFactory as soleProducer.</summary>
    public static bool IsSoleProducer(BuildingRegistration factoryReg, string pkey)
    {
        if (factoryReg == null || string.IsNullOrEmpty(pkey)) return false;
        return ProducerCount(pkey) == 1;
    }
}
