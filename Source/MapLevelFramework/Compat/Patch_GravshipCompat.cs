using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MapLevelFramework
{
    /// <summary>
    /// 逆重飞船兼容 - 起飞时捕获层级地图数据。
    /// </summary>
    [HarmonyPatch(typeof(WorldComponent_GravshipController), "RemoveGravshipFromMap")]
    public static class Patch_GravshipLaunch
    {
        /// <summary>
        /// 全局标志：为 true 时 Building_Stairs 的 SpawnSetup/DeSpawn 跳过层级操作。
        /// </summary>
        internal static bool suppressStairsLevelOps;

        [HarmonyPrepare]
        public static bool Prepare() => ModsConfig.OdysseyActive;

        [HarmonyPrefix]
        public static void Prefix(Building_GravEngine engine)
        {
            Map map = engine.Map;
            if (map == null) return;

            LevelManager mgr = LevelManager.GetManager(map);
            if (mgr == null || !mgr.AllLevels.Any()) return;

            HashSet<IntVec3> substructure = engine.ValidSubstructure;
            if (substructure == null || substructure.Count == 0) return;

            // 找到飞船底盘上的所有楼梯
            var capturedElevations = new HashSet<int>();
            foreach (IntVec3 cell in substructure)
            {
                var things = map.thingGrid.ThingsListAtFast(cell);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Building_Stairs stairs && stairs.Spawned)
                        capturedElevations.Add(stairs.targetElevation);
                }
            }

            if (capturedElevations.Count == 0) return;

            var manager = Current.Game.GetComponent<MLF_GravshipManager>();
            if (manager == null) return;

            suppressStairsLevelOps = true;

            // 捕获每个层级的数据
            foreach (int elevation in capturedElevations.OrderByDescending(e => e))
            {
                if (elevation == 0) continue;
                var level = mgr.GetLevel(elevation);
                if (level?.LevelMap == null) continue;

                var storage = CaptureLevelData(level);
                if (storage != null)
                    manager.StoredLevels.Add(storage);
            }

            // 销毁层级地图（内容已捕获）
            foreach (int elevation in capturedElevations.OrderByDescending(e => e))
            {
                if (elevation == 0) continue;
                mgr.RemoveLevel(elevation);
            }

            Log.Message($"[MLF] Gravship launch: captured {manager.StoredLevels.Count} levels.");
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            suppressStairsLevelOps = false;
        }

        /// <summary>
        /// 从层级地图中捕获所有内容。Things 和 Pawns 会被 DeSpawn。
        /// </summary>
        private static GravshipLevelStorage CaptureLevelData(LevelData level)
        {
            Map levelMap = level.LevelMap;
            if (levelMap == null) return null;

            var storage = new GravshipLevelStorage
            {
                elevation = level.elevation,
                area = level.area,
                isUnderground = level.isUnderground,
                usableCellsList = level.usableCells != null
                    ? new List<IntVec3>(level.usableCells) : null
            };

            // 捕获地形和屋顶
            var cells = level.usableCells ?? level.area.Cells.ToHashSet();
            foreach (IntVec3 cell in cells)
            {
                if (!cell.InBounds(levelMap)) continue;
                storage.terrains[cell] = levelMap.terrainGrid.TerrainAt(cell);
                RoofDef roof = levelMap.roofGrid.RoofAt(cell);
                if (roof != null)
                    storage.roofs[cell] = roof;
            }

            // 捕获 Pawns（先处理，避免被 thing 列表包含）
            foreach (Pawn pawn in levelMap.mapPawns.AllPawnsSpawned.ToList())
            {
                if (!pawn.Spawned) continue;
                storage.pawns.Add(pawn);
                storage.pawnPositions.Add(pawn.Position);
                storage.pawnRotations.Add(pawn.Rotation);
                pawn.DeSpawn(DestroyMode.WillReplace);
            }

            // 捕获 Things
            foreach (Thing thing in levelMap.listerThings.AllThings.ToList())
            {
                if (!thing.Spawned) continue;
                if (thing is Mote || thing is Filth) continue;
                storage.things.Add(thing);
                storage.thingPositions.Add(thing.Position);
                storage.thingRotations.Add(thing.Rotation);
                thing.DeSpawn(DestroyMode.WillReplace);
            }

            return storage;
        }
    }

    /// <summary>
    /// 逆重飞船兼容 - 降落时恢复层级地图数据。
    /// </summary>
    [HarmonyPatch(typeof(WorldComponent_GravshipController), "PlaceGravship")]
    public static class Patch_GravshipLanding
    {
        [HarmonyPrepare]
        public static bool Prepare() => ModsConfig.OdysseyActive;

        [HarmonyPrefix]
        public static void Prefix()
        {
            var manager = Current.Game.GetComponent<MLF_GravshipManager>();
            if (manager != null && manager.StoredLevels.Count > 0)
                Patch_GravshipLaunch.suppressStairsLevelOps = true;
        }

        [HarmonyPostfix]
        public static void Postfix(Map map)
        {
            Patch_GravshipLaunch.suppressStairsLevelOps = false;

            var manager = Current.Game.GetComponent<MLF_GravshipManager>();
            if (manager == null || manager.StoredLevels.Count == 0) return;

            LevelManager mgr = LevelManager.GetManager(map);
            if (mgr == null) return;

            foreach (var storage in manager.StoredLevels)
            {
                RestoreLevelFromStorage(storage, mgr);
            }

            Log.Message($"[MLF] Gravship landing: restored {manager.StoredLevels.Count} levels.");
            manager.StoredLevels.Clear();
        }
        private static void RestoreLevelFromStorage(GravshipLevelStorage storage, LevelManager mgr)
        {
            // 创建层级（生成空白子地图）
            var level = mgr.RegisterLevel(storage.elevation, storage.area,
                isUnderground: storage.isUnderground);
            if (level?.LevelMap == null)
            {
                Log.Error($"[MLF] Gravship: failed to recreate level {storage.elevation}");
                return;
            }

            Map levelMap = level.LevelMap;

            // 恢复 usableCells
            if (storage.usableCellsList != null)
            {
                level.usableCells = new HashSet<IntVec3>(storage.usableCellsList);
                level.RebuildActiveSections();
            }

            // 恢复地形
            foreach (var kvp in storage.terrains)
            {
                if (kvp.Key.InBounds(levelMap) && kvp.Value != null)
                    levelMap.terrainGrid.SetTerrain(kvp.Key, kvp.Value);
            }

            // 恢复 Things
            for (int i = 0; i < storage.things.Count; i++)
            {
                Thing thing = storage.things[i];
                if (thing == null || thing.Destroyed) continue;
                IntVec3 pos = storage.thingPositions[i];
                Rot4 rot = storage.thingRotations[i];
                if (!pos.InBounds(levelMap)) continue;

                try
                {
                    GenSpawn.Spawn(thing, pos, levelMap, rot, WipeMode.Vanish);
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[MLF] Gravship: failed to restore {thing}: {ex.Message}");
                }
            }

            // 恢复 Pawns
            for (int i = 0; i < storage.pawns.Count; i++)
            {
                Pawn pawn = storage.pawns[i];
                if (pawn == null || pawn.Destroyed) continue;
                IntVec3 pos = storage.pawnPositions[i];
                if (!pos.InBounds(levelMap))
                    pos = levelMap.Center;

                try
                {
                    GenSpawn.Spawn(pawn, pos, levelMap);
                    pawn.Rotation = storage.pawnRotations[i];
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[MLF] Gravship: failed to restore pawn {pawn.LabelShort}: {ex.Message}");
                }
            }

            // 恢复屋顶
            foreach (var kvp in storage.roofs)
            {
                if (kvp.Key.InBounds(levelMap) && kvp.Value != null)
                    levelMap.roofGrid.SetRoof(kvp.Key, kvp.Value);
            }

            Log.Message($"[MLF] Restored level {storage.elevation}: " +
                $"{storage.things.Count} things, {storage.pawns.Count} pawns");
        }
    }
}
