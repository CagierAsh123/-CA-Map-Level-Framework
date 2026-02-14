using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// ColonistBar.CheckRecacheEntries 补丁：
    /// 1. Transpiler 排除子地图，防止出现独立地图栏分组
    /// 2. Postfix 将子地图上的殖民者注入宿主地图的分组中
    /// </summary>
    [HarmonyPatch(typeof(ColonistBar), "CheckRecacheEntries")]
    public static class Patch_ColonistBar_CheckRecacheEntries
    {
        private static FieldInfo cachedEntriesField;
        private static FieldInfo cachedReorderableGroupsField;
        private static FieldInfo drawLocsFinderField;
        private static FieldInfo cachedDrawLocsField;
        private static FieldInfo cachedScaleField;
        private static MethodInfo calculateDrawLocsMethod;

        static Patch_ColonistBar_CheckRecacheEntries()
        {
            var t = typeof(ColonistBar);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            cachedEntriesField = t.GetField("cachedEntries", flags);
            cachedReorderableGroupsField = t.GetField("cachedReorderableGroups", flags);
            drawLocsFinderField = t.GetField("drawLocsFinder", flags);
            cachedDrawLocsField = t.GetField("cachedDrawLocs", flags);
            cachedScaleField = t.GetField("cachedScale", flags);
            calculateDrawLocsMethod = typeof(ColonistBarDrawLocsFinder).GetMethod(
                "CalculateDrawLocs",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(List<Vector2>), typeof(float).MakeByRefType(), typeof(int) },
                null);
        }

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

        public static void Postfix(ColonistBar __instance)
        {
            if (cachedEntriesField == null) return;

            var entries = cachedEntriesField.GetValue(__instance) as List<ColonistBar.Entry>;
            if (entries == null) return;

            bool added = false;

            foreach (Map map in Find.Maps)
            {
                if (!LevelManager.IsLevelMap(map, out var manager, out var levelData))
                    continue;

                Map hostMap = manager.map;

                // 找到宿主地图的 group 编号
                int hostGroup = -1;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].map == hostMap)
                    {
                        hostGroup = entries[i].group;
                        break;
                    }
                }
                if (hostGroup < 0) continue;

                // 将子地图的殖民者加入宿主地图的分组
                foreach (Pawn pawn in map.mapPawns.FreeColonists)
                {
                    // 避免重复添加
                    bool exists = false;
                    for (int i = 0; i < entries.Count; i++)
                    {
                        if (entries[i].pawn == pawn)
                        {
                            exists = true;
                            break;
                        }
                    }
                    if (exists) continue;

                    entries.Add(new ColonistBar.Entry(pawn, hostMap, hostGroup));
                    added = true;
                }
            }

            if (added)
            {
                // 重建 reorderable groups 列表
                var reorderGroups = cachedReorderableGroupsField?.GetValue(__instance) as List<int>;
                if (reorderGroups != null)
                {
                    reorderGroups.Clear();
                    for (int i = 0; i < entries.Count; i++)
                        reorderGroups.Add(-1);
                }

                // 重新计算绘制位置
                var finder = drawLocsFinderField?.GetValue(__instance);
                var drawLocs = cachedDrawLocsField?.GetValue(__instance) as List<Vector2>;
                if (finder != null && drawLocs != null && calculateDrawLocsMethod != null)
                {
                    int groupCount = 0;
                    for (int i = 0; i < entries.Count; i++)
                        groupCount = Math.Max(groupCount, entries[i].group + 1);

                    var args = new object[] { drawLocs, 0f, groupCount };
                    calculateDrawLocsMethod.Invoke(finder, args);
                    cachedScaleField?.SetValue(__instance, (float)args[1]);
                }
            }
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
