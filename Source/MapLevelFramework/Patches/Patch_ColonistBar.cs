using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// ColonistBar.CheckRecacheEntries 补丁 - 从顶部殖民者栏中隐藏层级子地图。
    ///
    /// 参照 VMF 的 Patch_ColonistBar_CheckRecacheEntries。
    /// </summary>
    [HarmonyPatch(typeof(ColonistBar), "CheckRecacheEntries")]
    public static class Patch_ColonistBar_CheckRecacheEntries
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var findMaps = AccessTools.PropertyGetter(typeof(Find), "Maps");
            int idx = list.FindIndex(c => c.Calls(findMaps)) + 1;
            if (idx > 0)
            {
                list.Insert(idx, CodeInstruction.Call(
                    typeof(Patch_ColonistBar_CheckRecacheEntries), "ExcludeLevelMaps"));
            }
            return list;
        }

        private static IEnumerable<Map> ExcludeLevelMaps(IEnumerable<Map> maps)
        {
            if (maps == null) return null;
            return maps.Where(m =>
            {
                LevelManager manager;
                LevelData levelData;
                return !LevelManager.IsLevelMap(m, out manager, out levelData);
            });
        }
    }
}
