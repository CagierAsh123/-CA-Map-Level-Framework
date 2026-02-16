using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// TradeUtility.AllLaunchableThingsForTrade 补丁 -
    /// 轨道交易只查当前地图的信标范围，需要把其他楼层的可交易物品也加进去。
    /// </summary>
    [HarmonyPatch(typeof(TradeUtility), nameof(TradeUtility.AllLaunchableThingsForTrade))]
    public static class Patch_TradeUtility_AllLaunchableThingsForTrade
    {
        private static bool appending;

        public static void Postfix(ref IEnumerable<Thing> __result, Map map, ITrader trader)
        {
            if (appending) return; // 防止递归
            if (map == null) return;

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

            if (mgr == null || mgr.LevelCount == 0) return;

            __result = AppendOtherLevels(__result, map, baseMap, mgr, trader);
        }

        private static IEnumerable<Thing> AppendOtherLevels(
            IEnumerable<Thing> original, Map currentMap, Map baseMap,
            LevelManager mgr, ITrader trader)
        {
            foreach (var thing in original)
                yield return thing;

            appending = true;
            try
            {
                if (currentMap != baseMap)
                {
                    foreach (var thing in TradeUtility.AllLaunchableThingsForTrade(baseMap, trader))
                        yield return thing;
                }

                foreach (var level in mgr.AllLevels)
                {
                    if (level.LevelMap != null && level.LevelMap != currentMap)
                    {
                        foreach (var thing in TradeUtility.AllLaunchableThingsForTrade(level.LevelMap, trader))
                            yield return thing;
                    }
                }
            }
            finally
            {
                appending = false;
            }
        }
    }
}
