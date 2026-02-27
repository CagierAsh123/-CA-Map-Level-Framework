using System;
using System.Collections.Generic;
using HarmonyLib;
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
        // 编译后的字段访问器（替代 FieldInfo 反射）
        private static readonly AccessTools.FieldRef<MapDrawer, Section[,]> sectionsRef =
            AccessTools.FieldRefAccess<MapDrawer, Section[,]>("sections");
        private static readonly AccessTools.FieldRef<Section, List<SectionLayer>> layersRef =
            AccessTools.FieldRefAccess<Section, List<SectionLayer>>("layers");

        // 需要跳过的 SectionLayer 类型（替代 GetType().Name 字符串比较）
        private static readonly HashSet<Type> skipLayerTypes = new HashSet<Type>
        {
            typeof(SectionLayer_FogOfWar),
            typeof(SectionLayer_Snow),
            typeof(SectionLayer_ThingsPowerGrid), // 电力覆盖层（蓝色线条）
        };

        static LevelRenderer()
        {
            // 尝试添加可能不存在的类型
            var darkness = AccessTools.TypeByName("Verse.SectionLayer_Darkness");
            if (darkness != null) skipLayerTypes.Add(darkness);
            var lighting = AccessTools.TypeByName("Verse.SectionLayer_LightingOverlay");
            if (lighting != null) skipLayerTypes.Add(lighting);
        }
        private const float YOffset = 0.5f;

        // 每层额外的 Y 偏移，确保高层在深度缓冲中覆盖低层
        // 需要足够大以分离不同楼层的深度（AltitudeLayer 间距 ≈ 0.366）
        private const float YOffsetPerLevel = 0.5f;

        // render queue 基础提升量：确保子地图材质在基地图之后绘制
        // 需要足够大以覆盖 renderPrecedence 差异（原版自然地形最高约 380）
        private const int RenderQueueElevation = 500;

        // 每层额外的 render queue 提升量
        // 保持极小值，主要依靠 Y 偏移（深度缓冲）处理层间遮挡
        // 10 × 20层 = 200，加上基础 100 = 300，原始材质最高约 2600
        // 总计 2900，刚好不超过 GUI overlay 最低值 2900
        private const int RenderQueuePerLevel = 10;

        // 材质副本缓存：(原始材质, 层级序号) → 提升 render queue 后的副本
        private static Dictionary<(Material, int), Material> elevatedMaterials = new Dictionary<(Material, int), Material>();

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

            Section[,] sections = sectionsRef(levelMap.mapDrawer);
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
                    List<SectionLayer> layers = layersRef(section);
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
        /// 每个层级使用不同的 queue 偏移，确保高层覆盖低层。
        /// </summary>
        private static Material GetElevatedMaterial(Material original, int levelIndex = 0)
        {
            if (original == null) return null;
            var key = (original, levelIndex);
            if (!elevatedMaterials.TryGetValue(key, out Material elevated))
            {
                elevated = new Material(original);
                elevated.renderQueue = original.renderQueue + RenderQueueElevation + levelIndex * RenderQueuePerLevel;
                elevatedMaterials[key] = elevated;
            }
            return elevated;
        }

        /// <summary>
        /// 渲染子地图的静态 mesh。
        /// levelIndex 用于 render queue 分层：高层级用更高的 queue 确保覆盖低层级。
        /// 注意：被高层覆盖的建筑/物品已在 TakePrintFrom 补丁中从 mesh 移除，
        /// 这里不需要额外的 section 级别排除。
        /// </summary>
        public static void DrawLevelMapMesh(Map levelMap, LevelData level, int levelIndex = 0)
        {
            if (levelMap?.mapDrawer == null) return;
            if (sectionsRef == null || layersRef == null) return;

            EnsureSkipMaterialsInit();
            float yOff = YOffset + levelIndex * YOffsetPerLevel;
            Vector3 drawOffset = new Vector3(0f, yOff, 0f);

            Section[,] sections = sectionsRef(levelMap.mapDrawer);
            if (sections == null) return;

            for (int x = 0; x < sections.GetLength(0); x++)
            {
                for (int z = 0; z < sections.GetLength(1); z++)
                {
                    Section section = sections[x, z];
                    if (section == null) continue;
                    if (!level.IsSectionActive(x, z)) continue;

                    List<SectionLayer> layers = layersRef(section);
                    if (layers == null) continue;

                    foreach (SectionLayer layer in layers)
                    {
                        if (layer == null) continue;

                        if (skipLayerTypes.Contains(layer.GetType())) continue;

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
                                GetElevatedMaterial(subMesh.material, levelIndex),
                                0
                            );
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 渲染子地图的动态 Thing。
        /// 只绘制 usableCells 内的 Thing，跳过被高层覆盖的格子。
        /// </summary>
        public static void DrawLevelDynamicThings(Map levelMap, LevelData level, int levelIndex = 0, HashSet<IntVec3> excludeCells = null)
        {
            if (levelMap?.dynamicDrawManager == null) return;

            IReadOnlyList<Thing> drawThings = levelMap.dynamicDrawManager.DrawThings;
            int count = drawThings.Count;
            if (count == 0) return;

            var usable = level.usableCells;
            float yOff = YOffset + levelIndex * YOffsetPerLevel;

            for (int i = 0; i < count; i++)
            {
                Thing thing = drawThings[i];
                try
                {
                    if (thing == null || thing.Destroyed) continue;
                    if (usable != null && !usable.Contains(thing.Position)) continue;
                    if (usable == null && !level.area.Contains(thing.Position)) continue;
                    // 跳过被高层覆盖的格子（中间层的 pawn/item 不应透过高层地板显示）
                    if (excludeCells != null && excludeCells.Contains(thing.Position)) continue;

                    thing.DynamicDrawPhase(DrawPhase.EnsureInitialized);
                    thing.DynamicDrawPhase(DrawPhase.ParallelPreDraw);

                    Vector3 drawLoc = thing.DrawPos;
                    drawLoc.y += yOff;
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
    }
}
