using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MapLevelFramework
{
    /// <summary>
    /// 跳楼工具 - 找到上层可以跳下的格子（OpenAir 旁边的可站立格子）。
    /// 也提供右键菜单选项。
    /// </summary>
    public static class JumpDownUtility
    {
        /// <summary>
        /// 检查一个格子是否可以跳下（旁边有 OpenAir 且在上层地图）。
        /// </summary>
        public static bool CanJumpDownAt(IntVec3 cell, Map map)
        {
            if (!LevelManager.IsLevelMap(map, out _, out var levelData))
                return false;
            if (levelData.hostMap == null) return false;

            var openAir = Patches.RoofFloorSync.OpenAir;
            if (openAir == null) return false;

            // 检查该格子本身可站立
            if (!cell.Standable(map)) return false;

            // 检查相邻格子是否有 OpenAir（悬崖边缘）
            for (int i = 0; i < 4; i++)
            {
                IntVec3 adj = cell + GenAdj.CardinalDirections[i];
                if (adj.InBounds(map) && map.terrainGrid.TerrainAt(adj) == openAir)
                {
                    // 确认下层该位置可以站立
                    if (adj.InBounds(levelData.hostMap) && adj.Standable(levelData.hostMap))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 找到跳下后的落点（OpenAir 旁边的格子对应的下层位置）。
        /// </summary>
        public static IntVec3 GetLandingCell(IntVec3 edgeCell, Map upperMap)
        {
            if (!LevelManager.IsLevelMap(upperMap, out _, out var levelData))
                return IntVec3.Invalid;

            var openAir = Patches.RoofFloorSync.OpenAir;
            if (openAir == null) return IntVec3.Invalid;

            for (int i = 0; i < 4; i++)
            {
                IntVec3 adj = edgeCell + GenAdj.CardinalDirections[i];
                if (adj.InBounds(upperMap) && upperMap.terrainGrid.TerrainAt(adj) == openAir)
                {
                    if (levelData.hostMap != null && adj.InBounds(levelData.hostMap)
                        && adj.Standable(levelData.hostMap))
                        return adj;
                }
            }
            return IntVec3.Invalid;
        }

        /// <summary>
        /// 为右键菜单生成"跳下"选项。
        /// </summary>
        public static FloatMenuOption GetJumpDownOption(Pawn pawn, IntVec3 clickCell)
        {
            if (!CanJumpDownAt(clickCell, pawn.Map))
                return null;

            return new FloatMenuOption("跳下楼", delegate
            {
                Job job = JobMaker.MakeJob(MLF_JobDefOf.MLF_JumpDown, clickCell);
                pawn.jobs.TryTakeOrderedJob(job);
            });
        }
    }
}
