using System;
using HarmonyLib;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// 层级子地图 Tick 抑制 - 跳过不需要的地图系统，大幅降低多层开销。
    /// 每个层级子地图是完整 Map，默认跑全套 MapPreTick + MapPostTick。
    /// 3 层楼 = 2 个子地图 = 地图系统开销 ×3。通过只保留必要系统来优化。
    /// </summary>

    [HarmonyPatch(typeof(Map), nameof(Map.MapPreTick))]
    public static class Patch_MapPreTick
    {
        public static bool Prefix(Map __instance)
        {
            if (!(__instance.Parent is LevelMapParent))
                return true; // 非层级地图，走原版

            // ===== 保留的系统 =====
            try
            {
                __instance.itemAvailability.Tick();
            }
            catch (Exception ex) { Log.ErrorOnce($"[MLF] itemAvailability.Tick: {ex.Message}", __instance.uniqueID ^ 0x1A2B); }

            try
            {
                __instance.listerHaulables.ListerHaulablesTick();
            }
            catch (Exception ex) { Log.ErrorOnce($"[MLF] ListerHaulablesTick: {ex.Message}", __instance.uniqueID ^ 0x3C4D); }

            try
            {
                __instance.roofCollapseBufferResolver.CollapseRoofsMarkedToCollapse();
            }
            catch (Exception ex) { Log.ErrorOnce($"[MLF] CollapseRoofs: {ex.Message}", __instance.uniqueID ^ 0x5E6F); }

            // 跳过: windManager.WindManagerTick() — 楼层内无风
            // 跳过: autoBuildRoofAreaSetter — 楼层屋顶由 MLF 管理

            try
            {
                __instance.mapTemperature.MapTemperatureTick();
            }
            catch (Exception ex) { Log.ErrorOnce($"[MLF] MapTemperatureTick: {ex.Message}", __instance.uniqueID ^ 0x7A8B); }

            __instance.temporaryThingDrawer.Tick();

            try
            {
                __instance.pathFinder.PathFinderTick();
            }
            catch (Exception ex) { Log.ErrorOnce($"[MLF] PathFinderTick: {ex.Message}", __instance.uniqueID ^ 0x9CAD); }

            return false; // 跳过原方法
        }
    }
    [HarmonyPatch(typeof(Map), nameof(Map.MapPostTick))]
    public static class Patch_MapPostTick
    {
        public static bool Prefix(Map __instance)
        {
            if (!(__instance.Parent is LevelMapParent))
                return true; // 非层级地图，走原版

            // ===== 跳过的系统 =====
            // wildAnimalSpawner — 楼层不刷野生动物
            // wildPlantSpawner — 楼层不刷野生植物
            // passingShipManager — 子地图无过路商船
            // weatherManager — 已与主地图共享
            // weatherDecider — 已与主地图共享
            // gameConditionManager — 已与主地图共享
            // lordsStarter — 子地图不独立发起聚会
            // pollutionGrid — 楼层无污染扩散
            // waterBodyTracker — 楼层无水体
            // TileMutatorDef — 世界地块变异不适用子地图

            // ===== 保留的系统 =====
            try
            {
                __instance.powerNetManager.PowerNetsTick();
            }
            catch (Exception ex) { Log.ErrorOnce($"[MLF] PowerNetsTick: {ex.Message}", __instance.uniqueID ^ 0xB1C2); }

            try
            {
                __instance.steadyEnvironmentEffects.SteadyEnvironmentEffectsTick();
            }
            catch (Exception ex) { Log.ErrorOnce($"[MLF] SteadyEnvironmentEffects: {ex.Message}", __instance.uniqueID ^ 0xD3E4); }

            try
            {
                __instance.tempTerrain.Tick();
            }
            catch (Exception ex) { Log.ErrorOnce($"[MLF] tempTerrain: {ex.Message}", __instance.uniqueID ^ 0xF506); }

            try
            {
                __instance.gasGrid.Tick();
            }
            catch (Exception ex) { Log.ErrorOnce($"[MLF] gasGrid: {ex.Message}", __instance.uniqueID ^ 0x1728); }

            try
            {
                __instance.deferredSpawner.DeferredSpawnerTick();
            }
            catch (Exception ex) { Log.ErrorOnce($"[MLF] deferredSpawner: {ex.Message}", __instance.uniqueID ^ 0x394A); }

            try
            {
                __instance.lordManager.LordManagerTick();
            }
            catch (Exception ex) { Log.ErrorOnce($"[MLF] lordManager: {ex.Message}", __instance.uniqueID ^ 0x5B6C); }

            try
            {
                __instance.debugDrawer.DebugDrawerTick();
            }
            catch (Exception ex) { Log.ErrorOnce($"[MLF] debugDrawer: {ex.Message}", __instance.uniqueID ^ 0x7D8E); }

            try
            {
                __instance.resourceCounter.ResourceCounterTick();
            }
            catch (Exception ex) { Log.ErrorOnce($"[MLF] resourceCounter: {ex.Message}", __instance.uniqueID ^ 0x9FA0); }

            try
            {
                __instance.fireWatcher.FireWatcherTick();
            }
            catch (Exception ex) { Log.ErrorOnce($"[MLF] fireWatcher: {ex.Message}", __instance.uniqueID ^ 0xB1C3); }

            try
            {
                __instance.flecks.FleckManagerTick();
            }
            catch (Exception ex) { Log.ErrorOnce($"[MLF] flecks: {ex.Message}", __instance.uniqueID ^ 0xD3E5); }

            try
            {
                __instance.effecterMaintainer.EffecterMaintainerTick();
            }
            catch (Exception ex) { Log.ErrorOnce($"[MLF] effecterMaintainer: {ex.Message}", __instance.uniqueID ^ 0xF507); }

            MapComponentUtility.MapComponentTick(__instance);

            return false; // 跳过原方法
        }
    }
}
