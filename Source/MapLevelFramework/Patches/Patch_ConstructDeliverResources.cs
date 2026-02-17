using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// WorkGiver_ConstructDeliverResources.ResourceDeliverJobFor 补丁 -
    /// 当本层找不到材料（vanilla 返回 null）时，搜索其他楼层。
    /// 找到后让 pawn 走楼梯去取材料再回来送到蓝图/框架。
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources), "ResourceDeliverJobFor")]
    public static class Patch_ConstructDeliverResources_ResourceDeliverJobFor
    {
        public static void Postfix(ref Job __result, Pawn pawn, IConstructible c)
        {
            if (__result != null) return;
            if (c is Blueprint_Install) return;
            if (CrossLevelJobUtility.Scanning) return;
            if (pawn?.Map == null || !pawn.Spawned) return;

            // 紧急需求检查：困了/饿了不跑楼梯
            if (HasUrgentNeed(pawn)) return;

            Map pawnMap = pawn.Map;

            LevelManager mgr;
            Map baseMap;
            if (LevelManager.IsLevelMap(pawnMap, out var parentMgr, out _))
            {
                mgr = parentMgr;
                baseMap = parentMgr.map;
            }
            else
            {
                mgr = LevelManager.GetManager(pawnMap);
                baseMap = pawnMap;
            }

            if (mgr == null || mgr.LevelCount == 0) return;

            Thing constructThing = c as Thing;
            if (constructThing == null) return;

            int currentElev = CrossLevelJobUtility.GetMapElevation(pawnMap, mgr, baseMap);

            // 遍历材料需求，找第一个本层缺少但其他层有的
            foreach (var cost in c.TotalMaterialCost())
            {
                int needed = c.ThingCountNeeded(cost.thingDef);
                if (needed <= 0) continue;

                // 搜索其他楼层
                var result = FindMaterialOnOtherFloor(
                    pawn, pawnMap, baseMap, mgr, currentElev, cost.thingDef);

                if (result.map == null) continue;

                int targetElev = CrossLevelJobUtility.GetMapElevation(result.map, mgr, baseMap);

                // 找当前层通往目标层方向的楼梯（一次走一层）
                int nextElev = targetElev > currentElev
                    ? currentElev + 1
                    : currentElev - 1;

                Building_Stairs stairs = CrossLevelJobUtility.FindStairsToElevation(
                    pawn, pawnMap, nextElev);
                if (stairs == null) continue;

                // 记录冷却
                CrossLevelJobUtility.RecordRedirect(pawn);

                // 存储取材数据
                CrossLevelJobUtility.StoreFetchData(pawn.thingIDNumber,
                    new CrossLevelJobUtility.FetchData
                    {
                        thingDef = cost.thingDef,
                        target = constructThing,
                        returnElevation = currentElev,
                        needType = CrossLevelJobUtility.NeedType.Construction
                    });

                // 创建 MLF_ReturnWithMaterial 作为延迟 job
                Job fetchJob = JobMaker.MakeJob(MLF_JobDefOf.MLF_ReturnWithMaterial);
                CrossLevelJobUtility.StoreDeferredJob(pawn, fetchJob);

                // 返回 MLF_UseStairs job
                __result = JobMaker.MakeJob(MLF_JobDefOf.MLF_UseStairs, stairs);
                return;
            }
        }

        /// <summary>
        /// 检查 pawn 当前是否不适合跑楼梯取材料：
        /// - 睡觉/娱乐/冥想管制时段
        /// - 休息或饥饿低于阈值
        /// </summary>
        private static bool HasUrgentNeed(Pawn pawn)
        {
            // 管制时段检查：非工作/任意时段不跑楼梯
            if (pawn.timetable != null)
            {
                var assignment = pawn.timetable.CurrentAssignment;
                if (assignment == TimeAssignmentDefOf.Sleep
                    || assignment == TimeAssignmentDefOf.Joy
                    || assignment == TimeAssignmentDefOf.Meditate)
                    return true;
            }

            if (pawn.needs?.rest != null && pawn.needs.rest.CurLevel < 0.25f)
                return true;
            if (pawn.needs?.food != null && pawn.needs.food.CurLevel < 0.2f)
                return true;
            return false;
        }

        private static (Map map, Thing thing) FindMaterialOnOtherFloor(
            Pawn pawn, Map pawnMap, Map baseMap, LevelManager mgr,
            int currentElev, ThingDef thingDef)
        {
            // 按距离当前层远近搜索
            Map bestMap = null;
            Thing bestThing = null;
            int bestDist = int.MaxValue;

            // 检查基地图
            if (pawnMap != baseMap)
            {
                Thing t = FindBestMaterial(baseMap, pawn, thingDef);
                if (t != null)
                {
                    int dist = System.Math.Abs(0 - currentElev);
                    if (dist < bestDist)
                    {
                        bestMap = baseMap;
                        bestThing = t;
                        bestDist = dist;
                    }
                }
            }

            // 检查各层级地图
            foreach (var level in mgr.AllLevels)
            {
                if (level.LevelMap == null || level.LevelMap == pawnMap) continue;

                Thing t = FindBestMaterial(level.LevelMap, pawn, thingDef);
                if (t != null)
                {
                    int dist = System.Math.Abs(level.elevation - currentElev);
                    if (dist < bestDist)
                    {
                        bestMap = level.LevelMap;
                        bestThing = t;
                        bestDist = dist;
                    }
                }
            }

            return (bestMap, bestThing);
        }

        private static Thing FindBestMaterial(Map map, Pawn pawn, ThingDef thingDef)
        {
            List<Thing> things = map.listerThings.ThingsOfDef(thingDef);
            for (int i = 0; i < things.Count; i++)
            {
                if (!things[i].IsForbidden(pawn) && things[i].stackCount > 0)
                    return things[i];
            }
            return null;
        }

        /// <summary>
        /// 快速检查：建造物是否需要材料且其他楼层有。
        /// 用于扫描模式下判断"这里有建造工作"。
        /// </summary>
        private static bool AnyMaterialNeededFromOtherFloor(Pawn pawn, IConstructible c)
        {
            Map pawnMap = pawn.Map;
            LevelManager mgr;
            Map baseMap;
            if (LevelManager.IsLevelMap(pawnMap, out var parentMgr, out _))
            {
                mgr = parentMgr;
                baseMap = parentMgr.map;
            }
            else
            {
                mgr = LevelManager.GetManager(pawnMap);
                baseMap = pawnMap;
            }
            if (mgr == null || mgr.LevelCount == 0) return false;

            int currentElev = CrossLevelJobUtility.GetMapElevation(pawnMap, mgr, baseMap);

            foreach (var cost in c.TotalMaterialCost())
            {
                if (c.ThingCountNeeded(cost.thingDef) <= 0) continue;
                var result = FindMaterialOnOtherFloor(pawn, pawnMap, baseMap, mgr, currentElev, cost.thingDef);
                if (result.map != null) return true;
            }
            return false;
        }
    }
}
