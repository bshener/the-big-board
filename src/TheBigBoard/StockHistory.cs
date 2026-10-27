using System.Collections.Generic;
using UnityEngine;

namespace TheBigBoard;

/// <summary>
/// Own movement ledger. The game records no per-store inbound and no daily
/// factory production, so we keep our own books with the conservation law:
///
///   IN(window) = stock(end) - stock(start) + OUT(window)
///
/// OUT per day comes from game logs (orderHistory / deliveryTransactions /
/// live paid orders); stock(start) comes from our day-boundary snapshots.
/// Snapshots persist per-save in GameInstance.modData (compact indexed
/// format; the ledger logs its own byte size on every save so the cost is
/// visible in Player.log). Only pairs with stock &gt; 0 are stored; days older
/// than the 7-day window are pruned; a hard row cap guards degenerate saves.
///
/// Warmup: a product is covered once snapshots bracket its buckets
/// (today: 1 rollover, yesterday: 2, week: 7). Until then the table falls
/// back to transfer attribution / wholesale counters / "...".
/// Known bias: theft, spoilage and player-carried goods lower stock without
/// an OUT record, so IN reads slightly low (same bias as vanilla Balance).
/// </summary>
public static class StockHistory
{
    // FROZEN for save compat: renaming this key orphans existing save-game ledgers.
    const string SnapKey = "MyFirstMod.stockSnaps.v1";
    const int KeepDays = 7; // week IN needs S(D-6): keep D-6..D
    const int MaxRows = 20000;

    static readonly Dictionary<string, long> _snaps = new Dictionary<string, long>();
    static readonly HashSet<int> _days = new HashSet<int>();
    static int _lastDay = -1;
    static int _firstDay = -1;
    static bool _loaded;
    static bool _snapRunning;

    /// <summary>Days with a completed start-of-day snapshot (missing pair =
    /// true zero, not a gap). Distinct from FirstDay: a day counts only
    /// after its sweep finishes, so aborted sweeps never read as zeros.
    /// </summary>
    public static bool HasDay(int day)
    {
        return _days.Contains(day);
    }

    static string SKey(int day, string addr, string pkey)
    {
        return day + "\n" + addr + "\n" + pkey;
    }

    public static string AddrKey(BuildingRegistration reg)
    {
        if (reg == null) return "#0";
        return (reg.StreetName != null ? reg.StreetName : "") + "#" + reg.StreetNumber;
    }

    public static int FirstDay
    {
        get { return _firstDay; }
    }

    public static bool TryGet(int day, string addr, string pkey, out long stock)
    {
        return _snaps.TryGetValue(SKey(day, addr, pkey), out stock);
    }

    public static void Tick()
    {
        try
        {
            var save = SaveGameManager.Current;
            if (save == null) return;
            if (!_loaded)
            {
                Load(save);
                _loaded = true;
            }
            int day = save.Day;
            if (_lastDay < 0)
            {
                // First sighting (often a mid-day mod load): baseline only,
                // no snapshot — a mid-day stock is not a day-start value.
                _lastDay = day;
                return;
            }
            if (day == _lastDay || _snapRunning) return;
            _lastDay = day;
            PhoneButtonInstaller.RunJob(SnapshotRoutine(save, day));
        }
        catch { }
    }

    static void Load(GameInstance save)
    {
        _snaps.Clear();
        _days.Clear();
        _firstDay = -1;
        try
        {
            if (save == null || save.modData == null) return;
            string blob;
            if (!save.modData.TryGetValue(SnapKey, out blob) || string.IsNullOrEmpty(blob)) return;
            string[] lines = blob.Split('\n');
            int i = 0;
            if (i >= lines.Length || lines[i++] != "v1") return;
            if (i >= lines.Length || lines[i++] != "A") return;
            var addrs = new List<string>();
            while (i < lines.Length && lines[i] != "P") addrs.Add(lines[i++]);
            if (i >= lines.Length) return;
            i++; // P
            var prods = new List<string>();
            while (i < lines.Length && lines[i] != "R") prods.Add(lines[i++]);
            if (i >= lines.Length) return;
            i++; // R
            for (; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;
                string[] parts = line.Split(' ');
                if (parts.Length != 4) continue;
                int day, ai, pi;
                long stock;
                if (!int.TryParse(parts[0], out day)) continue;
                if (!int.TryParse(parts[1], out ai) || ai < 0 || ai >= addrs.Count) continue;
                if (!int.TryParse(parts[2], out pi) || pi < 0 || pi >= prods.Count) continue;
                if (!long.TryParse(parts[3], out stock)) continue;
                _snaps[SKey(day, addrs[ai], prods[pi])] = stock;
                _days.Add(day);
                if (_firstDay < 0 || day < _firstDay) _firstDay = day;
            }
            Debug.Log("[The Big Board stock] ledger loaded: " + _snaps.Count + " entries from day " + _firstDay + ".");
        }
        catch { }
    }

