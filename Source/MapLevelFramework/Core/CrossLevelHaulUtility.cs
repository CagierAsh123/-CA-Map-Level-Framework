using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MapLevelFramework
{
    /// <summary>
    /// 跨层搬运工具 - 检查其他楼层是否有更好的仓库。
    /// </summary>
    public static class CrossLevelHaulUtility
    {
        /// <summary>
        /// 检查其他楼层是否有更好的仓库可以存放该物品。
        /// 返回目标地图、目标格子、以及当前地图上应走的楼梯。
        /// </summary>
        public static bool TryFindBetterStorageOnOtherLevel(
            Thing thing, Pawn pawn,
            out Map destMap, out IntVec3 destCell, out Building_Stairs stairs)
        {
            destMap = null;
            destCell = IntVec3.Invalid;
            stairs = null;

            if (thing == null || pawn?.Map == null) return false;

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

            if (mgr == null || mgr.LevelCount == 0) return false;

            StoragePriority currentPriority = StoreUtility.CurrentStoragePriorityOf(thing);
            int currentElev = CrossLevelJobUtility.GetMapElevation(pawnMap, mgr, baseMap);

            // 收集其他层级地图
            var otherMaps = new List<(Map map, int elevation)>();
            if (pawnMap != baseMap)
                otherMaps.Add((baseMap, 0));
            foreach (var level in mgr.AllLevels)
            {
                if (level.LevelMap != null && level.LevelMap != pawnMap)
                    otherMaps.Add((level.LevelMap, level.elevation));
            }

            if (otherMaps.Count == 0) return false;

            // 按距离当前层排序（优先近的楼层）
            otherMaps.Sort((a, b) =>
                System.Math.Abs(a.elevation - currentElev)
                    .CompareTo(System.Math.Abs(b.elevation - currentElev)));

            StoragePriority bestFoundPriority = currentPriority;

            foreach (var (otherMap, targetElev) in otherMaps)
            {
                // 找当前地图上通往该方向的楼梯
                int nextElev = targetElev > currentElev
                    ? currentElev + 1
                    : currentElev - 1;
                Building_Stairs candidateStairs = CrossLevelJobUtility.FindStairsToElevation(pawn, pawnMap, nextElev);
                if (candidateStairs == null) continue;

                // 在目标地图上找更好的仓库
                if (TryFindStorageCellOnMap(thing, otherMap, bestFoundPriority, out IntVec3 cell, out StoragePriority foundPriority))
                {
                    bestFoundPriority = foundPriority;
                    destMap = otherMap;
                    destCell = cell;
                    stairs = candidateStairs;
                }
            }

            return destMap != null;
        }

        /// <summary>
        /// 在指定地图上找到优先级高于 minPriority 的仓库格子。
        /// 不检查 pawn 可达性（pawn 转移后再走过去）。
        /// </summary>
        private static bool TryFindStorageCellOnMap(
            Thing thing, Map map, StoragePriority minPriority,
            out IntVec3 foundCell, out StoragePriority foundPriority)
        {
            foundCell = IntVec3.Invalid;
            foundPriority = StoragePriority.Unstored;

            var allGroups = map.haulDestinationManager.AllGroupsListForReading;
            for (int i = 0; i < allGroups.Count; i++)
            {
                SlotGroup group = allGroups[i];
                if (group.Settings.Priority <= minPriority) continue;
                if (!group.parent.Accepts(thing)) continue;

                // 找一个有空间的格子
                var cells = group.CellsList;
                for (int j = 0; j < cells.Count; j++)
                {
                    IntVec3 c = cells[j];
                    if (IsValidCellForThing(c, map, thing))
                    {
                        if (group.Settings.Priority > foundPriority)
                        {
                            foundPriority = group.Settings.Priority;
                            foundCell = c;
                        }
                        break; // 这个 group 有空位就够了
                    }
                }
            }

            return foundCell.IsValid;
        }

        /// <summary>
        /// 简化版格子有效性检查（不依赖 pawn 可达性）。
        /// </summary>
        private static bool IsValidCellForThing(IntVec3 c, Map map, Thing thing)
        {
            if (!c.InBounds(map)) return false;

            List<Thing> thingsAt = map.thingGrid.ThingsListAt(c);
            int itemCount = 0;
            for (int i = 0; i < thingsAt.Count; i++)
            {
                Thing t = thingsAt[i];
                if (t.def.category == ThingCategory.Item)
                {
                    itemCount++;
                    // 可以堆叠
                    if (t.CanStackWith(thing) && t.stackCount < t.def.stackLimit)
                        return true;
                }
                // 不可通行的建筑阻挡
                if (t.def.passability != Traversability.Standable
                    && t.def.surfaceType == SurfaceType.None
                    && t.def.category != ThingCategory.Item)
                    return false;
            }

            return itemCount < c.GetMaxItemsAllowedInCell(map);
        }
    }
}
