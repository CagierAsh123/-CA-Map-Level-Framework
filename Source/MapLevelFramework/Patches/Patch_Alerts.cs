using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// Alert 系统补丁 - 让 alert 聚合所有层级的资源。
    ///
    /// 子地图是 pocketMap（非 IsPlayerHome），原版 alert 不会检查子地图。
    /// 当基地图有子地图时，需要聚合所有层级的资源来避免误报。
    /// </summary>
    public static class Patch_Alerts
    {
        /// <summary>
        /// Alert_NeedColonistBeds - 如果基地图有子地图，跳过检查（床可能在其他楼层）。
        /// </summary>
        [HarmonyPatch(typeof(Alert_NeedColonistBeds), "NeedColonistBeds")]
        public static class Patch_NeedColonistBeds
        {
            public static bool Prefix(Map map, ref bool __result)
            {
                if (!CrossLevelUtility.HasLevels(map)) return true;
                // 有多层时不报警（床可能在其他楼层）
                __result = false;
                return false;
            }
        }

        /// <summary>
        /// Alert_NeedMealSource - 如果基地图有子地图，检查所有层级。
        /// </summary>
        [HarmonyPatch(typeof(Alert_NeedMealSource), "NeedMealSource")]
        public static class Patch_NeedMealSource
        {
            public static bool Prefix(Map map, ref bool __result)
            {
                if (!CrossLevelUtility.HasLevels(map)) return true;
                __result = false;
                return false;
            }
        }

        /// <summary>
        /// Alert_LowFood - 聚合所有层级的食物。
        /// </summary>
        [HarmonyPatch(typeof(Alert_LowFood), "MapWithLowFood")]
        public static class Patch_LowFood
        {
            public static bool Prefix(ref Map __result)
            {
                foreach (Map map in Find.Maps)
                {
                    if (!map.IsPlayerHome || !map.mapPawns.AnyColonistSpawned)
                        continue;

                    var mgr = LevelManager.GetManager(map);
                    if (mgr == null || mgr.LevelCount == 0)
                        continue; // 没有子地图，让原版处理

                    // 聚合所有层级的食物和人口
                    int colonists = 0;
                    float nutrition = 0f;
                    foreach (Map levelMap in CrossLevelUtility.GetAllLevelMaps(map))
                    {
                        colonists += levelMap.mapPawns.FreeColonistsSpawnedCount;
                        nutrition += levelMap.resourceCounter.TotalHumanEdibleNutrition;
                    }

                    if (nutrition < 4f * colonists)
                    {
                        __result = map;
                        return false;
                    }
                }

                // 检查没有子地图的地图（让原版逻辑处理）
                foreach (Map map in Find.Maps)
                {
                    if (!map.IsPlayerHome || !map.mapPawns.AnyColonistSpawned)
                        continue;

                    var mgr = LevelManager.GetManager(map);
                    if (mgr != null && mgr.LevelCount > 0)
                        continue; // 已经在上面处理过

                    if (map.resourceCounter.TotalHumanEdibleNutrition
                        < 4f * map.mapPawns.FreeColonistsSpawnedCount)
                    {
                        __result = map;
                        return false;
                    }
                }

                __result = null;
                return false;
            }
        }

        /// <summary>
        /// Alert_LowMedicine - 聚合所有层级的药品。
        /// </summary>
        [HarmonyPatch(typeof(Alert_LowMedicine), "MedicineCount")]
        public static class Patch_LowMedicine
        {
            public static bool Prefix(Map map, ref int __result)
            {
                if (!CrossLevelUtility.HasLevels(map)) return true;

                int total = 0;
                foreach (Map levelMap in CrossLevelUtility.GetAllLevelMaps(map))
                {
                    total += levelMap.resourceCounter.GetCountIn(ThingRequestGroup.Medicine);
                }
                __result = total;
                return false;
            }
        }
    }
}
