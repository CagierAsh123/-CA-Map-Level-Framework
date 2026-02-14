using Verse;

namespace MapLevelFramework
{
    /// <summary>
    /// 层级查询工具。
    /// 同尺寸子地图方案：子地图与基地图坐标完全一致，无需坐标转换。
    /// </summary>
    public static class LevelCoordUtility
    {
        /// <summary>
        /// 检查主地图坐标是否落在某个层级的覆盖区域内。
        /// 如果是，返回对应的 LevelData。
        /// </summary>
        public static bool TryGetLevelAtBaseCell(IntVec3 baseCell, Map baseMap, out LevelData level)
        {
            level = null;
            var mgr = LevelManager.GetManager(baseMap);
            if (mgr == null || !mgr.IsFocusingLevel) return false;

            var focused = mgr.GetLevel(mgr.FocusedElevation);
            if (focused != null && focused.ContainsBaseMapCell(baseCell))
            {
                level = focused;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 判断一个 Thing 是否在某个层级子地图上。
        /// </summary>
        public static bool IsOnLevelMap(this Thing thing, out LevelManager manager, out LevelData level)
        {
            manager = null;
            level = null;
            if (thing?.Map == null) return false;
            return LevelManager.IsLevelMap(thing.Map, out manager, out level);
        }
    }
}
