using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MapLevelFramework
{
    /// <summary>
    /// 楼梯建筑 - 放置后创建/更新目标层级。
    /// 上楼梯：扫描屋顶连通区域创建上层。
    /// 下楼梯：创建地下层（初始一格 + 边界岩石）。
    ///
    /// targetElevation 表示这个楼梯连接到的目标层级：
    /// - 正数 = 上层（1→2F, 2→3F）
    /// - 0 = 地面层
    /// - 负数 = 地下层（-1→B1, -2→B2）
    /// </summary>
    public class Building_Stairs : Building
    {
        /// <summary>
        /// 连接的目标 elevation。
        /// </summary>
        public int targetElevation = 1;

        /// <summary>
        /// 是否是自动生成的楼梯（不触发层级创建）。
        /// </summary>
        private bool autoSpawned;

        /// <summary>
        /// 该楼梯 Def 是否标记为下楼方向。
        /// </summary>
        public bool GoesDown => def.GetModExtension<StairsExtension>()?.goesDown ?? false;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);

            // 注册到楼梯缓存（包括 respawningAfterLoad 和 autoSpawned）
            StairsCache.Register(this);

            if (respawningAfterLoad) return;
            if (autoSpawned) return;
            if (Patch_GravshipLaunch.suppressStairsLevelOps) return;

            bool goesDown = GoesDown;

            // 先检查是否在子地图上建造
            if (LevelManager.IsLevelMap(map, out var parentMgr, out var levelData))
            {
                // 在子地图上建造
                if (goesDown)
                    targetElevation = levelData.elevation - 1; // 继续往下
                else
                    targetElevation = levelData.elevation + 1; // 继续往上

                // targetElevation == 0 表示连接回地面层，不需要创建层级
                if (targetElevation == 0)
                    return;

                if (goesDown)
                    CreateUndergroundLevel(parentMgr);
                else
                    CreateOrUpdateLevel(parentMgr, map);
                return;
            }

            // 在基地图上建造
            var mgr = LevelManager.GetManager(map);
            if (mgr != null)
            {
                if (goesDown)
                {
                    targetElevation = -1; // 基地图 → B1
                    CreateUndergroundLevel(mgr);
                }
                else
                {
                    CreateOrUpdateLevel(mgr, map);
                }
            }
        }

        // ========== Gizmo 按钮 ==========

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos())
                yield return g;

            // 获取 LevelManager
            LevelManager mgr;
            if (LevelManager.IsLevelMap(this.Map, out var parentMgr, out _))
                mgr = parentMgr;
            else
                mgr = LevelManager.GetManager(this.Map);

            if (mgr == null) yield break;

            bool levelExists = mgr.GetLevel(targetElevation) != null;

            if (!levelExists)
            {
                // 创建层级按钮
                var stairs = this;
                var localMgr = mgr;
                yield return new Command_Action
                {
                    defaultLabel = "创建层级",
                    defaultDesc = $"创建 {GetElevationLabel(targetElevation)} 层级。",
                    icon = TexCommand.DesirePower,
                    action = delegate
                    {
                        // 取消任何活跃的建造指示器，防止幽灵残留
                        Find.DesignatorManager.Deselect();
                        if (stairs.GoesDown)
                            stairs.CreateUndergroundLevel(localMgr);
                        else
                            stairs.CreateOrUpdateLevel(localMgr, stairs.Map);
                    }
                };
            }
            else
            {
                // 销毁层级按钮
                var localMgr = mgr;
                int localElev = targetElevation;
                yield return new Command_Action
                {
                    defaultLabel = "销毁层级",
                    defaultDesc = $"销毁 {GetElevationLabel(targetElevation)} 层级。该层上的所有物品和建筑将被移除，殖民者会被转移回地面。",
                    icon = TexCommand.ClearPrioritizedWork,
                    action = delegate
                    {
                        Find.DesignatorManager.Deselect();
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                            $"确定要销毁 {GetElevationLabel(localElev)} 吗？\n该层上的所有建筑和物品将被移除。",
                            delegate
                            {
                                localMgr.RemoveLevel(localElev);
                            },
                            destructive: true));
                    }
                };
            }
        }

        private static string GetElevationLabel(int elevation)
        {
            if (elevation > 0) return $"{elevation + 1}F";
            if (elevation < 0) return $"B{-elevation}";
            return "地面";
        }

        // ========== 右键菜单 ==========

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (var opt in base.GetFloatMenuOptions(selPawn))
                yield return opt;

            // pawn 必须和楼梯在同一张地图
            if (selPawn.Map != this.Map) yield break;

            if (!StairTransferUtility.TryGetTransferTarget(this, out Map destMap, out IntVec3 destPos))
                yield break;

            // 判断上楼还是下楼
            int currentElevation = GetCurrentElevation();
            string label = targetElevation > currentElevation ? "上楼" : "下楼";

            yield return new FloatMenuOption(label, delegate
            {
                Job job = JobMaker.MakeJob(MLF_JobDefOf.MLF_UseStairs, this);
                selPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            });
        }

        /// <summary>
        /// 获取楼梯所在的当前层级 elevation。地面 = 0。
        /// </summary>
        public int GetCurrentElevation()
        {
            if (LevelManager.IsLevelMap(this.Map, out _, out var levelData))
                return levelData.elevation;
            return 0; // 地面
        }

        // ========== 地下层级创建 ==========

        /// <summary>
        /// 创建地下层级。不扫描屋顶，直接创建地下地图，初始可用区域为楼梯一格。
        /// </summary>
        public void CreateUndergroundLevel(LevelManager mgr)
        {
            // 楼梯位置作为初始区域（1格）
            CellRect bounds = new CellRect(Position.x, Position.z, 1, 1);
            var initialCells = new HashSet<IntVec3> { Position };

            var existing = mgr.GetLevel(targetElevation);
            if (existing != null)
            {
                // 地下层已存在：加入新的楼梯入口
                if (existing.usableCells == null)
                    existing.usableCells = new HashSet<IntVec3>();
                existing.usableCells.Add(Position);
                existing.area = RecalcBounds(existing.usableCells);
                existing.RebuildActiveSections();

                // 在子地图铺地板 + 放楼梯 + 生成边界岩石
                TerrainDef dirtFloor = DefDatabase<TerrainDef>.GetNamedSilentFail("MLF_DirtFloor");
                if (dirtFloor != null && existing.LevelMap != null)
                    existing.LevelMap.terrainGrid.SetTerrain(Position, dirtFloor);

                SpawnStairsOnLevel(existing.LevelMap, Position);
                RockFrontierUtility.SpawnInitialFrontier(existing.LevelMap, Position, existing);

                // 新入口格子标记为 Home（清洁等 WorkGiver 依赖）
                LevelManager.ExpandHomeArea(existing.LevelMap, existing);

                Log.Message($"[MLF] Updated underground level {targetElevation}: added entrance at {Position}");
            }
            else
            {
                // 创建新地下层
                var level = mgr.RegisterLevel(targetElevation, bounds, isUnderground: true);
                if (level != null)
                {
                    level.usableCells = initialCells;
                    level.RebuildActiveSections();

                    // 在子地图放楼梯 + 生成边界岩石
                    SpawnStairsOnLevel(level.LevelMap, Position);
                    RockFrontierUtility.SpawnInitialFrontier(level.LevelMap, Position, level);

                    Log.Message($"[MLF] Created underground level {targetElevation} at {Position}");
                }
            }
        }

        // ========== 上层层级创建 ==========

        /// <summary>
        /// 扫描有屋顶的连通区域，创建或更新层级。
        /// scanMap 是楼梯所在的地图（用于扫描屋顶），可能是基地图或子地图。
        /// </summary>
        public void CreateOrUpdateLevel(LevelManager mgr, Map scanMap)
        {
            HashSet<IntVec3> roofedCells = ScanRoofedArea(scanMap, Position);

            if (roofedCells.Count == 0)
            {
                Log.Warning("[MLF] Stairs placed but no roofed cells found nearby.");
                return;
            }

            // 计算包围矩形
            int minX = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxZ = int.MinValue;
            foreach (IntVec3 cell in roofedCells)
            {
                if (cell.x < minX) minX = cell.x;
                if (cell.z < minZ) minZ = cell.z;
                if (cell.x > maxX) maxX = cell.x;
                if (cell.z > maxZ) maxZ = cell.z;
            }
            CellRect bounds = new CellRect(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);

            var existing = mgr.GetLevel(targetElevation);
            if (existing != null)
            {
                // 层级已存在：合并新区域到已有区域（不是替换）
                if (existing.usableCells == null)
                    existing.usableCells = new HashSet<IntVec3>();
                existing.usableCells.UnionWith(roofedCells);

                // 重新计算包围矩形（从所有 usableCells）
                existing.area = RecalcBounds(existing.usableCells);

                // 刷新子地图地形（新增的格子铺地板）
                RefreshLevelTerrain(existing, mgr.map);
                existing.RebuildActiveSections();

                // 在子地图同位置放置楼梯（如果还没有）
                SpawnStairsOnLevel(existing.LevelMap, Position);

                // 新增格子标记为 Home（清洁等 WorkGiver 依赖）
                LevelManager.ExpandHomeArea(existing.LevelMap, existing);

                Log.Message($"[MLF] Updated level {targetElevation}: total {existing.usableCells.Count} cells, bounds={existing.area}");
            }
            else
            {
                // 创建新层级
                var level = mgr.RegisterLevel(targetElevation, bounds);
                if (level != null)
                {
                    level.usableCells = roofedCells;
                    level.RebuildActiveSections();

                    // 在子地图同位置放置楼梯
                    SpawnStairsOnLevel(level.LevelMap, Position);

                    Log.Message($"[MLF] Created level {targetElevation}: {roofedCells.Count} cells, bounds={bounds}");
                }
            }
        }

        /// <summary>
        /// 在子地图的指定位置放置楼梯（如果该位置还没有楼梯）。
        /// 自动生成的楼梯指向回来的方向。
        /// </summary>
        private void SpawnStairsOnLevel(Map levelMap, IntVec3 pos)
        {
            if (levelMap == null || !pos.InBounds(levelMap)) return;

            // 检查该位置是否已有楼梯
            var things = levelMap.thingGrid.ThingsListAtFast(pos);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Building_Stairs) return;
            }

            // 自动生成的楼梯方向与当前楼梯相反
            // 当前是下楼梯 → 子地图上生成上楼梯（回到上层）
            // 当前是上楼梯 → 子地图上生成下楼梯（回到下层）... 不对，上楼梯的子地图楼梯也是回来
            // 统一逻辑：自动生成的楼梯 targetElevation = 当前楼梯所在层
            var stairsDef = this.def; // 使用相同的 def（视觉一致）
            var stairs = (Building_Stairs)ThingMaker.MakeThing(stairsDef, this.Stuff);
            stairs.autoSpawned = true;
            stairs.targetElevation = GetCurrentElevation();
            GenSpawn.Spawn(stairs, pos, levelMap);
        }

        /// <summary>
        /// 从起点 FloodFill 扫描所有有屋顶的连通格子。
        /// </summary>
        private static HashSet<IntVec3> ScanRoofedArea(Map map, IntVec3 start)
        {
            var result = new HashSet<IntVec3>();
            if (!start.InBounds(map)) return result;

            var queue = new Queue<IntVec3>();
            var visited = new HashSet<IntVec3>();

            // 起点本身不要求有屋顶（楼梯口可以露天）
            queue.Enqueue(start);
            visited.Add(start);
            if (map.roofGrid.RoofAt(start) != null)
                result.Add(start);

            while (queue.Count > 0)
            {
                IntVec3 current = queue.Dequeue();
                for (int i = 0; i < 4; i++)
                {
                    IntVec3 neighbor = current + GenAdj.CardinalDirections[i];
                    if (!neighbor.InBounds(map)) continue;
                    if (visited.Contains(neighbor)) continue;
                    visited.Add(neighbor);

                    if (map.roofGrid.RoofAt(neighbor) != null)
                    {
                        result.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 刷新已有层级的地形（屋顶变化后调用）。
        /// </summary>
        private static void RefreshLevelTerrain(LevelData level, Map hostMap)
        {
            Map levelMap = level.LevelMap;
            if (levelMap == null) return;

            TerrainDef floor = DefDatabase<TerrainDef>.GetNamedSilentFail("WoodPlankFloor")
                ?? TerrainDefOf.WoodPlankFloor;
            TerrainDef openAir = DefDatabase<TerrainDef>.GetNamedSilentFail("MLF_OpenAir");
            TerrainDef levelBase = DefDatabase<TerrainDef>.GetNamedSilentFail("MLF_LevelBase");
            if (openAir == null) return;

            // 获取 underGrid 用于设置底层地形
            var underGridField = typeof(TerrainGrid).GetField("underGrid",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            TerrainDef[] underGrid = underGridField?.GetValue(levelMap.terrainGrid) as TerrainDef[];

            foreach (IntVec3 cell in level.area)
            {
                if (!cell.InBounds(levelMap)) continue;

                if (level.usableCells != null && level.usableCells.Contains(cell))
                {
                    if (levelMap.terrainGrid.TerrainAt(cell) == openAir)
                    {
                        levelMap.terrainGrid.SetTerrain(cell, floor);
                    }
                    // 确保底层是 LevelBase（支持所有地板建造）
                    if (underGrid != null && levelBase != null)
                    {
                        int index = levelMap.cellIndices.CellToIndex(cell);
                        underGrid[index] = levelBase;
                    }
                }
                else
                {
                    levelMap.terrainGrid.SetTerrain(cell, openAir);
                }

                levelMap.mapDrawer.MapMeshDirty(cell, MapMeshFlagDefOf.Terrain);
            }
        }

        /// <summary>
        /// 从格子集合计算包围矩形。
        /// </summary>
        private static CellRect RecalcBounds(HashSet<IntVec3> cells)
        {
            int minX = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxZ = int.MinValue;
            foreach (IntVec3 cell in cells)
            {
                if (cell.x < minX) minX = cell.x;
                if (cell.z < minZ) minZ = cell.z;
                if (cell.x > maxX) maxX = cell.x;
                if (cell.z > maxZ) maxZ = cell.z;
            }
            return new CellRect(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);
        }

        // ========== 销毁 ==========

        public override void DeSpawn(DestroyMode mode)
        {
            Map stairMap = this.Map;
            int targetElev = this.targetElevation;
            bool wasAutoSpawned = this.autoSpawned;

            // 从缓存移除（必须在 base.DeSpawn 之前，因为之后 Map 为 null）
            StairsCache.Deregister(this, stairMap);

            base.DeSpawn(mode);

            // 逆重飞船起飞期间不触发层级销毁（数据已单独捕获）
            if (Patch_GravshipLaunch.suppressStairsLevelOps) return;

            // 自动生成的楼梯（子地图侧）不触发层级销毁
            if (wasAutoSpawned) return;

            // 检查同地图上是否还有其他楼梯连接到同一层级
            if (HasOtherStairsToElevation(stairMap, targetElev))
                return;

            // 这是最后一个楼梯 → 销毁层级（含级联 + pawn 转移）
            LevelManager mgr;
            if (LevelManager.IsLevelMap(stairMap, out var parentMgr, out _))
                mgr = parentMgr;
            else
                mgr = LevelManager.GetManager(stairMap);

            mgr?.RemoveLevel(targetElev);
        }

        /// <summary>
        /// 检查地图上是否还有其他楼梯连接到指定 elevation。
        /// </summary>
        private static bool HasOtherStairsToElevation(Map map, int targetElevation)
        {
            if (map == null) return false;
            var list = StairsCache.GetStairs(map, targetElevation);
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].autoSpawned)
                    return true;
            }
            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref targetElevation, "targetElevation", 1);
            Scribe_Values.Look(ref autoSpawned, "autoSpawned", false);
        }
    }
}
