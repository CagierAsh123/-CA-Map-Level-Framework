using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MapLevelFramework.CrossFloor
{
    /// <summary>
    /// 跨楼层可达性检查工具。
    /// 偷懒方案：任意传送器互通，只要两层都有传送器就可达。
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
                // 偷懒方案：两层都有传送器 + pawn 能到达本层任意传送器 = 可达
                bool result = CanReachAnyStairs(startMap, start, traverseParams)
                    && HasAnyStairs(destMap);
                cache[cacheKey] = (curTick, result);
                return result;
            }
            finally
            {
                working = false;
            }
        }

        /// <summary>
        /// 检查 pawn 能否到达 startMap 上的任意传送器。
        /// </summary>
        private static bool CanReachAnyStairs(Map map, IntVec3 start, TraverseParms traverseParams)
        {
            var allStairs = StairsCache.GetAllStairsOnMap(map);
            if (allStairs == null || allStairs.Count == 0) return false;

            for (int i = 0; i < allStairs.Count; i++)
            {
                if (!allStairs[i].Spawned) continue;
                if (map.reachability.CanReach(start, allStairs[i],
                    PathEndMode.OnCell, traverseParams))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 检查地图上是否有任意传送器。
        /// </summary>
        private static bool HasAnyStairs(Map map)
        {
            var allStairs = StairsCache.GetAllStairsOnMap(map);
            if (allStairs == null || allStairs.Count == 0) return false;
            for (int i = 0; i < allStairs.Count; i++)
            {
                if (allStairs[i].Spawned) return true;
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
