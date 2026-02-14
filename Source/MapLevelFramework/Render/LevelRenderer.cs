using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace MapLevelFramework.Render
{
    /// <summary>
    /// 层级叠加渲染器 - 将子地图的内容渲染到主地图的指定区域上。
    ///
    /// 核心原理：
    /// 1. 获取子地图的 Section mesh 数据
    /// 2. 用 Graphics.DrawMesh + offset 画到主地图对应位置（Y 偏移防止 z-fighting）
    /// 3. 动态 Thing 也通过 offset 重新定位绘制
    /// 4. 主地图在 area 内的动态物体由 Patch_DynamicDrawManager 跳过
    /// </summary>
    public static class LevelRenderer
    {
        // 反射缓存
        private static FieldInfo sectionsField;
        private static FieldInfo layersField;
        private static FieldInfo dirtyFlagsField;

        // 层级内容的 Y 偏移，确保渲染在主地图地形之上
        private const float YOffset = 0.5f;

        // render queue 提升量：确保子地图地形材质在基地图地形之后绘制。
        // RimWorld 地形 shader 通常 ZWrite Off（边缘混合需要），
        // 导致 render queue 高的材质覆盖低的，无视深度。
        private const int RenderQueueElevation = 500;

        // 材质副本缓存：原始材质 → 提升 render queue 后的副本
        private static Dictionary<Material, Material> elevatedMaterials = new Dictionary<Material, Material>();

        // 需要跳过绘制的材质（MLF_OpenAir 等虚空地形，不应覆盖基地图）
        private static HashSet<Material> skipMaterials;

        // 跟踪哪些子地图已完成首次全量重建
        private static HashSet<int> initializedMaps = new HashSet<int>();

        /// <summary>
        /// 清除指定地图的初始化缓存，下一帧会触发 RegenerateEverythingNow。
        /// </summary>
        public static void ClearInitializedMap(int mapId)
        {
            initializedMaps.Remove(mapId);
        }

        static LevelRenderer()
        {
            sectionsField = typeof(MapDrawer).GetField("sections",
                BindingFlags.Instance | BindingFlags.NonPublic);
            layersField = typeof(Section).GetField("layers",
                BindingFlags.Instance | BindingFlags.NonPublic);
            dirtyFlagsField = typeof(Section).GetField("dirtyFlags",
                BindingFlags.Instance | BindingFlags.NonPublic);
        }

        /// <summary>
        /// 更新子地图的 section mesh。
        ///
        /// 关键问题：Game.UpdatePlay 对所有地图调用 MapUpdate → MapMeshDrawerUpdate_First，
        /// 其中 TryUpdate(viewRect) 会清零 dirtyFlags。如果 section 不在 viewRect 内，
        /// layer.Dirty 被设为 true 但不会 Regenerate。而我们的 DrawLevelMapMesh 不走
        /// DrawSection（它会检查 anyLayerDirty 并补充重生成），所以必须手动处理。
        ///
        /// 方案：先处理残留的 dirtyFlags（如果有），再检查每个 layer.Dirty 并重生成。
        /// </summary>
        public static void UpdateLevelMapSections(Map levelMap, LevelData level)
        {
            if (levelMap?.mapDrawer == null) return;

            Section[,] sections = sectionsField?.GetValue(levelMap.mapDrawer) as Section[,];
            if (sections == null) return;

            int mapId = levelMap.uniqueID;

            if (!initializedMaps.Contains(mapId))
            {
                levelMap.mapDrawer.RegenerateEverythingNow();
                initializedMaps.Add(mapId);
                return;
            }

            int lenX = sections.GetLength(0);
            int lenZ = sections.GetLength(1);
            CellRect fullRect = new CellRect(0, 0, levelMap.Size.x, levelMap.Size.z);

            for (int x = 0; x < lenX; x++)
            {
                for (int z = 0; z < lenZ; z++)
                {
                    Section section = sections[x, z];
                    if (section == null || !level.IsSectionActive(x, z)) continue;

                    // 1. 如果还有未处理的 dirtyFlags（理论上 Game.MapUpdate 已处理，但以防万一）
                    if (section.dirtyFlags != 0UL)
                    {
                        section.TryUpdate(fullRect);
                    }

                    // 2. 补充重生成：Game.MapUpdate 的 TryUpdate(viewRect) 可能只标记了
                    //    layer.Dirty 而没有 Regenerate（section 不在相机视野内时）。
                    //    我们的 DrawLevelMapMesh 不走 DrawSection，所以必须在这里处理。
                    List<SectionLayer> layers = layersField?.GetValue(section) as List<SectionLayer>;
                    if (layers != null)
                    {
                        bool anyRegenerated = false;
                        for (int i = 0; i < layers.Count; i++)
                        {
                            if (layers[i].Dirty)
                            {
                                try { layers[i].Regenerate(); }
                                catch (Exception) { }
                                layers[i].Dirty = false;
                                anyRegenerated = true;
                            }
                        }
                        if (anyRegenerated)
                        {
                            for (int i = 0; i < layers.Count; i++)
                            {
                                layers[i].RefreshSubMeshBounds();
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 初始化需要跳过的材质集合（延迟初始化，等 DefDatabase 就绪）。
        /// </summary>
        private static void EnsureSkipMaterialsInit()
        {
            if (skipMaterials != null) return;
            skipMaterials = new HashSet<Material>();
            TerrainDef openAir = DefDatabase<TerrainDef>.GetNamedSilentFail("MLF_OpenAir");
            if (openAir?.DrawMatSingle != null) skipMaterials.Add(openAir.DrawMatSingle);
        }

        /// <summary>
        /// 获取提升 render queue 的材质副本。
        /// RimWorld 地形 shader 通常 ZWrite Off，render queue 决定绘制顺序。
        /// 子地图材质需要更高的 queue 才能覆盖基地图地形。
        /// </summary>
        private static Material GetElevatedMaterial(Material original)
        {
            if (original == null) return null;
            if (!elevatedMaterials.TryGetValue(original, out Material elevated))
            {
                elevated = new Material(original);
                elevated.renderQueue = original.renderQueue + RenderQueueElevation;
                elevatedMaterials[original] = elevated;
            }
            return elevated;
        }

        /// <summary>
        /// 渲染子地图的静态 mesh。
        /// 只绘制包含 usableCell 的 section，精确避免间隙区域的 OpenAir 覆盖基地图。
        /// 使用提升 render queue 的材质副本，确保子地图地形覆盖基地图地形。
        /// </summary>
        public static void DrawLevelMapMesh(Map levelMap, LevelData level)
        {
            if (levelMap?.mapDrawer == null) return;
            if (sectionsField == null || layersField == null) return;

            EnsureSkipMaterialsInit();
            Vector3 drawOffset = new Vector3(0f, YOffset, 0f);

            Section[,] sections = sectionsField.GetValue(levelMap.mapDrawer) as Section[,];
            if (sections == null) return;

            for (int x = 0; x < sections.GetLength(0); x++)
            {
                for (int z = 0; z < sections.GetLength(1); z++)
                {
                    Section section = sections[x, z];
                    if (section == null) continue;
                    if (!level.IsSectionActive(x, z)) continue;

                    List<SectionLayer> layers = layersField.GetValue(section) as List<SectionLayer>;
                    if (layers == null) continue;

                    foreach (SectionLayer layer in layers)
                    {
                        if (layer == null) continue;

                        string layerName = layer.GetType().Name;
                        if (ShouldSkipLayer(layerName)) continue;

                        foreach (LayerSubMesh subMesh in layer.subMeshes)
                        {
                            if (!subMesh.finalized || subMesh.disabled) continue;
                            if (subMesh.mesh == null || subMesh.material == null ||
                                subMesh.mesh.vertexCount <= 0) continue;

                            // 跳过 MLF_OpenAir 等虚空地形材质，不覆盖基地图
                            if (skipMaterials != null && skipMaterials.Contains(subMesh.material))
                                continue;

                            Graphics.DrawMesh(
                                subMesh.mesh,
                                Matrix4x4.TRS(drawOffset, Quaternion.identity, Vector3.one),
                                GetElevatedMaterial(subMesh.material),
                                0
                            );
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 渲染子地图的动态 Thing。
        /// 只绘制 usableCells 内的 Thing。
        /// </summary>
        public static void DrawLevelDynamicThings(Map levelMap, LevelData level)
        {
            if (levelMap?.dynamicDrawManager == null) return;

            IReadOnlyList<Thing> drawThings = levelMap.dynamicDrawManager.DrawThings;
            int count = drawThings.Count;
            if (count == 0) return;

            var usable = level.usableCells;

            for (int i = 0; i < count; i++)
            {
                Thing thing = drawThings[i];
                try
                {
                    if (thing == null || thing.Destroyed) continue;
                    if (usable != null && !usable.Contains(thing.Position)) continue;
                    if (usable == null && !level.area.Contains(thing.Position)) continue;

                    thing.DynamicDrawPhase(DrawPhase.EnsureInitialized);
                    thing.DynamicDrawPhase(DrawPhase.ParallelPreDraw);

                    Vector3 drawLoc = thing.DrawPos;
                    drawLoc.y += YOffset;
                    thing.DynamicDrawPhaseAt(DrawPhase.Draw, drawLoc, false);

                    Pawn pawn = thing as Pawn;
                    if (pawn != null)
                    {
                        pawn.DrawShadowAt(drawLoc);
                    }
                }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// 渲染子地图的覆盖层：designations、overlays、flecks 等。
        /// </summary>
        public static void DrawLevelOverlays(Map levelMap)
        {
            if (levelMap == null) return;

            try
            {
                levelMap.designationManager?.DrawDesignations();
            }
            catch (Exception) { }

            try
            {
                levelMap.overlayDrawer?.DrawAllOverlays();
            }
            catch (Exception) { }

            try
            {
                levelMap.flecks?.FleckManagerDraw();
            }
            catch (Exception) { }

            try
            {
                levelMap.temporaryThingDrawer?.Draw();
            }
            catch (Exception) { }
        }

        // ---- 边界高亮 ----
        private static List<IntVec3> edgeCellsCache;
        private static int lastUsableCellsCount = -1;

        /// <summary>
        /// 绘制楼层边缘高亮边框。
        /// </summary>
        public static void DrawLevelBoundaryOverlay(LevelData level, Map baseMap)
        {
            var usable = level.usableCells;
            if (usable == null || usable.Count == 0) return;

            if (edgeCellsCache == null || lastUsableCellsCount != usable.Count)
            {
                edgeCellsCache = new List<IntVec3>(usable);
                lastUsableCellsCount = usable.Count;
            }
            GenDraw.DrawFieldEdges(edgeCellsCache, new Color(0.2f, 0.8f, 1f, 0.6f));
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
