using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// 聚焦层级时，隐藏基地图在 level area 内的 zone 覆盖层。
    /// SectionLayer_Zones.Regenerate 逐格生成 zone mesh，
    /// 我们在 ZoneAt 调用后插入检查：如果该格在活跃层级区域内则跳过。
    /// </summary>
    public static class Patch_ZoneLayer
    {
        private static readonly FieldInfo sectionField =
            typeof(SectionLayer).GetField("section", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        public static void Apply(Harmony harmony)
        {
            var zoneLayerType = AccessTools.TypeByName("Verse.SectionLayer_Zones");
            if (zoneLayerType == null) return;

            var regenerate = AccessTools.Method(zoneLayerType, "Regenerate");
            if (regenerate == null) return;

            harmony.Patch(regenerate,
                transpiler: new HarmonyMethod(typeof(Patch_ZoneLayer), nameof(Transpiler)));
        }

        /// <summary>
        /// 在 Regenerate 中，ZoneAt 返回 zone 后、null 检查前，
        /// 插入：如果当前有 ActiveRenderFilter 且 cell 在 area 内，将 zone 置为 null。
        /// </summary>
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var zoneAt = AccessTools.Method(typeof(ZoneManager), "ZoneAt");

            for (int i = 0; i < list.Count; i++)
            {
                yield return list[i];

                // 在每个 ZoneAt 调用后插入过滤
                if (list[i].Calls(zoneAt))
                {
                    // 栈顶是 Zone（或 null）。
                    // 加载当前 cell 坐标（i, j 在局部变量中）并调用过滤方法。
                    // Regenerate 的局部变量布局：
                    //   0 = num (float altitude)
                    //   1 = zoneManager
                    //   2 = cellRect
                    //   3 = i (x)
                    //   4 = j (z)
                    yield return new CodeInstruction(OpCodes.Ldarg_0); // this (SectionLayer)
                    yield return new CodeInstruction(OpCodes.Ldloc_3); // i
                    yield return new CodeInstruction(OpCodes.Ldloc_S, (byte)4); // j
                    yield return CodeInstruction.Call(typeof(Patch_ZoneLayer), nameof(FilterZone));
                }
            }
        }

        /// <summary>
        /// 如果聚焦层级且该格在 level area 内，返回 null 以跳过 zone 绘制。
        /// </summary>
        public static Zone FilterZone(Zone zone, SectionLayer layer, int x, int z)
        {
            if (zone == null) return null;

            var filter = LevelManager.ActiveRenderFilter;
            if (filter == null) return zone;

            // 只过滤基地图的 zone layer（子地图的 zone 不过滤）
            Section sec = sectionField?.GetValue(layer) as Section;
            Map layerMap = sec?.map;
            if (filter.hostMap != layerMap) return zone;

            IntVec3 cell = new IntVec3(x, 0, z);
            if (LevelManager.IsInActiveRenderArea(cell))
                return null;

            return zone;
        }
    }
}
