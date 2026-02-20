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
    ///   P3: 本层无工作，其他层有 → UseStairs 过去
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage))]
    public static class Patch_JobGiver_Work_CrossFloor
    {
        private static readonly Dictionary<int, int> lastCrossFloorTick
            = new Dictionary<int, int>();
        private const int CooldownTicks = 600;

        public static void Postfix(
            ref ThinkResult __result,
            JobGiver_Work __instance,
            Pawn pawn)
        {
            if (__result.Job != null) return;
            if (__instance.emergency) return;

            Map pawnMap = pawn?.Map;
            if (pawnMap == null) return;
            if (!pawnMap.IsPartOfFloorSystem()) return;
            if (!pawn.IsColonist) return;

            int curTick = Find.TickManager?.TicksGame ?? 0;
            if (lastCrossFloorTick.TryGetValue(pawn.thingIDNumber, out int lastTick)
                && curTick - lastTick < CooldownTicks)
                return;

            // P1: 本层材料 → 其他层蓝图需要 → HaulToStairs
            Job haulJob = TryCreateHaulToStairsJob(pawn, pawnMap);
            if (haulJob != null)
            {
                lastCrossFloorTick[pawn.thingIDNumber] = curTick;
                __result = new ThinkResult(haulJob, __instance, null, false);
                return;
            }

            // P2: 本层蓝图缺材料，其他层有 → UseStairs 去取
            Job fetchJob = TryCreateFetchMaterialJob(pawn, pawnMap);
            if (fetchJob != null)
            {
                lastCrossFloorTick[pawn.thingIDNumber] = curTick;
                __result = new ThinkResult(fetchJob, __instance, null, false);
                return;
            }

            // P3: 本层无工作 → 去有工作的楼层
            Job goJob = TryCreateGoToWorkFloorJob(pawn, pawnMap);
            if (goJob != null)
            {
                lastCrossFloorTick[pawn.thingIDNumber] = curTick;
                __result = new ThinkResult(goJob, __instance, null, false);
                return;
            }
        }

        /// <summary>
        /// P1: 本层有材料，其他层蓝图/框架需要 → 搬到楼梯传送过去。
        /// </summary>
        private static Job TryCreateHaulToStairsJob(Pawn pawn, Map pawnMap)
        {
            int pawnElev = FloorMapUtility.GetMapElevation(pawnMap);

            foreach (Map otherMap in pawnMap.BaseMapAndFloorMaps())
            {
                if (otherMap == pawnMap) continue;

                // 检查该层的蓝图和框架
                var constructibles = GetConstructibles(otherMap);
                for (int i = 0; i < constructibles.Count; i++)
                {
                    IConstructible c = constructibles[i] as IConstructible;
                    if (c == null) continue;

                    foreach (var cost in c.TotalMaterialCost())
                    {
                        int needed = c.ThingCountNeeded(cost.thingDef);
                        if (needed <= 0) continue;

                        // 扣除目标层已有的散落材料（已传送但还没搬到蓝图的）
                        int looseOnDest = CountLooseMaterial(
                            otherMap, cost.thingDef);
                        needed -= looseOnDest;
                        if (needed <= 0) continue;

                        // 在本层找这个材料
                        Thing material = FindMaterialOnMap(
                            pawnMap, pawn, cost.thingDef);
                        if (material == null) continue;

                        // 找通往目标层的楼梯
                        int otherElev = FloorMapUtility.GetMapElevation(otherMap);
                        int nextElev = otherElev > pawnElev
                            ? pawnElev + 1 : pawnElev - 1;
                        Building_Stairs stairs =
                            FloorMapUtility.FindStairsToElevation(
                                pawn, pawnMap, nextElev);
                        if (stairs == null) continue;

                        int toCarry = Math.Min(needed, material.stackCount);
                        Job job = JobMaker.MakeJob(
                            MLF_JobDefOf.MLF_HaulToStairs,
                            material, stairs);
                        job.count = toCarry;
                        return job;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// P2: 本层蓝图缺材料（本层没有），其他层有 → UseStairs 去那层取。
        /// </summary>
        private static Job TryCreateFetchMaterialJob(Pawn pawn, Map pawnMap)
        {
            int pawnElev = FloorMapUtility.GetMapElevation(pawnMap);
            var constructibles = GetConstructibles(pawnMap);

            for (int i = 0; i < constructibles.Count; i++)
            {
                IConstructible c = constructibles[i] as IConstructible;
                if (c == null) continue;

                foreach (var cost in c.TotalMaterialCost())
                {
                    int needed = c.ThingCountNeeded(cost.thingDef);
                    if (needed <= 0) continue;

                    // 本层有这个材料？跳过（原版会处理）
                    if (pawnMap.listerThings.ThingsOfDef(cost.thingDef).Count > 0)
                        continue;

                    // 其他层找
                    foreach (Map otherMap in pawnMap.BaseMapAndFloorMaps())
                    {
                        if (otherMap == pawnMap) continue;
                        if (otherMap.listerThings.ThingsOfDef(
                                cost.thingDef).Count == 0)
                            continue;

                        int otherElev = FloorMapUtility.GetMapElevation(otherMap);
                        int nextElev = otherElev > pawnElev
                            ? pawnElev + 1 : pawnElev - 1;
                        Building_Stairs stairs =
                            FloorMapUtility.FindStairsToElevation(
                                pawn, pawnMap, nextElev);
                        if (stairs == null) continue;

                        return JobMaker.MakeJob(
                            MLF_JobDefOf.MLF_UseStairs, stairs);
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// P3: 本层无工作 → 去有工作的最近楼层。
        /// </summary>
        private static Job TryCreateGoToWorkFloorJob(Pawn pawn, Map pawnMap)
        {
            int currentElev = FloorMapUtility.GetMapElevation(pawnMap);
            Map bestFloor = null;
            int bestElevDist = int.MaxValue;

            foreach (Map otherMap in pawnMap.BaseMapAndFloorMaps())
            {
                if (otherMap == pawnMap) continue;
                if (!FloorHasWork(otherMap)) continue;

                int otherElev = FloorMapUtility.GetMapElevation(otherMap);
                int dist = Math.Abs(otherElev - currentElev);
                if (dist < bestElevDist)
                {
                    bestElevDist = dist;
                    bestFloor = otherMap;
                }
            }

            if (bestFloor == null) return null;

            int targetElev = FloorMapUtility.GetMapElevation(bestFloor);
            int nextElev = targetElev > currentElev
                ? currentElev + 1 : currentElev - 1;
            Building_Stairs stairs =
                FloorMapUtility.FindStairsToElevation(pawn, pawnMap, nextElev);
            if (stairs == null) return null;

            return JobMaker.MakeJob(MLF_JobDefOf.MLF_UseStairs, stairs);
        }

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

        private static bool FloorHasWork(Map map)
        {
            if (map.listerThings.ThingsInGroup(
                    ThingRequestGroup.Blueprint).Count > 0)
                return true;
            if (map.listerThings.ThingsInGroup(
                    ThingRequestGroup.BuildingFrame).Count > 0)
                return true;
            if (map.listerHaulables
                    .ThingsPotentiallyNeedingHauling().Count > 0)
                return true;
            var filthLister = map.listerFilthInHomeArea;
            if (filthLister != null
                && filthLister.FilthInHomeArea.Count > 0)
                return true;
            return false;
        }

        public static void ClearCooldowns()
        {
            lastCrossFloorTick.Clear();
        }
    }
}
