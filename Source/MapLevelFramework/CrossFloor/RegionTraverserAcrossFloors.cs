using System;
using System.Collections.Generic;
using Verse;

namespace MapLevelFramework.CrossFloor
{
    /// <summary>
    /// 跨楼层 Region BFS 遍历器。
    /// 在标准 Region BFS 基础上，当遇到包含楼梯的 Region 时，
    /// 将目标楼层对应位置的 Region 加入 BFS 队列，实现跨图遍历。
    /// 参考 VMF 的 RegionTraverserAcrossMaps。
    /// </summary>
    public static class RegionTraverserAcrossFloors
    {
        private static readonly Queue<BFSWorker> freeWorkers = new Queue<BFSWorker>();
        public static int NumWorkers = 8;

        static RegionTraverserAcrossFloors()
        {
            RecreateWorkers();
        }

        public static void RecreateWorkers()
        {
            freeWorkers.Clear();
            for (int i = 0; i < NumWorkers; i++)
            {
                freeWorkers.Enqueue(new BFSWorker());
            }
        }

        public static void BreadthFirstTraverse(
            IntVec3 start, Map map,
            RegionEntryPredicate entryCondition,
            RegionProcessor regionProcessor,
            int maxRegions = 999999,
            RegionType traversableRegionTypes = RegionType.Set_Passable)
        {
            Region region = map.regionGrid?.GetValidRegionAt(start);
            if (region != null)
            {
                BreadthFirstTraverse(region, entryCondition, regionProcessor,
                    maxRegions, traversableRegionTypes);
            }
        }

        public static void BreadthFirstTraverse(
            Region root,
            RegionEntryPredicate entryCondition,
            RegionProcessor regionProcessor,
            int maxRegions = 999999,
            RegionType traversableRegionTypes = RegionType.Set_Passable)
        {
            if (freeWorkers.Count == 0)
            {
                Log.Error("[MLF] No free workers for cross-floor BFS. Either BFS recurred deeper than "
                    + NumWorkers + ", or a bug. Resetting.");
                return;
            }
            if (root == null)
            {
                Log.Error("[MLF] BreadthFirstTraverse with null root region.");
                return;
            }

            BFSWorker worker = freeWorkers.Dequeue();
            try
            {
                worker.BreadthFirstTraverseWork(root, entryCondition, regionProcessor,
                    maxRegions, traversableRegionTypes);
            }
            catch (Exception ex)
            {
                Log.Error("[MLF] Exception in cross-floor BFS: " + ex);
            }
            finally
            {
                worker.Clear();
                freeWorkers.Enqueue(worker);
            }
        }

        private class BFSWorker
        {
            private readonly Queue<Region> open = new Queue<Region>();
            private readonly HashSet<Region> closed = new HashSet<Region>();
            private int numRegionsProcessed;

            public void Clear()
            {
                open.Clear();
                closed.Clear();
            }

            public void BreadthFirstTraverseWork(
                Region root,
                RegionEntryPredicate entryCondition,
                RegionProcessor regionProcessor,
                int maxRegions,
                RegionType traversableRegionTypes)
            {
                if ((root.type & traversableRegionTypes) == 0) return;

                Clear();
                numRegionsProcessed = 0;
                open.Enqueue(root);
                closed.Add(root);

                while (open.Count > 0)
                {
                    Region region = open.Dequeue();

                    // 处理当前 Region
                    if (regionProcessor != null && regionProcessor(region))
                        return;

                    if (!region.IsDoorway)
                        numRegionsProcessed++;

                    if (numRegionsProcessed >= maxRegions)
                        return;

                    // 跨楼层跳转：检查当前 Region 是否包含楼梯
                    TryEnqueueCrossFloorRegions(region, traversableRegionTypes);

                    // 标准：遍历相邻 Region
                    for (int i = 0; i < region.links.Count; i++)
                    {
                        RegionLink link = region.links[i];
                        for (int j = 0; j < 2; j++)
                        {
                            Region neighbor = link.regions[j];
                            if (neighbor == null) continue;
                            if (closed.Contains(neighbor)) continue;
                            if ((neighbor.type & traversableRegionTypes) == 0) continue;
                            if (entryCondition != null && !entryCondition(region, neighbor)) continue;

                            open.Enqueue(neighbor);
                            closed.Add(neighbor);
                        }
                    }
                }
            }

            /// <summary>
            /// 检查 Region 所在地图的所有楼梯，如果楼梯在当前 Region 内，
            /// 将目标楼层对应位置的 Region 加入 BFS 队列。
            /// </summary>
            private void TryEnqueueCrossFloorRegions(Region region, RegionType traversableRegionTypes)
            {
                Map regionMap = region.Map;
                if (regionMap == null) return;

                var allStairs = StairsCache.GetAllStairsOnMap(regionMap);
                if (allStairs == null || allStairs.Count == 0) return;

                for (int i = 0; i < allStairs.Count; i++)
                {
                    Building_Stairs stairs = allStairs[i];
                    if (!stairs.Spawned) continue;

                    // 检查楼梯是否在当前 Region 内
                    Region stairRegion = regionMap.regionGrid.GetRegionAt_NoRebuild_InvalidAllowed(stairs.Position);
                    if (stairRegion != region) continue;

                    // 电梯模型：遍历楼梯井内所有可达楼层
                    IntVec3 stairPos = stairs.Position;
                    foreach (Map floorMap in regionMap.BaseMapAndFloorMaps())
                    {
                        if (floorMap == regionMap) continue;
                        if (!FloorMapUtility.HasStairsAtPosition(floorMap, stairPos)) continue;
                        if (!stairPos.InBounds(floorMap)) continue;

                        Region destRegion = floorMap.regionGrid.GetRegionAt_NoRebuild_InvalidAllowed(stairPos);
                        if (destRegion == null) continue;
                        if (closed.Contains(destRegion)) continue;
                        if ((destRegion.type & traversableRegionTypes) == 0) continue;

                        open.Enqueue(destRegion);
                        closed.Add(destRegion);
                    }
                }
            }
        }
    }
}