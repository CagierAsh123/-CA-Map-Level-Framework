using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MapLevelFramework.CrossFloor
{
    /// <summary>
    /// 在 job 分配层面处理跨楼层工作和材料搬运。
    /// 当 pawn 在当前楼层找不到工作时，按优先级：
    ///   P1: 本层有材料，其他层蓝图需要 → HaulToStairs（搬到楼梯传送）
    ///   P2: 本层有蓝图缺材料，其他层有材料 → UseStairs 去取
    ///   P3: 本层无工作，其他层有 → UseStairs 过去（带意图）
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage))]
    public static class Patch_JobGiver_Work_CrossFloor
    {
        private static readonly Dictionary<int, int> lastCrossFloorTick
            = new Dictionary<int, int>();
        private const int CooldownTicks = 600;

        // 失败楼层追踪：pawnId → (mapId → failTick)
        // 当 pawn 到达某楼层但原版找不到工作时，标记该楼层为"失败"
        // P3 会跳过最近失败的楼层，防止反复弹跳
        private static readonly Dictionary<int, Dictionary<int, int>> failedFloors
            = new Dictionary<int, Dictionary<int, int>>();
        private const int FailedFloorCooldown = 2500; // ~42 秒

        private static bool DebugLog => MapLevelFrameworkMod.Settings?.debugPathfindingAndJob ?? false;

        private static string ElevLabel(int elev)
        {
            if (elev > 0) return $"{elev + 1}F";
            if (elev < 0) return $"B{-elev}";
            return "1F";
        }

        private static void LogPJ(Pawn pawn, string msg)
        {
            if (DebugLog)
                Log.Message($"【MLF】寻路与job检测-{pawn.LabelShort}—{msg}");
        }

        public static void Postfix(
            ref ThinkResult __result,
            JobGiver_Work __instance,
            Pawn pawn)
        {
            if (__instance.emergency) return;

            Map pawnMap = pawn?.Map;
            if (pawnMap == null) return;
            if (!pawnMap.IsPartOfFloorSystem()) return;
            if (!pawn.IsColonist) return;

            int pawnElev = FloorMapUtility.GetMapElevation(pawnMap);
            int curTick = Find.TickManager?.TicksGame ?? 0;
            if (lastCrossFloorTick.TryGetValue(pawn.thingIDNumber, out int lastTick)
                && curTick - lastTick < CooldownTicks)
            {
                return;
            }

            // ===== 本层有工作：检查其他层是否有更高优先级的 =====
            if (__result.Job != null)
            {
                // 清除当前楼层的失败标记
                ClearFailedFloor(pawn.thingIDNumber, pawnMap.uniqueID);

                int localPri = GetJobPriority(pawn, __result.Job);
                if (localPri <= 1) return; // 已是最高优先级，不用比

                Job betterJob = TryFindHigherPriorityWork(pawn, pawnMap, localPri);
                if (betterJob != null)
                {
                    int destElev = betterJob.targetB.IsValid ? betterJob.targetB.Cell.x : -999;
                    LogPJ(pawn, $"本层有工作(pri={localPri})，但{ElevLabel(destElev)}有更高优先级工作→跨层");
                    __result = new ThinkResult(betterJob, __instance, null, false);
                }
                return;
            }

            // ===== 本层无工作：跨层扫描 =====
            // 标记当前楼层为"失败"（原版找不到工作），防止其他层 P3 把 pawn 送回来
            MarkFloorFailed(pawn.thingIDNumber, pawnMap.uniqueID, curTick);
            LogPJ(pawn, $"在{ElevLabel(pawnElev)}，本层无工作，开始跨层扫描");

            // P1: 本层有材料，其他层蓝图需要 → 封装投递 job（全程手持）
            Job deliverJob = TryCreateCrossFloorDeliverJob(pawn, pawnMap);
            if (deliverJob != null)
            {
                LogPJ(pawn, $"P1命中→封装投递 {deliverJob.targetA.Thing?.LabelShort}");
                __result = new ThinkResult(deliverJob, __instance, null, false);
                return;
            }

            // P3: 本层无工作 → 去有工作的楼层（智能过滤）
            Job goJob = TryCreateGoToWorkFloorJob(pawn, pawnMap);
            if (goJob != null)
            {
                int destElev = goJob.targetB.IsValid ? goJob.targetB.Cell.x : -999;
                LogPJ(pawn, $"P3命中→去{ElevLabel(destElev)}找工作");
                __result = new ThinkResult(goJob, __instance, null, false);
                return;
            }

            LogPJ(pawn, "P3未命中，无跨层工作");
            // 扫描完毕，设置冷却（即使没找到，也避免频繁扫描）
            lastCrossFloorTick[pawn.thingIDNumber] = curTick;
        }

        // ========== 优先级比较 ==========

        /// <summary>
        /// 获取 job 对应的工作优先级（1=最高，4=最低）。
        /// </summary>
        private static int GetJobPriority(Pawn pawn, Job job)
        {
            if (job?.workGiverDef?.workType == null) return 4;
            if (pawn.workSettings == null) return 4;
            return pawn.workSettings.GetPriority(job.workGiverDef.workType);
        }

        /// <summary>
        /// 在其他楼层找比 localPriority 更高优先级的工作。
        /// 返回 UseStairs job（带意图），或 null。
        /// </summary>
        private static Job TryFindHigherPriorityWork(Pawn pawn, Map pawnMap, int localPriority)
        {
            var ws = pawn.workSettings;
            if (ws == null) return null;

            int bestElev = 0;
            Map bestFloor = null;
            Thing bestTarget = null;
            int bestPri = localPriority; // 要找比这个更高（数字更小）的

            foreach (Map otherMap in pawnMap.BaseMapAndFloorMaps())
            {
                if (otherMap == pawnMap) continue;

                // 跳过最近失败的楼层
                int curTick = Find.TickManager?.TicksGame ?? 0;
                if (IsFloorFailed(pawn.thingIDNumber, otherMap.uniqueID, curTick))
                    continue;

                // 遍历工作类型，只看优先级比本层当前 job 更高的
                Thing target = FindWorkWithPriorityBetterThan(otherMap, pawn, bestPri);
                if (target == null) continue;

                int otherElev = FloorMapUtility.GetMapElevation(otherMap);
                // 找到了更高优先级的工作
                bestFloor = otherMap;
                bestElev = otherElev;
                bestTarget = target;
                // 继续找，可能有更高优先级的
            }

            if (bestFloor == null) return null;

            Building_Stairs stairs = FloorMapUtility.FindStairsToFloor(pawn, pawnMap, bestElev);
            if (stairs == null) return null;

            if (bestTarget != null)
            {
                CrossFloorIntent.Set(pawn, bestFloor.uniqueID, bestTarget.Position, bestTarget.def);
            }

            Job job = JobMaker.MakeJob(MLF_JobDefOf.MLF_UseStairs, stairs);
            job.targetB = new IntVec3(bestElev, 0, 0);
            return job;
        }

        /// <summary>
        /// 在指定地图上找优先级比 maxPri 更高（数字更小）的工作目标。
        /// </summary>
        private static Thing FindWorkWithPriorityBetterThan(Map map, Pawn pawn, int maxPri)
        {
            var ws = pawn.workSettings;

            // 按优先级从高到低检查各工作类型
            // 消防 pri=1 通常
            if (CheckWorkType(ws, WorkTypeDefOf.Firefighter, maxPri))
            {
                var fires = map.listerThings.ThingsOfDef(ThingDefOf.Fire);
                if (fires.Count > 0) return fires[0];
            }

            // 医疗
            if (CheckWorkType(ws, WorkTypeDefOf.Doctor, maxPri))
            {
                var colonists = map.mapPawns.FreeColonistsSpawned;
                for (int i = 0; i < colonists.Count; i++)
                {
                    if (colonists[i].Downed || (colonists[i].health?.HasHediffsNeedingTend(false) ?? false))
                        return colonists[i]; // pawn 也是 Thing
                }
            }

            // 建造 — 智能过滤：已完成框架、材料已齐的蓝图、拆除
            if (CheckWorkType(ws, WorkTypeDefOf.Construction, maxPri))
            {
                // 已完成框架
                var frames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
                for (int fi = 0; fi < frames.Count; fi++)
                {
                    if (frames[fi].IsForbidden(pawn)) continue;
                    Frame frame = frames[fi] as Frame;
                    if (frame != null && frame.IsCompleted()) return frames[fi];
                }
                // 拆除
                Thing dt = FirstDesignationTarget(map, DesignationDefOf.Deconstruct);
                if (dt != null) return dt;
            }

            // 搬运
            if (CheckWorkType(ws, WorkTypeDefOf.Hauling, maxPri))
            {
                var haulables = map.listerHaulables.ThingsPotentiallyNeedingHauling();
                if (haulables.Count > 0)
                {
                    foreach (Thing h in haulables) return h;
                }
            }

            // 清洁
            if (CheckWorkType(ws, WorkTypeDefOf.Cleaning, maxPri))
            {
                var filth = map.listerFilthInHomeArea?.FilthInHomeArea;
                if (filth != null && filth.Count > 0) return filth[0];
            }

            // 开采
            if (CheckWorkType(ws, WorkTypeDefOf.Mining, maxPri))
            {
                Thing t = FirstDesignationTarget(map, DesignationDefOf.Mine);
                if (t != null) return t;
            }

            // 割除/收获
            if (CheckWorkType(ws, WorkTypeDefOf.PlantCutting, maxPri))
            {
                Thing t = FirstDesignationTarget(map, DesignationDefOf.CutPlant);
                if (t != null) return t;
                t = FirstDesignationTarget(map, DesignationDefOf.HarvestPlant);
                if (t != null) return t;
            }

            // 狩猎
            if (CheckWorkType(ws, WorkTypeDefOf.Hunting, maxPri))
            {
                Thing t = FirstDesignationTarget(map, DesignationDefOf.Hunt);
                if (t != null) return t;
            }

            // 驯服
            if (CheckWorkType(ws, WorkTypeDefOf.Handling, maxPri))
            {
                Thing t = FirstDesignationTarget(map, DesignationDefOf.Tame);
                if (t != null) return t;
            }

            return null;
        }

        /// <summary>
        /// 检查 pawn 是否启用了该工作类型，且优先级比 maxPri 更高（数字更小）。
        /// </summary>
        private static bool CheckWorkType(Pawn_WorkSettings ws, WorkTypeDef workType, int maxPri)
        {
            if (!ws.WorkIsActive(workType)) return false;
            return ws.GetPriority(workType) < maxPri;
        }

        /// <summary>
        /// P1（新版）: 本层有材料，其他层蓝图/框架需要 → 封装投递 job。
        /// 全程手持材料：拿起 → 走楼梯 → 传送 → 交给原版 HaulToContainer。
        /// </summary>
        private static Job TryCreateCrossFloorDeliverJob(Pawn pawn, Map pawnMap)
        {
            if (!pawn.workSettings.WorkIsActive(WorkTypeDefOf.Construction)
                && !pawn.workSettings.WorkIsActive(WorkTypeDefOf.Hauling))
                return null;

            foreach (Map otherMap in pawnMap.BaseMapAndFloorMaps())
            {
                if (otherMap == pawnMap) continue;

                // 扫描其他层的蓝图和框架
                var constructibles = otherMap.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
                var frames = otherMap.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);

                Job job = TryScanConstructibles(pawn, pawnMap, otherMap, constructibles);
                if (job != null) return job;

                job = TryScanConstructibles(pawn, pawnMap, otherMap, frames);
                if (job != null) return job;
            }
            return null;
        }

        private static Job TryScanConstructibles(
            Pawn pawn, Map pawnMap, Map otherMap, List<Thing> constructibles)
        {
            int otherElev = FloorMapUtility.GetMapElevation(otherMap);
            for (int ci = 0; ci < constructibles.Count; ci++)
            {
                IConstructible c = constructibles[ci] as IConstructible;
                if (c == null) continue;
                if (constructibles[ci].IsForbidden(pawn)) continue;

                var costs = c.TotalMaterialCost();
                for (int i = 0; i < costs.Count; i++)
                {
                    int needed = c.ThingCountNeeded(costs[i].thingDef);
                    if (needed <= 0) continue;

                    // 目标层已有足够散落材料？跳过（不用 ThingsAvailableAnywhere，它在子地图上有误报）
                    int looseOnDest = CountLooseMaterial(otherMap, costs[i].thingDef);
                    if (looseOnDest >= needed)
                        continue;
                    int actualNeeded = needed - looseOnDest;

                    // 本层有这个材料？
                    Thing material = FindMaterialOnMap(pawnMap, pawn, costs[i].thingDef);
                    if (material == null)
                    {
                        LogPJ(pawn, $"  P1: {ElevLabel(otherElev)}蓝图需要{costs[i].thingDef.label}x{actualNeeded}，本层找不到");
                        continue;
                    }

                    // 找到楼梯
                    Building_Stairs stairs =
                        FloorMapUtility.FindStairsToFloor(pawn, pawnMap, otherElev);
                    if (stairs == null) break; // 这层没楼梯井连通，跳过整层

                    int toCarry = System.Math.Min(actualNeeded, material.stackCount);
                    toCarry = System.Math.Min(toCarry,
                        pawn.carryTracker.MaxStackSpaceEver(material.def));

                    LogPJ(pawn, $"  P1命中: 搬{material.LabelShort}x{toCarry}→{ElevLabel(otherElev)} 给{constructibles[ci].LabelShort}@{constructibles[ci].Position}");

                    IntVec3 encoded = new IntVec3(
                        otherElev,
                        constructibles[ci].Position.x,
                        constructibles[ci].Position.z);

                    Job job = JobMaker.MakeJob(
                        MLF_JobDefOf.MLF_DeliverResourcesCrossFloor,
                        material, stairs);
                    job.targetC = encoded;
                    job.count = toCarry;
                    return job;
                }
            }
            return null;
        }

        /// <summary>
        /// P1（旧版，保留备用）: 本层有材料，其他层需要 → 搬到楼梯传送过去。
        /// 覆盖：建造材料、燃料、Bill 原料、药品、囚犯食物。
        /// </summary>
        private static Job TryCreateHaulToStairsJob(Pawn pawn, Map pawnMap)
        {
            foreach (Map otherMap in pawnMap.BaseMapAndFloorMaps())
            {
                if (otherMap == pawnMap) continue;

                // 收集该层所有材料需求
                CollectMaterialNeeds(otherMap, needsBuffer);

                for (int i = 0; i < needsBuffer.Count; i++)
                {
                    var need = needsBuffer[i];

                    // 扣除目标层已有的散落材料
                    int looseOnDest = CountLooseMaterial(otherMap, need.thingDef);
                    int actualNeeded = need.count - looseOnDest;
                    if (actualNeeded <= 0) continue;

                    // 在本层找这个材料
                    Thing material = FindMaterialOnMap(pawnMap, pawn, need.thingDef);
                    if (material == null) continue;

                    // 电梯模式：直接找通往目标层的楼梯（楼梯井）
                    int otherElev = FloorMapUtility.GetMapElevation(otherMap);
                    Building_Stairs stairs =
                        FloorMapUtility.FindStairsToFloor(pawn, pawnMap, otherElev);
                    if (stairs == null) break; // 这层没楼梯井连通，跳过整层

                    int toCarry = Math.Min(actualNeeded, material.stackCount);
                    Job job = JobMaker.MakeJob(
                        MLF_JobDefOf.MLF_HaulToStairs, material, stairs);
                    job.count = toCarry;
                    job.targetC = new IntVec3(otherElev, 0, 0);
                    return job;
                }
            }
            return null;
        }

        /// <summary>
        /// P2: 本层有需求但缺材料，其他层有 → UseStairs 去取。
        /// </summary>
        private static Job TryCreateFetchMaterialJob(Pawn pawn, Map pawnMap)
        {
            // 收集本层的材料需求
            CollectMaterialNeeds(pawnMap, needsBuffer);

            for (int i = 0; i < needsBuffer.Count; i++)
            {
                var need = needsBuffer[i];

                // 本层有这个材料？跳过（原版会处理）
                if (pawnMap.listerThings.ThingsOfDef(need.thingDef).Count > 0)
                    continue;

                // 其他层找
                foreach (Map otherMap in pawnMap.BaseMapAndFloorMaps())
                {
                    if (otherMap == pawnMap) continue;
                    if (otherMap.listerThings.ThingsOfDef(need.thingDef).Count == 0)
                        continue;

                    int otherElev = FloorMapUtility.GetMapElevation(otherMap);
                    Building_Stairs stairs =
                        FloorMapUtility.FindStairsToFloor(pawn, pawnMap, otherElev);
                    if (stairs == null) continue;

                    Job job = JobMaker.MakeJob(MLF_JobDefOf.MLF_UseStairs, stairs);
                    job.targetB = new IntVec3(otherElev, 0, 0);
                    return job;
                }
            }
            return null;
        }

        /// <summary>
        /// P3: 本层无工作 → 去有工作的最近楼层。
        /// 尝试找到具体工作目标并设置意图，到达后直接执行。
        /// </summary>
        private static Job TryCreateGoToWorkFloorJob(Pawn pawn, Map pawnMap)
        {
            int currentElev = FloorMapUtility.GetMapElevation(pawnMap);
            Map bestFloor = null;
            int bestElevDist = int.MaxValue;
            int bestElev = 0;
            Thing bestTarget = null;

            foreach (Map otherMap in pawnMap.BaseMapAndFloorMaps())
            {
                if (otherMap == pawnMap) continue;

                int otherElev = FloorMapUtility.GetMapElevation(otherMap);

                // 跳过最近失败的楼层（防止弹跳）
                int curTick = Find.TickManager?.TicksGame ?? 0;
                if (IsFloorFailed(pawn.thingIDNumber, otherMap.uniqueID, curTick))
                {
                    LogPJ(pawn, $"  {ElevLabel(otherElev)}最近失败，跳过");
                    continue;
                }

                Thing workTarget = FindFirstWorkTarget(otherMap, pawn);
                if (workTarget == null && !FloorHasWork(otherMap, pawn)) continue;

                int dist = Math.Abs(otherElev - currentElev);
                if (dist < bestElevDist)
                {
                    bestElevDist = dist;
                    bestFloor = otherMap;
                    bestElev = otherElev;
                    bestTarget = workTarget;
                }
            }

            if (bestFloor == null) return null;

            // 电梯模式：直接找通往目标层的楼梯
            Building_Stairs stairs =
                FloorMapUtility.FindStairsToFloor(pawn, pawnMap, bestElev);
            if (stairs == null) return null;

            // 设置意图（如果找到了具体目标）
            if (bestTarget != null)
            {
                CrossFloorIntent.Set(pawn,
                    bestFloor.uniqueID,
                    bestTarget.Position,
                    bestTarget.def);
                LogPJ(pawn, $"P3: 设置意图 → {bestTarget.LabelShort} at {bestTarget.Position}");
            }

            Job job = JobMaker.MakeJob(MLF_JobDefOf.MLF_UseStairs, stairs);
            job.targetB = new IntVec3(bestElev, 0, 0);
            return job;
        }

        /// <summary>
        /// 在目标楼层找到第一个具体的工作目标 Thing。
        /// 用于设置意图，到达后直接执行而不是让原版重新随机扫描。
        /// 返回 null 表示有工作但无法确定具体 Thing（如研究）。
        /// </summary>
        private static Thing FindFirstWorkTarget(Map map, Pawn pawn)
        {
            var ws = pawn.workSettings;
            if (ws == null) return null;

            // 建造 — 只返回可执行的目标（已完成框架、拆除）
            if (ws.WorkIsActive(WorkTypeDefOf.Construction))
            {
                // 已完成框架
                var frames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
                for (int fi = 0; fi < frames.Count; fi++)
                {
                    if (frames[fi].IsForbidden(pawn)) continue;
                    Frame frame = frames[fi] as Frame;
                    if (frame != null && frame.IsCompleted()) return frames[fi];
                }
                // 拆除
                Thing dt = FirstDesignationTarget(map, DesignationDefOf.Deconstruct);
                if (dt != null) return dt;
            }

            // 搬运
            if (ws.WorkIsActive(WorkTypeDefOf.Hauling))
            {
                var haulables = map.listerHaulables.ThingsPotentiallyNeedingHauling();
                if (haulables.Count > 0)
                {
                    foreach (Thing h in haulables)
                    {
                        if (!h.IsForbidden(pawn)) return h;
                    }
                }
            }

            // 清洁
            if (ws.WorkIsActive(WorkTypeDefOf.Cleaning))
            {
                var filth = map.listerFilthInHomeArea?.FilthInHomeArea;
                if (filth != null && filth.Count > 0)
                {
                    for (int i = 0; i < filth.Count; i++)
                    {
                        if (!filth[i].IsForbidden(pawn)) return filth[i];
                    }
                }
            }

            // 开采（从 designation 获取 Thing）
            if (ws.WorkIsActive(WorkTypeDefOf.Mining))
            {
                Thing t = FirstDesignationTarget(map, DesignationDefOf.Mine);
                if (t != null) return t;
            }

            // 拆除/打磨 — 已在上面的 Construction 块中处理

            // 割除/收获
            if (ws.WorkIsActive(WorkTypeDefOf.PlantCutting))
            {
                Thing t = FirstDesignationTarget(map, DesignationDefOf.CutPlant);
                if (t != null) return t;
                t = FirstDesignationTarget(map, DesignationDefOf.HarvestPlant);
                if (t != null) return t;
            }

            // 狩猎
            if (ws.WorkIsActive(WorkTypeDefOf.Hunting))
            {
                Thing t = FirstDesignationTarget(map, DesignationDefOf.Hunt);
                if (t != null) return t;
            }

            // 驯服
            if (ws.WorkIsActive(WorkTypeDefOf.Handling))
            {
                Thing t = FirstDesignationTarget(map, DesignationDefOf.Tame);
                if (t != null) return t;
            }

            // 灭火
            if (ws.WorkIsActive(WorkTypeDefOf.Firefighter))
            {
                var fires = map.listerThings.ThingsOfDef(ThingDefOf.Fire);
                if (fires.Count > 0) return fires[0];
            }

            // 研究、医疗、监管等 → 无法确定具体 Thing，返回 null
            return null;
        }

        private static Thing FirstSpawnedThing(Map map, ThingRequestGroup group)
        {
            var things = map.listerThings.ThingsInGroup(group);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].Spawned) return things[i];
            }
            return null;
        }

        private static Thing FirstDesignationTarget(Map map, DesignationDef def)
        {
            foreach (Designation d in map.designationManager.SpawnedDesignationsOfDef(def))
            {
                if (d.target.HasThing && d.target.Thing.Spawned)
                    return d.target.Thing;
            }
            return null;
        }

        // ========== 材料需求收集系统 ==========

        private struct MaterialNeed
        {
            public ThingDef thingDef;
            public int count;
        }

        private static readonly List<MaterialNeed> needsBuffer = new List<MaterialNeed>();

        /// <summary>
        /// 收集指定地图上所有材料需求（建造、加油、Bill、医疗、囚犯食物）。
        /// </summary>
        private static void CollectMaterialNeeds(Map map, List<MaterialNeed> result)
        {
            result.Clear();

            // 1. 建造材料（蓝图/框架）
            AddConstructionNeeds(map, result);

            // 2. 加油（CompRefuelable）
            AddRefuelNeeds(map, result);

            // 3. Bill 原料（工作台）
            AddBillNeeds(map, result);

            // 4. 医疗（需要治疗的 pawn）
            AddMedicineNeeds(map, result);

            // 5. 囚犯食物
            AddPrisonerFoodNeeds(map, result);
        }

        private static void AddConstructionNeeds(Map map, List<MaterialNeed> result)
        {
            var constructibles = GetConstructibles(map);
            for (int i = 0; i < constructibles.Count; i++)
            {
                IConstructible c = constructibles[i] as IConstructible;
                if (c == null) continue;
                foreach (var cost in c.TotalMaterialCost())
                {
                    int needed = c.ThingCountNeeded(cost.thingDef);
                    if (needed > 0)
                        result.Add(new MaterialNeed { thingDef = cost.thingDef, count = needed });
                }
            }
        }

        private static void AddRefuelNeeds(Map map, List<MaterialNeed> result)
        {
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                CompRefuelable comp = b.TryGetComp<CompRefuelable>();
                if (comp == null || comp.IsFull) continue;

                ThingDef fuelDef = comp.Props.fuelFilter?.AnyAllowedDef;
                if (fuelDef == null) continue;

                int needed = (int)Math.Ceiling((double)comp.GetFuelCountToFullyRefuel());
                if (needed > 0)
                    result.Add(new MaterialNeed { thingDef = fuelDef, count = needed });
            }
        }

        private static void AddBillNeeds(Map map, List<MaterialNeed> result)
        {
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                IBillGiver billGiver = b as IBillGiver;
                if (billGiver == null) continue;

                BillStack bills = billGiver.BillStack;
                if (bills == null || bills.Count == 0) continue;

                for (int bi = 0; bi < bills.Count; bi++)
                {
                    Bill bill = bills[bi];
                    if (!bill.ShouldDoNow()) continue;

                    foreach (IngredientCount ing in bill.recipe.ingredients)
                    {
                        // 取 filter 中第一个允许的 ThingDef
                        ThingDef ingDef = ing.filter?.AnyAllowedDef;
                        if (ingDef == null) continue;

                        // 本层已有这个原料 → 原版处理，跳过
                        if (map.listerThings.ThingsOfDef(ingDef).Count > 0)
                            continue;

                        int needed = (int)Math.Ceiling(
                            (double)ing.GetBaseCount());
                        if (needed > 0)
                            result.Add(new MaterialNeed { thingDef = ingDef, count = needed });
                    }
                }
            }
        }

        private static void AddMedicineNeeds(Map map, List<MaterialNeed> result)
        {
            // 有需要治疗的殖民者但本层没药
            bool needsMedicine = false;
            var colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                if (colonists[i].health?.HasHediffsNeedingTend(false) ?? false)
                {
                    needsMedicine = true;
                    break;
                }
            }
            if (!needsMedicine) return;

            // 本层有药就不管
            if (map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine).Count > 0)
                return;

            // 需要药但没有 → 加入需求（用 MedicineIndustrial 作为默认）
            result.Add(new MaterialNeed
            {
                thingDef = ThingDefOf.MedicineIndustrial,
                count = 1
            });
            // 也接受草药
            result.Add(new MaterialNeed
            {
                thingDef = ThingDefOf.MedicineHerbal,
                count = 1
            });
        }

        private static void AddPrisonerFoodNeeds(Map map, List<MaterialNeed> result)
        {
            if (map.mapPawns.PrisonersOfColonySpawned.Count == 0) return;

            // 本层有食物就不管
            if (map.listerThings.ThingsInGroup(
                    ThingRequestGroup.FoodSourceNotPlantOrTree).Count > 0)
                return;

            // 需要食物但没有 → 加入需求（用 MealSimple 作为默认）
            result.Add(new MaterialNeed
            {
                thingDef = ThingDefOf.MealSimple,
                count = 1
            });
        }

        // ========== 工具方法 ==========

        /// <summary>
        /// 获取地图上所有蓝图和框架。
        /// </summary>
        private static List<Thing> GetConstructibles(Map map)
        {
            var result = new List<Thing>();
            result.AddRange(
                map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));
            result.AddRange(
                map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame));
            return result;
        }

        /// <summary>
        /// 统计地图上散落的指定材料总数（已 Spawn、未被安装的）。
        /// 用于避免重复搬运：目标层已经有足够材料就不再搬了。
        /// </summary>
        private static int CountLooseMaterial(Map map, ThingDef matDef)
        {
            int total = 0;
            var things = map.listerThings.ThingsOfDef(matDef);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].Spawned)
                    total += things[i].stackCount;
            }
            return total;
        }

        /// <summary>
        /// 在指定地图上找 pawn 可达的、未被禁止的材料。
        /// </summary>
        private static Thing FindMaterialOnMap(
            Map map, Pawn pawn, ThingDef matDef)
        {
            var things = map.listerThings.ThingsOfDef(matDef);
            Thing best = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (!t.Spawned) continue;
                if (t.IsForbidden(pawn)) continue;
                if (!pawn.CanReserve(t)) continue;
                if (!pawn.CanReach(t, PathEndMode.ClosestTouch,
                        Danger.Deadly)) continue;

                float dist = t.Position.DistanceToSquared(pawn.Position);
                if (dist < bestDist)
                {
                    best = t;
                    bestDist = dist;
                }
            }
            return best;
        }

        /// <summary>
        /// 检查该楼层是否有这个 pawn 能做的工作。
        /// 根据 pawn 的 workSettings 过滤，避免白跑。
        /// </summary>
        private static bool FloorHasWork(Map map, Pawn pawn)
        {
            var ws = pawn.workSettings;
            if (ws == null) return false;

            int elev = FloorMapUtility.GetMapElevation(map);

            // 建造（蓝图、框架）— 智能过滤：只有材料已齐或不需要材料时才算
            if (ws.WorkIsActive(WorkTypeDefOf.Construction))
            {
                // 已完成的框架（可直接 FinishFrame）
                var frames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
                for (int i = 0; i < frames.Count; i++)
                {
                    if (frames[i].IsForbidden(pawn)) continue;
                    Frame frame = frames[i] as Frame;
                    if (frame != null && frame.IsCompleted())
                    { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 已完成框架(FinishFrame)"); return true; }
                }

                // 蓝图 — 只有本层有材料且未被禁止时才算（否则由封装 job 搬材料）
                var blueprints = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
                for (int i = 0; i < blueprints.Count; i++)
                {
                    if (blueprints[i].IsForbidden(pawn)) continue;
                    IConstructible c = blueprints[i] as IConstructible;
                    if (c == null) continue;
                    if (HasAllMaterialsOnMap(map, c, pawn))
                    { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 蓝图材料已齐(Construction)"); return true; }
                }

                // 零成本蓝图
                for (int i = 0; i < blueprints.Count; i++)
                {
                    if (blueprints[i].IsForbidden(pawn)) continue;
                    IConstructible c = blueprints[i] as IConstructible;
                    if (c != null && c.TotalMaterialCost().Count == 0)
                    { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 零成本蓝图(Construction)"); return true; }
                }

                // 拆除/打磨（不需要材料）
                if (HasAnyDesignation(map, DesignationDefOf.Deconstruct)
                    || HasAnyDesignation(map, DesignationDefOf.SmoothFloor)
                    || HasAnyDesignation(map, DesignationDefOf.SmoothWall))
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 拆除/打磨(Construction)"); return true; }

                // 建造/拆除屋顶
                if (FloorHasRoofWork(map))
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 屋顶(Construction)"); return true; }
            }

            // 搬运
            if (ws.WorkIsActive(WorkTypeDefOf.Hauling))
            {
                if (map.listerHaulables
                        .ThingsPotentiallyNeedingHauling().Count > 0)
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 搬运(Hauling)"); return true; }
            }

            // 清洁
            if (ws.WorkIsActive(WorkTypeDefOf.Cleaning))
            {
                var filthLister = map.listerFilthInHomeArea;
                if (filthLister != null
                    && filthLister.FilthInHomeArea.Count > 0)
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 清洁(Cleaning)"); return true; }
            }

            // 医疗：倒地或需要治疗的殖民者
            if (ws.WorkIsActive(WorkTypeDefOf.Doctor))
            {
                var colonists = map.mapPawns.FreeColonistsSpawned;
                for (int i = 0; i < colonists.Count; i++)
                {
                    Pawn p = colonists[i];
                    if (p.Downed || (p.health?.HasHediffsNeedingTend(false) ?? false))
                    { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 医疗(Doctor)"); return true; }
                }
            }

            // 监管：有囚犯
            if (ws.WorkIsActive(WorkTypeDefOf.Warden))
            {
                if (map.mapPawns.PrisonersOfColonySpawned.Count > 0)
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 监管(Warden)"); return true; }
            }

            // 开采
            if (ws.WorkIsActive(WorkTypeDefOf.Mining))
            {
                if (HasAnyDesignation(map, DesignationDefOf.Mine))
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 开采(Mining)"); return true; }
            }

            // 建造类指示（拆除、打磨）— 已在上面的 Construction 块中处理

            // 种植类指示（割除、收获）
            if (ws.WorkIsActive(WorkTypeDefOf.PlantCutting))
            {
                if (HasAnyDesignation(map, DesignationDefOf.CutPlant)
                    || HasAnyDesignation(map, DesignationDefOf.HarvestPlant))
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 割除/收获(PlantCutting)"); return true; }
            }

            // 狩猎
            if (ws.WorkIsActive(WorkTypeDefOf.Hunting))
            {
                if (HasAnyDesignation(map, DesignationDefOf.Hunt))
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 狩猎(Hunting)"); return true; }
            }

            // 驯服
            if (ws.WorkIsActive(WorkTypeDefOf.Handling))
            {
                if (HasAnyDesignation(map, DesignationDefOf.Tame))
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 驯服(Handling)"); return true; }
            }

            // 灭火
            if (ws.WorkIsActive(WorkTypeDefOf.Firefighter))
            {
                if (map.listerThings.ThingsOfDef(ThingDefOf.Fire).Count > 0)
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 灭火(Firefighter)"); return true; }
            }

            // 研究
            if (ws.WorkIsActive(WorkTypeDefOf.Research))
            {
                if (Find.ResearchManager?.GetProject() != null
                    && map.listerBuildings.ColonistsHaveResearchBench())
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 研究(Research)"); return true; }
            }

            LogPJ(pawn, $"  {ElevLabel(elev)}无匹配工作");
            return false;
        }

        private static bool HasAnyDesignation(Map map, DesignationDef def)
        {
            foreach (var _ in map.designationManager.SpawnedDesignationsOfDef(def))
                return true;
            return false;
        }

        /// <summary>
        /// 检查地图上是否有屋顶建造/拆除工作。
        /// BuildRoof 区域内有未封顶格子 → 需要建造屋顶。
        /// NoRoof 区域内有已封顶格子 → 需要拆除屋顶。
        /// </summary>
        private static bool FloorHasRoofWork(Map map)
        {
            var buildRoof = map.areaManager.BuildRoof;
            if (buildRoof != null && buildRoof.TrueCount > 0)
            {
                foreach (IntVec3 cell in buildRoof.ActiveCells)
                {
                    if (!map.roofGrid.Roofed(cell))
                        return true;
                }
            }
            var noRoof = map.areaManager.NoRoof;
            if (noRoof != null && noRoof.TrueCount > 0)
            {
                foreach (IntVec3 cell in noRoof.ActiveCells)
                {
                    if (map.roofGrid.Roofed(cell))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 检查蓝图/框架所需的所有材料是否在指定地图上可用。
        /// 用于 P3 智能过滤：只有材料已齐才派遣 pawn 过去。
        /// 使用 CountLooseMaterial 直接计数，避免 ThingsAvailableAnywhere 在子地图上的误报。
        /// </summary>
        private static bool HasAllMaterialsOnMap(Map map, IConstructible c, Pawn pawn)
        {
            var costs = c.TotalMaterialCost();
            for (int i = 0; i < costs.Count; i++)
            {
                int needed = c.ThingCountNeeded(costs[i].thingDef);
                if (needed <= 0) continue;
                int loose = CountLooseMaterial(map, costs[i].thingDef);
                if (loose < needed)
                    return false;
            }
            return true;
        }

        // ========== 失败楼层追踪 ==========

        private static void MarkFloorFailed(int pawnId, int mapId, int tick)
        {
            if (!failedFloors.TryGetValue(pawnId, out var fails))
            {
                fails = new Dictionary<int, int>();
                failedFloors[pawnId] = fails;
            }
            fails[mapId] = tick;
        }

        private static void ClearFailedFloor(int pawnId, int mapId)
        {
            if (failedFloors.TryGetValue(pawnId, out var fails))
                fails.Remove(mapId);
        }

        private static bool IsFloorFailed(int pawnId, int mapId, int curTick)
        {
            if (!failedFloors.TryGetValue(pawnId, out var fails)) return false;
            if (!fails.TryGetValue(mapId, out int failTick)) return false;
            if (curTick - failTick >= FailedFloorCooldown)
            {
                fails.Remove(mapId);
                return false;
            }
            return true;
        }

        public static void ClearCooldowns()
        {
            lastCrossFloorTick.Clear();
            failedFloors.Clear();
        }
    }
}
