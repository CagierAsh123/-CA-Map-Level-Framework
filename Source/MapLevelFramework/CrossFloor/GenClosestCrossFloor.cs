using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MapLevelFramework.CrossFloor
{
    /// <summary>
    /// 跨楼层物品搜索。
    /// 当本层 GenClosest.ClosestThingReachable 返回 null 时，搜索其他楼层。
    /// </summary>
    public static class GenClosestCrossFloor
    {
        /// <summary>
        /// 在其他楼层搜索最近的可达物品。
        /// 注意：不调用原版 validator，因为 validator 假设 Thing 和 pawn 在同一地图。
        /// 只做基础检查（spawned、not forbidden、跨层可达）。
        /// pawn 到达目标楼层后，AI 会重新验证。
        /// </summary>
        public static Thing ClosestThingOnOtherFloors(
            IntVec3 root, Map map, ThingRequest thingReq,
            PathEndMode peMode, TraverseParms traverseParams,
            float maxDistance, Predicate<Thing> validator)
        {
            if (!map.IsPartOfFloorSystem()) return null;
            if (thingReq.group == ThingRequestGroup.Everything) return null;
            if (thingReq.IsUndefined) return null;

            Pawn pawn = traverseParams.pawn;
            Thing bestThing = null;
            float bestDist = float.MaxValue;

            foreach (Map otherMap in map.BaseMapAndFloorMaps())
            {
                if (otherMap == map) continue;

                List<Thing> things = otherMap.listerThings.ThingsMatching(thingReq);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (!t.Spawned) continue;

                    // 基础检查（不调用 validator，避免跨图 CanReach 错误）
                    if (pawn != null && t.IsForbidden(pawn)) continue;

                    // 跨楼层可达性检查
                    if (!CrossFloorReachabilityUtility.CanReach(
                        map, root, otherMap, t.Position,
                        peMode, traverseParams))
                        continue;

                    // 估算距离：到楼梯的距离 + 楼梯到目标的距离
                    float dist = EstimateCrossFloorDist(root, map, t.Position, otherMap);
                    if (dist < bestDist && dist <= maxDistance)
                    {
                        bestDist = dist;
                        bestThing = t;
                    }
                }
            }

            return bestThing;
        }

        /// <summary>
        /// 估算跨楼层距离：pawn 到最近楼梯 + 楼梯到目标。
        /// 楼梯在两层的位置相同，所以只需要计算到楼梯的距离 + 楼梯到目标的距离。
        /// </summary>
        public static float EstimateCrossFloorDist(
            IntVec3 start, Map startMap, IntVec3 dest, Map destMap)
        {
            var allStairs = StairsCache.GetAllStairsOnMap(startMap);
            if (allStairs == null || allStairs.Count == 0) return float.MaxValue;

            float bestDist = float.MaxValue;
            for (int i = 0; i < allStairs.Count; i++)
            {
                Building_Stairs stairs = allStairs[i];
                if (!stairs.Spawned) continue;

                // 到楼梯的距离 + 楼梯位置到目标的距离 + 楼层惩罚
                float toStairs = start.DistanceTo(stairs.Position);
                float fromStairs = stairs.Position.DistanceTo(dest);
                float floorPenalty = 50f; // 跨层额外代价
                float total = toStairs + fromStairs + floorPenalty;

                if (total < bestDist)
                    bestDist = total;
            }

            return bestDist;
        }
    }
}
