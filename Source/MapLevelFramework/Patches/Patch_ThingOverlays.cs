using HarmonyLib;
using RimWorld;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// Thing.DrawGUIOverlay 补丁 -
    /// 聚焦层级时，跳过主地图上位于层级 area 内的物体 GUI 覆盖层
    /// （物品数量标签等）。
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.DrawGUIOverlay))]
    public static class Patch_Thing_DrawGUIOverlay
    {
        public static bool Prefix(Thing __instance)
        {
            var filter = LevelManager.ActiveRenderFilter;
            if (filter == null) return true;
            if (filter.hostMap != __instance.Map) return true;
            return !filter.ContainsBaseMapCell(__instance.Position);
        }
    }

    /// <summary>
    /// Pawn.DrawGUIOverlay 补丁 -
    /// Pawn 重写了 DrawGUIOverlay 且不调用 base，
    /// 所以需要单独 patch 来隐藏层级 area 内的 Pawn 名字。
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawGUIOverlay))]
    public static class Patch_Pawn_DrawGUIOverlay
    {
        public static bool Prefix(Pawn __instance)
        {
            var filter = LevelManager.ActiveRenderFilter;
            if (filter == null) return true;
            if (filter.hostMap != __instance.Map) return true;
            return !filter.ContainsBaseMapCell(__instance.Position);
        }
    }

    /// <summary>
    /// OverlayDrawer.DrawOverlay 补丁 -
    /// 聚焦层级时，跳过主地图上位于层级 area 内的物体覆盖层
    /// （禁止红叉、缺电、故障等图标）。
    /// </summary>
    [HarmonyPatch(typeof(OverlayDrawer), nameof(OverlayDrawer.DrawOverlay))]
    public static class Patch_OverlayDrawer_DrawOverlay
    {
        public static bool Prefix(Thing t)
        {
            var filter = LevelManager.ActiveRenderFilter;
            if (filter == null) return true;
            if (filter.hostMap != t.Map) return true;
            return !filter.ContainsBaseMapCell(t.Position);
        }
    }

    /// <summary>
    /// Pawn.DrawShadowAt 补丁 -
    /// DynamicDrawManager 在 shouldDrawShadow 时直接调用此方法，
    /// 绕过了 DynamicDrawPhase，需要单独 patch。
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawShadowAt))]
    public static class Patch_Pawn_DrawShadowAt
    {
        public static bool Prefix(Pawn __instance)
        {
            var filter = LevelManager.ActiveRenderFilter;
            if (filter == null) return true;
            if (filter.hostMap != __instance.Map) return true;
            return !filter.ContainsBaseMapCell(__instance.Position);
        }
    }
}
