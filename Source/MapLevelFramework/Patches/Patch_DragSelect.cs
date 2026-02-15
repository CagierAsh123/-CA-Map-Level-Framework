using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// SelectableByMapClick 补丁 - 聚焦层级时，主地图上层级区域内的物体不可被选中。
    /// 防止拖拽框选时选到"楼下"的东西。
    /// </summary>
    [HarmonyPatch(typeof(ThingSelectionUtility), "SelectableByMapClick")]
    public static class Patch_SelectableByMapClick
    {
        public static void Postfix(Thing t, ref bool __result)
        {
            if (!__result) return;
            var filter = LevelManager.ActiveRenderFilter;
            if (filter == null) return;
            if (filter.hostMap != t.Map) return;
            if (LevelManager.IsInActiveRenderArea(t.Position))
                __result = false;
        }
    }

    /// <summary>
    /// Selector.SelectInsideDragBox 补丁 - 聚焦层级时，
    /// 拖拽框选应该选中子地图上的物体而非主地图上的。
    ///
    /// 原版用 Find.CurrentMap（主地图）的 thingGrid 查询，
    /// 坐标也是主地图坐标，无法选中子地图物体。
    ///
    /// 修复：Postfix 中补充子地图物体的选择。
    /// 主地图层级区域内的物体已被 SelectableByMapClick 过滤。
    /// </summary>
    [HarmonyPatch(typeof(Selector), "SelectInsideDragBox")]
    public static class Patch_Selector_SelectInsideDragBox
    {
        public static void Postfix(Selector __instance)
        {
            var baseMap = Find.CurrentMap;
            if (baseMap == null) return;

            var mgr = LevelManager.GetManager(baseMap);
            if (mgr == null || !mgr.IsFocusingLevel) return;

            var level = mgr.GetLevel(mgr.FocusedElevation);
            if (level?.LevelMap == null) return;

            DragBox dragBox = __instance.dragBox;
            Map subMap = level.LevelMap;

            // dragBox 的 LeftX/RightX/BotZ/TopZ 是主地图世界坐标
            // 同尺寸子地图方案：坐标一致，直接使用
            int minX = Mathf.FloorToInt(dragBox.LeftX);
            int maxX = Mathf.FloorToInt(dragBox.RightX);
            int minZ = Mathf.FloorToInt(dragBox.BotZ);
            int maxZ = Mathf.FloorToInt(dragBox.TopZ);

            // 收集子地图中可选的物体
            var things = new HashSet<Thing>();
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (!cell.InBounds(subMap)) continue;

                    List<Thing> cellThings = subMap.thingGrid.ThingsListAt(cell);
                    for (int i = 0; i < cellThings.Count; i++)
                    {
                        Thing t = cellThings[i];
                        if (t.def.selectable && !t.def.neverMultiSelect)
                        {
                            things.Add(t);
                        }
                    }
                }
            }

            if (things.Count == 0) return;

            // 按优先级选择：殖民者 > 人形 > 动物 > 机械体 > 任意可选
            if (SelectWhere(__instance, things, t => t is Pawn p && p.IsColonist)) return;
            if (SelectWhere(__instance, things, t => t is Pawn p && p.RaceProps.Humanlike)) return;
            if (SelectWhere(__instance, things, t => t is Pawn p && p.RaceProps.Animal)) return;
            if (SelectWhere(__instance, things, t => t is Pawn p && p.RaceProps.IsMechanoid)) return;
            SelectWhere(__instance, things, t => t.def.selectable);
        }

        private static bool SelectWhere(Selector selector, HashSet<Thing> things, System.Predicate<Thing> pred)
        {
            bool any = false;
            foreach (var t in things)
            {
                if (pred(t))
                {
                    any = true;
                    selector.Select(t, true, true);
                }
            }
            return any;
        }
    }
}
