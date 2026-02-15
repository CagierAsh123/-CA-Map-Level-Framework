using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace MapLevelFramework
{
    /// <summary>
    /// 地下层地图的 GenStep。
    /// 全图铺 MLF_OpenAir，楼梯位置铺 MLF_DirtFloor，周围一圈生成 MLF_RockWall。
    /// </summary>
    public class GenStep_UndergroundInterior : GenStep
    {
        private static readonly FieldInfo underGridField =
            typeof(TerrainGrid).GetField("underGrid", BindingFlags.Instance | BindingFlags.NonPublic);

        public override int SeedPart => 7654322;

        public override void Generate(Map map, GenStepParams parms)
        {
            var lmp = map.Parent as LevelMapParent;
            if (lmp == null) return;

            TerrainDef openAir = DefDatabase<TerrainDef>.GetNamedSilentFail("MLF_OpenAir");
            TerrainDef dirtFloor = DefDatabase<TerrainDef>.GetNamedSilentFail("MLF_DirtFloor");
            TerrainDef levelBase = DefDatabase<TerrainDef>.GetNamedSilentFail("MLF_LevelBase");

            if (openAir == null || dirtFloor == null) return;

            TerrainDef[] underGrid = underGridField?.GetValue(map.terrainGrid) as TerrainDef[];

            // 全图铺 OpenAir（虚空）
            foreach (IntVec3 cell in map.AllCells)
            {
                map.terrainGrid.SetTerrain(cell, openAir);
            }

            // 楼梯位置（由 LevelMapParent 传入的 area 中心）
            IntVec3 stairPos = lmp.area.CenterCell;

            // 楼梯那一格铺泥地
            if (stairPos.InBounds(map))
            {
                map.terrainGrid.SetTerrain(stairPos, dirtFloor);
                if (underGrid != null && levelBase != null)
                {
                    int idx = map.cellIndices.CellToIndex(stairPos);
                    underGrid[idx] = levelBase;
                }
            }

            // 设置初始 usableCells（通过 LevelMapParent 传递给 LevelData）
            // 注意：此时 LevelData 还没有完全初始化，usableCells 会在 Building_Stairs 中设置
            // 这里只负责地形生成

            // 周围一圈生成岩石墙（由 Building_Stairs.CreateOrUpdateLevel 在地图生成后调用）
            // GenStep 阶段不 spawn 建筑，避免时序问题
        }
    }
}
