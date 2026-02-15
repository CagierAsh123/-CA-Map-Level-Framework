using HarmonyLib;
using RimWorld;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// GenConstruct.CanPlaceBlueprintAt 补丁 -
    /// 在层级子地图上，限制建筑/地板只能放置在 usableCells 范围内。
    /// 防止上帝模式下在楼层外放置蓝图。
    /// </summary>
    [HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.CanPlaceBlueprintAt))]
    public static class Patch_GenConstruct_CanPlaceBlueprintAt
    {
        public static bool Prefix(ref AcceptanceReport __result, BuildableDef entDef, IntVec3 center, Rot4 rot, Map map)
        {
            if (map == null) return true;
            if (!LevelManager.IsLevelMap(map, out _, out var level)) return true;
            if (level.usableCells == null) return true;

            // 检查建筑占用的所有格子是否都在可用区域内
            CellRect rect = GenAdj.OccupiedRect(center, rot, entDef.Size);
            foreach (IntVec3 cell in rect)
            {
                if (!level.usableCells.Contains(cell))
                {
                    __result = new AcceptanceReport("Outside level area");
                    return false;
                }
            }
            return true;
        }
    }
}
