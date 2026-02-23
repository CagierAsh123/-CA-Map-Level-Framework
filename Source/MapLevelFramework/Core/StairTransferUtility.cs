using Verse;
using Verse.AI;
using MapLevelFramework.CrossFloor;

namespace MapLevelFramework
{
    /// <summary>
    /// 楼层传送工具 - 将 pawn 从一个地图转移到另一个地图。
    /// 偷懒方案：任意传送器互通，落点为目标层最近的传送器位置。
    /// </summary>
    public static class StairTransferUtility
    {
        /// <summary>
        /// 将 pawn 转移到目标地图的指定位置。
        /// </summary>
        public static void TransferPawn(Pawn pawn, Map destMap, IntVec3 destPos)
        {
            if (pawn == null || destMap == null) return;
            if (!destPos.InBounds(destMap))
            {
                Log.Error($"[MLF] TransferPawn: destPos {destPos} out of bounds for map.");
                return;
            }

            // 停止寻路
            pawn.pather?.StopDead();

            // 记录朝向
            Rot4 rotation = pawn.Rotation;

            // 保存携带的物品（DeSpawn 会调用 jobs.StopAll → 导致携带物品被丢弃）
            Thing carried = pawn.carryTracker?.CarriedThing;
            if (carried != null)
            {
                pawn.carryTracker.innerContainer.Remove(carried);
            }

            // DeSpawn
            if (pawn.Spawned)
                pawn.DeSpawn(DestroyMode.Vanish);

            // Spawn 到目标地图
            GenSpawn.Spawn(pawn, destPos, destMap);

            // 恢复朝向
            pawn.Rotation = rotation;

            // 恢复携带的物品
            if (carried != null && !carried.Destroyed)
            {
                pawn.carryTracker.innerContainer.TryAdd(carried);
            }

            Log.Message($"[MLF] Transferred {pawn.LabelShort} to map {destMap.uniqueID} at {destPos}");
        }

        /// <summary>
        /// 根据楼梯的 targetElevation 确定目标地图和位置。
        /// 支持 N 层：targetElevation=0 → 基地图，其他 → 对应层级子地图。
        /// </summary>
        public static bool TryGetTransferTarget(Building_Stairs stairs, out Map destMap, out IntVec3 destPos)
        {
            return TryGetTransferTarget(stairs, stairs.targetElevation, out destMap, out destPos);
        }

        /// <summary>
        /// 偷懒方案：指定目标楼层 elevation，落点为目标层任意传送器位置。
        /// 优先同位置，否则找目标层最近的传送器。
        /// preferredDest：如果有效，选离它最近的传送器（而非离出发点最近）。
        /// </summary>
        public static bool TryGetTransferTarget(Building_Stairs stairs, int targetElevation, out Map destMap, out IntVec3 destPos, IntVec3 preferredDest = default)
        {
            destMap = null;
            destPos = IntVec3.Invalid;

            Map stairsMap = stairs.Map;
            if (stairsMap == null) return false;

            LevelManager mgr;
            Map baseMap;
            if (LevelManager.IsLevelMap(stairsMap, out var parentMgr, out _))
            {
                mgr = parentMgr;
                baseMap = parentMgr.map;
            }
            else
            {
                mgr = LevelManager.GetManager(stairsMap);
                baseMap = stairsMap;
            }

            if (mgr == null) return false;

            if (targetElevation == 0)
            {
                destMap = baseMap;
            }
            else
            {
                var level = mgr.GetLevel(targetElevation);
                if (level?.LevelMap == null) return false;
                destMap = level.LevelMap;
            }

            if (destMap == null || destMap == stairsMap) return false;

            // 同位置且无 preferredDest → 直接用
            if (!preferredDest.IsValid && FloorMapUtility.HasStairsAtPosition(destMap, stairs.Position))
            {
                destPos = stairs.Position;
                return true;
            }

            // 找目标层的传送器
            var targetStairs = StairsCache.GetAllStairsOnMap(destMap);
            if (targetStairs == null || targetStairs.Count == 0) return false;

            bool hasPreferred = preferredDest.IsValid && preferredDest != IntVec3.Zero;
            var noPawnParams = TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly);

            // 有 preferredDest 时：优先选和目的地可达的传送器（同区域/同房间），其中取最近的
            Building_Stairs bestReachable = null;
            float bestReachDist = float.MaxValue;
            Building_Stairs bestAny = null;
            float bestAnyDist = float.MaxValue;

            for (int i = 0; i < targetStairs.Count; i++)
            {
                var s = targetStairs[i];
                if (!s.Spawned) continue;
                float dist = s.Position.DistanceToSquared(hasPreferred ? preferredDest : stairs.Position);

                if (dist < bestAnyDist)
                {
                    bestAny = s;
                    bestAnyDist = dist;
                }

                if (hasPreferred && dist < bestReachDist
                    && destMap.reachability.CanReach(s.Position, preferredDest,
                        PathEndMode.OnCell, noPawnParams))
                {
                    bestReachable = s;
                    bestReachDist = dist;
                }
            }

            // 优先可达的，否则回退到最近的
            Building_Stairs chosen = bestReachable ?? bestAny;
            if (chosen == null) return false;
            destPos = chosen.Position;
            return true;
        }
    }
}
