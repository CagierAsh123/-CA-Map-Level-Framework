using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace MapLevelFramework.Render
{
    /// <summary>
    /// 层级叠加渲染器 - 将子地图的内容渲染到主地图的指定区域上。
    /// 
    /// 核心原理：
    /// 1. 获取子地图的 Section mesh 数据
    /// 2. 用 Graphics.DrawMesh + offset 画到主地图对应位置
    /// 3. 动态 Thing 也通过 offset 重新定位绘制
    /// </summary>
    public static class LevelRenderer
    {
        // 反射缓存
        private static FieldInfo sectionsField;
        private static FieldInfo layersField;

        static LevelRenderer()
        {
            sectionsField = typeof(MapDrawer).GetField("sections",
                BindingFlags.Instance | BindingFlags.NonPublic);
            layersField = typeof(Section).GetField("layers",
                BindingFlags.Instance | BindingFlags.NonPublic);
        }

        /// <summary>
        /// 渲染子地图的静态 mesh（地形、建筑等 SectionLayer）。
        /// </summary>
        public static void DrawLevelMapMesh(Map levelMap, Vector3 drawOffset)
        {
            if (levelMap?.mapDrawer == null) return;
            if (sectionsField == null || layersField == null) return;

            Section[,] sections = sectionsField.GetValue(levelMap.mapDrawer) as Section[,];
            if (sections == null) return;

            for (int x = 0; x < sections.GetLength(0); x++)
            {
                for (int z = 0; z < sections.GetLength(1); z++)
                {
                    Section section = sections[x, z];
                    if (section == null) continue;

                    List<SectionLayer> layers = layersField.GetValue(section) as List<SectionLayer>;
                    if (layers == null) continue;

                    foreach (SectionLayer layer in layers)
                    {
                        if (layer == null) continue;

                        // 跳过迷雾/光照层（子地图的迷雾不应该画到主地图上）
                        string layerName = layer.GetType().Name;
                        if (ShouldSkipLayer(layerName)) continue;

                        foreach (LayerSubMesh subMesh in layer.subMeshes)
                        {
                            if (subMesh.mesh != null && subMesh.material != null &&
                                subMesh.mesh.vertexCount > 0)
                            {
                                Graphics.DrawMesh(
                                    subMesh.mesh,
                                    Matrix4x4.TRS(drawOffset, Quaternion.identity, Vector3.one),
                                    subMesh.material,
                                    0
                                );
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 渲染子地图的动态 Thing（Pawn、物品等）。
        /// </summary>
        public static void DrawLevelDynamicThings(Map levelMap, LevelData level)
        {
            if (levelMap?.listerThings == null) return;

            Vector3 offset = LevelCoordUtility.GetDrawOffset(level);

            foreach (Thing thing in levelMap.listerThings.AllThings)
            {
                if (thing.def.drawerType == DrawerType.None) continue;

                try
                {
                    Vector3 drawLoc = thing.DrawPos + offset;
                    thing.DynamicDrawPhaseAt(DrawPhase.Draw, drawLoc, false);
                }
                catch (Exception)
                {
                    // 静默忽略个别 Thing 的渲染错误
                }
            }
        }

        /// <summary>
        /// 判断是否应该跳过某个 SectionLayer。
        /// </summary>
        private static bool ShouldSkipLayer(string layerName)
        {
            switch (layerName)
            {
                case "SectionLayer_FogOfWar":
                case "SectionLayer_Darkness":
                case "SectionLayer_LightingOverlay":
                case "SectionLayer_Snow":
                    return true;
                default:
                    return false;
            }
        }
    }
}
