using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MapLevelFramework.CrossFloor
{
    /// <summary>
    /// 跨楼层可达性检查工具。
    /// 检查 pawn 是否能通过楼梯从一个楼层到达另一个楼层的目标。
    /// </summary>
    public static class CrossFloorReachabilityUtility
    {
        // 防止递归调用
        public static bool working;

        // 简单缓存：(startMapId, destMapId) → (tick, result)
        private static readonly Dictionary<long, (int tick, bool result)> cache
            = new Dictionary<long, (int, bool)>();
        private const int CacheDurationTicks = 120;

        /// <summary>
        /// 跨楼层可达性检查。
        /// </summary>
        public static bool CanReach(
            Map startMap, IntVec3 start,
            Map destMap, IntVec3 destCell,
            PathEndMode peMode, TraverseParms traverseParams)
        {
            if (startMap == destMap)
                return startMap.reachability.CanReach(start,
                    destCell, peMode, traverseParams);

            // 不在同一建筑群
            if (startMap.GetBaseMap() != destMap.GetBaseMap())
                return false;

            // 检查缓存
            long cacheKey = ((long)startMap.uniqueID << 32) | (uint)destMap.uniqueID;
            int curTick = Find.TickManager?.TicksGame ?? 0;
            if (cache.TryGetValue(cacheKey, out var cached) &&
                curTick - cached.tick < CacheDurationTicks)
            {
                return cached.result;
            }

            if (working) return false;
            working = true;
            try
            {
                bool result = CanReachViaStairs(startMap, start, destMap, destCell, traverseParams);
                cache[cacheKey] = (curTick, result);
                return result;
            }
            finally
            {
                working = false;
            }
        }

        /// <summary>
        /// 分段检查：pawn 能到达当前层的楼梯 → 楼梯目标层能到达 dest。
        /// </summary>
        private static bool CanReachViaStairs(
            Map startMap, IntVec3 start,
            Map destMap, IntVec3 destCell,
            TraverseParms traverseParams)
        {
            // 获取当前地图上所有楼梯
            var allStairs = StairsCache.GetAllStairsOnMap(startMap);
            if (allStairs == null || allStairs.Count == 0) return false;

            // 判断 pawn 是否在 startMap 上（首跳 vs 中间跳）
            // 中间跳时 pawn 不在 startMap，必须用无 pawn 的 TraverseParms
            Pawn pawn = traverseParams.pawn;
            bool pawnOnStartMap = pawn != null && pawn.Map == startMap;
            TraverseParms localParams = pawnOnStartMap
                ? traverseParams
                : TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly);
            TraverseParms noPawnParams = TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly);

            for (int i = 0; i < allStairs.Count; i++)
            {
                Building_Stairs stairs = allStairs[i];
                if (!stairs.Spawned) continue;

                // 能到达楼梯？
                if (!startMap.reachability.CanReach(start, stairs,
                    PathEndMode.OnCell, localParams))
                    continue;

                // 电梯模型：遍历楼梯井内所有可达楼层（同位置有楼梯的楼层）
                IntVec3 stairPos = stairs.Position;
                foreach (Map floorMap in startMap.BaseMapAndFloorMaps())
                {
                    if (floorMap == startMap) continue;
                    if (!FloorMapUtility.HasStairsAtPosition(floorMap, stairPos)) continue;

                    if (floorMap == destMap)
                    {
                        // 楼梯井直达目标层 → 检查目标层内可达性
                        if (destMap.reachability.CanReach(stairPos, destCell,
                            PathEndMode.OnCell, noPawnParams))
                            return true;
                    }
                    else
                    {
                        // 目标层不在此楼梯井 → 到中间层后换楼梯井（两跳）
                        if (CanReachViaStairs(floorMap, stairPos, destMap, destCell, traverseParams))
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 清除缓存（地图变化时调用）。
        /// </summary>
        public static void ClearCache()
        {
            cache.Clear();
        }
    }
}
