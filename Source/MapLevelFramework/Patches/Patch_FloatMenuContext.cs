using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// FloatMenuMakerMap.GetOptions 补丁 - 聚焦层级时，重定向右键菜单到子地图。
    /// 同尺寸子地图方案：坐标一致，只需切换 currentMapIndex。
    /// </summary>
    [HarmonyPatch(typeof(FloatMenuMakerMap), "GetOptions",
        new[] { typeof(List<Pawn>), typeof(Vector3), typeof(FloatMenuContext) },
        new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out })]
    public static class Patch_FloatMenuMakerMap_GetOptions
    {
        private static sbyte savedMapIndex = -1;

        public static void Prefix(Vector3 clickPos)
        {
            savedMapIndex = -1;

            var baseMap = Find.CurrentMap;
            if (baseMap == null) return;

            var mgr = LevelManager.GetManager(baseMap);
            if (mgr == null || !mgr.IsFocusingLevel) return;

            var level = mgr.GetLevel(mgr.FocusedElevation);
            if (level?.LevelMap == null) return;

            IntVec3 cell = IntVec3Utility.ToIntVec3(clickPos);
            if (!level.ContainsBaseMapCell(cell)) return;

            // 临时切换到子地图（坐标一致，无需转换 clickPos）
            int subMapIndex = Find.Maps.IndexOf(level.LevelMap);
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
