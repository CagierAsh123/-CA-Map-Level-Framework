using System.Collections.Generic;
using LudeonTK;
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
