using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// 格子过滤 - 对层级子地图，SteadyEnvironmentEffects 只处理可用区域内的格子。
    /// 层级子地图是全尺寸 Map（如 250×250），但可用区域可能只有一小块。
    /// 原版每 tick 随机选 map.Area * 0.0006 个格子处理，大部分选到空白区域白跑。
    /// </summary>
    [HarmonyPatch]
    public static class Patch_SteadyEnvironmentEffects_CellFilter
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(SteadyEnvironmentEffects), "DoCellSteadyEffects");
        }

        public static bool Prefix(IntVec3 c, Map ___map)
        {
            if (___map?.Parent is LevelMapParent lmp && lmp.levelData != null)
            {
                return lmp.levelData.IsCellUsable(c);
            }
            return true;
        }
    }
}
