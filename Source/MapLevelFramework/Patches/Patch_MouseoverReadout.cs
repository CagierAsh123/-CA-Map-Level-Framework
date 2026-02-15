using HarmonyLib;
using UnityEngine;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// MouseoverReadout.MouseoverReadoutOnGUI 补丁 - 聚焦层级时，
    /// 仅当鼠标在层级区域内时，临时切换 currentMapIndex 到子地图。
    /// 同尺寸子地图方案：坐标一致，只需切换地图。
    /// </summary>
    [HarmonyPatch(typeof(MouseoverReadout), "MouseoverReadoutOnGUI")]
    public static class Patch_MouseoverReadout_OnGUI
    {
        private static sbyte savedMapIndex = -1;

        public static void Prefix()
        {
            savedMapIndex = -1;

            var baseMap = Find.CurrentMap;
            if (baseMap == null) return;

            var mgr = LevelManager.GetManager(baseMap);
            if (mgr == null || !mgr.IsFocusingLevel) return;

            IntVec3 cell = IntVec3Utility.ToIntVec3(UI.MouseMapPosition());
            var topLevel = LevelManager.GetTopmostLevelAt(cell);
            if (topLevel?.LevelMap == null) return;

            int subMapIndex = Find.Maps.IndexOf(topLevel.LevelMap);
            if (subMapIndex >= 0)
            {
                savedMapIndex = Current.Game.currentMapIndex;
                Current.Game.currentMapIndex = (sbyte)subMapIndex;
            }
        }

        public static void Finalizer()
        {
            if (savedMapIndex >= 0)
            {
                Current.Game.currentMapIndex = (sbyte)savedMapIndex;
                savedMapIndex = -1;
            }
        }
    }
}
