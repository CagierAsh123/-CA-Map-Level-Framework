using HarmonyLib;
using RimWorld;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// Thing.Destroy 补丁 - 在地下层子地图上挖掘岩石后触发边界扩展。
    /// 直接补丁 Thing.Destroy（Mineable 不重写 Destroy，用 typeof(Mineable) 可能无法解析）。
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.Destroy))]
    public static class Patch_Mineable_Destroy
    {
        public static void Prefix(Thing __instance, out (Map map, IntVec3 pos, bool valid) __state)
        {
            if (__instance is Mineable && __instance.Spawned)
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
                Log.Message($"[MLF] Rock mined at {__state.pos} on underground level {levelData.elevation}");
                RockFrontierUtility.OnRockMined(__state.map, __state.pos, levelData);
            }
        }
    }
}
