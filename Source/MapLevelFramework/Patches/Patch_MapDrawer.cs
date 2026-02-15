using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// MapDrawer 补丁 - 在主地图渲染完成后，从低到高叠加渲染所有中间层级的内容。
    /// 聚焦 3F 时渲染顺序：2F → 3F，实现多层遮挡合成。
    /// </summary>
    [HarmonyPatch(typeof(MapDrawer), "DrawMapMesh")]
    public static class Patch_MapDrawer_DrawMapMesh
    {
        public static void Postfix(MapDrawer __instance)
        {
            Map baseMap = Traverse.Create(__instance).Field("map").GetValue<Map>();
            if (baseMap == null) return;

            var mgr = LevelManager.GetManager(baseMap);
            if (mgr == null || !mgr.IsFocusingLevel) return;

            var renderLevels = LevelManager.ActiveRenderLevels;
            if (renderLevels == null || renderLevels.Count == 0) return;

            // 从低到高逐层渲染
            for (int i = 0; i < renderLevels.Count; i++)
            {
                var level = renderLevels[i];
                if (level?.LevelMap == null) continue;

                // 计算被高层覆盖的格子（中间层的动态物体不应透过高层地板显示）
                HashSet<IntVec3> excludeCells = null;
                if (i < renderLevels.Count - 1)
                {
                    excludeCells = new HashSet<IntVec3>();
                    for (int j = i + 1; j < renderLevels.Count; j++)
                    {
                        var higher = renderLevels[j];
                        if (higher.usableCells != null)
                            excludeCells.UnionWith(higher.usableCells);
                        else
                            foreach (IntVec3 c in higher.area)
                                excludeCells.Add(c);
                    }
                }

                Render.LevelRenderer.UpdateLevelMapSections(level.LevelMap, level);
                Render.LevelRenderer.DrawLevelMapMesh(level.LevelMap, level, i, excludeCells);
                Render.LevelRenderer.DrawLevelDynamicThings(level.LevelMap, level, i, excludeCells);
                Render.LevelRenderer.DrawLevelOverlays(level.LevelMap);
            }

            // 边界高亮只画聚焦层
            var focusedLevel = renderLevels[renderLevels.Count - 1];
            Render.LevelRenderer.DrawLevelBoundaryOverlay(focusedLevel, baseMap);
        }
    }
}
