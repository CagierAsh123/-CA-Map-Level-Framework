using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

namespace MapLevelFramework
{
    /// <summary>
    /// 跨层级 Job 扫描共用工具。
    /// 提供"临时传送 pawn → 在目标地图调用原版 AI → 找到工作就派去楼梯"的通用流程。
    /// </summary>
    public static class CrossLevelJobUtility
    {
        /// <summary>递归防护</summary>
        public static bool Scanning { get; private set; }

        /// <summary>
        /// 重定向冷却间隔（tick）。防止 pawn 在两层之间反复弹跳。
        /// 250 ticks ≈ 4 秒游戏时间。
        /// </summary>
        public const int RedirectCooldownTicks = 250;

        // 每个 pawn 的上次重定向 tick（key = pawn.thingIDNumber）
        private static readonly Dictionary<int, int> lastRedirectTick = new Dictionary<int, int>();

        // 延迟 job 存储：pawn 到达楼梯后恢复在目标地图找到的 job
        private static readonly Dictionary<int, Job> deferredJobs = new Dictionary<int, Job>();

        // 反射字段：Thing.mapIndexOrState / Thing.positionInt
        private static readonly FieldInfo mapIndexField =
            typeof(Thing).GetField("mapIndexOrState",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo positionField =
            typeof(Thing).GetField("positionInt",
                BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// 取出并移除 pawn 的延迟 job（到达楼梯转移后恢复用）。
        /// </summary>
        public static bool TryPopDeferredJob(Pawn pawn, out Job job)
        {
            if (pawn != null && deferredJobs.TryGetValue(pawn.thingIDNumber, out job))
            {
                deferredJobs.Remove(pawn.thingIDNumber);
                return job != null;
            }
            job = null;
            return false;
        }

        /// <summary>
        /// 通用跨层扫描。遍历其他层级地图，临时传送 pawn 后调用 tryFindJob。
        /// 如果在某层找到 job，将其存入 deferredJobs，返回一个 MLF_UseStairs job 让 pawn 走到楼梯。
        /// pawn 到达楼梯转移后，由 JobDriver_UseStairs 恢复延迟的 job。
        /// </summary>
        /// <param name="pawn">要扫描的 pawn</param>
        /// <param name="tryFindJob">在目标地图上尝试找 job 的委托，返回找到的 Job 或 null</param>
        /// <returns>MLF_UseStairs job，或 null</returns>
        public static Job TryCrossLevelScan(Pawn pawn, Func<Job> tryFindJob)
        {
            if (Scanning) return null;
            if (pawn?.Map == null || !pawn.Spawned) return null;

            // 冷却检查：防止反复跳层
            int now = GenTicks.TicksGame;
            int pawnId = pawn.thingIDNumber;
            if (lastRedirectTick.TryGetValue(pawnId, out int lastTick)
                && now - lastTick < RedirectCooldownTicks)
                return null;

            Map pawnMap = pawn.Map;
            LevelManager mgr;
            Map baseMap;

            if (LevelManager.IsLevelMap(pawnMap, out var parentMgr, out _))
            {
                mgr = parentMgr;
                baseMap = parentMgr.map;
            }
            else
            {
                mgr = LevelManager.GetManager(pawnMap);
                baseMap = pawnMap;
            }

            if (mgr == null || !mgr.AllLevels.Any()) return null;

            int currentElev = GetMapElevation(pawnMap, mgr, baseMap);

            // 收集其他层级地图，按距离当前层远近排序
            var otherMaps = new List<(Map map, int elevation)>();
            if (pawnMap != baseMap)
                otherMaps.Add((baseMap, 0));
            foreach (var level in mgr.AllLevels)
            {
                if (level.LevelMap != null && level.LevelMap != pawnMap)
                    otherMaps.Add((level.LevelMap, level.elevation));
            }

            if (otherMaps.Count == 0) return null;

            otherMaps.Sort((a, b) =>
                Math.Abs(a.elevation - currentElev)
                    .CompareTo(Math.Abs(b.elevation - currentElev)));

            // 保存 pawn 原始状态
            sbyte origMapIndex = (sbyte)mapIndexField.GetValue(pawn);
            IntVec3 origPos = (IntVec3)positionField.GetValue(pawn);

            Scanning = true;
            try
            {
                foreach (var (otherMap, targetElev) in otherMaps)
                {
                    // 一次走一层
                    int nextElev = targetElev > currentElev
                        ? currentElev + 1
                        : currentElev - 1;

                    // 找当前地图上通往该方向的楼梯
                    Building_Stairs stairs = FindStairsToElevation(pawn, pawnMap, nextElev);
                    if (stairs == null) continue;

                    IntVec3 stairPos = stairs.Position;
                    if (!stairPos.InBounds(otherMap)) continue;

                    sbyte destMapIndex = (sbyte)Find.Maps.IndexOf(otherMap);
                    if (destMapIndex < 0) continue;

                    // 临时传送 pawn 到目标地图
                    mapIndexField.SetValue(pawn, destMapIndex);
                    positionField.SetValue(pawn, stairPos);

                    try
                    {
                        Job foundJob = tryFindJob();
                        if (foundJob != null)
                        {
                            // 找到了！恢复 pawn 位置，存储延迟 job，记录冷却，返回楼梯 job
                            mapIndexField.SetValue(pawn, origMapIndex);
                            positionField.SetValue(pawn, origPos);
                            deferredJobs[pawnId] = foundJob;
                            lastRedirectTick[pawnId] = now;
                            return JobMaker.MakeJob(MLF_JobDefOf.MLF_UseStairs, stairs);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[MLF] Cross-level scan error on map {otherMap.uniqueID}: {ex.Message}");
                    }
                }
            }
            finally
            {
                mapIndexField.SetValue(pawn, origMapIndex);
                positionField.SetValue(pawn, origPos);
                Scanning = false;
            }

            return null;
        }

        /// <summary>
        /// 获取地图的 elevation（0 = 基地图）。
        /// </summary>
        public static int GetMapElevation(Map map, LevelManager mgr, Map baseMap)
        {
            if (map == baseMap) return 0;
            var level = mgr.GetLevelForMap(map);
            return level?.elevation ?? 0;
        }

        /// <summary>
        /// 找到当前地图上通往指定 elevation 的最近楼梯。
        /// 同时搜索 MLF_Stairs 和 MLF_StairsDown。
        /// </summary>
        public static Building_Stairs FindStairsToElevation(Pawn pawn, Map map, int targetElevation)
        {
            Building_Stairs best = null;
            float bestDist = float.MaxValue;

            // 搜索所有 Building_Stairs（不限定 defName，兼容上楼梯和下楼梯）
            var allThings = map.listerThings.AllThings;
            for (int i = 0; i < allThings.Count; i++)
            {
                if (allThings[i] is Building_Stairs stairs && stairs.Spawned
                    && stairs.targetElevation == targetElevation)
                {
                    float dist = stairs.Position.DistanceToSquared(pawn.Position);
                    if (dist < bestDist && pawn.CanReach(stairs, PathEndMode.OnCell, Danger.Some))
                    {
                        best = stairs;
                        bestDist = dist;
                    }
                }
            }
            return best;
        }
    }
}
