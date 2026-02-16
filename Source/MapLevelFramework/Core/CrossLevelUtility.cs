using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MapLevelFramework
{
    /// <summary>
    /// 跨层级工具方法 - 聚合基地图和所有子地图的数据。
    /// </summary>
    public static class CrossLevelUtility
    {
        /// <summary>
        /// 获取基地图及其所有子地图。
        /// 如果 map 本身是子地图，先找到基地图再获取所有层级。
        /// </summary>
        public static IEnumerable<Map> GetAllLevelMaps(Map map)
        {
            if (map == null) yield break;

            Map baseMap = map;
            LevelManager mgr;

            if (LevelManager.IsLevelMap(map, out var parentMgr, out _))
            {
                baseMap = parentMgr.map;
                mgr = parentMgr;
            }
            else
            {
                mgr = LevelManager.GetManager(map);
            }

            yield return baseMap;

            if (mgr != null)
            {
                foreach (var level in mgr.AllLevels)
                {
                    if (level.LevelMap != null)
                        yield return level.LevelMap;
                }
            }
        }

        /// <summary>
        /// 检查 map 是否有关联的层级（是基地图且有子地图，或是子地图）。
        /// </summary>
        public static bool HasLevels(Map map)
        {
            if (map == null) return false;
            if (LevelManager.IsLevelMap(map, out _, out _)) return true;
            var mgr = LevelManager.GetManager(map);
            return mgr != null && mgr.LevelCount > 0;
        }
    }
}
