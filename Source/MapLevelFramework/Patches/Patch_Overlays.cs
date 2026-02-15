using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// 环境信息/美观度/房间覆盖层补丁。
    ///
    /// 聚焦层级时，临时切换 currentMapIndex 到子地图，
    /// 让整个调用链（ShouldShowRoomStats、ShouldShowBeauty、FillWindow 等）
    /// 都使用子地图的数据。
    /// </summary>
    public static class Patch_Overlays
    {
        // ========== 共用的 Prefix/Finalizer 逻辑 ==========

        private static sbyte savedMapIndex = -1;
        private static int swapDepth = 0;

        /// <summary>
        /// 聚焦层级且鼠标在任何可见层级区域内时，临时切换 Find.CurrentMap 到对应子地图。
        /// 优先使用最高层级（3F 区域用 3F，2F 阳台区域用 2F）。
        /// </summary>
        private static void SwapToLevelMap()
        {
            swapDepth++;
            if (swapDepth > 1) return; // 已经切换过了（嵌套调用）

            savedMapIndex = -1;
            var baseMap = Find.CurrentMap;
            if (baseMap == null) return;

            var mgr = LevelManager.GetManager(baseMap);
            if (mgr == null || !mgr.IsFocusingLevel) return;

            IntVec3 cell = UI.MouseCell();
            var topLevel = LevelManager.GetTopmostLevelAt(cell);
            if (topLevel?.LevelMap == null) return;

            int subMapIndex = Find.Maps.IndexOf(topLevel.LevelMap);
            if (subMapIndex >= 0)
            {
                savedMapIndex = Current.Game.currentMapIndex;
                Current.Game.currentMapIndex = (sbyte)subMapIndex;
            }
        }

        private static void RestoreLevelMap()
        {
            swapDepth--;
            if (swapDepth > 0) return;

            if (savedMapIndex >= 0)
            {
                Current.Game.currentMapIndex = savedMapIndex;
                savedMapIndex = -1;
            }
        }

        // ========== BeautyDrawer ==========

        [HarmonyPatch(typeof(BeautyDrawer), "DrawBeautyAroundMouse")]
        public static class Patch_BeautyDrawer
        {
            public static void Prefix() => SwapToLevelMap();
            public static void Finalizer() => RestoreLevelMap();
        }

        // ========== EnvironmentStatsDrawer ==========

        /// <summary>
        /// EnvironmentStatsOnGUI 调用链：
        /// ShouldShowWindowNow → ShouldShowRoomStats / ShouldShowBeauty
        /// DrawInfoWindow → FillWindow → BeautyUtility / GetRoom
        /// 全部使用 Find.CurrentMap，一次 Prefix/Finalizer 全覆盖。
        /// </summary>
        [HarmonyPatch(typeof(EnvironmentStatsDrawer), "EnvironmentStatsOnGUI")]
        public static class Patch_EnvironmentStatsOnGUI
        {
            public static void Prefix() => SwapToLevelMap();
            public static void Finalizer() => RestoreLevelMap();
        }

        /// <summary>
        /// DrawRoomOverlays 单独调用（不在 EnvironmentStatsOnGUI 内），
        /// 也使用 Find.CurrentMap 获取房间并绘制边框。
        /// </summary>
        [HarmonyPatch(typeof(EnvironmentStatsDrawer), "DrawRoomOverlays")]
        public static class Patch_DrawRoomOverlays
        {
            public static void Prefix() => SwapToLevelMap();
            public static void Finalizer() => RestoreLevelMap();
        }
    }
}
