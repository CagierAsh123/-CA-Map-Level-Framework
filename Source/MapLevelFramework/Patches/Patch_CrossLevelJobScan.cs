using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// 跨层级工作扫描 - 让 pawn 在当前地图找不到工作时，自动去其他楼层找工作。
    /// 使用 CrossLevelJobUtility 共用的跨层扫描逻辑。
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Work), "TryIssueJobPackage")]
    public static class Patch_CrossLevelJobScan
    {
        public static void Postfix(
            ref ThinkResult __result,
            JobGiver_Work __instance,
            Pawn pawn,
            JobIssueParams jobParams)
        {
            if (CrossLevelJobUtility.Scanning) return;
            if (__result != ThinkResult.NoJob) return;
            if (pawn?.Map == null || !pawn.Spawned) return;

            Job stairJob = CrossLevelJobUtility.TryCrossLevelScan(pawn, () =>
            {
                ThinkResult result = __instance.TryIssueJobPackage(pawn, jobParams);
                return result != ThinkResult.NoJob ? result.Job : null;
            });

            if (stairJob != null)
            {
                __result = new ThinkResult(stairJob, __instance, null, false);
                return;
            }

            // 跨层材料搬运：本层有材料，其他层有需求 → 拿材料走楼梯送过去
            Job fetchJob = TryCrossLevelMaterialFetch(pawn);
            if (fetchJob != null)
            {
                __result = new ThinkResult(fetchJob, __instance, null, false);
                return;
            }
        }

        /// <summary>
        /// 通用跨层材料搬运扫描：扫描其他楼层的需求（建造、加油等），
        /// 如果本层有所需材料，返回 MLF_ReturnWithMaterial job。
        /// </summary>
        private static Job TryCrossLevelMaterialFetch(Pawn pawn)
        {
            if (pawn?.Map == null) return null;
            if (CrossLevelJobUtility.IsOnCooldown(pawn, CrossLevelJobUtility.FetchMaterialCooldownTicks))
                return null;

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
            if (mgr == null || mgr.LevelCount == 0) return null;

            // 检查 pawn 启用了哪些工作类型
            bool canConstruct = pawn.workSettings != null
                && pawn.workSettings.WorkIsActive(WorkTypeDefOf.Construction);
            bool canHaul = pawn.workSettings != null
                && pawn.workSettings.WorkIsActive(WorkTypeDefOf.Hauling);

            if (!canConstruct && !canHaul) return null;

            int currentElev = CrossLevelJobUtility.GetMapElevation(pawnMap, mgr, baseMap);

            // 收集其他楼层地图
            var otherMaps = new List<(Map map, int elevation)>();
            if (pawnMap != baseMap)
                otherMaps.Add((baseMap, 0));
            foreach (var level in mgr.AllLevels)
            {
                if (level.LevelMap != null && level.LevelMap != pawnMap)
                    otherMaps.Add((level.LevelMap, level.elevation));
            }
            if (otherMaps.Count == 0) return null;

            // 按距离排序
            otherMaps.Sort((a, b) =>
                System.Math.Abs(a.elevation - currentElev)
                    .CompareTo(System.Math.Abs(b.elevation - currentElev)));

            foreach (var (otherMap, targetElev) in otherMaps)
            {
                // 确保有楼梯可以到达
                int nextElev = targetElev > currentElev
                    ? currentElev + 1
                    : currentElev - 1;
                if (CrossLevelJobUtility.FindStairsToElevation(pawn, pawnMap, nextElev) == null)
                    continue;

                // 建造：蓝图和框架
                if (canConstruct)
                {
                    Job job = TryFetchForConstruction(pawn, pawnMap, otherMap, targetElev);
                    if (job != null) return job;
                }

                // 加油：需要燃料的建筑
                if (canHaul)
                {
                    Job job = TryFetchForRefuel(pawn, pawnMap, otherMap, targetElev);
                    if (job != null) return job;
                }
            }

            return null;
        }

        // ========== 建造 ==========

        private static Job TryFetchForConstruction(Pawn pawn, Map pawnMap, Map targetMap, int targetElev)
        {
            // 蓝图
            var blueprints = targetMap.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
            for (int i = 0; i < blueprints.Count; i++)
            {
                if (blueprints[i] is Blueprint_Install) continue;
                Job job = TryFetchForConstructible(pawn, pawnMap, blueprints[i], targetElev);
                if (job != null) return job;
            }
            // 框架
            var frames = targetMap.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
            for (int i = 0; i < frames.Count; i++)
            {
                Job job = TryFetchForConstructible(pawn, pawnMap, frames[i], targetElev);
                if (job != null) return job;
            }
            return null;
        }

        private static Job TryFetchForConstructible(Pawn pawn, Map pawnMap, Thing constructThing, int targetElev)
        {
            IConstructible c = constructThing as IConstructible;
            if (c == null) return null;
            if (constructThing.IsForbidden(pawn)) return null;

            foreach (var cost in c.TotalMaterialCost())
            {
                int needed = c.ThingCountNeeded(cost.thingDef);
                if (needed <= 0) continue;

                Thing material = FindMaterialOnMap(pawn, pawnMap, cost.thingDef);
                if (material == null) continue;

                return MakeFetchJob(pawn, cost.thingDef, constructThing, targetElev,
                    CrossLevelJobUtility.NeedType.Construction);
            }
            return null;
        }

        // ========== 加油 ==========

        private static Job TryFetchForRefuel(Pawn pawn, Map pawnMap, Map targetMap, int targetElev)
        {
            var refuelables = targetMap.listerThings.ThingsInGroup(ThingRequestGroup.Refuelable);
            for (int i = 0; i < refuelables.Count; i++)
            {
                Thing t = refuelables[i];
                if (t.IsForbidden(pawn)) continue;
                if (t.Faction != pawn.Faction) continue;

                CompRefuelable comp = t.TryGetComp<CompRefuelable>();
                if (comp == null || comp.IsFull) continue;
                if (!comp.allowAutoRefuel || !comp.ShouldAutoRefuelNow) continue;

                // 找本层有没有合适的燃料
                ThingFilter fuelFilter = comp.Props.fuelFilter;
                Thing fuel = GenClosest.ClosestThingReachable(
                    pawn.Position, pawnMap,
                    fuelFilter.BestThingRequest,
                    PathEndMode.ClosestTouch,
                    TraverseParms.For(pawn),
                    9999f,
                    f => !f.IsForbidden(pawn) && pawn.CanReserve(f)
                         && fuelFilter.Allows(f));

                if (fuel == null) continue;

                return MakeFetchJob(pawn, fuel.def, t, targetElev,
                    CrossLevelJobUtility.NeedType.Refuel);
            }
            return null;
        }

        // ========== 通用工具 ==========

        private static Thing FindMaterialOnMap(Pawn pawn, Map map, ThingDef thingDef)
        {
            return GenClosest.ClosestThingReachable(
                pawn.Position, map,
                ThingRequest.ForDef(thingDef),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                9999f,
                t => !t.IsForbidden(pawn) && pawn.CanReserve(t) && t.stackCount > 0);
        }

        private static Job MakeFetchJob(Pawn pawn, ThingDef materialDef, Thing target,
            int targetElev, CrossLevelJobUtility.NeedType needType)
        {
            CrossLevelJobUtility.StoreFetchData(pawn.thingIDNumber,
                new CrossLevelJobUtility.FetchData
                {
                    thingDef = materialDef,
                    target = target,
                    returnElevation = targetElev,
                    needType = needType
                });
            CrossLevelJobUtility.RecordRedirect(pawn);
            return JobMaker.MakeJob(MLF_JobDefOf.MLF_ReturnWithMaterial);
        }
    }
}
