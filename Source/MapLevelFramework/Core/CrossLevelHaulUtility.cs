using System.Collections.Generic;
using RimWorld;
using Verse;
using MapLevelFramework.CrossFloor;

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
            if (!pawnMap.IsPartOfFloorSystem()) return false;

            StoragePriority currentPriority = StoreUtility.CurrentStoragePriorityOf(thing);
            int currentElev = FloorMapUtility.GetMapElevation(pawnMap);

            // 收集其他层级地图并按距离排序
            var otherMaps = new List<(Map map, int elevation)>();
            foreach (Map m in pawnMap.BaseMapAndFloorMaps())
            {
                if (m != pawnMap)
                    otherMaps.Add((m, FloorMapUtility.GetMapElevation(m)));
            }
            if (otherMaps.Count == 0) return false;

            otherMaps.Sort((a, b) =>
                System.Math.Abs(a.elevation - currentElev)
                    .CompareTo(System.Math.Abs(b.elevation - currentElev)));

            StoragePriority bestFoundPriority = currentPriority;

            foreach (var (otherMap, targetElev) in otherMaps)
            {
                int nextElev = targetElev > currentElev
                    ? currentElev + 1 : currentElev - 1;
                Building_Stairs candidateStairs =
                    FloorMapUtility.FindStairsToElevation(pawn, pawnMap, nextElev);
                if (candidateStairs == null) continue;

                if (TryFindStorageCellOnMap(thing, otherMap, bestFoundPriority,
                        out IntVec3 cell, out StoragePriority foundPriority))
                {
                    bestFoundPriority = foundPriority;
                    destMap = otherMap;
                    destCell = cell;
                    stairs = candidateStairs;
                }
            }

            return destMap != null;
        }

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
                        break;
                    }
                }
            }

            return foundCell.IsValid;
        }

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
                    if (t.CanStackWith(thing) && t.stackCount < t.def.stackLimit)
                        return true;
                }
                if (t.def.passability != Traversability.Standable
                    && t.def.surfaceType == SurfaceType.None
                    && t.def.category != ThingCategory.Item)
                    return false;
            }

            return itemCount < c.GetMaxItemsAllowedInCell(map);
        }
    }
}
