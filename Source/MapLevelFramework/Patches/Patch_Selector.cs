using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// Selector.SelectableObjectsUnderMouse 补丁 - 聚焦层级时，从子地图获取可选物体。
    /// 使用 GetTopmostLevelAt 查询鼠标位置的最高可见层级，
    /// 这样聚焦 3F 时也能选中 2F 阳台上的物体。
    /// 同尺寸子地图方案：坐标一致，无需转换。
    /// </summary>
    [HarmonyPatch(typeof(Selector), "SelectableObjectsUnderMouse")]
    public static class Patch_Selector_SelectableObjectsUnderMouse
    {
        public static bool Prefix(ref IEnumerable<object> __result)
        {
            var baseMap = Find.CurrentMap;
            if (baseMap == null) return true;

            var mgr = LevelManager.GetManager(baseMap);
            if (mgr == null || !mgr.IsFocusingLevel) return true;

            // 只允许选择当前聚焦层的物体
            Map focusedMap = mgr.FocusedMap;
            if (focusedMap == null || focusedMap == baseMap) return true;

            Vector3 mousePos = UI.MouseMapPosition();
            IntVec3 cell = IntVec3Utility.ToIntVec3(mousePos);
            if (!cell.InBounds(focusedMap)) return true;

            var objects = new List<object>();

            foreach (Pawn pawn in focusedMap.mapPawns.AllPawnsSpawned)
            {
                float dist = (pawn.DrawPos - mousePos).MagnitudeHorizontal();
                if (dist < 0.4f)
                {
                    objects.Add(pawn);
                }
            }

            foreach (Thing thing in focusedMap.thingGrid.ThingsAt(cell))
            {
                if (!objects.Contains(thing))
                {
                    objects.Add(thing);
                }
            }

            Zone zone = focusedMap.zoneManager.ZoneAt(cell);
            if (zone != null)
            {
                objects.Add(zone);
            }

            __result = objects;
            return false;
        }
    }

    /// <summary>
    /// Selector.SelectInternal 补丁 - 防止选中子地图物体时切换地图和跳转视角。
    ///
    /// 原版逻辑：如果 thing.MapHeld != Find.CurrentMap，会切换到 thing 的地图并跳转摄像机。
    /// 子地图物体的 MapHeld 是子地图（不同于主地图），会触发地图切换 + 摄像机跳到本地坐标(0,0)。
    ///
    /// 修复：选中子地图物体时，直接设 Game.currentMapIndex（绕过 setter 的
    /// Notify_SwitchedMap 通知），使 map == Find.CurrentMap 为 true，跳过地图切换逻辑。
    /// 注意：Game.currentMapIndex 是 public sbyte 字段，可以直接访问。
    /// </summary>
    [HarmonyPatch(typeof(Selector), "SelectInternal")]
    public static class Patch_Selector_SelectInternal
    {
        private static sbyte savedMapIndex = -1;

        public static void Prefix(object obj)
        {
            savedMapIndex = -1;

            // 获取对象所在的 Map
            Map objMap = null;
            if (obj is Thing thing)
                objMap = thing.Map;
            else if (obj is Zone zone)
                objMap = zone.Map;

            if (objMap == null) return;

            LevelManager manager;
            LevelData level;
            if (LevelManager.IsLevelMap(objMap, out manager, out level))
            {
                int subMapIndex = Find.Maps.IndexOf(objMap);
                if (subMapIndex >= 0)
                {
                    savedMapIndex = Current.Game.currentMapIndex;
                    Current.Game.currentMapIndex = (sbyte)subMapIndex;
                }
            }
        }

        public static void Finalizer()
        {
            if (savedMapIndex >= 0)
            {
                Current.Game.currentMapIndex = savedMapIndex;
                savedMapIndex = -1;
            }
        }
    }
}
