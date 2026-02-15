using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// 屋顶覆盖层补丁 - 聚焦层级时，屋顶覆盖层显示最高覆盖层级的屋顶数据。
    /// 聚焦 3F 时：3F 区域显示 3F 屋顶，2F 阳台区域显示 2F 屋顶。
    /// </summary>
    public static class Patch_RoofOverlay
    {
        [HarmonyPatch(typeof(RoofGrid), "GetCellBool")]
        public static class Patch_GetCellBool
        {
            public static void Postfix(int index, ref bool __result, Map ___map)
            {
                var filter = LevelManager.ActiveRenderFilter;
                if (filter == null) return;

                var mgr = LevelManager.GetManager(___map);
                if (mgr == null || !mgr.IsFocusingLevel) return;

                IntVec3 cell = ___map.cellIndices.IndexToCell(index);
                var topLevel = LevelManager.GetTopmostLevelAt(cell);
                if (topLevel?.LevelMap == null) return;

                __result = topLevel.LevelMap.roofGrid.Roofed(index);
            }
        }

        [HarmonyPatch(typeof(RoofGrid), "GetCellExtraColor")]
        public static class Patch_GetCellExtraColor
        {
            public static void Postfix(int index, ref Color __result, Map ___map)
            {
                var filter = LevelManager.ActiveRenderFilter;
                if (filter == null) return;

                var mgr = LevelManager.GetManager(___map);
                if (mgr == null || !mgr.IsFocusingLevel) return;

                IntVec3 cell = ___map.cellIndices.IndexToCell(index);
                var topLevel = LevelManager.GetTopmostLevelAt(cell);
                if (topLevel?.LevelMap == null) return;

                RoofDef roof = topLevel.LevelMap.roofGrid.RoofAt(index);
                __result = (RoofDefOf.RoofRockThick != null && roof == RoofDefOf.RoofRockThick)
                    ? Color.gray
                    : Color.white;
            }
        }
    }
}
