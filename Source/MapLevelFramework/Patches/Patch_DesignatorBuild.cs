using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// Designator_Build.ProcessInput 补丁 -
    /// 原版只查当前地图的材料，层级地图上没有材料时会提示"缺少材料"。
    /// 需要把其他楼层的材料也纳入可选列表。
    /// </summary>
    [HarmonyPatch(typeof(Designator_Build), nameof(Designator_Build.ProcessInput))]
    public static class Patch_DesignatorBuild_ProcessInput
    {
        private static readonly AccessTools.FieldRef<Designator_Build, BuildableDef> entDefRef =
            AccessTools.FieldRefAccess<Designator_Build, BuildableDef>("entDef");

        private static readonly AccessTools.FieldRef<Designator_Build, ThingDef> stuffDefRef =
            AccessTools.FieldRefAccess<Designator_Build, ThingDef>("stuffDef");

        private static readonly AccessTools.FieldRef<Designator_Build, bool> writeStuffRef =
            AccessTools.FieldRefAccess<Designator_Build, bool>("writeStuff");

        private static readonly System.Func<Designator, bool> CanInteract =
            AccessTools.MethodDelegate<System.Func<Designator, bool>>(
                AccessTools.Method(typeof(Designator), "CheckCanInteract"));

        /// <summary>
        /// 层级地图上，材料选择菜单需要检查所有楼层的材料。
        /// 仅在层级地图 + 需要选材料时替换原方法。
        /// </summary>
        public static bool Prefix(Designator_Build __instance, Event ev)
        {
            Map map = __instance.Map;
            if (map == null) return true;

            // 非层级地图走原版
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
            if (mgr == null || mgr.LevelCount == 0) return true;

            ThingDef thingDef = entDefRef(__instance) as ThingDef;
            if (thingDef == null || !thingDef.MadeFromStuff)
                return true; // 不需要选材料，走原版

            // === 替换原版 ProcessInput 逻辑 ===
            if (!CanInteract(__instance)) return false;

            // 收集所有楼层的可用材料类型
            HashSet<ThingDef> allStuffs = new HashSet<ThingDef>();
            CollectStuffs(map, allStuffs);
            if (map != baseMap) CollectStuffs(baseMap, allStuffs);
            foreach (var level in mgr.AllLevels)
            {
                if (level.LevelMap != null && level.LevelMap != map)
                    CollectStuffs(level.LevelMap, allStuffs);
            }

            List<FloatMenuOption> list = new List<FloatMenuOption>();
            foreach (ThingDef stuffDef in allStuffs.OrderByDescending(d =>
                d.stuffProps?.commonality ?? float.PositiveInfinity)
                .ThenBy(d => d.BaseMarketValue))
            {
                if (!stuffDef.IsStuff || !stuffDef.stuffProps.CanMake(thingDef))
                    continue;

                ThingDef localStuff = stuffDef;
                string text = GenLabel.ThingLabel(entDefRef(__instance), localStuff, 1).CapitalizeFirst();
                list.Add(new FloatMenuOption(text, delegate
                {
                    // 选中指示器
                    Find.DesignatorManager.Select(__instance);
                    stuffDefRef(__instance) = localStuff;
                    writeStuffRef(__instance) = true;
                }, localStuff));
            }

            if (list.Count == 0)
            {
                Messages.Message("NoStuffsToBuildWith".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            FloatMenu floatMenu = new FloatMenu(list);
            floatMenu.onCloseCallback = delegate
            {
                writeStuffRef(__instance) = true;
            };
            Find.WindowStack.Add(floatMenu);
            Find.DesignatorManager.Select(__instance);
            return false;
        }

        private static void CollectStuffs(Map map, HashSet<ThingDef> result)
        {
            foreach (var kvp in map.resourceCounter.AllCountedAmounts)
            {
                if (kvp.Value > 0 || map.listerThings.ThingsOfDef(kvp.Key).Count > 0)
                    result.Add(kvp.Key);
            }
        }
    }
}
