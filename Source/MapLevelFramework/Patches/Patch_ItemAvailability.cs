using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// ItemAvailability.ThingsAvailableAnywhere 补丁 -
    /// 当本层找不到足够材料时，检查基地图和其他层级地图。
    /// 让 ResourceDeliverJobFor 通过第一道门，进入后续的跨层取材逻辑。
    /// </summary>
    [HarmonyPatch(typeof(ItemAvailability), nameof(ItemAvailability.ThingsAvailableAnywhere))]
    public static class Patch_ItemAvailability_ThingsAvailableAnywhere
    {
        private static readonly AccessTools.FieldRef<ItemAvailability, Map> mapRef =
            AccessTools.FieldRefAccess<ItemAvailability, Map>("map");

        public static void Postfix(ref bool __result, ItemAvailability __instance,
            ThingDef need, int amount, Pawn pawn)
        {
            if (__result) return; // 本层已经够了
            if (need == null) return; // Blueprint_Install 无材料需求

            Map thisMap = mapRef(__instance);
            if (thisMap == null) return;

            LevelManager mgr;
            Map baseMap;
            if (LevelManager.IsLevelMap(thisMap, out var parentMgr, out _))
            {
                mgr = parentMgr;
                baseMap = parentMgr.map;
            }
            else
            {
                mgr = LevelManager.GetManager(thisMap);
                baseMap = thisMap;
            }

            if (mgr == null || mgr.LevelCount == 0) return;

            // 统计其他楼层的材料总量
            int total = 0;

            if (thisMap != baseMap)
                total += CountAvailable(baseMap, need, pawn);

            foreach (var level in mgr.AllLevels)
            {
                if (level.LevelMap != null && level.LevelMap != thisMap)
                {
                    total += CountAvailable(level.LevelMap, need, pawn);
                    if (total >= amount) break;
                }
            }

            if (total >= amount)
                __result = true;
        }

        private static int CountAvailable(Map map, ThingDef def, Pawn pawn)
        {
            List<Thing> things = map.listerThings.ThingsOfDef(def);
            int count = 0;
            for (int i = 0; i < things.Count; i++)
            {
                if (!things[i].IsForbidden(pawn))
                    count += things[i].stackCount;
            }
            return count;
        }
    }
}
