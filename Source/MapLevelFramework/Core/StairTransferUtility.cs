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

            // DeSpawn
            if (pawn.Spawned)
                pawn.DeSpawn(DestroyMode.Vanish);

            // Spawn 到目标地图
            GenSpawn.Spawn(pawn, destPos, destMap);

            // 恢复朝向
            pawn.Rotation = rotation;

            Log.Message($"[MLF] Transferred {pawn.LabelShort} to map {destMap.uniqueID} at {destPos}");
        }

        /// <summary>
        /// 判断楼梯所在的地图是基地图还是子地图，返回目标地图和位置。
        /// </summary>
        public static bool TryGetTransferTarget(Building_Stairs stairs, out Map destMap, out IntVec3 destPos)
        {
            destMap = null;
            destPos = IntVec3.Invalid;

            Map stairsMap = stairs.Map;
            if (stairsMap == null) return false;

            // 检查楼梯是否在子地图上
            if (LevelManager.IsLevelMap(stairsMap, out var manager, out var levelData))
            {
                // 在子地图上 → 目标是基地图
                destMap = levelData.hostMap;
                destPos = stairs.Position; // 同坐标
                return destMap != null;
            }

            // 在基地图上 → 目标是子地图
            var mgr = LevelManager.GetManager(stairsMap);
            if (mgr == null) return false;

            var level = mgr.GetLevel(stairs.targetElevation);
            if (level?.LevelMap == null) return false;

            destMap = level.LevelMap;
            destPos = stairs.Position; // 同坐标
            return true;
        }
    }
}
