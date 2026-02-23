using System;
using Verse;
using Verse.AI;

namespace MapLevelFramework.CrossFloor
{
    /// <summary>
    /// 跨层 Job 统一入口。所有 JobGiver patch 共用。
    /// 负责：搜索跨层目标 → 创建 UseStairs job → 设置意图（工作类）。
    /// </summary>
    public static class CrossFloorJobUtility
    {
        private static bool DebugLog =>
            MapLevelFrameworkMod.Settings?.debugPathfindingAndJob ?? false;

        /// <summary>
        /// 为跨层目标创建 UseStairs job。
        /// 目标在其他楼层且可达 → 返回走楼梯的 job。
        /// targetC 存储目标位置提示，传送时选离它最近的传送器落点。
        /// </summary>
        public static Job TryMakeStairsJobForTarget(Pawn pawn, Thing target)
        {
            if (target == null || pawn?.Map == null) return null;
            if (target.Map == pawn.Map) return null;
            if (!pawn.Map.IsPartOfFloorSystem()) return null;

            int destElev = FloorMapUtility.GetMapElevation(target.Map);
            var stairs = FloorMapUtility.FindStairsToFloor(pawn, pawn.Map, destElev);
            if (stairs == null) return null;

            Job job = JobMaker.MakeJob(MLF_JobDefOf.MLF_UseStairs, stairs);
            job.targetB = new IntVec3(destElev, 0, 0);
            job.targetC = target.Position; // 目的地提示
            return job;
        }

        /// <summary>
        /// 在其他楼层搜索最近物品，返回 UseStairs job。
        /// 用于需求类（食物/床/娱乐）— 不设置意图，需求可替代。
        /// </summary>
        public static Job FindNeedAcrossFloors(
            Pawn pawn, ThingRequest req,
            PathEndMode peMode, TraverseParms tp,
            float maxDist, Predicate<Thing> validator = null)
        {
            Thing found = GenClosestCrossFloor.ClosestThingOnOtherFloors(
                pawn.Position, pawn.Map, req, peMode, tp, maxDist, validator);
            if (found == null) return null;

            Job stairsJob = TryMakeStairsJobForTarget(pawn, found);
            if (stairsJob != null && DebugLog)
            {
                int destElev = FloorMapUtility.GetMapElevation(found.Map);
                Log.Message($"【MLF】寻路与job检测-{pawn.LabelShort}—" +
                    $"跨层需求: 找到{found.LabelShort}在{ElevLabel(destElev)}");
            }
            return stairsJob;
        }

        /// <summary>
        /// 在其他楼层搜索最近物品，返回 UseStairs job + 设置意图。
        /// 用于工作类 — 到达后需要执行特定目标。
        /// </summary>
        public static Job FindWorkAcrossFloors(
            Pawn pawn, ThingRequest req,
            PathEndMode peMode, TraverseParms tp,
            float maxDist, Predicate<Thing> validator = null)
        {
            Thing found = GenClosestCrossFloor.ClosestThingOnOtherFloors(
                pawn.Position, pawn.Map, req, peMode, tp, maxDist, validator);
            if (found == null) return null;

            Job stairsJob = TryMakeStairsJobForTarget(pawn, found);
            if (stairsJob == null) return null;

            // 设置意图：到达后执行这个特定目标
            CrossFloorIntent.Set(pawn,
                found.Map.uniqueID,
                found.Position,
                found.def);

            if (DebugLog)
            {
                int destElev = FloorMapUtility.GetMapElevation(found.Map);
                Log.Message($"【MLF】寻路与job检测-{pawn.LabelShort}—" +
                    $"跨层工作: 找到{found.LabelShort}在{ElevLabel(destElev)}，已设置意图");
            }
            return stairsJob;
        }

        /// <summary>
        /// 为指定目标地图创建 UseStairs job（不搜索，直接去）。
        /// destHint：最终目标位置，传送时选离它最近的传送器落点。
        /// </summary>
        public static Job TryGoToMap(Pawn pawn, Map targetMap, IntVec3 destHint = default)
        {
            if (pawn?.Map == null || targetMap == null) return null;
            if (pawn.Map == targetMap) return null;

            int targetElev = FloorMapUtility.GetMapElevation(targetMap);
            var stairs = FloorMapUtility.FindStairsToFloor(pawn, pawn.Map, targetElev);
            if (stairs == null) return null;

            Job job = JobMaker.MakeJob(MLF_JobDefOf.MLF_UseStairs, stairs);
            job.targetB = new IntVec3(targetElev, 0, 0);
            if (destHint.IsValid)
                job.targetC = destHint;
            return job;
        }

        private static string ElevLabel(int elev)
        {
            if (elev > 0) return $"{elev + 1}F";
            if (elev < 0) return $"B{-elev}";
            return "1F";
        }
    }
}