    static void Save(GameInstance save)
    {
        try
        {
            if (save == null) return;
            if (save.modData == null) save.modData = new Dictionary<string, string>();
            var addrIdx = new Dictionary<string, int>();
            var prodIdx = new Dictionary<string, int>();
            var addrs = new List<string>();
            var prods = new List<string>();
            var rows = new List<string>();
            foreach (var kv in _snaps)
            {
                string[] kp = kv.Key.Split('\n');
                if (kp.Length != 3) continue;
                int ai;
                if (!addrIdx.TryGetValue(kp[1], out ai))
                {
                    ai = addrs.Count;
                    addrs.Add(kp[1]);
                    addrIdx[kp[1]] = ai;
                }
                int pi;
                if (!prodIdx.TryGetValue(kp[2], out pi))
                {
                    pi = prods.Count;
                    prods.Add(kp[2]);
                    prodIdx[kp[2]] = pi;
                }
                rows.Add(kp[0] + " " + ai + " " + pi + " " + kv.Value);
            }
            var sb = new System.Text.StringBuilder(rows.Count * 12 + 1024);
            sb.Append("v1\nA\n");
            foreach (var a in addrs) { sb.Append(a); sb.Append('\n'); }
            sb.Append("P\n");
            foreach (var p in prods) { sb.Append(p); sb.Append('\n'); }
            sb.Append("R\n");
            foreach (var r in rows) { sb.Append(r); sb.Append('\n'); }
            string blob = sb.ToString();
            save.modData[SnapKey] = blob;
            // Self-measurement: the real save cost, visible in Player.log.
            Debug.Log("[The Big Board stock] ledger saved: " + _snaps.Count + " entries, ~" + (blob.Length / 1024) + " KB (day " + _lastDay + ").");
        }
        catch { }
    }

    static void Prune(int today)
    {
        try
        {
            int cutoff = today - KeepDays + 1; // keep D-6..D
            var drop = new List<string>();
            foreach (var k in _snaps.Keys)
            {
                int nl = k.IndexOf('\n');
                if (nl <= 0) { drop.Add(k); continue; }
                int day;
                if (!int.TryParse(k.Substring(0, nl), out day) || day < cutoff) drop.Add(k);
            }
            foreach (var k in drop) _snaps.Remove(k);
            _days.RemoveWhere(d => d < cutoff);
            if (drop.Count > 0 && _firstDay >= 0)
            {
                if (_firstDay < cutoff)
                {
                    _firstDay = -1;
                    foreach (var k in _snaps.Keys)
                    {
                        int nl = k.IndexOf('\n');
                        if (nl <= 0) continue;
                        int day;
                        if (int.TryParse(k.Substring(0, nl), out day) && (_firstDay < 0 || day < _firstDay))
                            _firstDay = day;
                    }
                }
            }
            // Hard cap for degenerate saves: drop oldest days first.
            if (_snaps.Count > MaxRows)
            {
                var days = new SortedSet<int>();
                foreach (var k in _snaps.Keys)
                {
                    int nl = k.IndexOf('\n');
                    if (nl <= 0) continue;
                    int day;
                    if (int.TryParse(k.Substring(0, nl), out day)) days.Add(day);
                }
                foreach (int d in days)
                {
                    if (_snaps.Count <= MaxRows) break;
                    var dropDay = new List<string>();
                    string prefix = d + "\n";
                    foreach (var k in _snaps.Keys)
                        if (k.StartsWith(prefix)) dropDay.Add(k);
                    foreach (var k in dropDay) _snaps.Remove(k);
                    _days.Remove(d); // capped day no longer trustworthy as swept
                }
                Debug.Log("[The Big Board stock] ledger capped at " + MaxRows + " rows; oldest days dropped.");
            }
        }
        catch { }
    }

    static System.Collections.IEnumerator SnapshotRoutine(GameInstance save, int day)
    {
        _snapRunning = true;
        try
        {
            // Same building recipe as the stock search: player buildings plus
            // every warehouse registration as a safety net.
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
            int pairs = 0;
            foreach (var reg in buildings)
            {
                if (SaveGameManager.Current == null) yield break; // quit to menu mid-sweep
                string addr;
                try { addr = AddrKey(reg); }
                catch { continue; }
                // Cheap enumeration first (cargo scan only); the authoritative
                // helper runs solely for stocked pairs so snapshot amounts use
                // the exact same measure as the table's STOCK column.
                var rawByPkey = new Dictionary<string, string>();
                try
                {
                    if (reg.itemInstances != null)
                    {
                        foreach (var inst in reg.itemInstances.Values)
                        {
                            if (inst == null) continue;
                            if (inst.cargoInstances != null)
                            {
                                foreach (var c in inst.cargoInstances)
                                {
                                    if (c == null || c.itemName == null || c.amount <= 0) continue;
                                    string pkey = c.itemName.ToLowerInvariant();
                                    if (!rawByPkey.ContainsKey(pkey)) rawByPkey[pkey] = c.itemName;
                                }
                            }
#pragma warning disable 0618 // shelfItem obsolete since EA 0.8, still read for discovery
                            var sh = inst.shelfItem;
#pragma warning restore 0618
                            if (sh != null && sh.itemName != null && sh.stock > 0)
                            {
                                string pkey = sh.itemName.ToLowerInvariant();
                                if (!rawByPkey.ContainsKey(pkey)) rawByPkey[pkey] = sh.itemName;
                            }
                        }
                    }
                }
                catch { }
                foreach (var kv in rawByPkey)
                {
                    int amt = 0;
                    try { amt = Helpers.BuildingHelper.CountTotalResourcesInStock(reg, kv.Value); }
                    catch { amt = 0; }
                    if (amt <= 0) continue;
                    _snaps[SKey(day, addr, kv.Key)] = amt;
                    pairs++;
                }
                if (_firstDay < 0 || day < _firstDay) _firstDay = day;
                // One building per frame: the game never hitches on rollover.
                yield return null;
            }
            _days.Add(day); // sweep completed: missing pairs are true zeros
            Prune(day);
            var live = SaveGameManager.Current;
            if (live != null) Save(live);
            Debug.Log("[The Big Board stock] snapshot day " + day + ": " + pairs + " pairs.");
        }
        finally
        {
            _snapRunning = false;
        }
    }
}
