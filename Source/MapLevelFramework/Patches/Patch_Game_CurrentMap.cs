using HarmonyLib;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// Game.CurrentMap setter 补丁 - 防止直接切换到上层子地图。
    /// 上层子地图：重定向到宿主主地图并聚焦该层级。
    /// 地下层子地图：允许直接切换（地下层使用原生渲染）。
    /// </summary>
    [HarmonyPatch(typeof(Game), "CurrentMap", MethodType.Setter)]
    public static class Patch_Game_CurrentMap
    {
        public static void Prefix(ref Map value)
        {
            if (value == null) return;
            if (LevelManager.SuppressAutoFocus) return;

            // 检查是否是层级子地图
            if (LevelManager.IsLevelMap(value, out var manager, out var levelData))
            {
                // 地下层：允许直接切换到子地图
                if (levelData.isUnderground)
                    return;

                // 上层：不要切换到子地图，而是切换到宿主主地图并聚焦该层级
                if (manager.map != null && manager.map.Index >= 0)
                {
                    value = manager.map;
                    manager.FocusLevel(levelData.elevation);
                }
            }
        }
    }
}
