using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MapLevelFramework.CrossFloor
{
    /// <summary>
    /// 楼层地图工具方法。
    /// 类似 VMF 的 VehicleMapUtility，提供跨楼层地图的基础查询。
    /// </summary>
    public static class FloorMapUtility
    {
        /// <summary>
        /// 获取基地图。如果已是基地图则返回自身。
        /// </summary>
        public static Map GetBaseMap(this Map map)
        {
            if (map == null) return null;
            if (LevelManager.IsLevelMap(map, out var parentMgr, out _))
                return parentMgr.map;
            return map;
        }

        /// <summary>
        /// 获取基地图 + 所有楼层子地图。
        /// </summary>
        public static IEnumerable<Map> BaseMapAndFloorMaps(this Map map)
        {
            Map baseMap = map.GetBaseMap();
            if (baseMap == null) yield break;

            yield return baseMap;

            LevelManager mgr = LevelManager.GetManager(baseMap);
            if (mgr == null) yield break;

            foreach (var level in mgr.AllLevels)
            {
                if (level.LevelMap != null)
                    yield return level.LevelMap;
            }
        }

        /// <summary>
        /// 是否属于楼层系统（基地图有 LevelManager，或自身是子地图）。
        /// </summary>
        public static bool IsPartOfFloorSystem(this Map map)
        {
            if (map == null) return false;
            if (LevelManager.IsLevelMap(map, out _, out _)) return true;
            LevelManager mgr = LevelManager.GetManager(map);
            return mgr != null && CrossLevelUtility.HasLevels(map);
        }

        /// <summary>
        /// 获取地图的楼层高度。基地图=0，子地图=对应 elevation。
        /// </summary>
        public static int GetMapElevation(Map map)
        {
            if (map == null) return 0;
            if (LevelManager.IsLevelMap(map, out _, out var levelData))
                return levelData.elevation;
            return 0;
        }

        /// <summary>
        /// 在指定地图上找到通往目标楼层的最近可达楼梯（旧接口，仅查 targetElevation 匹配的楼梯）。
        /// </summary>
        public static Building_Stairs FindStairsToElevation(Pawn pawn, Map map, int targetElevation)
        {
            var stairs = StairsCache.GetStairs(map, targetElevation);
            if (stairs == null || stairs.Count == 0) return null;

            Building_Stairs best = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < stairs.Count; i++)
            {
                var s = stairs[i];
                if (!s.Spawned) continue;
                if (!pawn.CanReach(s, PathEndMode.OnCell, Danger.Deadly)) continue;

                float dist = s.Position.DistanceToSquared(pawn.Position);
                if (dist < bestDist)
                {
                    best = s;
                    bestDist = dist;
                }
            }

            return best;
        }

        // ========== 电梯模式：楼梯井 ==========

        // FindStairsToFloor 缓存：避免同一 tick 内重复查找
        // key = (pawnId, mapId, targetElev), value = (stairs, tick)
        private static int _fstfCachePawnId;
        private static int _fstfCacheMapId;
        private static int _fstfCacheTargetElev;
        private static int _fstfCacheTick;
        private static Building_Stairs _fstfCacheResult;

        /// <summary>
        /// 获取指定 elevation 对应的 Map。elevation=0 返回基地图。
        /// </summary>
        public static Map GetMapForElevation(Map anyFloorMap, int elevation)
        {
            Map baseMap = anyFloorMap.GetBaseMap();
            if (baseMap == null) return null;

            if (elevation == 0) return baseMap;

            LevelManager mgr = LevelManager.GetManager(baseMap);
            if (mgr == null) return null;

            var level = mgr.GetLevel(elevation);
            return level?.LevelMap;
        }

        /// <summary>
        /// 检查地图上指定位置是否有楼梯。
        /// </summary>
        public static bool HasStairsAtPosition(Map map, IntVec3 pos)
        {
            if (map == null || !pos.InBounds(map)) return false;
            var things = map.thingGrid.ThingsListAtFast(pos);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Building_Stairs) return true;
            }
            return false;
        }

        /// <summary>
        /// 获取从该传送器可直达的所有楼层。
        /// 偷懒方案：任意传送器互通，所有有传送器的楼层都可达。
        /// </summary>
        public static List<(Map map, int elevation)> GetReachableFloors(Building_Stairs stairs)
        {
            var result = new List<(Map, int)>();
            if (stairs?.Map == null) return result;

            Map stairsMap = stairs.Map;

            foreach (Map floorMap in stairsMap.BaseMapAndFloorMaps())
            {
                if (floorMap == stairsMap) continue;
                // 偷懒方案：只要目标层有任意传送器就可达
                var targetStairs = StairsCache.GetAllStairsOnMap(floorMap);
                if (targetStairs != null && targetStairs.Count > 0)
                {
                    result.Add((floorMap, GetMapElevation(floorMap)));
                }
            }
            return result;
        }

        /// <summary>
        /// 偷懒方案：在当前地图上找到最近可达的传送器。
        /// 只要目标楼层有任意传送器就行，不要求同位置。
        /// 结果缓存 60 tick，避免同一扫描周期内重复查找。
        /// </summary>
        public static Building_Stairs FindStairsToFloor(Pawn pawn, Map pawnMap, int targetElevation)
        {
            int curTick = Find.TickManager?.TicksGame ?? 0;
            int pawnId = pawn?.thingIDNumber ?? 0;
            int mapId = pawnMap?.uniqueID ?? -1;

            // 缓存命中：同一 pawn、同一地图、同一目标层、60 tick 内
            if (pawnId == _fstfCachePawnId
                && mapId == _fstfCacheMapId
                && targetElevation == _fstfCacheTargetElev
                && curTick - _fstfCacheTick < 60
                && (_fstfCacheResult == null || _fstfCacheResult.Spawned))
            {
                return _fstfCacheResult;
            }

            Map targetMap = GetMapForElevation(pawnMap, targetElevation);
            if (targetMap == null || targetMap == pawnMap)
                return null;

            // 偷懒方案：只需目标层有任意传送器
            var targetStairs = StairsCache.GetAllStairsOnMap(targetMap);
            if (targetStairs == null || targetStairs.Count == 0)
                return null;

            var allStairs = StairsCache.GetAllStairsOnMap(pawnMap);
            if (allStairs == null || allStairs.Count == 0)
                return null;

            Building_Stairs best = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < allStairs.Count; i++)
            {
                var s = allStairs[i];
                if (!s.Spawned) continue;
                // 不再检查 HasStairsAtPosition — 任意传送器互通
                if (!pawn.CanReach(s, PathEndMode.OnCell, Danger.Deadly)) continue;

                float dist = s.Position.DistanceToSquared(pawn.Position);
                if (dist < bestDist)
                {
                    best = s;
                    bestDist = dist;
                }
            }

            // 更新缓存
            _fstfCachePawnId = pawnId;
            _fstfCacheMapId = mapId;
            _fstfCacheTargetElev = targetElevation;
            _fstfCacheTick = curTick;
            _fstfCacheResult = best;

            bool debug = MapLevelFrameworkMod.Settings?.debugPathfindingAndJob ?? false;
            if (debug)
                Log.Message($"【MLF】FindStairsToFloor: elev={GetMapElevation(pawnMap)}→{targetElevation}, 结果={best?.Position.ToString() ?? "null"}");

            return best;
        }
    }
}
