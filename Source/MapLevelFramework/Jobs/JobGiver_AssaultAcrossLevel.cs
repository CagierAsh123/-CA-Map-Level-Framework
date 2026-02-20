using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using MapLevelFramework.CrossFloor;

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

            Map pawnMap = pawn.Map;
            if (!pawnMap.IsPartOfFloorSystem()) return null;

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

            if (mgr == null || mgr.LevelCount == 0) return null;

            // 先检查当前地图是否有可攻击目标（有的话不跨层）
            if (HasHostileTargets(pawn, pawnMap)) return null;

            int currentElev = FloorMapUtility.GetMapElevation(pawnMap);

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
                Building_Stairs stairs = FloorMapUtility.FindStairsToFloor(pawn, pawnMap, targetElev);
                if (stairs != null)
                {
                    Job job = JobMaker.MakeJob(MLF_JobDefOf.MLF_UseStairs, stairs);
                    job.targetB = new IntVec3(targetElev, 0, 0);
                    return job;
                }
            }

            return null;
        }

        private bool HasHostileTargets(Pawn pawn, Map map)
        {
            if (map.mapPawns.FreeColonistsSpawnedCount > 0)
                return true;

            foreach (var p in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if (p.Spawned) return true;
            }

            return false;
        }
    }
}
