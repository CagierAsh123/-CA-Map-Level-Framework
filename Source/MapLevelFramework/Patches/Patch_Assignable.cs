using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// 跨层级分配补丁 - 让床位、冥想点等可以分配给其他楼层的 pawn。
    ///
    /// CompAssignableToPawn.AssigningCandidates 默认只返回同地图的 pawn。
    /// 需要包含所有层级的 pawn，这样二楼的床可以分配给一楼的殖民者。
    /// </summary>
    public static class Patch_Assignable
    {
        [HarmonyPatch(typeof(CompAssignableToPawn), "get_AssigningCandidates")]
        public static class Patch_AssigningCandidates
        {
            public static void Postfix(CompAssignableToPawn __instance, ref IEnumerable<Pawn> __result)
            {
                Map thingMap = __instance.parent?.Map;
                if (thingMap == null) return;

                var otherPawns = GetOtherLevelPawns(thingMap);
                if (otherPawns == null) return;

                __result = __result.Concat(otherPawns);
            }
        }

        [HarmonyPatch(typeof(CompAssignableToPawn_Bed), "get_AssigningCandidates")]
        public static class Patch_AssigningCandidates_Bed
        {
            public static void Postfix(CompAssignableToPawn_Bed __instance, ref IEnumerable<Pawn> __result)
            {
                Map thingMap = __instance.parent?.Map;
                if (thingMap == null) return;

                var otherPawns = GetOtherLevelPawns(thingMap);
                if (otherPawns == null) return;

                __result = __result.Concat(otherPawns);
            }
        }

        private static IEnumerable<Pawn> GetOtherLevelPawns(Map map)
        {
            LevelManager mgr;
            Map baseMap;

            if (LevelManager.IsLevelMap(map, out var parentMgr, out _))
            {
                mgr = parentMgr;
                baseMap = parentMgr.map;
            }
            else
            {
                mgr = LevelManager.GetManager(map);
                baseMap = map;
            }

            if (mgr == null || mgr.LevelCount == 0) return null;

            var result = new List<Pawn>();

            // 如果当前是子地图，添加基地图的 pawn
            if (map != baseMap)
            {
                foreach (var p in baseMap.mapPawns.FreeColonists)
                {
                    if (!result.Contains(p))
                        result.Add(p);
                }
            }

            // 添加所有其他层级的 pawn
            foreach (var level in mgr.AllLevels)
            {
                if (level.LevelMap == null || level.LevelMap == map) continue;
                foreach (var p in level.LevelMap.mapPawns.FreeColonists)
                {
                    if (!result.Contains(p))
                        result.Add(p);
                }
            }

            return result.Count > 0 ? result : null;
        }
    }
}
