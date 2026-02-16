using HarmonyLib;
using RimWorld;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// RecipeWorkerCounter.CountProducts 补丁 -
    /// 工作台"维持数量"清单只查当前地图，需要加上其他楼层的产品数量。
    /// </summary>
    [HarmonyPatch(typeof(RecipeWorkerCounter), nameof(RecipeWorkerCounter.CountProducts))]
    public static class Patch_RecipeWorkerCounter_CountProducts
    {
        public static void Postfix(ref int __result, RecipeWorkerCounter __instance,
            Bill_Production bill)
        {
            if (!__instance.CanCountProducts(bill)) return;

            Map billMap = bill.Map;
            if (billMap == null) return;

            LevelManager mgr;
            Map baseMap;
            if (LevelManager.IsLevelMap(billMap, out var parentMgr, out _))
            {
                mgr = parentMgr;
                baseMap = parentMgr.map;
            }
            else
            {
                mgr = LevelManager.GetManager(billMap);
                baseMap = billMap;
            }

            if (mgr == null || mgr.LevelCount == 0) return;

            // 在其他楼层统计产品数量
            if (billMap != baseMap)
                __result += CountOnMap(baseMap, __instance, bill);

            foreach (var level in mgr.AllLevels)
            {
                if (level.LevelMap != null && level.LevelMap != billMap)
                    __result += CountOnMap(level.LevelMap, __instance, bill);
            }
        }

        private static int CountOnMap(Map map, RecipeWorkerCounter counter, Bill_Production bill)
        {
            var products = bill.recipe?.products;
            if (products == null || products.Count == 0) return 0;

            ThingDefCountClass product = products[0];
            ThingDef def = product.thingDef;

            // 简单资源走 resourceCounter 快速路径
            if (def.CountAsResource && !bill.includeEquipped
                && (bill.includeTainted || !def.IsApparel || !def.apparel.careIfWornByCorpse)
                && bill.GetIncludeSlotGroup() == null
                && bill.hpRange.min == 0f && bill.hpRange.max == 1f
                && bill.qualityRange.min == QualityCategory.Awful
                && bill.qualityRange.max == QualityCategory.Legendary
                && !bill.limitToAllowedStuff)
            {
                return map.resourceCounter.GetCount(def);
            }

            // 慢速路径：遍历 listerThings
            int count = 0;
            var things = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < things.Count; i++)
            {
                if (counter.CountValidThing(things[i], bill, def))
                    count++;
            }
            return count;
        }
    }
}
