using HarmonyLib;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// MapDrawer 补丁 - 在主地图渲染完成后，叠加渲染聚焦层级的内容。
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

            var level = mgr.GetLevel(mgr.FocusedElevation);
            if (level?.LevelMap == null) return;

            // 子地图的 MapDrawer 不在主循环中更新，手动触发 section 重建
            Render.LevelRenderer.UpdateLevelMapSections(level.LevelMap, level);

            // 叠加渲染子地图的静态 mesh（只绘制 activeSections 内的 section）
            Render.LevelRenderer.DrawLevelMapMesh(level.LevelMap, level);

            // 叠加渲染子地图的动态 Thing（只绘制 usableCells 内的）
            Render.LevelRenderer.DrawLevelDynamicThings(level.LevelMap, level);

            // 叠加渲染子地图的覆盖层（designations、overlays、flecks 等）
            Render.LevelRenderer.DrawLevelOverlays(level.LevelMap);

            // 非楼层区域红色遮罩 + 楼层边缘高亮
            Render.LevelRenderer.DrawLevelBoundaryOverlay(level, baseMap);
        }
    }
}
