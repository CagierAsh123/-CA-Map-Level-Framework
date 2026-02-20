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

            // 有自己的床在其他楼层 → 优先去那里
            Building_Bed ownedBed = pawn.ownership?.OwnedBed;
            if (ownedBed != null && ownedBed.Map != pawn.Map)
            {
                Job stairJob = CrossLevelNeedsUtility.TryGoToMap(
                    pawn, ownedBed.Map);
                if (stairJob != null)
                {
                    __result = stairJob;
                    return;
                }
            }

            // 当前层找不到床 → 找其他层有空床的
            if (__result != null) return;
            __result = CrossLevelNeedsUtility.TryFindNeedOnOtherFloor(
                pawn, CrossLevelNeedsUtility.HasAvailableBed);
        }
    }

    // ========== 饿了找食物 ==========
    [HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
    public static class Patch_CrossLevel_GetFood
    {
        public static void Postfix(ref Job __result, Pawn pawn)
        {
            if (__result != null) return;
            if (pawn?.Map == null || !pawn.IsColonist) return;
            if (!pawn.Map.IsPartOfFloorSystem()) return;

            __result = CrossLevelNeedsUtility.TryFindNeedOnOtherFloor(
                pawn, CrossLevelNeedsUtility.HasFood);
        }
    }

    // ========== 找娱乐 ==========
    [HarmonyPatch(typeof(JobGiver_GetJoy), "TryGiveJob")]
    public static class Patch_CrossLevel_GetJoy
    {
        public static void Postfix(ref Job __result, Pawn pawn)
        {
            if (__result != null) return;
            if (pawn?.Map == null || !pawn.IsColonist) return;
            if (!pawn.Map.IsPartOfFloorSystem()) return;

            __result = CrossLevelNeedsUtility.TryFindNeedOnOtherFloor(
                pawn, CrossLevelNeedsUtility.HasJoySource);
        }
    }

    /// <summary>
    /// 跨层需求工具方法。
    /// </summary>
    public static class CrossLevelNeedsUtility
    {
        /// <summary>
        /// 尝试派 pawn 走楼梯去指定地图。
        /// </summary>
        public static Job TryGoToMap(Pawn pawn, Map targetMap)
        {
            Map pawnMap = pawn.Map;
            int pawnElev = FloorMapUtility.GetMapElevation(pawnMap);
            int targetElev = FloorMapUtility.GetMapElevation(targetMap);
            if (pawnElev == targetElev) return null;

            int nextElev = targetElev > pawnElev
                ? pawnElev + 1 : pawnElev - 1;
            Building_Stairs stairs =
                FloorMapUtility.FindStairsToElevation(pawn, pawnMap, nextElev);
            if (stairs == null) return null;

            return JobMaker.MakeJob(MLF_JobDefOf.MLF_UseStairs, stairs);
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
    }
}
