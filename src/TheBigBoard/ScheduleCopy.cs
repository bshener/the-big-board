using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BAModAPI;
using BigAmbitions.Characters.Skills;
using BigAmbitions.DayNightCycle;
using BigAmbitions.Items;
using Entities;
using Helpers;
using Streets;

namespace TheBigBoard;

/// <summary>
/// SCHEDULE COPY. Copies a whole weekly work plan (opening hours + all
/// shifts, openingHourSlots and WorkShifts on each ScheduleDay) from a base
/// shop to a target shop. Rules, per user request:
/// - Only between shops of the same business type (different scheduling
///   models like HQ shared-shifts and warehouse driver slots are excluded
///   from the shop list altogether).
/// - Workstations are matched by itemName (cash register -> cash register);
///   base shifts whose station has no counterpart on the target are skipped
///   and reported.
/// - Staffing: each distinct base employee maps to one target employee.
///   Target's own assigned staff is used first, then idle hires (hired but
///   unassigned) which get assigned to the target, then waiting candidates
///   (CandidateEmployeeInstances with a matching primary skill) which are
///   hired straight into the target (same assignedAddress + HireCandidate
///   recipe as the game's own mass action). Pairing respects part/full-time
///   demands: a part-time source set goes to a part-time hand, full-time to
///   full-time, "any" (no demand info) swings either way. Bands never
///   cross: leftovers stay empty and are reported; more heads are ordered
///   from the Employee tab.
/// All game mutations use the vanilla recipes (ClearWorkShifts, new
/// WorkShift, assignedAddress set, UpdateWeeklyHoursAndDays,
/// UpdateAssignedWorkStationItems, SaveGameManager.MarkChange,
/// HeadhunterPlan.StartRecruiting, RecruitmentCampaign list).
/// </summary>
public static class ScheduleCopy
{
    const string HeadquartersType = "ba:businesstype_headquarters";
    const string WarehouseType = "ba:businesstype_warehouse";
    const string EmptyType = "ba:businesstype_empty";

    public sealed class CopyResult
    {
        public bool ok;
        public string message = "";
    }

