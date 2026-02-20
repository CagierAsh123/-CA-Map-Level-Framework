using Verse;

namespace MapLevelFramework
{
    /// <summary>
    /// 楼梯跨图转移工具 - 将 pawn 从一个地图转移到另一个地图。
    /// Demo 阶段使用简单的 DeSpawn/Spawn，不保留 job 状态。
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
        /// 电梯模式：指定目标楼层 elevation，直接传送到该楼层。
        /// 楼梯位置在所有楼层相同，无需坐标转换。
        /// </summary>
        public static bool TryGetTransferTarget(Building_Stairs stairs, int targetElevation, out Map destMap, out IntVec3 destPos)
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

            destPos = stairs.Position;
            return destMap != null && destMap != stairsMap;
        }
    }
}
