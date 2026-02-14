using System.Reflection;
using RimWorld;
using Verse;

namespace MapLevelFramework
{
    /// <summary>
    /// 层级地图的 GenStep - 根据下层屋顶铺设地板。
    /// 有屋顶的格子 → 木地板（可被玩家替换）
    /// 没屋顶的格子 → MLF_OpenAir（不可通行）
    /// </summary>
    public class GenStep_LevelInterior : GenStep
    {
        private static readonly FieldInfo underGridField =
            typeof(TerrainGrid).GetField("underGrid", BindingFlags.Instance | BindingFlags.NonPublic);

        public override int SeedPart => 7654321;

        public override void Generate(Map map, GenStepParams parms)
        {
            var lmp = map.Parent as LevelMapParent;
            if (lmp == null) return;

            // 确定默认地板
            string floorDefName = "WoodPlankFloor";
            if (lmp.levelDef != null && lmp.levelDef.defaultTerrain != null)
            {
                floorDefName = lmp.levelDef.defaultTerrain;
            }

            TerrainDef floorTerrain = DefDatabase<TerrainDef>.GetNamedSilentFail(floorDefName)
                ?? TerrainDefOf.WoodPlankFloor;
            TerrainDef openAir = DefDatabase<TerrainDef>.GetNamedSilentFail("MLF_OpenAir");
            TerrainDef levelBase = DefDatabase<TerrainDef>.GetNamedSilentFail("MLF_LevelBase");

            Map hostMap = lmp.sourceMap;
            CellRect area = lmp.area;

            // 获取 underGrid 数组用于直接设置底层地形
            TerrainDef[] underGrid = underGridField?.GetValue(map.terrainGrid) as TerrainDef[];

            TerrainGrid terrainGrid = map.terrainGrid;

            // 子地图与基地图同尺寸，坐标完全一致。
            // 先把所有格子设为 OpenAir（虚空），再填充 area 内的格子。
            foreach (IntVec3 cell in map.AllCells)
            {
                terrainGrid.SetTerrain(cell, openAir ?? TerrainDefOf.Soil);
            }

            // 只填充 area 范围内的格子（坐标与基地图一致，无需转换）
            foreach (IntVec3 cell in area)
            {
                if (!cell.InBounds(map) || !cell.InBounds(hostMap)) continue;

                bool hasRoof = hostMap.roofGrid.RoofAt(cell) != null;
                if (hasRoof)
                {
                    terrainGrid.SetTerrain(cell, floorTerrain);
                    // 底层设为 LevelBase（支持 Heavy affordance），而非 OpenAir
                    if (underGrid != null && levelBase != null)
                    {
                        int index = map.cellIndices.CellToIndex(cell);
                        underGrid[index] = levelBase;
                    }
                }
                // else: 已经是 OpenAir
            }
        }
    }
}
