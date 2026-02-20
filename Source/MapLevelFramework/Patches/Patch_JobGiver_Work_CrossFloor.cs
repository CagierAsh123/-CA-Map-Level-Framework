using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MapLevelFramework.CrossFloor
{
    /// <summary>
    /// 在 job 分配层面处理跨楼层工作和材料搬运。
    /// 当 pawn 在当前楼层找不到工作时，按优先级：
    ///   P1: 本层有材料，其他层蓝图需要 → HaulToStairs（搬到楼梯传送）
    ///   P2: 本层有蓝图缺材料，其他层有材料 → UseStairs 去取
    ///   P3: 本层无工作，其他层有 → UseStairs 过去
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage))]
    public static class Patch_JobGiver_Work_CrossFloor
    {
        private static readonly Dictionary<int, int> lastCrossFloorTick
            = new Dictionary<int, int>();
        private const int CooldownTicks = 600;

        private static bool DebugLog => MapLevelFrameworkMod.Settings?.debugPathfindingAndJob ?? false;

        private static string ElevLabel(int elev)
        {
            if (elev > 0) return $"{elev + 1}F";
            if (elev < 0) return $"B{-elev}";
            return "1F";
        }

        private static void LogPJ(Pawn pawn, string msg)
        {
            if (DebugLog)
                Log.Message($"【MLF】寻路与job检测-{pawn.LabelShort}—{msg}");
        }

        public static void Postfix(
            ref ThinkResult __result,
            JobGiver_Work __instance,
            Pawn pawn)
        {
            if (__result.Job != null) return;
            if (__instance.emergency) return;

            Map pawnMap = pawn?.Map;
            if (pawnMap == null) return;
            if (!pawnMap.IsPartOfFloorSystem()) return;
            if (!pawn.IsColonist) return;

            int pawnElev = FloorMapUtility.GetMapElevation(pawnMap);
            int curTick = Find.TickManager?.TicksGame ?? 0;
            if (lastCrossFloorTick.TryGetValue(pawn.thingIDNumber, out int lastTick)
                && curTick - lastTick < CooldownTicks)
            {
                LogPJ(pawn, $"在{ElevLabel(pawnElev)}，冷却中({curTick - lastTick}/{CooldownTicks})，跳过");
                return;
            }

            LogPJ(pawn, $"在{ElevLabel(pawnElev)}，本层无工作，开始跨层扫描");

            // P1: 本层材料 → 其他层蓝图需要 → HaulToStairs
            Job haulJob = TryCreateHaulToStairsJob(pawn, pawnMap);
            if (haulJob != null)
            {
                LogPJ(pawn, $"P1命中→搬运{haulJob.targetA.Thing?.LabelShort}到楼梯");
                lastCrossFloorTick[pawn.thingIDNumber] = curTick;
                __result = new ThinkResult(haulJob, __instance, null, false);
                return;
            }

            // P2: 本层蓝图缺材料，其他层有 → UseStairs 去取
            Job fetchJob = TryCreateFetchMaterialJob(pawn, pawnMap);
            if (fetchJob != null)
            {
                int destElev = fetchJob.targetB.IsValid ? fetchJob.targetB.Cell.x : -999;
                LogPJ(pawn, $"P2命中→去{ElevLabel(destElev)}取材料");
                lastCrossFloorTick[pawn.thingIDNumber] = curTick;
                __result = new ThinkResult(fetchJob, __instance, null, false);
                return;
            }

            // P3: 本层无工作 → 去有工作的楼层
            Job goJob = TryCreateGoToWorkFloorJob(pawn, pawnMap);
            if (goJob != null)
            {
                int destElev = goJob.targetB.IsValid ? goJob.targetB.Cell.x : -999;
                LogPJ(pawn, $"P3命中→去{ElevLabel(destElev)}找工作");
                lastCrossFloorTick[pawn.thingIDNumber] = curTick;
                __result = new ThinkResult(goJob, __instance, null, false);
                return;
            }

            LogPJ(pawn, "P1/P2/P3均未命中，无跨层工作");
        }

        /// <summary>
        /// P1: 本层有材料，其他层需要 → 搬到楼梯传送过去。
        /// 覆盖：建造材料、燃料、Bill 原料、药品、囚犯食物。
        /// </summary>
        private static Job TryCreateHaulToStairsJob(Pawn pawn, Map pawnMap)
        {
            foreach (Map otherMap in pawnMap.BaseMapAndFloorMaps())
            {
                if (otherMap == pawnMap) continue;

                // 收集该层所有材料需求
                CollectMaterialNeeds(otherMap, needsBuffer);

                for (int i = 0; i < needsBuffer.Count; i++)
                {
                    var need = needsBuffer[i];

                    // 扣除目标层已有的散落材料
                    int looseOnDest = CountLooseMaterial(otherMap, need.thingDef);
                    int actualNeeded = need.count - looseOnDest;
                    if (actualNeeded <= 0) continue;

                    // 在本层找这个材料
                    Thing material = FindMaterialOnMap(pawnMap, pawn, need.thingDef);
                    if (material == null) continue;

                    // 电梯模式：直接找通往目标层的楼梯（楼梯井）
                    int otherElev = FloorMapUtility.GetMapElevation(otherMap);
                    Building_Stairs stairs =
                        FloorMapUtility.FindStairsToFloor(pawn, pawnMap, otherElev);
                    if (stairs == null) break; // 这层没楼梯井连通，跳过整层

                    int toCarry = Math.Min(actualNeeded, material.stackCount);
                    Job job = JobMaker.MakeJob(
                        MLF_JobDefOf.MLF_HaulToStairs, material, stairs);
                    job.count = toCarry;
                    job.targetC = new IntVec3(otherElev, 0, 0);
                    return job;
                }
            }
            return null;
        }

        /// <summary>
        /// P2: 本层有需求但缺材料，其他层有 → UseStairs 去取。
        /// </summary>
        private static Job TryCreateFetchMaterialJob(Pawn pawn, Map pawnMap)
        {
            // 收集本层的材料需求
            CollectMaterialNeeds(pawnMap, needsBuffer);

            for (int i = 0; i < needsBuffer.Count; i++)
            {
                var need = needsBuffer[i];

                // 本层有这个材料？跳过（原版会处理）
                if (pawnMap.listerThings.ThingsOfDef(need.thingDef).Count > 0)
                    continue;

                // 其他层找
                foreach (Map otherMap in pawnMap.BaseMapAndFloorMaps())
                {
                    if (otherMap == pawnMap) continue;
                    if (otherMap.listerThings.ThingsOfDef(need.thingDef).Count == 0)
                        continue;

                    int otherElev = FloorMapUtility.GetMapElevation(otherMap);
                    Building_Stairs stairs =
                        FloorMapUtility.FindStairsToFloor(pawn, pawnMap, otherElev);
                    if (stairs == null) continue;

                    Job job = JobMaker.MakeJob(MLF_JobDefOf.MLF_UseStairs, stairs);
                    job.targetB = new IntVec3(otherElev, 0, 0);
                    return job;
                }
            }
            return null;
        }

        /// <summary>
        /// P3: 本层无工作 → 去有工作的最近楼层。
        /// FloorHasWork 按 pawn 的工作设置过滤，不会白跑。
        /// </summary>
        private static Job TryCreateGoToWorkFloorJob(Pawn pawn, Map pawnMap)
        {
            int currentElev = FloorMapUtility.GetMapElevation(pawnMap);
            Map bestFloor = null;
            int bestElevDist = int.MaxValue;
            int bestElev = 0;

            foreach (Map otherMap in pawnMap.BaseMapAndFloorMaps())
            {
                if (otherMap == pawnMap) continue;
                if (!FloorHasWork(otherMap, pawn)) continue;

                int otherElev = FloorMapUtility.GetMapElevation(otherMap);
                int dist = Math.Abs(otherElev - currentElev);
                if (dist < bestElevDist)
                {
                    bestElevDist = dist;
                    bestFloor = otherMap;
                    bestElev = otherElev;
                }
            }

            if (bestFloor == null) return null;

            // 电梯模式：直接找通往目标层的楼梯
            Building_Stairs stairs =
                FloorMapUtility.FindStairsToFloor(pawn, pawnMap, bestElev);
            if (stairs == null) return null;

            Job job = JobMaker.MakeJob(MLF_JobDefOf.MLF_UseStairs, stairs);
            job.targetB = new IntVec3(bestElev, 0, 0);
            return job;
        }

        // ========== 材料需求收集系统 ==========

        private struct MaterialNeed
        {
            public ThingDef thingDef;
            public int count;
        }

        private static readonly List<MaterialNeed> needsBuffer = new List<MaterialNeed>();

        /// <summary>
        /// 收集指定地图上所有材料需求（建造、加油、Bill、医疗、囚犯食物）。
        /// </summary>
        private static void CollectMaterialNeeds(Map map, List<MaterialNeed> result)
        {
            result.Clear();

            // 1. 建造材料（蓝图/框架）
            AddConstructionNeeds(map, result);

            // 2. 加油（CompRefuelable）
            AddRefuelNeeds(map, result);

            // 3. Bill 原料（工作台）
            AddBillNeeds(map, result);

            // 4. 医疗（需要治疗的 pawn）
            AddMedicineNeeds(map, result);

            // 5. 囚犯食物
            AddPrisonerFoodNeeds(map, result);
        }

        private static void AddConstructionNeeds(Map map, List<MaterialNeed> result)
        {
            var constructibles = GetConstructibles(map);
            for (int i = 0; i < constructibles.Count; i++)
            {
                IConstructible c = constructibles[i] as IConstructible;
                if (c == null) continue;
                foreach (var cost in c.TotalMaterialCost())
                {
                    int needed = c.ThingCountNeeded(cost.thingDef);
                    if (needed > 0)
                        result.Add(new MaterialNeed { thingDef = cost.thingDef, count = needed });
                }
            }
        }

        private static void AddRefuelNeeds(Map map, List<MaterialNeed> result)
        {
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                CompRefuelable comp = b.TryGetComp<CompRefuelable>();
                if (comp == null || comp.IsFull) continue;

                ThingDef fuelDef = comp.Props.fuelFilter?.AnyAllowedDef;
                if (fuelDef == null) continue;

                int needed = (int)Math.Ceiling((double)comp.GetFuelCountToFullyRefuel());
                if (needed > 0)
                    result.Add(new MaterialNeed { thingDef = fuelDef, count = needed });
            }
        }

        private static void AddBillNeeds(Map map, List<MaterialNeed> result)
        {
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                IBillGiver billGiver = b as IBillGiver;
                if (billGiver == null) continue;

                BillStack bills = billGiver.BillStack;
                if (bills == null || bills.Count == 0) continue;

                for (int bi = 0; bi < bills.Count; bi++)
                {
                    Bill bill = bills[bi];
                    if (!bill.ShouldDoNow()) continue;

                    foreach (IngredientCount ing in bill.recipe.ingredients)
                    {
                        // 取 filter 中第一个允许的 ThingDef
                        ThingDef ingDef = ing.filter?.AnyAllowedDef;
                        if (ingDef == null) continue;

                        // 本层已有这个原料 → 原版处理，跳过
                        if (map.listerThings.ThingsOfDef(ingDef).Count > 0)
                            continue;

                        int needed = (int)Math.Ceiling(
                            (double)ing.GetBaseCount());
                        if (needed > 0)
                            result.Add(new MaterialNeed { thingDef = ingDef, count = needed });
                    }
                }
            }
        }

        private static void AddMedicineNeeds(Map map, List<MaterialNeed> result)
        {
            // 有需要治疗的殖民者但本层没药
            bool needsMedicine = false;
            var colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                if (colonists[i].health?.HasHediffsNeedingTend(false) ?? false)
                {
                    needsMedicine = true;
                    break;
                }
            }
            if (!needsMedicine) return;

            // 本层有药就不管
            if (map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine).Count > 0)
                return;

            // 需要药但没有 → 加入需求（用 MedicineIndustrial 作为默认）
            result.Add(new MaterialNeed
            {
                thingDef = ThingDefOf.MedicineIndustrial,
                count = 1
            });
            // 也接受草药
            result.Add(new MaterialNeed
            {
                thingDef = ThingDefOf.MedicineHerbal,
                count = 1
            });
        }

        private static void AddPrisonerFoodNeeds(Map map, List<MaterialNeed> result)
        {
            if (map.mapPawns.PrisonersOfColonySpawned.Count == 0) return;

            // 本层有食物就不管
            if (map.listerThings.ThingsInGroup(
                    ThingRequestGroup.FoodSourceNotPlantOrTree).Count > 0)
                return;

            // 需要食物但没有 → 加入需求（用 MealSimple 作为默认）
            result.Add(new MaterialNeed
            {
                thingDef = ThingDefOf.MealSimple,
                count = 1
            });
        }

        // ========== 工具方法 ==========

        /// <summary>
        /// 获取地图上所有蓝图和框架。
        /// </summary>
        private static List<Thing> GetConstructibles(Map map)
        {
            var result = new List<Thing>();
            result.AddRange(
                map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));
            result.AddRange(
                map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame));
            return result;
        }

        /// <summary>
        /// 统计地图上散落的指定材料总数（已 Spawn、未被安装的）。
        /// 用于避免重复搬运：目标层已经有足够材料就不再搬了。
        /// </summary>
        private static int CountLooseMaterial(Map map, ThingDef matDef)
        {
            int total = 0;
            var things = map.listerThings.ThingsOfDef(matDef);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].Spawned)
                    total += things[i].stackCount;
            }
            return total;
        }

        /// <summary>
        /// 在指定地图上找 pawn 可达的、未被禁止的材料。
        /// </summary>
        private static Thing FindMaterialOnMap(
            Map map, Pawn pawn, ThingDef matDef)
        {
            var things = map.listerThings.ThingsOfDef(matDef);
            Thing best = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (!t.Spawned) continue;
                if (t.IsForbidden(pawn)) continue;
                if (!pawn.CanReserve(t)) continue;
                if (!pawn.CanReach(t, PathEndMode.ClosestTouch,
                        Danger.Deadly)) continue;

                float dist = t.Position.DistanceToSquared(pawn.Position);
                if (dist < bestDist)
                {
                    best = t;
                    bestDist = dist;
                }
            }
            return best;
        }

        /// <summary>
        /// 检查该楼层是否有这个 pawn 能做的工作。
        /// 根据 pawn 的 workSettings 过滤，避免白跑。
        /// </summary>
        private static bool FloorHasWork(Map map, Pawn pawn)
        {
            var ws = pawn.workSettings;
            if (ws == null) return false;

            int elev = FloorMapUtility.GetMapElevation(map);

            // 建造（蓝图、框架）
            if (ws.WorkIsActive(WorkTypeDefOf.Construction))
            {
                if (map.listerThings.ThingsInGroup(
                        ThingRequestGroup.Blueprint).Count > 0)
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 蓝图(Construction)"); return true; }
                if (map.listerThings.ThingsInGroup(
                        ThingRequestGroup.BuildingFrame).Count > 0)
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 框架(Construction)"); return true; }
            }

            // 搬运
            if (ws.WorkIsActive(WorkTypeDefOf.Hauling))
            {
                if (map.listerHaulables
                        .ThingsPotentiallyNeedingHauling().Count > 0)
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 搬运(Hauling)"); return true; }
            }

            // 清洁
            if (ws.WorkIsActive(WorkTypeDefOf.Cleaning))
            {
                var filthLister = map.listerFilthInHomeArea;
                if (filthLister != null
                    && filthLister.FilthInHomeArea.Count > 0)
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 清洁(Cleaning)"); return true; }
            }

            // 医疗：倒地或需要治疗的殖民者
            if (ws.WorkIsActive(WorkTypeDefOf.Doctor))
            {
                var colonists = map.mapPawns.FreeColonistsSpawned;
                for (int i = 0; i < colonists.Count; i++)
                {
                    Pawn p = colonists[i];
                    if (p.Downed || (p.health?.HasHediffsNeedingTend(false) ?? false))
                    { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 医疗(Doctor)"); return true; }
                }
            }

            // 监管：有囚犯
            if (ws.WorkIsActive(WorkTypeDefOf.Warden))
            {
                if (map.mapPawns.PrisonersOfColonySpawned.Count > 0)
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 监管(Warden)"); return true; }
            }

            // 开采
            if (ws.WorkIsActive(WorkTypeDefOf.Mining))
            {
                if (HasAnyDesignation(map, DesignationDefOf.Mine))
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 开采(Mining)"); return true; }
            }

            // 建造类指示（拆除、打磨）
            if (ws.WorkIsActive(WorkTypeDefOf.Construction))
            {
                if (HasAnyDesignation(map, DesignationDefOf.Deconstruct)
                    || HasAnyDesignation(map, DesignationDefOf.SmoothFloor)
                    || HasAnyDesignation(map, DesignationDefOf.SmoothWall))
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 拆除/打磨(Construction)"); return true; }
            }

            // 种植类指示（割除、收获）
            if (ws.WorkIsActive(WorkTypeDefOf.PlantCutting))
            {
                if (HasAnyDesignation(map, DesignationDefOf.CutPlant)
                    || HasAnyDesignation(map, DesignationDefOf.HarvestPlant))
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 割除/收获(PlantCutting)"); return true; }
            }

            // 狩猎
            if (ws.WorkIsActive(WorkTypeDefOf.Hunting))
            {
                if (HasAnyDesignation(map, DesignationDefOf.Hunt))
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 狩猎(Hunting)"); return true; }
            }

            // 驯服
            if (ws.WorkIsActive(WorkTypeDefOf.Handling))
            {
                if (HasAnyDesignation(map, DesignationDefOf.Tame))
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 驯服(Handling)"); return true; }
            }

            // 灭火
            if (ws.WorkIsActive(WorkTypeDefOf.Firefighter))
            {
                if (map.listerThings.ThingsOfDef(ThingDefOf.Fire).Count > 0)
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 灭火(Firefighter)"); return true; }
            }

            // 研究
            if (ws.WorkIsActive(WorkTypeDefOf.Research))
            {
                if (Find.ResearchManager?.GetProject() != null
                    && map.listerBuildings.ColonistsHaveResearchBench())
                { LogPJ(pawn, $"  {ElevLabel(elev)}有工作: 研究(Research)"); return true; }
            }

            LogPJ(pawn, $"  {ElevLabel(elev)}无匹配工作");
            return false;
        }

        private static bool HasAnyDesignation(Map map, DesignationDef def)
        {
            foreach (var _ in map.designationManager.SpawnedDesignationsOfDef(def))
                return true;
            return false;
        }

        public static void ClearCooldowns()
        {
            lastCrossFloorTick.Clear();
        }
    }
}
