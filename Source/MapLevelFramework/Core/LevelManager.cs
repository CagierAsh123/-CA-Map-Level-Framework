using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MapLevelFramework
{
    /// <summary>
    /// 层级管理器 - 挂在主地图上的 MapComponent。
    /// 管理该地图上所有层级子地图的创建、切换、销毁。
    /// 
    /// 类比 VMF 中 VehiclePawnWithMap 持有 interiorMap 的角色，
    /// 但这里是一个 MapComponent，可以管理多个层级。
    /// </summary>
    public class LevelManager : MapComponent, IExposable
    {
        // ========== 层级数据 ==========

        /// <summary>
        /// 最大层数限制（含地面层）。
        /// 受渲染深度约束：Building Y = 5.988 + levelIndex × 0.5，
        /// MetaOverlays Y = 14.268，levelIndex 最大 16（即 18F）。
        /// 超过此限制，楼层内容会在深度缓冲中遮挡 GUI overlay。
        /// </summary>
        public const int MaxTotalFloors = 18;
        // 最大子层级数 = MaxTotalFloors - 1（地面层不算）
        private const int MaxSubLevels = MaxTotalFloors - 1;

        /// <summary>
        /// 所有已创建的层级，按 elevation 索引。
        /// elevation 0 = 主地图（不在此字典中）。
        /// </summary>
        private Dictionary<int, LevelData> levels = new Dictionary<int, LevelData>();

        /// <summary>
        /// 当前聚焦的层级 elevation。0 = 主地图（地面）。
        /// </summary>
        private int focusedElevation = 0;

        // ========== 渲染过滤 ==========

        /// <summary>
        /// 当前激活的渲染过滤层级（最高层）。非 null 时，Thing.Print 和 Thing.DynamicDrawPhase
        /// 补丁会跳过该层级 area 内的主地图物体。
        /// 由 FocusLevel() 设置/清除。
        /// </summary>
        internal static LevelData ActiveRenderFilter;

        /// <summary>
        /// 所有需要渲染的层级（从低到高排列）。
        /// 聚焦 3F 时包含 [2F, 3F]，聚焦 2F 时包含 [2F]。
        /// 由 FocusLevel() 设置/清除。
        /// </summary>
        internal static List<LevelData> ActiveRenderLevels;

        // ========== 属性 ==========

        /// <summary>
        /// 当前聚焦的 elevation。
        /// </summary>
        public int FocusedElevation => focusedElevation;

        /// <summary>
        /// 当前聚焦的层级 Map。如果聚焦地面则返回主地图。
        /// </summary>
        public Map FocusedMap
        {
            get
            {
                if (focusedElevation == 0) return map;
                if (levels.TryGetValue(focusedElevation, out var data) && data.LevelMap != null)
                    return data.LevelMap;
                return map;
            }
        }

        /// <summary>
        /// 是否正在聚焦某个非地面层级。
        /// </summary>
        public bool IsFocusingLevel => focusedElevation != 0;

        /// <summary>
        /// 所有已注册的层级 elevation 列表（不含 0）。
        /// </summary>
        public IEnumerable<int> AllElevations => levels.Keys.OrderBy(e => e);

        /// <summary>
        /// 所有已注册的层级数据。
        /// </summary>
        public IEnumerable<LevelData> AllLevels => levels.Values;

        // ========== 构造 ==========

        public LevelManager(Map map) : base(map) { }

        // ========== 公共 API ==========

        /// <summary>
        /// 注册一个新层级。如果该 elevation 已存在则返回已有数据。
        /// </summary>
        /// <param name="elevation">层级高度（非0）</param>
        /// <param name="area">该层级在主地图上覆盖的区域</param>
        /// <param name="levelDef">层级定义（可选）</param>
        /// <param name="mapSize">子地图尺寸（可选，默认使用 area 的包围矩形）</param>
        /// <param name="isUnderground">是否为地下层</param>
        /// <returns>层级数据</returns>
        public LevelData RegisterLevel(int elevation, CellRect area, LevelDef levelDef = null, IntVec3? mapSize = null, bool isUnderground = false)
        {
            if (elevation == 0)
            {
                Log.Error("[MapLevelFramework] Cannot register elevation 0 (reserved for ground level).");
                return null;
            }

            if (levels.Count >= MaxSubLevels)
            {
                Messages.Message(
                    $"[MLF] 最大 {MaxTotalFloors} 层，不服找泰南，再往上就突破天道了",
                    MessageTypeDefOf.RejectInput, false);
                Log.Warning($"[MapLevelFramework] Cannot register elevation {elevation}: " +
                            $"max {MaxTotalFloors} floors reached (rendering depth limit).");
                return null;
            }

            if (levels.TryGetValue(elevation, out var existing))
            {
                Log.Warning($"[MapLevelFramework] Elevation {elevation} already registered, returning existing.");
                return existing;
            }

            var data = new LevelData
            {
                elevation = elevation,
                area = area,
                levelDef = levelDef,
                hostMap = map,
                isUnderground = isUnderground
            };

            // 子地图与基地图同尺寸，坐标系完全一致，无需任何坐标转换
            IntVec3 size = map.Size;

            // 生成子地图
            try
            {
                var levelMap = GenerateLevelMap(data, size);
                if (levelMap == null)
                {
                    Log.Error($"[MapLevelFramework] GenerateLevelMap returned null for elevation {elevation}.");
                    return null;
                }
                levels[elevation] = data;
                Log.Message($"[MapLevelFramework] Registered level elevation={elevation}, " +
                            $"area={area}, mapSize={size}, tag={levelDef?.levelTag ?? "none"}");
            }
            catch (Exception ex)
            {
                Log.Error($"[MapLevelFramework] Failed to generate level map for elevation {elevation}: {ex}");
                return null;
            }

            return data;
        }

        /// <summary>
        /// 获取指定 elevation 的层级数据。
        /// </summary>
        public LevelData GetLevel(int elevation)
        {
            levels.TryGetValue(elevation, out var data);
            return data;
        }

        /// <summary>
        /// 获取指定 Map 对应的层级数据（如果该 Map 是某个层级的子地图）。
        /// </summary>
        public LevelData GetLevelForMap(Map levelMap)
        {
            return levels.Values.FirstOrDefault(d => d.LevelMap == levelMap);
        }

        /// <summary>
        /// 切换聚焦到指定层级。0 = 回到地面。
        /// 地下层（elevation &lt; 0）：直接切换 CurrentMap 到子地图，不走叠加渲染。
        /// 上层（elevation &gt; 0）：保持基地图为 CurrentMap，叠加渲染子地图内容。
        /// </summary>
        public void FocusLevel(int elevation)
        {
            if (elevation != 0 && !levels.ContainsKey(elevation))
            {
                Log.Warning($"[MapLevelFramework] Elevation {elevation} not registered.");
                return;
            }

            int old = focusedElevation;
            if (old == elevation) return; // 防止重复切换

            // 切换前：标记所有之前渲染的层级 area 为脏（恢复建筑显示）
            if (ActiveRenderLevels != null)
            {
                foreach (var lvl in ActiveRenderLevels)
                {
                    MarkAreaSectionsDirty(lvl.area);
                    MarkLevelMapSectionsDirty(lvl);
                }
            }
            else if (old != 0 && levels.TryGetValue(old, out var oldLevel))
            {
                MarkAreaSectionsDirty(oldLevel.area);
                MarkLevelMapSectionsDirty(oldLevel);
            }

            focusedElevation = elevation;

            // 地下层：直接切换 CurrentMap，不需要叠加渲染
            if (elevation < 0 && levels.TryGetValue(elevation, out var undergroundLevel) && undergroundLevel.isUnderground)
            {
                ActiveRenderFilter = null;
                ActiveRenderLevels = null;

                if (undergroundLevel.LevelMap != null)
                {
                    // 允许切换到子地图（Patch_Game_CurrentMap 会检查 isUnderground）
                    Current.Game.CurrentMap = undergroundLevel.LevelMap;
                }

                Log.Message($"[MapLevelFramework] Focus switched to underground: {old} -> {elevation}");
                return;
            }

            // 从地下层切回地面或上层：恢复 CurrentMap 为基地图
            if (old < 0 && levels.TryGetValue(old, out var oldUnderground) && oldUnderground.isUnderground)
            {
                Current.Game.CurrentMap = map;
            }

            // 上层或地面：更新渲染过滤器
            if (elevation != 0 && levels.TryGetValue(elevation, out var newLevel))
            {
                ActiveRenderFilter = newLevel;
                // 收集所有 <= 聚焦层的层级，按 elevation 升序
                var renderLevels = new List<LevelData>();
                foreach (int elev in AllElevations)
                {
                    if (elev > 0 && elev <= elevation) // 只收集上层
                    {
                        var lvl = GetLevel(elev);
                        if (lvl != null) renderLevels.Add(lvl);
                    }
                }
                ActiveRenderLevels = renderLevels;
                foreach (var lvl in renderLevels)
                {
                    MarkAreaSectionsDirty(lvl.area);
                    MarkLevelMapSectionsDirty(lvl);
                }
            }
            else
            {
                ActiveRenderFilter = null;
                ActiveRenderLevels = null;
            }

            Log.Message($"[MapLevelFramework] Focus switched: {old} -> {elevation}");

            // 标记屋顶覆盖层为脏，让 GetCellBool 补丁生效
            map.roofGrid.Drawer.SetDirty();
        }

        /// <summary>
        /// 标记主地图上指定区域内的 section 为脏，触发重新生成。
        /// 这会导致 SectionLayer_Things 重新调用 Thing.Print，
        /// 届时我们的补丁会根据 ActiveRenderFilter 决定是否跳过。
        /// </summary>
        private void MarkAreaSectionsDirty(CellRect area)
        {
            if (map?.mapDrawer == null) return;

            ulong dirtyFlags = MapMeshFlagDefOf.Things | MapMeshFlagDefOf.Buildings
                             | MapMeshFlagDefOf.BuildingsDamage | MapMeshFlagDefOf.Zone;

            foreach (IntVec3 cell in area)
            {
                if (cell.InBounds(map))
                {
                    map.mapDrawer.MapMeshDirty(cell, dirtyFlags);
                }
            }
        }

        /// <summary>
        /// 标记子地图上可用区域内的 section 为脏，触发重新生成。
        /// 这会导致子地图的 SectionLayer_ThingsGeneral 重新调用 TakePrintFrom，
        /// 届时补丁会根据 IsCoveredByHigherLevel 决定是否跳过被覆盖的建筑/物品。
        /// </summary>
        private void MarkLevelMapSectionsDirty(LevelData level)
        {
            Map levelMap = level?.LevelMap;
            if (levelMap?.mapDrawer == null) return;

            ulong dirtyFlags = MapMeshFlagDefOf.Things | MapMeshFlagDefOf.Buildings
                             | MapMeshFlagDefOf.BuildingsDamage;

            if (level.usableCells != null)
            {
                foreach (IntVec3 cell in level.usableCells)
                {
                    if (cell.InBounds(levelMap))
                        levelMap.mapDrawer.MapMeshDirty(cell, dirtyFlags);
                }
            }
            else
            {
                foreach (IntVec3 cell in level.area)
                {
                    if (cell.InBounds(levelMap))
                        levelMap.mapDrawer.MapMeshDirty(cell, dirtyFlags);
                }
            }
        }

        /// <summary>
        /// 移除一个层级及其子地图。
        /// </summary>
        public void RemoveLevel(int elevation)
        {
            if (!levels.TryGetValue(elevation, out var data))
                return;

            if (focusedElevation == elevation)
                FocusLevel(0);

            // 清理子地图
            if (data.LevelMap != null)
            {
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    if (data.mapParent != null)
                    {
                        data.mapParent.sourceMap = null;
                        Find.World.pocketMaps.Remove(data.mapParent);
                    }
                    if (Find.Maps.Contains(data.LevelMap))
                    {
                        Current.Game.DeinitAndRemoveMap(data.LevelMap, false);
                    }
                });
            }

            levels.Remove(elevation);
            Log.Message($"[MapLevelFramework] Removed level elevation={elevation}");
        }

        // ========== 静态查询 ==========

        /// <summary>
        /// 获取指定地图的 LevelManager（如果存在）。
        /// </summary>
        public static LevelManager GetManager(Map map)
        {
            return map?.GetComponent<LevelManager>();
        }

        /// <summary>
        /// 判断一个 Map 是否是某个层级的子地图。
        /// </summary>
        public static bool IsLevelMap(Map map, out LevelManager manager, out LevelData levelData)
        {
            manager = null;
            levelData = null;

            if (map?.Parent is LevelMapParent lmp && lmp.hostManager != null)
            {
                manager = lmp.hostManager;
                levelData = manager.GetLevelForMap(map);
                return levelData != null;
            }
            return false;
        }

        /// <summary>
        /// 获取当前应该用于交互的 Map。
        /// 聚焦层级时始终返回子地图，否则返回主地图。
        /// </summary>
        public static Map CurrentInteractionMap
        {
            get
            {
                var baseMap = Find.CurrentMap;
                if (baseMap == null) return null;

                var mgr = GetManager(baseMap);
                if (mgr == null || !mgr.IsFocusingLevel) return baseMap;

                var level = mgr.GetLevel(mgr.FocusedElevation);
                if (level?.LevelMap == null) return baseMap;

                return level.LevelMap;
            }
        }

        /// <summary>
        /// 检查基地图坐标是否在任何活跃渲染层级的区域内。
        /// 用于替代 ActiveRenderFilter.ContainsBaseMapCell，支持多层遮挡。
        /// </summary>
        public static bool IsInActiveRenderArea(IntVec3 baseCell)
        {
            var levels = ActiveRenderLevels;
            if (levels == null) return false;
            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i].ContainsBaseMapCell(baseCell)) return true;
            }
            return false;
        }

        /// <summary>
        /// 获取覆盖指定基地图坐标的最高层级（用于屋顶等需要取最高层数据的场景）。
        /// </summary>
        public static LevelData GetTopmostLevelAt(IntVec3 baseCell)
        {
            var levels = ActiveRenderLevels;
            if (levels == null) return null;
            for (int i = levels.Count - 1; i >= 0; i--)
            {
                if (levels[i].ContainsBaseMapCell(baseCell)) return levels[i];
            }
            return null;
        }

        /// <summary>
        /// 检查指定 CellRect 是否与任何活跃渲染层级的区域重叠。
        /// </summary>
        public static bool OverlapsActiveRenderArea(CellRect rect)
        {
            var levels = ActiveRenderLevels;
            if (levels == null) return false;
            for (int i = 0; i < levels.Count; i++)
            {
                if (rect.Overlaps(levels[i].area)) return true;
            }
            return false;
        }

        /// <summary>
        /// 检查子地图上的某个格子是否被更高层级覆盖。
        /// 用于 TakePrintFrom 补丁：中间层的建筑/物品在被高层覆盖时不应烘焙进 mesh。
        /// </summary>
        public static bool IsCoveredByHigherLevel(Map subMap, IntVec3 pos)
        {
            var renderLevels = ActiveRenderLevels;
            if (renderLevels == null || renderLevels.Count < 2) return false;

            // 找到该子地图对应的层级索引
            int levelIdx = -1;
            for (int i = 0; i < renderLevels.Count; i++)
            {
                if (renderLevels[i].LevelMap == subMap)
                {
                    levelIdx = i;
                    break;
                }
            }
            // 不在渲染列表中，或者是最高层（聚焦层）→ 不被覆盖
            if (levelIdx < 0 || levelIdx >= renderLevels.Count - 1) return false;

            // 检查是否被任何更高层级覆盖
            for (int j = levelIdx + 1; j < renderLevels.Count; j++)
            {
                if (renderLevels[j].ContainsBaseMapCell(pos)) return true;
            }
            return false;
        }

        // ========== 生命周期 ==========

        public override void FinalizeInit()
        {
            base.FinalizeInit();

            // 加载存档后恢复 hostManager 关联（LevelMapParent 不序列化 hostManager）
            foreach (var data in levels.Values)
            {
                if (data.mapParent != null)
                {
                    data.mapParent.hostManager = this;
                }

                // 确保地下层子地图有 UndergroundMapComponent
                if (data.isUnderground && data.LevelMap != null)
                {
                    bool hasComp = false;
                    foreach (var comp in data.LevelMap.components)
                    {
                        if (comp is UndergroundMapComponent) { hasComp = true; break; }
                    }
                    if (!hasComp)
                    {
                        var comp = new UndergroundMapComponent(data.LevelMap);
                        data.LevelMap.components.Add(comp);
                        comp.FinalizeInit();
                    }
                }
            }

            // 修复旧存档的 underGrid（确保可用区域的底层是 MLF_LevelBase）
            RepairUnderGrid();

            // 加载存档后恢复渲染过滤器
            if (focusedElevation != 0 && levels.TryGetValue(focusedElevation, out var focusData))
            {
                if (focusData.isUnderground)
                {
                    // 地下层：不需要渲染过滤器，CurrentMap 已经是子地图
                    ActiveRenderFilter = null;
                    ActiveRenderLevels = null;
                }
                else
                {
                    ActiveRenderFilter = focusData;
                    var renderLevels = new List<LevelData>();
                    foreach (int elev in AllElevations)
                    {
                        if (elev > 0 && elev <= focusedElevation)
                        {
                            var lvl = GetLevel(elev);
                            if (lvl != null) renderLevels.Add(lvl);
                        }
                    }
                    ActiveRenderLevels = renderLevels;
                    foreach (var lvl in renderLevels)
                    {
                        MarkAreaSectionsDirty(lvl.area);
                        MarkLevelMapSectionsDirty(lvl);
                    }
                }
            }
        }

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            if (levels.Count > 0)
            {
                Gui.LevelSwitcherUI.DrawLevelSwitcher(this);
            }
        }

        // ========== 内部方法 ==========

        /// <summary>
        /// 修复旧存档中 underGrid 未正确设置为 MLF_LevelBase 的问题。
        /// 在 FinalizeInit 中调用，确保所有层级子地图的可用区域底层正确。
        /// </summary>
        private void RepairUnderGrid()
        {
            TerrainDef levelBase = DefDatabase<TerrainDef>.GetNamedSilentFail("MLF_LevelBase");
            if (levelBase == null) return;

            var underGridField = typeof(TerrainGrid).GetField("underGrid",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (underGridField == null) return;

            TerrainDef openAir = DefDatabase<TerrainDef>.GetNamedSilentFail("MLF_OpenAir");

            foreach (var level in levels.Values)
            {
                Map levelMap = level.LevelMap;
                if (levelMap == null) continue;

                TerrainDef[] underGrid = underGridField.GetValue(levelMap.terrainGrid) as TerrainDef[];
                if (underGrid == null) continue;

                int fixedCount = 0;
                foreach (IntVec3 cell in levelMap.AllCells)
                {
                    if (level.usableCells != null && !level.usableCells.Contains(cell)) continue;
                    if (!level.area.Contains(cell)) continue;

                    int idx = levelMap.cellIndices.CellToIndex(cell);
                    TerrainDef top = levelMap.terrainGrid.TerrainAt(cell);

                    // 可行走地板的底层应该是 LevelBase
                    if (top != openAir && top.passability != Traversability.Impassable
                        && underGrid[idx] != levelBase)
                    {
                        underGrid[idx] = levelBase;
                        fixedCount++;
                    }
                }

                if (fixedCount > 0)
                    Log.Message($"[MLF] Repaired {fixedCount} underGrid cells on level {level.elevation}");
            }
        }

        private Map GenerateLevelMap(LevelData data, IntVec3 size)
        {
            // 创建 MapParent
            var parentDef = DefDatabase<WorldObjectDef>.GetNamedSilentFail("MLF_LevelMap");
            if (parentDef == null)
            {
                Log.Error("[MapLevelFramework] WorldObjectDef 'MLF_LevelMap' not found. Using fallback.");
                parentDef = WorldObjectDefOf.PocketMap;
            }

            var mapParent = (LevelMapParent)WorldObjectMaker.MakeWorldObject(parentDef);
            mapParent.hostManager = this;
            mapParent.levelDef = data.levelDef;
            mapParent.elevation = data.elevation;
            mapParent.area = data.area;
            mapParent.Tile = 0;
            mapParent.sourceMap = map;

            // 根据是否地下层选择不同的 MapGenerator
            string genDefName = data.isUnderground
                ? "MLF_UndergroundMapGenerator"
                : "MLF_LevelMapGenerator";
            var genDef = DefDatabase<MapGeneratorDef>.GetNamedSilentFail(genDefName);
            if (genDef == null)
            {
                Log.Error($"[MapLevelFramework] MapGeneratorDef '{genDefName}' not found.");
                return null;
            }
            mapParent.mapGenerator = genDef;

            data.mapParent = mapParent;

            // 生成地图（参照 VMF 的 GenerateVehicleMap）
            Map levelMap = MapGenerator.GenerateMap(size, mapParent, genDef,
                mapParent.ExtraGenStepDefs, null, true, false);
            Find.World.pocketMaps.Add(mapParent);

            // 清除子地图迷雾（层级地图不需要战争迷雾）
            ClearFog(levelMap);

            // 共享天气/光照（地下层不共享）
            if (!data.isUnderground && (data.levelDef == null || data.levelDef.shareWeather))
            {
                ShareEnvironment(levelMap, map);
            }

            // 地下层：添加 UndergroundMapComponent 用于绘制层级切换 UI
            if (data.isUnderground)
            {
                var comp = new UndergroundMapComponent(levelMap);
                levelMap.components.Add(comp);
                comp.FinalizeInit();
            }

            return levelMap;
        }

        private void ShareEnvironment(Map levelMap, Map parentMap)
        {
            try
            {
                levelMap.weatherDecider = parentMap.weatherDecider;
                levelMap.weatherManager = parentMap.weatherManager;
                levelMap.skyManager = parentMap.skyManager;
                levelMap.gameConditionManager = parentMap.gameConditionManager;
            }
            catch (Exception ex)
            {
                Log.Warning($"[MapLevelFramework] Failed to share environment: {ex.Message}");
            }
        }

        private void ClearFog(Map levelMap)
        {
            if (levelMap?.fogGrid == null) return;
            foreach (IntVec3 cell in levelMap.AllCells)
            {
                if (cell.InBounds(levelMap) && levelMap.fogGrid.IsFogged(cell))
                {
                    levelMap.fogGrid.Unfog(cell);
                }
            }
        }

        // ========== 序列化 ==========

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref focusedElevation, "focusedElevation", 0);

            // 序列化层级数据
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var elevationList = levels.Keys.ToList();
                var dataList = levels.Values.ToList();
                Scribe_Collections.Look(ref elevationList, "elevations", LookMode.Value);
                Scribe_Collections.Look(ref dataList, "levelDatas", LookMode.Deep);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                var elevationList = new List<int>();
                var dataList = new List<LevelData>();
                Scribe_Collections.Look(ref elevationList, "elevations", LookMode.Value);
                Scribe_Collections.Look(ref dataList, "levelDatas", LookMode.Deep);

                levels.Clear();
                if (elevationList != null && dataList != null)
                {
                    for (int i = 0; i < elevationList.Count && i < dataList.Count; i++)
                    {
                        dataList[i].hostMap = map;
                        levels[elevationList[i]] = dataList[i];
                    }
                }
            }
        }
    }
}
