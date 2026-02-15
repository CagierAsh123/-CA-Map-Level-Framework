using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MapLevelFramework
{
    /// <summary>
    /// 岩石边界扩展工具 - 使用原版岩石填充地下层边界。
    /// 挖掘后在新边界生成新岩石，实现动态扩展。
    /// </summary>
    public static class RockFrontierUtility
    {
        // 原版岩石墙 defName（Mineable 类型）
        private static readonly string[] VanillaRockDefs =
        {
            "Granite", "Marble", "Limestone", "Sandstone", "Slate"
        };

        // 稀有矿脉 defName + 生成权重（相对于普通岩石）
        private static readonly string[] OreDefs =
        {
            "MineableSteel", "MineableComponentsIndustrial", "MineableGold",
            "MineableSilver", "MineablePlasteel", "MineableUranium", "MineableJade"
        };

        private const float OreChance = 0.04f; // 4% 概率生成矿脉

        /// <summary>
        /// 获取一个随机的原版岩石 ThingDef。小概率返回矿脉。
        /// </summary>
        private static ThingDef GetRandomRockDef()
        {
            if (Rand.Value < OreChance)
            {
                string oreName = OreDefs[Rand.Range(0, OreDefs.Length)];
                ThingDef ore = DefDatabase<ThingDef>.GetNamedSilentFail(oreName);
                if (ore != null) return ore;
            }

            string rockName = VanillaRockDefs[Rand.Range(0, VanillaRockDefs.Length)];
            return DefDatabase<ThingDef>.GetNamedSilentFail(rockName)
                ?? ThingDefOf.Granite;
        }

        /// <summary>
        /// 岩石被挖掘后调用。将该格子加入 usableCells，铺地板，并在新暴露的邻居生成岩石。
        /// </summary>
        public static void OnRockMined(Map levelMap, IntVec3 minedPos, LevelData levelData)
        {
            if (levelData == null || levelMap == null) return;

            // 1. 被挖格子加入 usableCells
            if (levelData.usableCells == null)
                levelData.usableCells = new HashSet<IntVec3>();
            levelData.usableCells.Add(minedPos);

            // 2. 铺泥地
            TerrainDef dirtFloor = DefDatabase<TerrainDef>.GetNamedSilentFail("MLF_DirtFloor");
            if (dirtFloor != null)
                levelMap.terrainGrid.SetTerrain(minedPos, dirtFloor);

            // 3. 在新暴露的邻居生成岩石
            for (int i = 0; i < 4; i++)
            {
                IntVec3 neighbor = minedPos + GenAdj.CardinalDirections[i];
                if (!neighbor.InBounds(levelMap)) continue;
                if (levelData.usableCells.Contains(neighbor)) continue;

                // 检查该位置是否已有不可通行建筑
                if (neighbor.Impassable(levelMap)) continue;

                // 生成原版岩石
                ThingDef rockDef = GetRandomRockDef();
                if (rockDef == null) continue;
                Thing rock = ThingMaker.MakeThing(rockDef);
                GenSpawn.Spawn(rock, neighbor, levelMap);
            }

            // 4. 扩展包围矩形
            CellRect area = levelData.area;
            if (minedPos.x < area.minX || minedPos.x > area.maxX ||
                minedPos.z < area.minZ || minedPos.z > area.maxZ)
            {
                int minX = System.Math.Min(area.minX, minedPos.x);
                int minZ = System.Math.Min(area.minZ, minedPos.z);
                int maxX = System.Math.Max(area.maxX, minedPos.x);
                int maxZ = System.Math.Max(area.maxZ, minedPos.z);
                levelData.area = new CellRect(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);
            }
            levelData.RebuildActiveSections();

            // 5. 标记渲染脏
            levelMap.mapDrawer.MapMeshDirty(minedPos,
                MapMeshFlagDefOf.Things | MapMeshFlagDefOf.Buildings | MapMeshFlagDefOf.Terrain);
        }

        /// <summary>
        /// 在指定位置周围一圈生成原版岩石（初始边界）。
        /// </summary>
        public static void SpawnInitialFrontier(Map levelMap, IntVec3 center, LevelData levelData)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    IntVec3 pos = new IntVec3(center.x + dx, 0, center.z + dz);
                    if (!pos.InBounds(levelMap)) continue;
                    if (levelData.usableCells != null && levelData.usableCells.Contains(pos)) continue;
                    if (pos.Impassable(levelMap)) continue;

                    ThingDef rockDef = GetRandomRockDef();
                    if (rockDef == null) continue;
                    Thing rock = ThingMaker.MakeThing(rockDef);
                    GenSpawn.Spawn(rock, pos, levelMap);
                }
            }
        }
    }
}
