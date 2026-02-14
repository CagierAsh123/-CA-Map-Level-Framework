using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// 让工作/方案等 PawnTable 面板显示子地图上的殖民者。
    /// 基类 MainTabWindow_PawnTable.Pawns 只返回 Find.CurrentMap 的 FreeColonists，
    /// 这里 Postfix 追加所有层级子地图的殖民者。
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_PawnTable), "get_Pawns")]
    public static class Patch_PawnTable_Pawns
    {
        public static void Postfix(ref IEnumerable<Pawn> __result)
        {
            Map currentMap = Find.CurrentMap;
            if (currentMap == null) return;

            var mgr = LevelManager.GetManager(currentMap);
            if (mgr == null) return;

            // 收集所有层级子地图的 FreeColonists
            List<Pawn> extra = null;
            foreach (var level in mgr.AllLevels)
            {
                Map levelMap = level.LevelMap;
                if (levelMap == null) continue;

                foreach (Pawn p in levelMap.mapPawns.FreeColonists)
                {
                    if (extra == null) extra = new List<Pawn>();
                    extra.Add(p);
                }
            }

            if (extra != null)
                __result = __result.Concat(extra);
        }
    }

    /// <summary>
    /// 方案面板额外追加了 ColonySubhumansControllable（机械族等），
    /// 也需要包含子地图的。
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Schedule), "get_Pawns")]
    public static class Patch_Schedule_Pawns
    {
        public static void Postfix(ref IEnumerable<Pawn> __result)
        {
            Map currentMap = Find.CurrentMap;
            if (currentMap == null) return;

            var mgr = LevelManager.GetManager(currentMap);
            if (mgr == null) return;

            List<Pawn> extra = null;
            foreach (var level in mgr.AllLevels)
            {
                Map levelMap = level.LevelMap;
                if (levelMap == null) continue;

                foreach (Pawn p in levelMap.mapPawns.ColonySubhumansControllable)
                {
                    if (extra == null) extra = new List<Pawn>();
                    extra.Add(p);
                }
            }

            if (extra != null)
                __result = __result.Concat(extra);
        }
    }
}
