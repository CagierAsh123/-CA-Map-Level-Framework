using HarmonyLib;
using RimWorld;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// Mineable.Destroy 补丁 - 在地下层子地图上挖掘岩石后触发边界扩展。
    /// 注意：Mineable 不重写 Destroy，实际补丁目标是 Thing.Destroy。
    /// 使用 Thing __instance + 类型检查避免非 Mineable 实例的转换异常。
    /// </summary>
    [HarmonyPatch(typeof(Mineable), nameof(Mineable.Destroy))]
    public static class Patch_Mineable_Destroy
    {
        public static void Prefix(Thing __instance, out (Map map, IntVec3 pos, bool valid) __state)
        {
            if (__instance is Mineable)
                __state = (__instance.Map, __instance.Position, true);
            else
                __state = (null, IntVec3.Invalid, false);
        }

        public static void Postfix((Map map, IntVec3 pos, bool valid) __state)
        {
            if (!__state.valid || __state.map == null) return;

            // 检查是否在地下层子地图上
            if (LevelManager.IsLevelMap(__state.map, out _, out var levelData)
                && levelData != null && levelData.isUnderground)
            {
                RockFrontierUtility.OnRockMined(__state.map, __state.pos, levelData);
            }
        }
    }
}
