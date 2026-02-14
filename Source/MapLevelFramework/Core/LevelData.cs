using RimWorld.Planet;
using Verse;

namespace MapLevelFramework
{
    /// <summary>
    /// 单个层级的运行时数据。
    /// </summary>
    public class LevelData : IExposable
    {
        /// <summary>
        /// 层级高度序号。
        /// </summary>
        public int elevation;

        /// <summary>
        /// 该层级在主地图上覆盖的区域（主地图坐标）。
        /// </summary>
        public CellRect area;

        /// <summary>
        /// 当前层被聚焦时，是否将该层 Pawn 合并到主图的 AllPawnsSpawned 视图中。
        /// 这样原版 UI（鼠标提示、检查器、部分选择逻辑）能看到子层 Pawn。
        /// </summary>
        public bool includePawnsInBaseMap = true;

        /// <summary>
        /// 层级定义（可选）。
        /// </summary>
        public LevelDef levelDef;

        /// <summary>
        /// 宿主主地图。
        /// </summary>
        public Map hostMap;

        /// <summary>
        /// 该层级的 MapParent。
        /// </summary>
        public LevelMapParent mapParent;

        /// <summary>
        /// 该层级的子地图。
        /// </summary>
        public Map LevelMap
        {
            get
            {
                if (mapParent == null) return null;
                return mapParent.Map;
            }
            set
            {
                // LevelMap 由 MapGenerator 设置，通过 mapParent 间接持有
                // 这个 setter 仅用于初始化阶段
            }
        }

        /// <summary>
        /// 不规则区域的可用格子集合（子地图坐标）。
        /// 如果为 null，则 area 内全部可用。
        /// </summary>
        public System.Collections.Generic.HashSet<IntVec3> usableCells;

        /// <summary>
        /// 缓存：包含 usableCell 的 section 索引集合。
        /// section 大小 17x17，索引 = (cell.x / 17, cell.z / 17)。
        /// 在 usableCells 变化时调用 RebuildActiveSections() 刷新。
        /// </summary>
        private System.Collections.Generic.HashSet<ulong> activeSections;

        /// <summary>
        /// 重建活跃 section 缓存。usableCells 变化后必须调用。
        /// </summary>
        public void RebuildActiveSections()
        {
            activeSections = new System.Collections.Generic.HashSet<ulong>();
            var cells = usableCells;
            if (cells != null)
            {
                foreach (IntVec3 cell in cells)
                {
                    activeSections.Add(SectionKey(cell.x / 17, cell.z / 17));
                }
            }
            else
            {
                // 没有 usableCells 时，用 area 内所有 section
                for (int x = area.minX / 17; x <= area.maxX / 17; x++)
                {
                    for (int z = area.minZ / 17; z <= area.maxZ / 17; z++)
                    {
                        activeSections.Add(SectionKey(x, z));
                    }
                }
            }
        }

        /// <summary>
        /// 检查指定 section 索引是否包含可用格子。
        /// </summary>
        public bool IsSectionActive(int secX, int secZ)
        {
            if (activeSections == null) RebuildActiveSections();
            return activeSections.Contains(SectionKey(secX, secZ));
        }

        private static ulong SectionKey(int x, int z)
        {
            return ((ulong)(uint)x << 32) | (uint)z;
        }

        /// <summary>
        /// 检查子地图坐标是否在可用区域内。
        /// </summary>
        public bool IsCellUsable(IntVec3 levelCell)
        {
            if (usableCells == null) return true; // 全部可用
            return usableCells.Contains(levelCell);
        }

        /// <summary>
        /// 检查主地图坐标是否在该层级的覆盖区域内。
        /// </summary>
        public bool ContainsBaseMapCell(IntVec3 baseCell)
        {
            if (usableCells != null) return usableCells.Contains(baseCell);
            return area.Contains(baseCell);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref elevation, "elevation", 0);
            Scribe_Values.Look(ref area, "area");
            Scribe_Values.Look(ref includePawnsInBaseMap, "includePawnsInBaseMap", true);
            Scribe_Defs.Look(ref levelDef, "levelDef");
            Scribe_References.Look(ref mapParent, "mapParent");
            // hostMap 由 LevelManager 在加载后重新设置
        }
    }
}
