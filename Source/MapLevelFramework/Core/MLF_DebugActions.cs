using System.Collections.Generic;
using System.Reflection;
using LudeonTK;
using UnityEngine;
using Verse;

namespace MapLevelFramework
{
    /// <summary>
    /// 调试工具 - 通过开发者模式菜单测试框架功能。
    /// </summary>
    public static class MLF_DebugActions
    {
        [DebugAction("Map Level Framework", "Create Test Level (1F)",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void CreateTestLevel1F()
        {
            CreateTestLevel(1, "1F Test");
        }

        [DebugAction("Map Level Framework", "Create Test Level (B1)",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void CreateTestLevelB1()
        {
            CreateTestLevel(-1, "B1 Test");
        }

        [DebugAction("Map Level Framework", "Focus 1F",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Focus1F()
        {
            var mgr = LevelManager.GetManager(Find.CurrentMap);
            if (mgr == null) return;
            mgr.FocusLevel(1);
        }

        [DebugAction("Map Level Framework", "Focus Ground (0)",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void FocusGround()
        {
            var mgr = LevelManager.GetManager(Find.CurrentMap);
            if (mgr == null)
            {
                Log.Warning("[MLF Debug] No LevelManager on current map.");
                return;
            }
            mgr.FocusLevel(0);
        }

        [DebugAction("Map Level Framework", "List All Levels",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ListAllLevels()
        {
            var mgr = LevelManager.GetManager(Find.CurrentMap);
            if (mgr == null)
            {
                Log.Message("[MLF Debug] No LevelManager on current map.");
                return;
            }

            Log.Message($"[MLF Debug] Focused elevation: {mgr.FocusedElevation}");
            foreach (var level in mgr.AllLevels)
            {
                Log.Message($"  Elevation {level.elevation}: area={level.area}, " +
                            $"map={level.LevelMap?.uniqueID ?? -1}, " +
                            $"tag={level.levelDef?.levelTag ?? "none"}");
            }
        }

        [DebugAction("Map Level Framework", "Remove All Levels",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RemoveAllLevels()
        {
            var mgr = LevelManager.GetManager(Find.CurrentMap);
            if (mgr == null) return;

            var elevations = new List<int>(mgr.AllElevations);
            foreach (int e in elevations)
            {
                mgr.RemoveLevel(e);
            }
            Log.Message("[MLF Debug] All levels removed.");
        }

        [DebugAction("Map Level Framework", "Force Regen 2F",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ForceRegen2F()
        {
            var mgr = LevelManager.GetManager(Find.CurrentMap);
            if (mgr == null) return;
            var level = mgr.GetLevel(mgr.FocusedElevation);
            if (level?.LevelMap == null)
            {
                Log.Warning("[MLF Debug] No focused level map.");
                return;
            }
            // 清除 initializedMaps 缓存，下一帧会触发 RegenerateEverythingNow
            Render.LevelRenderer.ClearInitializedMap(level.LevelMap.uniqueID);
            Log.Message("[MLF Debug] Cleared initialized cache. Will RegenerateEverythingNow next frame.");
        }

        [DebugAction("Map Level Framework", "Debug Terrain Mesh",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DebugTerrainMesh()
        {
            var baseMap = Find.CurrentMap;
            var mgr = LevelManager.GetManager(baseMap);
            if (mgr == null || !mgr.IsFocusingLevel) return;
            var level = mgr.GetLevel(mgr.FocusedElevation);
            if (level?.LevelMap == null) return;

            Map levelMap = level.LevelMap;
            IntVec3 cell = UI.MouseCell();

            // 1. 两张地图的地形
            TerrainDef baseTerrain = baseMap.terrainGrid.TerrainAt(cell);
            TerrainDef levelTerrain = cell.InBounds(levelMap)
                ? levelMap.terrainGrid.TerrainAt(cell) : null;
            Log.Message($"[MLF Debug] Cell {cell}: baseTerrain={baseTerrain?.defName}, levelTerrain={levelTerrain?.defName}");

            // 2. Section 信息
            var sectionsField = typeof(MapDrawer).GetField("sections",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var layersField = typeof(Section).GetField("layers",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Section[,] sections = sectionsField?.GetValue(levelMap.mapDrawer) as Section[,];
            if (sections == null) { Log.Warning("[MLF Debug] sections is null"); return; }

            int sx = cell.x / 17;
            int sz = cell.z / 17;
            if (sx >= sections.GetLength(0) || sz >= sections.GetLength(1))
            { Log.Warning("[MLF Debug] section index out of range"); return; }

            Section section = sections[sx, sz];
            if (section == null) { Log.Warning("[MLF Debug] section is null"); return; }

            Log.Message($"[MLF Debug] Section[{sx},{sz}] dirtyFlags={section.dirtyFlags}, active={level.IsSectionActive(sx, sz)}");

            List<SectionLayer> layers = layersField?.GetValue(section) as List<SectionLayer>;
            if (layers == null) return;

            foreach (SectionLayer layer in layers)
            {
                string name = layer.GetType().Name;
                int totalVerts = 0;
                int subCount = layer.subMeshes.Count;
                int finalizedCount = 0;
                foreach (var sm in layer.subMeshes)
                {
                    if (sm.finalized) finalizedCount++;
                    if (sm.mesh != null) totalVerts += sm.mesh.vertexCount;
                }
                Log.Message($"  Layer {name}: subMeshes={subCount}, finalized={finalizedCount}/{subCount}, totalVerts={totalVerts}");
            }
        }

        private static void CreateTestLevel(int elevation, string name)
        {
            var map = Find.CurrentMap;
            if (map == null) return;

            var mgr = LevelManager.GetManager(map);
            if (mgr == null)
            {
                Log.Warning("[MLF Debug] No LevelManager on current map. It should be auto-added.");
                return;
            }

            // 在地图中心创建一个 13x13 的测试层级
            int cx = map.Size.x / 2;
            int cz = map.Size.z / 2;
            int halfSize = 6;
            CellRect area = new CellRect(cx - halfSize, cz - halfSize, 13, 13);

            var level = mgr.RegisterLevel(elevation, area);
            if (level != null)
            {
                Log.Message($"[MLF Debug] Created test level '{name}' at elevation {elevation}");
                mgr.FocusLevel(elevation);
            }
        }
    }
}
