using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace MapLevelFramework
{
    /// <summary>
    /// 袭击者跨层进攻 - 让敌对 pawn 通过楼梯进攻其他楼层的殖民者。
    /// 注入到 AssaultColony / Breaching 等 DutyDef 的 ThinkTree 中。
    /// </summary>
    public class JobGiver_AssaultAcrossLevel : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn?.Map == null || !pawn.Spawned) return null;
            if (CrossLevelJobUtility.Scanning) return null;

            Map pawnMap = pawn.Map;
            LevelManager mgr;
            Map baseMap;

            if (LevelManager.IsLevelMap(pawnMap, out var parentMgr, out _))
            {
                mgr = parentMgr;
                baseMap = parentMgr.map;
            }
            else
            {
                mgr = LevelManager.GetManager(pawnMap);
                baseMap = pawnMap;
            }

            if (mgr == null || !mgr.AllLevels.Any()) return null;

            // 先检查当前地图是否有可攻击目标（有的话不跨层）
            if (HasHostileTargets(pawn, pawnMap)) return null;

            int currentElev = CrossLevelJobUtility.GetMapElevation(pawnMap, mgr, baseMap);

            // 收集有殖民者的其他楼层，按距离排序
            var otherMaps = new List<(Map map, int elevation)>();
            if (pawnMap != baseMap && HasHostileTargets(pawn, baseMap))
                otherMaps.Add((baseMap, 0));
            foreach (var level in mgr.AllLevels)
            {
                if (level.LevelMap != null && level.LevelMap != pawnMap
                    && HasHostileTargets(pawn, level.LevelMap))
                    otherMaps.Add((level.LevelMap, level.elevation));
            }

            if (otherMaps.Count == 0) return null;

            otherMaps.Sort((a, b) =>
                System.Math.Abs(a.elevation - currentElev)
                    .CompareTo(System.Math.Abs(b.elevation - currentElev)));

            // 找最近的有目标的楼层，走楼梯过去
            foreach (var (_, targetElev) in otherMaps)
            {
                int nextElev = targetElev > currentElev
                    ? currentElev + 1
                    : currentElev - 1;

                Building_Stairs stairs = CrossLevelJobUtility.FindStairsToElevation(pawn, pawnMap, nextElev);
                if (stairs != null)
                {
                    return JobMaker.MakeJob(MLF_JobDefOf.MLF_UseStairs, stairs);
                }
            }

            return null;
        }

        /// <summary>
        /// 检查地图上是否有该 pawn 的敌对目标（殖民者、友方 pawn、炮塔等）。
        /// </summary>
        private bool HasHostileTargets(Pawn pawn, Map map)
        {
            // 检查殖民者
            if (map.mapPawns.FreeColonistsSpawnedCount > 0)
                return true;

            // 检查友方非殖民者 pawn（盟友等）
            foreach (var p in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if (p.Spawned) return true;
            }

            return false;
        }
    }
}
