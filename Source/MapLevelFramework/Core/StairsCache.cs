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
    }
}