    public static List<BuildingRegistration> PlayerShops()
    {
        var list = new List<BuildingRegistration>();
        try
        {
            var save = SaveGameManager.Current;
            if (save == null || save.BuildingRegistrations == null) return list;
            foreach (var reg in save.BuildingRegistrations)
            {
                if (reg == null || !reg.RentedByPlayer) continue;
                if (string.IsNullOrEmpty(reg.BusinessName)) continue;
                string bt = reg.businessTypeName ?? "";
                if (bt == EmptyType || bt == HeadquartersType || bt == WarehouseType) continue;
                if (reg.scheduleDays == null || reg.scheduleDays.Count == 0) continue;
                list.Add(reg);
            }
        }
        catch { }
        list.Sort((a, b) => string.Compare(ShopName(a), ShopName(b), StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public static string ShopName(BuildingRegistration reg)
    {
        try
        {
            if (reg == null) return "-";
            if (!string.IsNullOrEmpty(reg.BusinessName)) return reg.BusinessName;
            if (reg.Address != null) return reg.Address.ToFormattedString();
        }
        catch { }
        return "-";
    }


    public static string ShortType(string businessTypeName)
    {
        try
        {
            const string prefix = "ba:businesstype_";
            if (!string.IsNullOrEmpty(businessTypeName) && businessTypeName.StartsWith(prefix))
                return businessTypeName.Substring(prefix.Length);
        }
        catch { }
        return businessTypeName ?? "?";
    }

    static string DistStr(Dictionary<int, int> dist)
    {
        try
        {
            if (dist == null || dist.Count == 0) return "-";
            var keys = new List<int>(dist.Keys);
            keys.Sort();
            keys.Reverse();
            var parts = new List<string>();
            foreach (var k in keys) parts.Add(dist[k] + "x" + k + "h");
            return string.Join(" + ", parts.ToArray());
        }
        catch { return "?"; }
    }

    static string SkillLabel(string skill)
    {
        if (string.IsNullOrEmpty(skill)) return "?";
        const string prefix = "ba:skill_";
        try { if (skill.StartsWith(prefix)) return skill.Substring(prefix.Length); }
        catch { }
        return skill;
    }

    static string MixStr(Dictionary<string, int> m)
    {
        try
        {
            var p = new List<string>();
            foreach (var kv in m) p.Add(kv.Value + "x" + kv.Key);
            return string.Join(" + ", p.ToArray());
        }
        catch { return "?"; }
    }

    public static CopyResult Copy(BuildingRegistration baseReg, BuildingRegistration targetReg,
        ModContext context)
    {
        var res = new CopyResult();
        try
        {
            if (baseReg == null || targetReg == null)
            {
                res.message = "Pick a BASE and a TARGET shop first.";
                return res;
            }
            if (ReferenceEquals(baseReg, targetReg) || SameAddress(baseReg, targetReg))
            {
                res.message = "BASE and TARGET are the same shop.";
                return res;
            }
            string baseType = baseReg.businessTypeName ?? "";
            string targetType = targetReg.businessTypeName ?? "";
            if (baseType != targetType)
            {
                res.message = "Different business types (" + ShortType(baseType) + " vs " +
                    ShortType(targetType) + "). Copy is allowed between same-type shops only.";
                return res;
            }
            if (baseReg.scheduleDays == null || targetReg.scheduleDays == null ||
                baseReg.scheduleDays.Count != targetReg.scheduleDays.Count)
            {
                res.message = "Schedule day counts differ; cannot map days 1:1.";
                return res;
            }

            var save = SaveGameManager.Current;
            if (save == null)
            {
                res.message = "No save loaded.";
                return res;
            }
            if (baseReg.Address == null || targetReg.Address == null)
            {
                res.message = "Shop address not resolved (map not loaded?).";
                return res;
            }

            // ---- snapshot base plan (skill per distinct employee) ----
            var baseStations = StationNames(baseReg);
            List<string> needOrder;
            Dictionary<string, string> needSkill;
            Dictionary<int, int> baseDist;
            int baseShiftCount;
            SnapshotCrew(baseReg, out needOrder, out needSkill, out baseShiftCount, out baseDist);

            // ---- staff pools ----
            var managerIds = new HashSet<string>();
            try
            {
                if (save.hrManagerPlans != null)
                    foreach (var p in save.hrManagerPlans)
                        if (p != null && !string.IsNullOrEmpty(p.assignedEmployeeId))
                            managerIds.Add(p.assignedEmployeeId);
                if (save.headhunterPlans != null)
                    foreach (var p in save.headhunterPlans)
                        if (p != null && !string.IsNullOrEmpty(p.assignedEmployeeId))
                            managerIds.Add(p.assignedEmployeeId);
            }
            catch { }

            var targetAssigned = new Dictionary<string, List<EmployeeInstance>>();
            var idle = new Dictionary<string, List<EmployeeInstance>>();
            try
            {
                if (save.EmployeeInstances != null)
                {
                    foreach (var emp in save.EmployeeInstances)
                    {
                        if (emp == null || emp.IsCandidate || string.IsNullOrEmpty(emp.id)) continue;
                        if (managerIds.Contains(emp.id)) continue;
                        string skill = null;
                        try { skill = emp.GetPrimarySkill(); }
                        catch { continue; }
                        if (skill == null) continue;
                        if (emp.assignedAddress != null && !IsUnset(emp.assignedAddress) &&
                            emp.assignedAddress.Equals(targetReg.Address))
                            AddToPool(targetAssigned, skill, emp);
                        else if (emp.assignedAddress == null || IsUnset(emp.assignedAddress))
                            AddToPool(idle, skill, emp);
                    }
                }
            }
            catch { }

            // ---- waiting candidates: hireable hands whose primary skill
            // the base crew needs. Paired after idle hires; a picked
            // candidate is hired straight into the target below.
            var cands = new Dictionary<string, List<EmployeeInstance>>();
            try
            {
                var needSkills = new HashSet<string>();
                foreach (var kv in needSkill)
                    if (!string.IsNullOrEmpty(kv.Value)) needSkills.Add(kv.Value);
                var all = save.CandidateEmployeeInstances;
                if (all != null)
                {
                    foreach (var cand in all)
                    {
                        if (cand == null || !cand.IsCandidate) continue;
                        if (cand.candidateInfo == null) continue;
                        if (string.IsNullOrEmpty(cand.id)) continue;
                        if (managerIds.Contains(cand.id)) continue;
                        string skill = null;
                        try { skill = cand.GetPrimarySkill(); }
                        catch { continue; }
                        if (skill == null || !needSkills.Contains(skill)) continue;
                        AddToPool(cands, skill, cand);
                    }
                }
            }
            catch { }

            // ---- 1:1 pairing by order within the same hours band: both
            // sides are sorted by (primary skill, id), then person N here
            // takes over exactly the shifts person N works there. Part-time
            // source sets go to part-time hands, full-time to full-time;
            // "any" (no demand info) swings either way. Within a band the
            // same primary skill is preferred. Explicit demands
            // never cross bands: leftovers stay missing (recruit) instead
            // of seating a part-timer on full-time hours or vice versa. ----
            var mapping = new Dictionary<string, string>(); // baseEmpId -> targetEmpId
            var usedTarget = new HashSet<string>();
            var missing = new Dictionary<string, int>(); // skill -> headcount
            int fromTarget = 0;
            int fromIdle = 0;
            int fromCands = 0;
            var idleToAssign = new List<EmployeeInstance>();
            var needPart = new List<string>();
            var needFull = new List<string>();
            var needAny = new List<string>();
            try
            {
                var sortedNeed = new List<string>(needOrder);
                sortedNeed.Sort((a, b) =>
                {
                    try
                    {
                        string sa = null;
                        string sb = null;
                        needSkill.TryGetValue(a, out sa);
                        needSkill.TryGetValue(b, out sb);
                        int c = string.Compare(sa ?? "", sb ?? "", StringComparison.Ordinal);
                        if (c != 0) return c;
                        return string.Compare(a, b, StringComparison.Ordinal);
                    }
                    catch { return 0; }
                });
                var flatAssigned = new List<EmployeeInstance>();
                var flatIdle = new List<EmployeeInstance>();
                var flatCands = new List<EmployeeInstance>();
                foreach (var kv in targetAssigned)
                    if (kv.Value != null) flatAssigned.AddRange(kv.Value);
                foreach (var kv in idle)
                    if (kv.Value != null) flatIdle.AddRange(kv.Value);
                foreach (var kv in cands)
                    if (kv.Value != null) flatCands.AddRange(kv.Value);
                flatAssigned.Sort(EmpOrder);
                flatIdle.Sort(EmpOrder);
                flatCands.Sort(EmpOrder);
                var recHours = BaseWeeklyHours(baseReg);
                var curHours = BaseWeeklyHours(targetReg);
                needPart.Clear();
                needFull.Clear();
                needAny.Clear();
                foreach (var id in sortedNeed)
                {
                    string h = BaseHourDemand(id, recHours);
                    if (h == "part") needPart.Add(id);
                    else if (h == "full") needFull.Add(id);
                    else needAny.Add(id);
                }
                var idleIds = new HashSet<string>();
                foreach (var e in flatIdle)
                    if (e != null && !string.IsNullOrEmpty(e.id)) idleIds.Add(e.id);
                var candIds = new HashSet<string>();
                foreach (var e in flatCands)
                    if (e != null && !string.IsNullOrEmpty(e.id)) candIds.Add(e.id);
                var poolPart = new List<EmployeeInstance>();
                var poolFull = new List<EmployeeInstance>();
                var poolAny = new List<EmployeeInstance>();
                var orderedPool = new List<EmployeeInstance>(flatAssigned);
                orderedPool.AddRange(flatIdle);
                orderedPool.AddRange(flatCands);
                foreach (var e in orderedPool)
                {
                    if (e == null || string.IsNullOrEmpty(e.id)) continue;
                    int w = 0;
                    bool hasW = false;
                    // Only seated target crew has meaningful current hours;
                    // idle hands and fresh candidates go by demands alone.
                    if (!idleIds.Contains(e.id) && !candIds.Contains(e.id))
                        hasW = curHours.TryGetValue(e.id, out w);
                    string h = StaffHours(e, w, hasW);
                    if (h == "part") poolPart.Add(e);
                    else if (h == "full") poolFull.Add(e);
                    else poolAny.Add(e);
                }
                foreach (var id in needPart) SeatNeed(id, TakeHand(poolPart, poolAny, NeedSkill(id)));
                foreach (var id in needFull) SeatNeed(id, TakeHand(poolFull, poolAny, NeedSkill(id)));
                foreach (var id in needAny)
                {
                    string ws = NeedSkill(id);
                    var anyPick = TakeHand(poolAny, poolPart, ws);
                    if (anyPick == null) anyPick = TakeHand(poolFull, null, ws);
                    SeatNeed(id, anyPick);
                }
                string NeedSkill(string needId)
                {
                    string s = null;
                    needSkill.TryGetValue(needId, out s);
                    return s;
                }
                string PoolSkill(EmployeeInstance e)
                {
                    try { return e != null ? e.GetPrimarySkill() : null; }
                    catch { return null; }
                }
                EmployeeInstance TakeMatch(List<EmployeeInstance> l, string wantSkill)
                {
                    if (l == null || l.Count == 0 || string.IsNullOrEmpty(wantSkill)) return null;
                    for (int ti = 0; ti < l.Count; ti++)
                    {
                        if (PoolSkill(l[ti]) == wantSkill)
                        {
                            var e = l[ti];
                            l.RemoveAt(ti);
                            return e;
                        }
                    }
                    return null;
                }
                EmployeeInstance TakeHand(List<EmployeeInstance> a, List<EmployeeInstance> b,
                    string wantSkill)
                {
                    var m = TakeMatch(a, wantSkill);
                    if (m != null) return m;
                    if (a != null && a.Count > 0)
                    {
                        var e = a[0];
                        a.RemoveAt(0);
                        return e;
                    }
                    m = TakeMatch(b, wantSkill);
                    if (m != null) return m;
                    if (b != null && b.Count > 0)
                    {
                        var e = b[0];
                        b.RemoveAt(0);
                        return e;
                    }
                    return null;
                }
                void MissNeed(string needId)
                {
                    string skill = null;
                    needSkill.TryGetValue(needId, out skill);
                    if (string.IsNullOrEmpty(skill)) skill = "?";
                    int n;
                    missing.TryGetValue(skill, out n);
                    missing[skill] = n + 1;
                }
                void SeatNeed(string needId, EmployeeInstance pick)
                {
                    if (pick == null) { MissNeed(needId); return; }
                    if (candIds.Contains(pick.id))
                    {
                        try
                        {
                            pick.assignedAddress = targetReg.Address;
                            Helpers.EmployeeHelper.HireCandidate(pick);
                        }
                        catch
                        {
                            MissNeed(needId);
                            return;
                        }
                        fromCands++;
                    }
                    else if (idleIds.Contains(pick.id))
                    {
                        idleToAssign.Add(pick);
                        fromIdle++;
                    }
                    else fromTarget++;
                    mapping[needId] = pick.id;
                    usedTarget.Add(pick.id);
                }
            }
            catch { }
            // ---- target workstations by itemName ----
            var targetStations = new Dictionary<string, List<string>>();
            try
            {
                var assignable = new List<ItemInstance>();
                try { targetReg.GetAssignableItems(assignable); }
                catch { }
                if (assignable != null)
                    foreach (var it in assignable)
                    {
                        if (it == null || string.IsNullOrEmpty(it.itemName)) continue;
                        List<string> l;
                        if (!targetStations.TryGetValue(it.itemName, out l))
                        {
                            l = new List<string>();
                            targetStations[it.itemName] = l;
                        }
                        if (!l.Contains(it.id)) l.Add(it.id);
                    }
                // Fallback: any item instance, so cleaning stations etc. still map.
                if (targetReg.itemInstances != null)
                    foreach (var kv in targetReg.itemInstances)
                    {
                        if (kv.Value == null || string.IsNullOrEmpty(kv.Value.itemName)) continue;
                        List<string> l;
                        if (!targetStations.TryGetValue(kv.Value.itemName, out l))
                        {
                            l = new List<string>();
                            targetStations[kv.Value.itemName] = l;
                        }
                        if (!l.Contains(kv.Value.id)) l.Add(kv.Value.id);
                    }
            }
            catch { }
            // Each source station instance maps to ONE target instance of
            // the same itemName (first-seen order), so the source's
            // per-station structure is preserved exactly. Round-robin would
            // stack two same-hour shifts onto one register and the game UI
            // hides the overlap.
            var stationMap = new Dictionary<string, string>();
            var stationNext = new Dictionary<string, int>();

            // ---- clear target, write base plan ----
            var touched = new HashSet<string>();
            var wants = new List<SlotWant>();
            var writeDist = new Dictionary<int, int>();
            int written = 0;
            int skippedStation = 0;
            int openDays = 0;
            try
            {
                foreach (var day in targetReg.scheduleDays)
                {
                    if (day == null || day.workShifts == null) continue;
                    foreach (var sh in day.workShifts)
                        if (sh != null && !string.IsNullOrEmpty(sh.employeeId))
                            touched.Add(sh.employeeId);
                }
                foreach (var day in targetReg.scheduleDays)
                {
                    if (day == null) continue;
                    try { day.ClearWorkShifts(); }
                    catch { }
                }
                for (int i = 0; i < baseReg.scheduleDays.Count; i++)
                {
                    var bDay = baseReg.scheduleDays[i];
                    var tDay = targetReg.scheduleDays[i];
                    if (bDay == null || tDay == null) continue;
                    tDay.isOpen = bDay.isOpen;
                    if (!bDay.isOpen) continue;
                    openDays++;
                    try
                    {
                        tDay.openingHourSlots = new List<OpeningHourSlot>();
                        if (bDay.openingHourSlots != null)
                            foreach (var slot in bDay.openingHourSlots)
                                if (slot != null) tDay.openingHourSlots.Add(slot.Clone());
                    }
                    catch { }
                    if (bDay.workShifts == null) continue;
                    foreach (var bShift in bDay.workShifts)
                    {
                        if (bShift == null) continue;
                        string stationName = null;
                        if (!string.IsNullOrEmpty(bShift.itemInstanceId))
                            baseStations.TryGetValue(bShift.itemInstanceId, out stationName);
                        string targetStationId = MapStation(targetStations, stationNext,
                            stationMap, bShift.itemInstanceId, stationName);
                        if (targetStationId == null)
                        {
                            skippedStation++;
                            continue;
                        }
                        string empId = null;
                        if (!string.IsNullOrEmpty(bShift.employeeId))
                            mapping.TryGetValue(bShift.employeeId, out empId);
                        var nShift = new WorkShift
                        {
                            startingHour = bShift.startingHour,
                            endingHour = bShift.endingHour,
                            employeeId = empId,
                            itemInstanceId = targetStationId,
                            type = bShift.type
                        };
                        try
                        {
                            tDay.AddWorkShift(nShift);
                            written++;
                            try
                            {
                                int d = nShift.endingHour - nShift.startingHour;
                                int n;
                                writeDist.TryGetValue(d, out n);
                                writeDist[d] = n + 1;
                            }
                            catch { }
                            if (!string.IsNullOrEmpty(empId)) touched.Add(empId);
                            else
                            {
                                // Remember the slot for the seat step: whose
                                // weekly set it belongs to, in write order.
                                string want = null;
                                if (!string.IsNullOrEmpty(bShift.employeeId))
                                    needSkill.TryGetValue(bShift.employeeId, out want);
                                wants.Add(new SlotWant
                                {
                                    shift = nShift,
                                    skill = want,
                                    baseEmp = bShift.employeeId
                                });
                            }
                        }
                        catch { skippedStation++; }
                    }
                }
            }
            catch (Exception ex)
            {
                res.message = "Copy failed while writing shifts: " + ex.Message;
                return res;
            }

            RecordWants(targetReg, wants);

            // ---- employee bookkeeping on the target plan ----
            foreach (var empId in touched)
            {
                try
                {
                    var emp = Helpers.EmployeeHelper.GetEmployeeById(empId, false);
                    if (emp == null) continue;
                    emp.UpdateWeeklyHoursAndDays(targetReg.scheduleDays);
                    emp.UpdateAssignedWorkStationItems();
                }
                catch { }
            }
            try { SaveGameManager.MarkChange(); }
            catch { }
            RefreshScheduleUI(targetReg);

            // Idle hands join the target now. No recruitment orders here:
            // missing heads are ordered from the Employee tab instead.
            foreach (var emp in idleToAssign)
            {
                try { emp.assignedAddress = targetReg.Address; }
                catch
                {
                    string be = null;
                    foreach (var kv in mapping)
                        if (kv.Value == emp.id) { be = kv.Key; break; }
                    if (be != null)
                    {
                        mapping.Remove(be);
                        usedTarget.Remove(emp.id);
                    }
                    string ms = null;
                    needSkill.TryGetValue(be, out ms);
                    if (string.IsNullOrEmpty(ms)) ms = "?";
                    int mn;
                    missing.TryGetValue(ms, out mn);
                    missing[ms] = mn + 1;
                    fromIdle--;
                }
            }
            // ---- verify: per-day cover source vs target + skill mix ----
            string dayCover = "";
            string skillMix = "";
            try
            {
                var sc = new List<string>();
                var tc = new List<string>();
                for (int di = 0; di < baseReg.scheduleDays.Count; di++)
                {
                    int s = 0;
                    var dd = baseReg.scheduleDays[di];
                    if (dd != null && dd.workShifts != null)
                        foreach (var sh in dd.workShifts)
                            if (sh != null && !string.IsNullOrEmpty(sh.employeeId)) s++;
                    int t = 0, e = 0;
                    var td = (targetReg.scheduleDays != null
                        && di < targetReg.scheduleDays.Count)
                        ? targetReg.scheduleDays[di] : null;
                    if (td != null && td.workShifts != null)
                        foreach (var sh in td.workShifts)
                        {
                            if (sh == null) continue;
                            if (string.IsNullOrEmpty(sh.employeeId)) e++;
                            else t++;
                        }
                    sc.Add(s.ToString());
                    tc.Add(t + "+" + e);
                }
                dayCover = " day cover src[" + string.Join("/", sc.ToArray())
                    + "] tgt staffed+empty[" + string.Join("/", tc.ToArray()) + "]";
                var needCounts = new Dictionary<string, int>();
                foreach (var kv in needSkill)
                {
                    string k = string.IsNullOrEmpty(kv.Value) ? "?" : SkillLabel(kv.Value);
                    int n;
                    needCounts.TryGetValue(k, out n);
                    needCounts[k] = n + 1;
                }
                var seatCounts = new Dictionary<string, int>();
                foreach (var kv in mapping)
                {
                    string sk = "?";
                    try
                    {
                        var emp = Helpers.EmployeeHelper.GetEmployeeById(kv.Value, false);
                        if (emp != null) sk = SkillLabel(emp.GetPrimarySkill());
                    }
                    catch { }
                    int n;
                    seatCounts.TryGetValue(sk, out n);
                    seatCounts[sk] = n + 1;
                }
                skillMix = " skills need{" + MixStr(needCounts)
                    + "} seated{" + MixStr(seatCounts) + "}";
            }
            catch { }
            string staffNote;
            if (needOrder.Count == 0)
                staffNote = "base plan has no staffed shifts";
            else if (missing.Count == 0 && fromIdle == 0 && fromCands == 0)
                staffNote = "staffed fully from " + ShopName(targetReg) + " crew (" + fromTarget + "/" + needOrder.Count + ")";
            else
            {
                var parts = new List<string>();
                if (fromTarget > 0) parts.Add(fromTarget + " own crew");
                if (fromIdle > 0) parts.Add(fromIdle + " idle hired");
                if (fromCands > 0) parts.Add(fromCands + " candidates hired");
                parts.Add("bands part " + needPart.Count + " / full " + needFull.Count
                    + " / any " + needAny.Count);
                int missTotal = 0;
                foreach (var kv in missing) missTotal += kv.Value;
                if (missTotal > 0) parts.Add(missTotal + " unfilled (RECRUIT from Employee tab)");
                staffNote = "need " + needOrder.Count + ": " + string.Join(", ", parts.ToArray());
            }
            var msg = "COPY " + ShopName(baseReg) + " -> " + ShopName(targetReg) +
                " (" + ShortType(baseType) + "): " + openDays + " open days, " +
                written + "/" + baseShiftCount + " shifts written" +
                " [source: " + DistStr(baseDist) + " -> wrote: " + DistStr(writeDist) + "]." +
                dayCover + skillMix + " " + staffNote + ".";
            if (skippedStation > 0)
                msg += " Skipped " + skippedStation + " (no matching station).";
            res.ok = true;
            res.message = msg;
            try { context?.Logger.Info("The Big Board schedule copy: " + msg); }
            catch { }
            return res;
        }
        catch (Exception ex)
        {
            res.message = "Copy failed: " + ex.Message;
            return res;
        }
    }

    static bool SameAddress(BuildingRegistration a, BuildingRegistration b)
    {
        try
        {
            if (a == null || b == null) return false;
            string ka = (a.StreetName ?? "") + "#" + a.StreetNumber;
            string kb = (b.StreetName ?? "") + "#" + b.StreetNumber;
            return ka == kb;
        }
        catch { return false; }
    }

    static bool IsUnset(Address addr)
    {
        try
        {
            if (addr == null) return true;
            return string.IsNullOrEmpty(addr.streetName);
        }
        catch { return true; }
    }

    static void AddToPool(Dictionary<string, List<EmployeeInstance>> pool, string skill, EmployeeInstance emp)
    {
        List<EmployeeInstance> l;
        if (!pool.TryGetValue(skill, out l))
        {
            l = new List<EmployeeInstance>();
            pool[skill] = l;
        }
        l.Add(emp);
    }

    /// <summary>Deterministic order for 1:1 pairing: primary skill, then
    /// name, then id. Never throws (sorters must not).</summary>
    public static int EmpOrder(EmployeeInstance a, EmployeeInstance b)
    {
        try
        {
            string sa = null;
            string sb = null;
            string na = null;
            string nb = null;
            try { if (a != null) sa = a.GetPrimarySkill(); } catch { }
            try { if (b != null) sb = b.GetPrimarySkill(); } catch { }
            int c = string.Compare(sa ?? "", sb ?? "", StringComparison.Ordinal);
            if (c != 0) return c;
            try { if (a != null && a.characterData != null) na = a.characterData.name; } catch { }
            try { if (b != null && b.characterData != null) nb = b.characterData.name; } catch { }
            c = string.Compare(na ?? "", nb ?? "", StringComparison.Ordinal);
            if (c != 0) return c;
            string ia = null;
            string ib = null;
            try { if (a != null) ia = a.id; } catch { }
            try { if (b != null) ib = b.id; } catch { }
            return string.Compare(ia ?? "", ib ?? "", StringComparison.Ordinal);
        }
        catch { return 0; }
    }

    /// <summary>An empty target shift: which base person it belongs to and
    /// that person's primary skill, so a later hire takes over the whole
    /// weekly set 1:1.</summary>
    public sealed class SlotWant
    {
        public WorkShift shift;
        public string skill;
        public string baseEmp;
    }

    /// <summary>One headhunter job awaiting breaker choices: the reserved
    /// free plan plus what it should recruit. Nothing is started until the
    /// user confirms the checkboxes.</summary>
    public sealed class BreakerOrder
    {
        public Buildings.Office.Headquarters.HeadhunterPlan plan;
        public string skill;
        public int count;
        public string hours; // part / full / any
    }

    /// <summary>Top-ups for already-recruiting exact matches happen now;
    /// fresh plans come back as orders for the breaker UI. Assumes capacity
    /// was checked first, so every uncovered combo finds a free plan.</summary>
    public static List<BreakerOrder> PlanRecruitment(List<RecruitCombo> combos,
        List<string> notes)
    {
        var orders = new List<BreakerOrder>();
        try
        {
            var save = SaveGameManager.Current;
            if (save == null || save.headhunterPlans == null) return orders;
            var usedPlans = new HashSet<string>();
            foreach (var c in combos)
            {
                Buildings.Office.Headquarters.HeadhunterPlan match = null;
                foreach (var p in save.headhunterPlans)
                {
                    if (p == null || !p.isRecruiting) continue;
                    string s = null;
                    try { s = p.skillRecruiting; } catch { }
                    if (s != c.skill) continue;
                    if (PlanHoursSetting(p) != c.hours) continue;
                    match = p;
                    break;
                }
                if (match != null)
                {
                    try
                    {
                        if (match.remainingCandidatesToRecruit != -1)
                            match.remainingCandidatesToRecruit += c.count;
                        if (match.amountOfCandidatesToRecruitPreference != -1)
                            match.amountOfCandidatesToRecruitPreference += c.count;
                    }
                    catch { }
                    if (notes != null)
                        notes.Add("Headhunter: +" + c.count + " " + SkillLabel(c.skill) +
                            " (already recruiting, topped up).");
                    continue;
                }
                Buildings.Office.Headquarters.HeadhunterPlan free = null;
                foreach (var p in save.headhunterPlans)
                {
                    if (p == null || p.isRecruiting) continue;
                    string pid = null;
                    try { pid = p.id; } catch { }
                    if (string.IsNullOrEmpty(pid) || usedPlans.Contains(pid)) continue;
                    EmployeeInstance hh = null;
                    try { hh = p.HeadhunterInstance; } catch { }
                    if (hh == null) continue;
                    free = p;
                    usedPlans.Add(pid);
                    break;
                }
                if (free == null)
                {
                    if (notes != null)
                        notes.Add("No free headhunter left for " + SkillLabel(c.skill) +
                            " x" + c.count + " (skipped).");
                    continue;
                }
                orders.Add(new BreakerOrder
                {
                    plan = free,
                    skill = c.skill,
                    count = c.count,
                    hours = c.hours
                });
            }
        }
        catch { }
        return orders;
    }

    /// <summary>Base crew profile: distinct employees in first-seen order
    /// with primary skills, plus shift count and length distribution.
    /// Shared by the full copy and the target-free recruit-only order.</summary>
    static void SnapshotCrew(BuildingRegistration baseReg,
        out List<string> needOrder, out Dictionary<string, string> needSkill,
        out int shiftCount, out Dictionary<int, int> dist)
    {
        needOrder = new List<string>();
        needSkill = new Dictionary<string, string>();
        dist = new Dictionary<int, int>();
        shiftCount = 0;
        try
        {
            if (baseReg == null || baseReg.scheduleDays == null) return;
            foreach (var day in baseReg.scheduleDays)
            {
                if (day == null || day.workShifts == null) continue;
                foreach (var sh in day.workShifts)
                {
                    if (sh == null) continue;
                    shiftCount++;
                    try
                    {
                        int d = sh.endingHour - sh.startingHour;
                        int n;
                        dist.TryGetValue(d, out n);
                        dist[d] = n + 1;
                    }
                    catch { }
                    if (string.IsNullOrEmpty(sh.employeeId)) continue;
                    if (needSkill.ContainsKey(sh.employeeId)) continue;
                    needOrder.Add(sh.employeeId);
                    string skill = null;
                    try
                    {
                        var emp = Helpers.EmployeeHelper.GetEmployeeById(sh.employeeId, false);
                        if (emp != null) skill = emp.GetPrimarySkill();
                    }
                    catch { }
                    needSkill[sh.employeeId] = skill;
                }
            }
        }
        catch { }
    }

    /// <summary>Recruit-only order: reads the source crew's profile
    /// (skill + hour demand per head) and starts the headhunter search.
    /// Headhunter is mandatory here: agency finds whoever is around and
    /// most of them would never fit the pattern. No target needed; seating
    /// happens later in the Businesses tab.</summary>
    /// <summary>Crew profile batch for the breaker UI: top-ups for
    /// already-recruiting exact matches happen now, fresh plans come back
    /// as orders. Blocked message when headhunters are short (then orders
    /// and notes stay empty).</summary>
    public sealed class RecruitBatch
    {
        public string blocked;
        public string summary = "";
        public List<BreakerOrder> orders = new List<BreakerOrder>();
        public List<string> notes = new List<string>();
    }

    public static RecruitBatch RecruitCrew(BuildingRegistration baseReg, ModContext context)
    {
        var batch = new RecruitBatch();
        try
        {
            if (baseReg == null) { batch.blocked = "Pick a BASE shop first."; return batch; }
            List<string> needOrder;
            Dictionary<string, string> needSkill;
            int shiftCount;
            Dictionary<int, int> dist;
            SnapshotCrew(baseReg, out needOrder, out needSkill, out shiftCount, out dist);
            if (needOrder.Count == 0) { batch.blocked = "BASE has no staffed shifts."; return batch; }
            var counts = new Dictionary<string, int>();
            var parts = new Dictionary<string, int>();
            var fulls = new Dictionary<string, int>();
            var recHours = BaseWeeklyHours(baseReg);
            foreach (var empId in needOrder)
            {
                string skill = null;
                needSkill.TryGetValue(empId, out skill);
                if (string.IsNullOrEmpty(skill)) skill = "?";
                int n;
                counts.TryGetValue(skill, out n);
                counts[skill] = n + 1;
                string h = BaseHourDemand(empId, recHours);
                var dst = h == "part" ? parts : (h == "full" ? fulls : null);
                if (dst == null) continue;
                int m;
                dst.TryGetValue(skill, out m);
                dst[skill] = m + 1;
            }
            var combos = BuildCombos(counts, parts, fulls);
            int needPlans;
            int freePlans;
            int coveredCombos;
            string block = CheckHeadhunterCapacity(combos, out needPlans, out freePlans, out coveredCombos);
            if (block != null) { batch.blocked = block + " No changes made."; return batch; }
            batch.summary = "RECRUIT crew of " + ShopName(baseReg) + " (" + needOrder.Count + "):";
            batch.orders = PlanRecruitment(combos, batch.notes);
            return batch;
        }
        catch (Exception ex)
        {
            batch.blocked = "Recruit failed: " + ex.Message;
            return batch;
        }
    }

    /// <summary>Repaints the vanilla schedule UI when it is open on this
    /// business. Our shifts are written straight into scheduleDays,
    /// bypassing the game's per-day shift cache, so without this the
    /// BizMan screen keeps showing stale bars until its day is reselected.
    /// Same two calls the game itself makes after a shift change.</summary>
    public static void RefreshScheduleUI(BuildingRegistration reg)
    {
        try
        {
            var uis = UI.UIs.Instance;
            if (uis == null || uis.fullMenu == null) return;
            var bizMan = uis.fullMenu.bizMan;
            if (bizMan == null || bizMan.business == null) return;
            BuildingRegistration open = null;
            try { open = bizMan.business.buildingRegistration; } catch { return; }
            if (open == null || reg == null) return;
            bool same = false;
            try { same = open.Address == reg.Address; } catch { }
            if (!same) return;
            UI.Smartphone.Apps.BizMan.Schedule.ScheduleHelper.CacheWorkShifts();
            try { UI.Smartphone.Apps.BizMan.Schedule.ScheduleHelper.RequestScheduleScrollerReload.Invoke(false); }
            catch { }
            try { UI.Smartphone.Apps.BizMan.Schedule.ScheduleHelper.OnWorkShiftChanged.Invoke(); }
            catch { }
        }
        catch { }
    }

    static readonly Dictionary<string, List<SlotWant>> _wants =
        new Dictionary<string, List<SlotWant>>();

    public static string AddrKey(BuildingRegistration reg)
    {
        try { return (reg.StreetName ?? "") + "#" + reg.StreetNumber; }
        catch { return ""; }
    }

    /// <summary>Live list of unseated slots (shift refs are written into
    /// place by the seat step). Null when this copy wrote none.</summary>
    public static List<SlotWant> GetWants(BuildingRegistration reg)
    {
        try
        {
            List<SlotWant> l;
            if (reg != null && _wants.TryGetValue(AddrKey(reg), out l)) return l;
        }
        catch { }
        return null;
    }

    static void RecordWants(BuildingRegistration reg, List<SlotWant> wants)
    {
        try { _wants[AddrKey(reg)] = wants ?? new List<SlotWant>(); }
        catch { }
    }

    static Dictionary<string, string> StationNames(BuildingRegistration reg)
    {
        var map = new Dictionary<string, string>();
        try
        {
            if (reg.itemInstances != null)
                foreach (var kv in reg.itemInstances)
                    if (kv.Value != null && !string.IsNullOrEmpty(kv.Value.itemName))
                        map[kv.Value.id] = kv.Value.itemName;
        }
        catch { }
        return map;
    }

    /// <summary>Stable station mapping: a source station instance always
    /// resolves to the same target instance of the same itemName, so shifts
    /// that shared a register in the source share one in the target (and
    /// shifts on different registers never stack onto one).</summary>
    static string MapStation(Dictionary<string, List<string>> stations,
        Dictionary<string, int> next, Dictionary<string, string> map,
        string sourceId, string itemName)
    {
        try
        {
            if (string.IsNullOrEmpty(itemName)) return null;
            string hit;
            if (!string.IsNullOrEmpty(sourceId) && map.TryGetValue(sourceId, out hit))
                return hit;
            List<string> l;
            if (!stations.TryGetValue(itemName, out l) || l.Count == 0) return null;
            int c;
            next.TryGetValue(itemName, out c);
            string id = l[c % l.Count];
            next[itemName] = c + 1;
            if (!string.IsNullOrEmpty(sourceId)) map[sourceId] = id;
            return id;
        }
        catch { return null; }
    }

    // ---------- recruitment ----------

    /// <summary>Part/full demand of a staffer: explicit job demand wins,
    /// else current weekly hours (30h and under reads as part-time), else
    /// "any" (no info: flexible, fits whatever need is left).</summary>
    static string StaffHours(EmployeeInstance emp, int weeklyHours, bool hasWeekly)
    {
        try
        {
            if (emp != null && emp.demands != null)
                foreach (var d in emp.demands)
                {
                    if (d == "ba:jobdemand_parttime") return "part";
                    if (d == "ba:jobdemand_fulltime") return "full";
                }
            if (hasWeekly) return weeklyHours <= 30 ? "part" : "full";
        }
        catch { }
        return "any";
    }

    /// <summary>Base crew's hour type for an employee: explicit demand
    /// wins; otherwise weekly hours decide (30h and under reads as
    /// part-time). Covers veterans hired before the demand system or
    /// without an hours entry.</summary>
    static string BaseHourDemand(string empId, Dictionary<string, int> weeklyHours)
    {
        try
        {
            var emp = Helpers.EmployeeHelper.GetEmployeeById(empId, false);
            if (emp != null && emp.demands != null)
                foreach (var d in emp.demands)
                {
                    if (d == "ba:jobdemand_parttime") return "part";
                    if (d == "ba:jobdemand_fulltime") return "full";
                }
            if (weeklyHours != null)
            {
                int h;
                if (weeklyHours.TryGetValue(empId, out h))
                    return h <= 30 ? "part" : "full";
            }
        }
        catch { }
        return "";
    }

    /// <summary>Assigned weekly hours per employee on the base plan,
    /// summed straight from its shifts.</summary>
    static Dictionary<string, int> BaseWeeklyHours(BuildingRegistration baseReg)
    {
        var map = new Dictionary<string, int>();
        try
        {
            if (baseReg == null || baseReg.scheduleDays == null) return map;
            foreach (var day in baseReg.scheduleDays)
            {
                if (day == null || day.workShifts == null) continue;
                foreach (var sh in day.workShifts)
                {
                    if (sh == null || string.IsNullOrEmpty(sh.employeeId)) continue;
                    int n;
                    map.TryGetValue(sh.employeeId, out n);
                    map[sh.employeeId] = n + (sh.endingHour - sh.startingHour);
                }
            }
        }
        catch { }
        return map;
    }

    public const string BreakerPartTime = "ba:headhuntersdealbreaker_parttime";
    public const string BreakerFullTime = "ba:headhuntersdealbreaker_fulltime";

    /// <summary>One headhunter plan's worth of order: a skill, an hour
    /// type (part / full / any) and a headcount. The game's plans recruit
    /// exactly one skill with one hours setting, so mixed needs split.</summary>
    public sealed class RecruitCombo
    {
        public string skill;
        public string hours;
        public int count;
    }

    static List<RecruitCombo> BuildCombos(Dictionary<string, int> missing,
        Dictionary<string, int> part, Dictionary<string, int> full)
    {
        var list = new List<RecruitCombo>();
        try
        {
            if (missing == null) return list;
            foreach (var kv in missing)
            {
                int p = 0;
                int f = 0;
                if (part != null) part.TryGetValue(kv.Key, out p);
                if (full != null) full.TryGetValue(kv.Key, out f);
                if (p > 0) list.Add(new RecruitCombo { skill = kv.Key, hours = "part", count = p });
                if (f > 0) list.Add(new RecruitCombo { skill = kv.Key, hours = "full", count = f });
                int r = kv.Value - p - f;
                if (r > 0) list.Add(new RecruitCombo { skill = kv.Key, hours = "any", count = r });
            }
        }
        catch { }
        return list;
    }

    /// <summary>What hours a plan produces: part, full, any, or broken
    /// (both breakers set: the game stops such a plan immediately).</summary>
    static string PlanHoursSetting(Buildings.Office.Headquarters.HeadhunterPlan plan)
    {
        try
        {
            if (plan == null || plan.dealBreakerTypes == null) return "any";
            bool noPart = plan.dealBreakerTypes.Contains(BreakerFullTime);
            bool noFull = plan.dealBreakerTypes.Contains(BreakerPartTime);
            if (noPart && noFull) return "broken";
            if (noPart) return "part";
            if (noFull) return "full";
            return "any";
        }
        catch { return "any"; }
    }

    /// <summary>Pre-flight: every combo needs its own plan (exact skill +
    /// hours match; an already-recruiting match is topped up for free).
    /// Returns null when capacity suffices, else a warning naming the
    /// planned assignments and the shortfall. No side effects.</summary>
    static string CheckHeadhunterCapacity(List<RecruitCombo> combos,
        out int needPlans, out int freePlans, out int coveredCombos)
    {
        needPlans = 0;
        freePlans = 0;
        coveredCombos = 0;
        try
        {
            var save = SaveGameManager.Current;
            if (save == null || save.headhunterPlans == null)
                return "No headhunter data (save not loaded).";
            var uncovered = new List<string>();
            foreach (var p in save.headhunterPlans)
            {
                if (p == null) continue;
                try
                {
                    if (!p.isRecruiting && p.HeadhunterInstance != null) freePlans++;
                }
                catch { }
            }
            foreach (var c in combos)
            {
                bool covered = false;
                foreach (var p in save.headhunterPlans)
                {
                    if (p == null || !p.isRecruiting) continue;
                    string s = null;
                    try { s = p.skillRecruiting; } catch { }
                    if (s != c.skill) continue;
                    if (PlanHoursSetting(p) == c.hours) { covered = true; break; }
                }
                if (covered) { coveredCombos++; continue; }
                needPlans++;
                uncovered.Add(SkillLabel(c.skill) + "/" + c.hours + " x" + c.count);
            }
            if (needPlans <= freePlans) return null;
            string msg = "Not enough headhunters: need " + needPlans + " plan(s) for [" +
                string.Join(", ", uncovered.ToArray()) + "] but only " + freePlans + " free";
            if (coveredCombos > 0)
                msg += " (" + coveredCombos + " combo(s) already recruiting)";
            return msg + ".";
        }
        catch (Exception ex)
        {
            return "Headhunter check failed: " + ex.Message;
        }
    }
}
