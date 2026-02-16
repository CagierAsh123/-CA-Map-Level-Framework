using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// 事件防护补丁 - 禁止在子地图上触发袭击、闪电等事件。
    /// 子地图是建筑内部，不应该有独立的事件触发。
    /// </summary>
    public static class Patch_Events
    {
        /// <summary>
        /// 禁止在子地图上触发袭击。
        /// </summary>
        [HarmonyPatch(typeof(IncidentWorker_Raid), "TryExecuteWorker")]
        public static class Patch_DisableRaid
        {
            public static bool Prefix(IncidentParms parms)
            {
                if (parms?.target is Map map)
                    return !LevelManager.IsLevelMap(map, out _, out _);
                return true;
            }
        }

        /// <summary>
        /// 禁止在子地图上触发虫害。
        /// </summary>
        [HarmonyPatch(typeof(IncidentWorker_Infestation), "TryExecuteWorker")]
        public static class Patch_DisableInfestation
        {
            public static bool Prefix(IncidentParms parms)
            {
                if (parms?.target is Map map)
                    return !LevelManager.IsLevelMap(map, out _, out _);
                return true;
            }
        }

        /// <summary>
        /// Storyteller 计算时包含子地图的 pawn。
        /// 避免因为 pawn 在二楼而导致 storyteller 认为殖民地人少。
        /// </summary>
        [HarmonyPatch(typeof(Map), "get_PlayerPawnsForStoryteller")]
        public static class Patch_PlayerPawnsForStoryteller
        {
            public static void Postfix(Map __instance, ref IEnumerable<Pawn> __result)
            {
                var mgr = LevelManager.GetManager(__instance);
                if (mgr == null || mgr.LevelCount == 0) return;

                // 只在基地图上聚合（避免重复计算）
                if (LevelManager.IsLevelMap(__instance, out _, out _)) return;

                var basePawns = __result;
                __result = AggregatedPawns(basePawns, mgr);
            }

            private static IEnumerable<Pawn> AggregatedPawns(IEnumerable<Pawn> basePawns, LevelManager mgr)
            {
                foreach (var p in basePawns)
                    yield return p;

                foreach (var level in mgr.AllLevels)
                {
                    if (level.LevelMap == null) continue;
                    foreach (var p in level.LevelMap.PlayerPawnsForStoryteller)
                        yield return p;
                }
            }
        }
    }

    /// <summary>
    /// 交易补丁 - 交易时包含子地图上的物品。
    /// </summary>
    public static class Patch_Trade
    {
        [HarmonyPatch(typeof(Pawn_TraderTracker), "ColonyThingsWillingToBuy")]
        public static class Patch_PawnTrader
        {
            public static IEnumerable<Thing> Postfix(
                IEnumerable<Thing> __result, Pawn ___pawn)
            {
                foreach (var thing in __result)
                    yield return thing;

                Map traderMap = ___pawn.Map;
                if (traderMap == null) yield break;

                // 获取基地图的 LevelManager
                LevelManager mgr;
                if (LevelManager.IsLevelMap(traderMap, out var parentMgr, out _))
                    mgr = parentMgr;
                else
                    mgr = LevelManager.GetManager(traderMap);

                if (mgr == null) yield break;

                foreach (var level in mgr.AllLevels)
                {
                    Map levelMap = level.LevelMap;
                    if (levelMap == null || levelMap == traderMap) continue;

                    foreach (Thing thing in TradeUtility.AllLaunchableThingsForTrade(levelMap))
                        yield return thing;
                }
            }
        }

        [HarmonyPatch(typeof(TradeShip), "ColonyThingsWillingToBuy")]
        public static class Patch_TradeShip
        {
            public static IEnumerable<Thing> Postfix(
                IEnumerable<Thing> __result, TradeShip __instance)
            {
                foreach (var thing in __result)
                    yield return thing;

                Map shipMap = __instance.Map;
                if (shipMap == null) yield break;

                var mgr = LevelManager.GetManager(shipMap);
                if (mgr == null) yield break;

                foreach (var level in mgr.AllLevels)
                {
                    Map levelMap = level.LevelMap;
                    if (levelMap == null) continue;

                    foreach (Thing thing in TradeUtility.AllLaunchableThingsForTrade(levelMap))
                        yield return thing;
                }
            }
        }
    }
}
