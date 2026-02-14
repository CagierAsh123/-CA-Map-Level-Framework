using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// 屋顶 ↔ 地板双向同步系统。
    ///
    /// 规则：
    /// - 下层有屋顶 → 上层有地板
    /// - 下层屋顶被拆 → 上层地板变为 OpenAir
    /// - 上层建造地板 → 下层自动加屋顶
    /// - 上层移除地板（露出 OpenAir）→ 下层屋顶被拆
    ///
    /// 使用 syncing 标志防止无限递归。
    /// </summary>
    public static class RoofFloorSync
    {
        private static bool syncing = false;

        private static TerrainDef openAirDef;
        private static TerrainDef defaultFloorDef;

        private static readonly FieldInfo underGridField =
            typeof(TerrainGrid).GetField("underGrid", BindingFlags.Instance | BindingFlags.NonPublic);

        public static TerrainDef OpenAir
        {
            get
            {
                if (openAirDef == null)
                    openAirDef = DefDatabase<TerrainDef>.GetNamedSilentFail("MLF_OpenAir");
                return openAirDef;
            }
        }

        public static TerrainDef DefaultFloor
        {
            get
            {
                if (defaultFloorDef == null)
                    defaultFloorDef = DefDatabase<TerrainDef>.GetNamedSilentFail("WoodPlankFloor")
                        ?? TerrainDefOf.WoodPlankFloor;
                return defaultFloorDef;
            }
        }

        /// <summary>
        /// 下层屋顶变化 → 同步上层地板。
        /// </summary>
        public static void OnRoofChanged(Map hostMap, IntVec3 hostCell, RoofDef newRoof)
        {
            if (syncing) return;

            var mgr = LevelManager.GetManager(hostMap);
            if (mgr == null) return;

            // 查找该格子上方的层级
            foreach (var level in mgr.AllLevels)
            {
                if (level.LevelMap == null) continue;
                if (!level.ContainsBaseMapCell(hostCell)) continue;

                IntVec3 levelCell = hostCell; // 同尺寸子地图，坐标一致
                if (!levelCell.InBounds(level.LevelMap)) continue;

                syncing = true;
                try
                {
                    TerrainGrid grid = level.LevelMap.terrainGrid;
                    if (newRoof != null)
                    {
                        // 屋顶加上了 → 如果上层是 OpenAir，铺默认地板
                        if (grid.TerrainAt(levelCell) == OpenAir)
                        {
                            grid.SetTerrain(levelCell, DefaultFloor);
                            // 强制设 underGrid 为 OpenAir（SetTerrain 不会把 Impassable 地形存入 underGrid）
                            SetUnderTerrain(level.LevelMap, levelCell, OpenAir);
                        }
                    }
                    else
                    {
                        // 屋顶拆了 → 上层变为 OpenAir
                        grid.SetTerrain(levelCell, OpenAir);
                    }

                    // 标记 section 为脏以刷新渲染
                    level.LevelMap.mapDrawer.MapMeshDirty(levelCell, MapMeshFlagDefOf.Terrain);
                }
                finally
                {
                    syncing = false;
                }
                break;
            }
        }

        /// <summary>
        /// 直接设置 underGrid（绕过 SetTerrain 对 Impassable 地形的过滤）。
        /// </summary>
        private static void SetUnderTerrain(Map map, IntVec3 cell, TerrainDef terrain)
        {
            if (terrain == null || map == null) return;
            TerrainDef[] underGrid = underGridField?.GetValue(map.terrainGrid) as TerrainDef[];
            if (underGrid != null)
            {
                underGrid[map.cellIndices.CellToIndex(cell)] = terrain;
            }
        }

        /// <summary>
        /// 上层地板变化 → 同步下层屋顶。
        /// </summary>
        public static void OnFloorChanged(Map levelMap, IntVec3 levelCell, TerrainDef newTerrain)
        {
            if (syncing) return;

            LevelManager manager;
            LevelData level;
            if (!LevelManager.IsLevelMap(levelMap, out manager, out level))
                return;

            Map hostMap = level.hostMap;
            if (hostMap == null) return;

            IntVec3 hostCell = levelCell; // 同尺寸子地图，坐标一致
            if (!hostCell.InBounds(hostMap)) return;

            syncing = true;
            try
            {
                if (newTerrain == OpenAir)
                {
                    // 地板没了 → 拆下层屋顶
                    if (hostMap.roofGrid.RoofAt(hostCell) != null)
                    {
                        hostMap.roofGrid.SetRoof(hostCell, null);
                    }
                }
                else
                {
                    // 地板建好了 → 下层加屋顶（如果还没有）
                    if (hostMap.roofGrid.RoofAt(hostCell) == null)
                    {
                        hostMap.roofGrid.SetRoof(hostCell, RoofDefOf.RoofConstructed);
                    }
                }
            }
            finally
            {
                syncing = false;
            }
        }
    }

    /// <summary>
    /// RoofGrid.SetRoof 补丁 - 屋顶变化时同步上层地板。
    /// </summary>
    [HarmonyPatch(typeof(RoofGrid), "SetRoof")]
    public static class Patch_RoofGrid_SetRoof
    {
        private static readonly FieldInfo roofMapField =
            typeof(RoofGrid).GetField("map", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void Postfix(RoofGrid __instance, IntVec3 c, RoofDef def)
        {
            Map map = roofMapField?.GetValue(__instance) as Map;
            if (map != null)
            {
                RoofFloorSync.OnRoofChanged(map, c, def);
            }
        }
    }

    /// <summary>
    /// TerrainGrid.SetTerrain 补丁 - 上层地板变化时同步下层屋顶。
    /// </summary>
    [HarmonyPatch(typeof(TerrainGrid), "SetTerrain")]
    public static class Patch_TerrainGrid_SetTerrain
    {
        private static readonly FieldInfo terrainMapField =
            typeof(TerrainGrid).GetField("map", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void Postfix(TerrainGrid __instance, IntVec3 c, TerrainDef newTerr)
        {
            Map map = terrainMapField?.GetValue(__instance) as Map;
            if (map != null)
            {
                RoofFloorSync.OnFloorChanged(map, c, newTerr);
            }
        }
    }

    /// <summary>
    /// TerrainGrid.RemoveTopLayer 补丁 - 移除地板后检查是否露出 OpenAir。
    /// </summary>
    [HarmonyPatch(typeof(TerrainGrid), "RemoveTopLayer")]
    public static class Patch_TerrainGrid_RemoveTopLayer
    {
        private static readonly FieldInfo terrainMapField =
            typeof(TerrainGrid).GetField("map", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void Postfix(TerrainGrid __instance, IntVec3 c)
        {
            Map map = terrainMapField?.GetValue(__instance) as Map;
            if (map != null)
            {
                TerrainDef current = __instance.TerrainAt(c);
                RoofFloorSync.OnFloorChanged(map, c, current);
            }
        }
    }
}
