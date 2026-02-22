using System.Collections.Generic;
using Verse;

namespace MapLevelFramework
{
    /// <summary>
    /// 楼梯缓存 - 按地图和目标 elevation 索引所有楼梯，避免遍历 AllThings。
    /// Building_Stairs Spawn/DeSpawn 时自动更新。
    /// </summary>
    public static class StairsCache
    {
        // Map.uniqueID → (targetElevation → stairs list)
        private static readonly Dictionary<int, Dictionary<int, List<Building_Stairs>>> cache
            = new Dictionary<int, Dictionary<int, List<Building_Stairs>>>();

        public static void Register(Building_Stairs stairs)
        {
            if (stairs?.Map == null) return;
            int mapId = stairs.Map.uniqueID;
            if (!cache.TryGetValue(mapId, out var byElev))
            {
                byElev = new Dictionary<int, List<Building_Stairs>>();
                cache[mapId] = byElev;
            }
            if (!byElev.TryGetValue(stairs.targetElevation, out var list))
            {
                list = new List<Building_Stairs>();
                byElev[stairs.targetElevation] = list;
            }
            if (!list.Contains(stairs))
                list.Add(stairs);
            allStairsCacheTick = -1; // 使缓存失效
        }

        public static void Deregister(Building_Stairs stairs, Map map)
        {
            if (map == null) return;
            int mapId = map.uniqueID;
            if (!cache.TryGetValue(mapId, out var byElev)) return;
            if (!byElev.TryGetValue(stairs.targetElevation, out var list)) return;
            list.Remove(stairs);
            if (list.Count == 0)
                byElev.Remove(stairs.targetElevation);
            if (byElev.Count == 0)
                cache.Remove(mapId);
            allStairsCacheTick = -1; // 使缓存失效
        }

        /// <summary>
        /// 获取指定地图上通往指定 elevation 的所有楼梯。
        /// </summary>
        public static List<Building_Stairs> GetStairs(Map map, int targetElevation)
        {
            if (map == null) return null;
            if (!cache.TryGetValue(map.uniqueID, out var byElev)) return null;
            byElev.TryGetValue(targetElevation, out var list);
            return list;
        }

        /// <summary>
        /// 检查指定地图上是否有通往指定 elevation 的楼梯。
        /// </summary>
        public static bool HasStairs(Map map, int targetElevation)
        {
            var list = GetStairs(map, targetElevation);
            return list != null && list.Count > 0;
        }

        /// <summary>
        /// 清除指定地图的缓存（地图销毁时调用）。
        /// </summary>
        public static void ClearMap(int mapId)
        {
            cache.Remove(mapId);
        }

        /// <summary>
        /// 清除所有缓存。
        /// </summary>
        public static void ClearAll()
        {
            cache.Clear();
        }

        /// <summary>
        /// 获取指定地图上的所有楼梯（不区分目标 elevation）。
        /// 使用 per-map per-tick 缓存，安全支持递归调用。
        /// </summary>
        private static readonly Dictionary<int, List<Building_Stairs>> allStairsPerMap
            = new Dictionary<int, List<Building_Stairs>>();
        private static int allStairsCacheTick = -1;

        public static List<Building_Stairs> GetAllStairsOnMap(Map map)
        {
            if (map == null) return null;
            int mapId = map.uniqueID;
            int curTick = Find.TickManager?.TicksGame ?? 0;

            // 新 tick → 清除所有缓存
            if (curTick != allStairsCacheTick)
            {
                allStairsPerMap.Clear();
                allStairsCacheTick = curTick;
            }

            // 缓存命中
            if (allStairsPerMap.TryGetValue(mapId, out var cached))
                return cached;

            if (!cache.TryGetValue(mapId, out var byElev)) return null;

            var result = new List<Building_Stairs>();
            foreach (var list in byElev.Values)
            {
                result.AddRange(list);
            }
            allStairsPerMap[mapId] = result;
            return result;
        }
    }
}
