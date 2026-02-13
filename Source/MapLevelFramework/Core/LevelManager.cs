using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
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
        /// 所有已创建的层级，按 elevation 索引。
        /// elevation 0 = 主地图（不在此字典中）。
        /// </summary>
        private Dictionary<int, LevelData> levels = new Dictionary<int, LevelData>();

        /// <summary>
        /// 当前聚焦的层级 elevation。0 = 主地图（地面）。
        /// </summary>
        private int focusedElevation = 0;

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
        /// <returns>层级数据</returns>
        public LevelData RegisterLevel(int elevation, CellRect area, LevelDef levelDef = null, IntVec3? mapSize = null)
        {
            if (elevation == 0)
            {
                Log.Error("[MapLevelFramework] Cannot register elevation 0 (reserved for ground level).");
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
                hostMap = map
            };

            // 确定子地图尺寸
            IntVec3 size = mapSize ?? new IntVec3(area.Width, 1, area.Height);

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

            focusedElevation = elevation;
            Log.Message($"[MapLevelFramework] Focus switched: {old} -> {elevation}");
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
        /// 如果有聚焦的层级，返回该层级的 Map；否则返回 Find.CurrentMap。
        /// </summary>
        public static Map CurrentInteractionMap
        {
            get
            {
                var baseMap = Find.CurrentMap;
                if (baseMap == null) return null;

                var mgr = GetManager(baseMap);
                if (mgr != null && mgr.IsFocusingLevel)
                    return mgr.FocusedMap;

                return baseMap;
            }
        }

        // ========== 生命周期 ==========

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            if (levels.Count > 0)
            {
                Gui.LevelSwitcherUI.DrawLevelSwitcher(this);
            }
        }

        // ========== 内部方法 ==========

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
            mapParent.Tile = 0;
            mapParent.sourceMap = map;

            // 直接赋值 mapGenerator（参照 VMF 的做法）
            var genDef = DefDatabase<MapGeneratorDef>.GetNamedSilentFail("MLF_LevelMapGenerator");
            if (genDef == null)
            {
                Log.Error("[MapLevelFramework] MapGeneratorDef 'MLF_LevelMapGenerator' not found.");
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

            // 共享天气/光照（如果配置了）
            if (data.levelDef == null || data.levelDef.shareWeather)
            {
                ShareEnvironment(levelMap, map);
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
