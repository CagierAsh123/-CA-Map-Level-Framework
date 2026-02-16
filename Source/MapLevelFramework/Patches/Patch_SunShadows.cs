using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// SectionLayer_SunShadows.Regenerate 补丁 -
    /// 聚焦层级时，跳过主地图上位于层级 area 内的建筑阴影。
    /// 因为 SectionLayer_SunShadows 是 internal 类，需要手动 patch。
    /// </summary>
    public static class Patch_SunShadows
    {
        private static readonly Color32 LowVertexColor = new Color32(0, 0, 0, 0);
        private static readonly FieldInfo sectionField;
        private static bool loggedOnce;

        static Patch_SunShadows()
        {
            sectionField = typeof(SectionLayer).GetField("section",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (sectionField == null)
                Log.Error("[MLF] Failed to find SectionLayer.section field!");
        }

        private static Section GetSection(SectionLayer layer)
        {
            return sectionField?.GetValue(layer) as Section;
        }

        public static bool Prefix(SectionLayer __instance)
        {
            var filter = LevelManager.ActiveRenderFilter;
            if (filter == null) return true;

            var section = GetSection(__instance);
            if (section == null) return true;
            if (filter.hostMap != section.map) return true;
            if (!LevelManager.OverlapsActiveRenderArea(section.CellRect)) return true;

            if (!loggedOnce)
            {
                Log.Message("[MLF] SunShadows Prefix intercepted regeneration.");
                loggedOnce = true;
            }

            RegenerateFiltered(__instance, section);
            return false;
        }

        /// <summary>
        /// DrawLayer 前缀 - 如果 section 与任何层级区域重叠，直接跳过绘制。
        /// </summary>
        public static bool DrawLayerPrefix(SectionLayer __instance)
        {
            var filter = LevelManager.ActiveRenderFilter;
            if (filter == null) return true;

            var section = GetSection(__instance);
            if (section == null) return true;
            if (filter.hostMap != section.map) return true;

            return !LevelManager.OverlapsActiveRenderArea(section.CellRect);
        }

        private static void RegenerateFiltered(SectionLayer layer, Section section)
        {
            Map map = section.map;
            if (!MatBases.SunShadow.shader.isSupported) return;

            Building[] innerArray = map.edificeGrid.InnerArray;
            float alt = AltitudeLayer.Shadows.AltitudeFor();
            CellRect cellRect = new CellRect(section.botLeft.x, section.botLeft.z, 17, 17);
            cellRect.ClipInsideMap(map);

            LayerSubMesh subMesh = layer.GetSubMesh(MatBases.SunShadow);
            subMesh.Clear(MeshParts.All);
            subMesh.verts.Capacity = cellRect.Area * 2;
            subMesh.tris.Capacity = cellRect.Area * 4;
            subMesh.colors.Capacity = cellRect.Area * 2;

            CellIndices cellIndices = map.cellIndices;

            for (int i = cellRect.minX; i <= cellRect.maxX; i++)
            {
                for (int j = cellRect.minZ; j <= cellRect.maxZ; j++)
                {
                    if (LevelManager.IsInActiveRenderArea(new IntVec3(i, 0, j))) continue;

                    Building building = innerArray[cellIndices.CellToIndex(i, j)];
                    if (building == null || building.def.staticSunShadowHeight <= 0f)
                        continue;

                    float h = building.def.staticSunShadowHeight;
                    Color32 color = new Color32(0, 0, 0, (byte)(255f * h));
                    int count = subMesh.verts.Count;

                    subMesh.verts.Add(new Vector3(i, alt, j));
                    subMesh.verts.Add(new Vector3(i, alt, j + 1));
                    subMesh.verts.Add(new Vector3(i + 1, alt, j + 1));
                    subMesh.verts.Add(new Vector3(i + 1, alt, j));
                    subMesh.colors.Add(LowVertexColor);
                    subMesh.colors.Add(LowVertexColor);
                    subMesh.colors.Add(LowVertexColor);
                    subMesh.colors.Add(LowVertexColor);

                    int c2 = subMesh.verts.Count;
                    subMesh.tris.Add(c2 - 4);
                    subMesh.tris.Add(c2 - 3);
                    subMesh.tris.Add(c2 - 2);
                    subMesh.tris.Add(c2 - 4);
                    subMesh.tris.Add(c2 - 2);
                    subMesh.tris.Add(c2 - 1);

                    if (i > 0)
                    {
                        Building nb = innerArray[cellIndices.CellToIndex(i - 1, j)];
                        if (nb == null || nb.def.staticSunShadowHeight < h)
                        {
                            int c3 = subMesh.verts.Count;
                            subMesh.verts.Add(new Vector3(i, alt, j));
                            subMesh.verts.Add(new Vector3(i, alt, j + 1));
                            subMesh.colors.Add(color);
                            subMesh.colors.Add(color);
                            subMesh.tris.Add(count + 1);
                            subMesh.tris.Add(count);
                            subMesh.tris.Add(c3);
                            subMesh.tris.Add(c3);
                            subMesh.tris.Add(c3 + 1);
                            subMesh.tris.Add(count + 1);
                        }
                    }

                    if (i < map.Size.x - 1)
                    {
                        Building nb = innerArray[cellIndices.CellToIndex(i + 1, j)];
                        if (nb == null || nb.def.staticSunShadowHeight < h)
                        {
                            int c4 = subMesh.verts.Count;
                            subMesh.verts.Add(new Vector3(i + 1, alt, j + 1));
                            subMesh.verts.Add(new Vector3(i + 1, alt, j));
                            subMesh.colors.Add(color);
                            subMesh.colors.Add(color);
                            subMesh.tris.Add(count + 2);
                            subMesh.tris.Add(c4);
                            subMesh.tris.Add(c4 + 1);
                            subMesh.tris.Add(c4 + 1);
                            subMesh.tris.Add(count + 3);
                            subMesh.tris.Add(count + 2);
                        }
                    }

                    if (j > 0)
                    {
                        Building nb = innerArray[cellIndices.CellToIndex(i, j - 1)];
                        if (nb == null || nb.def.staticSunShadowHeight < h)
                        {
                            int c5 = subMesh.verts.Count;
                            subMesh.verts.Add(new Vector3(i, alt, j));
                            subMesh.verts.Add(new Vector3(i + 1, alt, j));
                            subMesh.colors.Add(color);
                            subMesh.colors.Add(color);
                            subMesh.tris.Add(count);
                            subMesh.tris.Add(count + 3);
                            subMesh.tris.Add(c5);
                            subMesh.tris.Add(count + 3);
                            subMesh.tris.Add(c5 + 1);
                            subMesh.tris.Add(c5);
                        }
                    }
                }
            }

            if (subMesh.verts.Count > 0)
            {
                subMesh.FinalizeMesh(MeshParts.Verts | MeshParts.Tris | MeshParts.Colors);
                subMesh.mesh.bounds = new Bounds(Vector3.zero, new Vector3(1000f, 1000f, 1000f));
            }
        }
    }
}