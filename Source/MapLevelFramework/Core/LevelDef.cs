using Verse;

namespace MapLevelFramework
{
    /// <summary>
    /// 层级定义 - 描述一个层级的元数据。
    /// 可以通过 XML Def 定义，也可以运行时动态创建。
    /// 
    /// 示例用途：
    /// - 多层建筑的 2F、3F
    /// - 地下层（矿洞、地窖）
    /// - 天空层（飞艇甲板）
    /// - 水底层
    /// </summary>
    public class LevelDef : Def
    {
        /// <summary>
        /// 层级高度序号。0 = 地面，正数 = 地上，负数 = 地下。
        /// 用于排序和 UI 显示。
        /// </summary>
        public int elevation = 0;

        /// <summary>
        /// 该层级的地图尺寸。如果为 null，使用 levelArea 的包围矩形大小。
        /// </summary>
        public IntVec2? mapSize = null;

        /// <summary>
        /// 是否有天花板（影响天气、掉落物等）。
        /// 地下层通常为 true，天空层通常为 false。
        /// </summary>
        public bool hasCeiling = false;

        /// <summary>
        /// 是否共享主地图的天气/光照系统。
        /// </summary>
        public bool shareWeather = true;

        /// <summary>
        /// 该层级的默认地形 Def 名称。
        /// </summary>
        public string defaultTerrain = "WoodPlankFloor";

        /// <summary>
        /// 该层级的标签，用于 API 查询过滤。
        /// 例如 "underground", "sky", "floor", "underwater"
        /// </summary>
        public string levelTag = "floor";
    }
}
