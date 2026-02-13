using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// SectionLayer_EdgeShadows.Regenerate 补丁 -
    /// 聚焦层级时，跳过主地图上位于层级 area 内的建筑边缘灰色阴影。
    ///
    /// EdgeShadows 是建筑底下/周围的灰色区域（不是太阳投射阴影）。
    /// 它检查 thing.def.castEdgeShadows 来决定是否渲染。
    /// </summary>
    [HarmonyPatch(typeof(SectionLayer_EdgeShadows), nameof(SectionLayer_EdgeShadows.Regenerate))]
    public static class Patch_EdgeShadows
    {
        private const float InDist = 0.45f;
        private static readonly Color32 Shadowed = new Color32(195, 195, 195, byte.MaxValue);
        private static readonly Color32 Lit = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);

        public static bool Prefix(SectionLayer_EdgeShadows __instance, Section ___section)
        {
            var filter = LevelManager.ActiveRenderFilter;
            if (filter == null) return true;
            if (___section == null) return true;
            if (filter.hostMap != ___section.map) return true;
            if (!___section.CellRect.Overlaps(filter.area)) return true;

            RegenerateFiltered(__instance, ___section, filter);
            return false;
        }

        private static bool HasEdgeShadow(int x, int z, Building[] innerArray,
            CellIndices cellIndices, Map map, CellRect filterArea)
        {
            if (x < 0 || z < 0 || x >= map.Size.x || z >= map.Size.z) return false;
            if (filterArea.Contains(new IntVec3(x, 0, z))) return false;
            var thing = innerArray[cellIndices.CellToIndex(x, z)];
            return thing != null && thing.def.castEdgeShadows;
        }

        private static void RegenerateFiltered(SectionLayer layer, Section section, LevelData filter)
        {
            Map map = section.map;
            Building[] innerArray = map.edificeGrid.InnerArray;
            float alt = AltitudeLayer.Shadows.AltitudeFor();
            CellRect cellRect = new CellRect(section.botLeft.x, section.botLeft.z, 17, 17);
            cellRect.ClipInsideMap(map);

            LayerSubMesh sm = layer.GetSubMesh(MatBases.EdgeShadow);
            sm.Clear(MeshParts.All);
            sm.verts.Capacity = cellRect.Area * 4;
            sm.colors.Capacity = cellRect.Area * 4;
            sm.tris.Capacity = cellRect.Area * 8;

            bool[] corner = new bool[4];
            bool[] card = new bool[4];
            bool[] diagOnly = new bool[4];
            CellIndices ci = map.cellIndices;
            CellRect fa = filter.area;

            for (int i = cellRect.minX; i <= cellRect.maxX; i++)
            {
                for (int j = cellRect.minZ; j <= cellRect.maxZ; j++)
                {
                    if (HasEdgeShadow(i, j, innerArray, ci, map, fa))
                    {
                        // 建筑正下方：完整灰色四边形
                        sm.verts.Add(new Vector3(i, alt, j));
                        sm.verts.Add(new Vector3(i, alt, j + 1));
                        sm.verts.Add(new Vector3(i + 1, alt, j + 1));
                        sm.verts.Add(new Vector3(i + 1, alt, j));
                        sm.colors.Add(Shadowed); sm.colors.Add(Shadowed);
                        sm.colors.Add(Shadowed); sm.colors.Add(Shadowed);
                        int c = sm.verts.Count;
                        sm.tris.Add(c - 4); sm.tris.Add(c - 3); sm.tris.Add(c - 2);
                        sm.tris.Add(c - 4); sm.tris.Add(c - 2); sm.tris.Add(c - 1);
                        continue;
                    }

                    // 非建筑格：检查邻居生成渐变边缘
                    for (int k = 0; k < 4; k++) { corner[k] = false; card[k] = false; diagOnly[k] = false; }

                    IntVec3[] cardDir = GenAdj.CardinalDirectionsAround;
                    for (int k = 0; k < 4; k++)
                    {
                        if (HasEdgeShadow(i + cardDir[k].x, j + cardDir[k].z, innerArray, ci, map, fa))
                        {
                            card[k] = true;
                            corner[(k + 3) % 4] = true;
                            corner[k] = true;
                        }
                    }

                    IntVec3[] diagDir = GenAdj.DiagonalDirectionsAround;
                    for (int l = 0; l < 4; l++)
                    {
                        if (!corner[l] && HasEdgeShadow(i + diagDir[l].x, j + diagDir[l].z, innerArray, ci, map, fa))
                        {
                            corner[l] = true;
                            diagOnly[l] = true;
                        }
                    }

                    // 四个角的渐变三角形（与原版逻辑完全一致）
                    int baseV = sm.verts.Count;
                    EmitCornerGradients(sm, i, j, alt, corner, card, diagOnly, baseV);
                }
            }

            if (sm.verts.Count > 0)
            {
                sm.FinalizeMesh(MeshParts.Verts | MeshParts.Tris | MeshParts.Colors);
            }
        }

        /// <summary>
        /// 生成四个角的渐变三角形，与原版 SectionLayer_EdgeShadows 逻辑一致。
        /// corner[0]=SW, corner[1]=NW, corner[2]=NE, corner[3]=SE
        /// card[0]=W, card[1]=N, card[2]=E, card[3]=S
        /// </summary>
        private static void EmitCornerGradients(LayerSubMesh sm, int i, int j, float alt,
            bool[] corner, bool[] card, bool[] diagOnly, int baseV)
        {
            // Corner 0 (SW corner at i,j)
            if (corner[0])
            {
                if (card[0] || card[1])
                {
                    float dz = card[0] ? InDist : 0f;
                    float dx = card[1] ? InDist : 0f;
                    sm.verts.Add(new Vector3(i, alt, j));
                    sm.colors.Add(Shadowed);
                    sm.verts.Add(new Vector3(i + dx, alt, j + dz));
                    sm.colors.Add(Lit);
                    if (corner[1] && !diagOnly[1])
                    {
                        AddStripTris(sm, sm.verts.Count);
                    }
                }
                else
                {
                    sm.verts.Add(new Vector3(i, alt, j));
                    sm.verts.Add(new Vector3(i, alt, j + InDist));
                    sm.verts.Add(new Vector3(i + InDist, alt, j));
                    AddCornerTriColors(sm);
                }
            }

            // Corner 1 (NW corner at i,j+1)
            if (corner[1])
            {
                if (card[1] || card[2])
                {
                    float dx = card[1] ? InDist : 0f;
                    float dz = card[2] ? -InDist : 0f;
                    sm.verts.Add(new Vector3(i, alt, j + 1));
                    sm.colors.Add(Shadowed);
                    sm.verts.Add(new Vector3(i + dx, alt, j + 1 + dz));
                    sm.colors.Add(Lit);
                    if (corner[2] && !diagOnly[2])
                    {
                        AddStripTris(sm, sm.verts.Count);
                    }
                }
                else
                {
                    sm.verts.Add(new Vector3(i, alt, j + 1));
                    sm.verts.Add(new Vector3(i + InDist, alt, j + 1));
                    sm.verts.Add(new Vector3(i, alt, j + 1 - InDist));
                    AddCornerTriColors(sm);
                }
            }

            // Corner 2 (NE corner at i+1,j+1)
            if (corner[2])
            {
                if (card[2] || card[3])
                {
                    float dz = card[2] ? -InDist : 0f;
                    float dx = card[3] ? -InDist : 0f;
                    sm.verts.Add(new Vector3(i + 1, alt, j + 1));
                    sm.colors.Add(Shadowed);
                    sm.verts.Add(new Vector3(i + 1 + dx, alt, j + 1 + dz));
                    sm.colors.Add(Lit);
                    if (corner[3] && !diagOnly[3])
                    {
                        AddStripTris(sm, sm.verts.Count);
                    }
                }
                else
                {
                    sm.verts.Add(new Vector3(i + 1, alt, j + 1));
                    sm.verts.Add(new Vector3(i + 1, alt, j + 1 - InDist));
                    sm.verts.Add(new Vector3(i + 1 - InDist, alt, j + 1));
                    AddCornerTriColors(sm);
                }
            }

            // Corner 3 (SE corner at i+1,j)
            if (corner[3])
            {
                if (card[3] || card[0])
                {
                    float dx = card[3] ? -InDist : 0f;
                    float dz = card[0] ? InDist : 0f;
                    sm.verts.Add(new Vector3(i + 1, alt, j));
                    sm.colors.Add(Shadowed);
                    sm.verts.Add(new Vector3(i + 1 + dx, alt, j + dz));
                    sm.colors.Add(Lit);
                    if (corner[0] && !diagOnly[0])
                    {
                        AddStripTris(sm, baseV);
                    }
                }
                else
                {
                    sm.verts.Add(new Vector3(i + 1, alt, j));
                    sm.verts.Add(new Vector3(i + 1 - InDist, alt, j));
                    sm.verts.Add(new Vector3(i + 1, alt, j + InDist));
                    AddCornerTriColors(sm);
                }
            }
        }

        /// <summary>
        /// 连接当前渐变条带到下一个角（原版 action2 逻辑）。
        /// </summary>
        private static void AddStripTris(LayerSubMesh sm, int nextIdx)
        {
            sm.tris.Add(sm.verts.Count - 2);
            sm.tris.Add(nextIdx);
            sm.tris.Add(sm.verts.Count - 1);
            sm.tris.Add(sm.verts.Count - 1);
            sm.tris.Add(nextIdx);
            sm.tris.Add(nextIdx + 1);
        }

        /// <summary>
        /// 对角线独立角的三角形（原版 action4 逻辑）。
        /// </summary>
        private static void AddCornerTriColors(LayerSubMesh sm)
        {
            sm.colors.Add(Shadowed);
            sm.colors.Add(Lit);
            sm.colors.Add(Lit);
            sm.tris.Add(sm.verts.Count - 3);
            sm.tris.Add(sm.verts.Count - 2);
            sm.tris.Add(sm.verts.Count - 1);
        }
    }
}