using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using MapLevelFramework.CrossFloor;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// 跨层需求处理 — 当 pawn 在当前层满足不了需求时，走楼梯去有资源的楼层。
    /// 不使用临时传送 hack，直接检查资源存在性，派 UseStairs job。
    /// pawn 到达后由原版 AI 自然分配需求 job。
    /// </summary>

    // ========== 困了找床 ==========
    [HarmonyPatch(typeof(JobGiver_GetRest), "TryGiveJob")]
    public static class Patch_CrossLevel_GetRest
    {
        public static void Postfix(ref Job __result, Pawn pawn)
        {
            if (pawn?.Map == null || !pawn.IsColonist) return;
            if (!pawn.Map.IsPartOfFloorSystem()) return;

            Need_Rest rest = pawn.needs?.rest;
            if (rest == null) return;

            // 有自己的床在其他楼层 → 只在 VeryTired 以上才跨层回去睡
            Building_Bed ownedBed = pawn.ownership?.OwnedBed;
            if (ownedBed != null && ownedBed.Map != pawn.Map
                && rest.CurCategory >= RestCategory.VeryTired)
            {
                Job stairJob = CrossLevelNeedsUtility.TryGoToMap(
                    pawn, ownedBed.Map);
                if (stairJob != null)
                {
                    CrossLevelNeedsUtility.LogNeed(pawn, "休息",
                        $"自己的床在{FloorMapUtility.GetMapElevation(ownedBed.Map)}F，很困了 (category={rest.CurCategory}, level={rest.CurLevelPercentage:P0})");
                    __result = stairJob;
                    return;
                }
            }

            // 当前层找不到床 → 只在 VeryTired 以上才跨层
            if (__result != null)
            {
                CrossLevelNeedsUtility.LogNeed(pawn, "休息", $"原版已分配job: {__result.def.defName}，跳过跨层");
                return;
            }
            if (rest.CurCategory < RestCategory.VeryTired) return;

            var job = CrossLevelNeedsUtility.TryFindNeedOnOtherFloor(
                pawn, CrossLevelNeedsUtility.HasAvailableBed);
            if (job != null)
            {
                CrossLevelNeedsUtility.LogNeed(pawn, "休息",
                    $"本层无空床，很困了跨层 (category={rest.CurCategory}, level={rest.CurLevelPercentage:P0})");
                __result = job;
            }
        }
    }

    // ========== 饿了找食物 ==========
    [HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
    public static class Patch_CrossLevel_GetFood
    {
        public static void Postfix(ref Job __result, Pawn pawn)
        {
            if (pawn?.Map == null || !pawn.IsColonist) return;
            if (!pawn.Map.IsPartOfFloorSystem()) return;

            if (__result != null)
            {
                CrossLevelNeedsUtility.LogNeed(pawn, "食物", $"原版已分配job: {__result.def.defName}，跳过跨层");
                return;
            }

            // 只在 UrgentlyHungry 或 Starving 时才跨层找食物。
            // 普通饥饿（Hungry）让 PrioritySorter 自然 fallthrough 到 Work，
            // 避免 pawn 刚到工作楼层就被拉回食物楼层。
            Need_Food food = pawn.needs?.food;
            if (food == null) return;
            if (food.CurCategory < HungerCategory.UrgentlyHungry)
            {
                CrossLevelNeedsUtility.LogNeed(pawn, "食物",
                    $"饥饿度不足，不跨层 (category={food.CurCategory}, level={food.CurLevelPercentage:P0})");
                return;
            }

            var job = CrossLevelNeedsUtility.TryFindNeedOnOtherFloor(
                pawn, CrossLevelNeedsUtility.HasFood);
            if (job != null)
            {
                CrossLevelNeedsUtility.LogNeed(pawn, "食物",
                    $"紧急饥饿跨层 (category={food.CurCategory}, level={food.CurLevelPercentage:P0})");
                __result = job;
            }
        }
    }

    // ========== 找娱乐 ==========
    [HarmonyPatch(typeof(JobGiver_GetJoy), "TryGiveJob")]
    public static class Patch_CrossLevel_GetJoy
    {
        public static void Postfix(ref Job __result, Pawn pawn)
        {
            if (pawn?.Map == null || !pawn.IsColonist) return;
            if (!pawn.Map.IsPartOfFloorSystem()) return;

            if (__result != null)
            {
                CrossLevelNeedsUtility.LogNeed(pawn, "娱乐", $"原版已分配job: {__result.def.defName}，跳过跨层");
                return;
            }

            // 只在 joy 非常低时才跨层，避免轻微缺乏就把 pawn 从工作楼层拉走
            Need_Joy joy = pawn.needs?.joy;
            if (joy == null) return;
            if (joy.CurLevelPercentage > 0.15f)
            {
                CrossLevelNeedsUtility.LogNeed(pawn, "娱乐",
                    $"娱乐度不够低，不跨层 (level={joy.CurLevelPercentage:P0})");
                return;
            }

            var job = CrossLevelNeedsUtility.TryFindNeedOnOtherFloor(
                pawn, CrossLevelNeedsUtility.HasJoySource);
            if (job != null)
            {
                CrossLevelNeedsUtility.LogNeed(pawn, "娱乐",
                    $"娱乐极低跨层 (level={joy.CurLevelPercentage:P0})");
                __result = job;
            }
        }
    }

    /// <summary>
    /// 跨层需求工具方法。
    /// </summary>
    public static class CrossLevelNeedsUtility
    {
        /// <summary>
        /// 尝试派 pawn 走楼梯去指定地图。电梯模式：直达目标楼层。
        /// </summary>
        public static Job TryGoToMap(Pawn pawn, Map targetMap)
        {
            Map pawnMap = pawn.Map;
            int pawnElev = FloorMapUtility.GetMapElevation(pawnMap);
            int targetElev = FloorMapUtility.GetMapElevation(targetMap);
            if (pawnElev == targetElev) return null;

            Building_Stairs stairs =
                FloorMapUtility.FindStairsToFloor(pawn, pawnMap, targetElev);
            if (stairs == null) return null;

            Job job = JobMaker.MakeJob(MLF_JobDefOf.MLF_UseStairs, stairs);
            job.targetB = new IntVec3(targetElev, 0, 0);
            return job;
        }

        /// <summary>
        /// 在其他楼层找满足条件的地图，派 pawn 走楼梯过去。
        /// </summary>
        public static Job TryFindNeedOnOtherFloor(
            Pawn pawn, Func<Map, bool> hasResource)
        {
            Map pawnMap = pawn.Map;
            int pawnElev = FloorMapUtility.GetMapElevation(pawnMap);
            Map bestMap = null;
            int bestDist = int.MaxValue;

            foreach (Map otherMap in pawnMap.BaseMapAndFloorMaps())
            {
                if (otherMap == pawnMap) continue;
                if (!hasResource(otherMap)) continue;

                int dist = Math.Abs(
                    FloorMapUtility.GetMapElevation(otherMap) - pawnElev);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestMap = otherMap;
                }
            }

            if (bestMap == null) return null;
            return TryGoToMap(pawn, bestMap);
        }

        public static bool HasAvailableBed(Map map)
        {
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                Building_Bed bed = b as Building_Bed;
                if (bed == null) continue;
                if (bed.Medical) continue;
                if (bed.CurOccupants != null) continue;
                return true;
            }
            return false;
        }

        public static bool HasFood(Map map)
        {
            return map.listerThings.ThingsInGroup(
                ThingRequestGroup.FoodSourceNotPlantOrTree).Count > 0;
        }

        public static bool HasJoySource(Map map)
        {
            return map.listerThings.ThingsInGroup(
                ThingRequestGroup.BuildingArtificial).Count > 0;
        }

        public static void LogNeed(Pawn pawn, string needType, string reason)
        {
            if (!MapLevelFrameworkMod.Settings.debugPathfindingAndJob) return;
            int elev = FloorMapUtility.GetMapElevation(pawn.Map);
            Log.Message($"【MLF】寻路与job检测-{pawn.LabelShort}—需求跨层: {needType}，原因: {reason}，当前楼层: {elev}F");
        }
    }
}
