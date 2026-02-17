using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// 跨层级工作扫描 - 让 pawn 在当前地图找不到工作时，自动去其他楼层找工作。
    /// 使用 CrossLevelJobUtility 共用的跨层扫描逻辑。
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Work), "TryIssueJobPackage")]
    public static class Patch_CrossLevelJobScan
    {
        public static void Postfix(
            ref ThinkResult __result,
            JobGiver_Work __instance,
            Pawn pawn,
            JobIssueParams jobParams)
        {
            if (CrossLevelJobUtility.Scanning) return;
            if (__result != ThinkResult.NoJob) return;
            if (pawn?.Map == null || !pawn.Spawned) return;

            Job stairJob = CrossLevelJobUtility.TryCrossLevelScan(pawn, () =>
            {
                ThinkResult result = __instance.TryIssueJobPackage(pawn, jobParams);
                return result != ThinkResult.NoJob ? result.Job : null;
            });

            if (stairJob != null)
            {
                __result = new ThinkResult(stairJob, __instance, null, false);
                return;
            }

            // 跨层材料搬运：本层有材料，其他层有需求 → 拿材料走楼梯送过去
            Job fetchJob = TryCrossLevelMaterialFetch(pawn);
            if (fetchJob != null)
            {
                __result = new ThinkResult(fetchJob, __instance, null, false);
                return;
            }
        }

        /// <summary>
        /// 通用跨层材料搬运扫描：扫描其他楼层的需求（建造、加油等），
        /// 如果本层有所需材料，返回 MLF_ReturnWithMaterial job。
        /// </summary>
        private static Job TryCrossLevelMaterialFetch(Pawn pawn)
        {
            if (pawn?.Map == null) return null;
            if (CrossLevelJobUtility.IsOnCooldown(pawn, CrossLevelJobUtility.FetchMaterialCooldownTicks))
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
            if (mgr == null || mgr.LevelCount == 0) return null;

            // 检查 pawn 启用了哪些工作类型
            bool canConstruct = pawn.workSettings != null
                && pawn.workSettings.WorkIsActive(WorkTypeDefOf.Construction);
            bool canHaul = pawn.workSettings != null
                && pawn.workSettings.WorkIsActive(WorkTypeDefOf.Hauling);
            bool canDoctor = pawn.workSettings != null
                && pawn.workSettings.WorkIsActive(WorkTypeDefOf.Doctor);
            bool canWarden = pawn.workSettings != null
                && pawn.workSettings.WorkIsActive(WorkTypeDefOf.Warden);
            bool canHandling = pawn.workSettings != null
                && pawn.workSettings.WorkIsActive(WorkTypeDefOf.Handling);
            WorkTypeDef childcareDef = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Childcare");
            bool canChildcare = childcareDef != null && pawn.workSettings != null
                && pawn.workSettings.WorkIsActive(childcareDef);

            if (!canConstruct && !canHaul && !canDoctor && !canWarden
                && !canHandling && !canChildcare) return null;

            // 按 pawn 工作优先级排序扫描类别（priority 1=最高, 4=最低）
            // category: 0=Construction, 1=Hauling(Refuel+Bill), 2=Doctor(Medicine+PatientFeed+Hemogen),
            //           3=Warden(WardFeed), 4=Handling(AnimalFeed), 5=Childcare(BabyFeed)
            var workPriorities = new List<(int priority, int category)>(6);
            if (canConstruct)
                workPriorities.Add((pawn.workSettings.GetPriority(WorkTypeDefOf.Construction), 0));
            if (canHaul)
                workPriorities.Add((pawn.workSettings.GetPriority(WorkTypeDefOf.Hauling), 1));
            if (canDoctor)
                workPriorities.Add((pawn.workSettings.GetPriority(WorkTypeDefOf.Doctor), 2));
            if (canWarden)
                workPriorities.Add((pawn.workSettings.GetPriority(WorkTypeDefOf.Warden), 3));
            if (canHandling)
                workPriorities.Add((pawn.workSettings.GetPriority(WorkTypeDefOf.Handling), 4));
            if (canChildcare)
                workPriorities.Add((pawn.workSettings.GetPriority(childcareDef), 5));
            workPriorities.Sort((a, b) => a.priority.CompareTo(b.priority));

            int currentElev = CrossLevelJobUtility.GetMapElevation(pawnMap, mgr, baseMap);

            // 收集其他楼层地图
            var otherMaps = new List<(Map map, int elevation)>();
            if (pawnMap != baseMap)
                otherMaps.Add((baseMap, 0));
            foreach (var level in mgr.AllLevels)
            {
                if (level.LevelMap != null && level.LevelMap != pawnMap)
                    otherMaps.Add((level.LevelMap, level.elevation));
            }
            if (otherMaps.Count == 0) return null;

            // 按距离排序
            otherMaps.Sort((a, b) =>
                System.Math.Abs(a.elevation - currentElev)
                    .CompareTo(System.Math.Abs(b.elevation - currentElev)));

            // ========== Phase 1: 其他层有需求，pawn 当前层（或第三层）有材料 ==========
            foreach (var (otherMap, targetElev) in otherMaps)
            {
                // 确保有楼梯可以到达
                int nextElev = targetElev > currentElev
                    ? currentElev + 1
                    : currentElev - 1;
                if (CrossLevelJobUtility.FindStairsToElevation(pawn, pawnMap, nextElev) == null)
                    continue;

                for (int wi = 0; wi < workPriorities.Count; wi++)
                {
                    Job job = TryFetchByCategory(workPriorities[wi].category,
                        pawn, pawnMap, otherMap, targetElev, otherMaps, currentElev);
                    if (job != null) return job;
                }
            }

            // ========== Phase 2: 反向取材 - 当前层有需求，其他层有材料 ==========
            // (pawn 在需求层，需要去其他层取材料回来)
            // 建造已由 Patch_ConstructDeliverResources 处理
            for (int wi = 0; wi < workPriorities.Count; wi++)
            {
                Job revJob = TryReverseFetchByCategory(workPriorities[wi].category,
                    pawn, pawnMap, otherMaps, currentElev);
                if (revJob != null) return revJob;
            }

            return null;
        }

        // ========== 按类别分发 ==========

        private static Job TryFetchByCategory(int category, Pawn pawn, Map pawnMap,
            Map otherMap, int targetElev, List<(Map map, int elevation)> otherMaps, int currentElev)
        {
            switch (category)
            {
                case 0: // Construction
                    return TryFetchForConstruction(pawn, pawnMap, otherMap, targetElev, otherMaps, currentElev);
                case 1: // Hauling: Refuel + Bill
                    Job r = TryFetchForRefuel(pawn, pawnMap, otherMap, targetElev, otherMaps, currentElev);
                    return r ?? TryFetchForBill(pawn, pawnMap, otherMap, targetElev, otherMaps, currentElev);
                case 2: // Doctor: Medicine + PatientFeed + Hemogen
                    Job m = TryFetchForMedicine(pawn, pawnMap, otherMap, targetElev, otherMaps, currentElev);
                    if (m != null) return m;
                    Job p = TryFetchForPatientFeed(pawn, pawnMap, otherMap, targetElev, otherMaps, currentElev);
                    return p ?? TryFetchForHemogen(pawn, pawnMap, otherMap, targetElev, otherMaps, currentElev);
                case 3: // Warden
                    return TryFetchForWardFeed(pawn, pawnMap, otherMap, targetElev, otherMaps, currentElev);
                case 4: // Handling
                    return TryFetchForAnimalFeed(pawn, pawnMap, otherMap, targetElev, otherMaps, currentElev);
                case 5: // Childcare
                    return TryFetchForBabyFeed(pawn, pawnMap, otherMap, targetElev, otherMaps, currentElev);
                default:
                    return null;
            }
        }

        private static Job TryReverseFetchByCategory(int category, Pawn pawn, Map pawnMap,
            List<(Map map, int elevation)> otherMaps, int currentElev)
        {
            switch (category)
            {
                case 1: // Hauling: Refuel + Bill
                    Job r = TryReverseFetchForRefuel(pawn, pawnMap, otherMaps, currentElev);
                    return r ?? TryReverseFetchForBill(pawn, pawnMap, otherMaps, currentElev);
                case 2: // Doctor: Medicine + PatientFeed + Hemogen
                    Job m = TryReverseFetchForMedicine(pawn, pawnMap, otherMaps, currentElev);
                    if (m != null) return m;
                    Job p = TryReverseFetchForPatientFeed(pawn, pawnMap, otherMaps, currentElev);
                    return p ?? TryReverseFetchForHemogen(pawn, pawnMap, otherMaps, currentElev);
                case 3: // Warden
                    return TryReverseFetchForWardFeed(pawn, pawnMap, otherMaps, currentElev);
                case 4: // Handling
                    return TryReverseFetchForAnimalFeed(pawn, pawnMap, otherMaps, currentElev);
                case 5: // Childcare
                    return TryReverseFetchForBabyFeed(pawn, pawnMap, otherMaps, currentElev);
                default: // 0=Construction 由 Patch_ConstructDeliverResources 处理
                    return null;
            }
        }

        // ========== 建造 ==========

        private static Job TryFetchForConstruction(Pawn pawn, Map pawnMap, Map targetMap, int targetElev,
            List<(Map map, int elevation)> otherMaps, int currentElev)
        {
            // 蓝图
            var blueprints = targetMap.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
            for (int i = 0; i < blueprints.Count; i++)
            {
                if (blueprints[i] is Blueprint_Install) continue;
                Job job = TryFetchForConstructible(pawn, pawnMap, blueprints[i], targetElev,
                    otherMaps, currentElev);
                if (job != null) return job;
            }
            // 框架
            var frames = targetMap.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
            for (int i = 0; i < frames.Count; i++)
            {
                Job job = TryFetchForConstructible(pawn, pawnMap, frames[i], targetElev,
                    otherMaps, currentElev);
                if (job != null) return job;
            }
            return null;
        }

        private static Job TryFetchForConstructible(Pawn pawn, Map pawnMap, Thing constructThing, int targetElev,
            List<(Map map, int elevation)> otherMaps, int currentElev)
        {
            IConstructible c = constructThing as IConstructible;
            if (c == null) return null;
            if (constructThing.IsForbidden(pawn)) return null;

            foreach (var cost in c.TotalMaterialCost())
            {
                int needed = c.ThingCountNeeded(cost.thingDef);
                if (needed <= 0) continue;

                // 先找 pawn 当前层
                Thing material = FindMaterialOnMap(pawn, pawnMap, cost.thingDef);
                if (material != null)
                    return MakeFetchJob(pawn, cost.thingDef, constructThing, targetElev,
                        CrossLevelJobUtility.NeedType.Construction);

                // 当前层没有 → 搜索其他层（三层情况）
                Job revJob = TryFindMaterialOnOtherMaps(pawn, pawnMap, cost.thingDef,
                    constructThing, targetElev, CrossLevelJobUtility.NeedType.Construction,
                    otherMaps, currentElev);
                if (revJob != null) return revJob;
            }
            return null;
        }

        // ========== 加油 ==========

        private static Job TryFetchForRefuel(Pawn pawn, Map pawnMap, Map targetMap, int targetElev,
            List<(Map map, int elevation)> otherMaps, int currentElev)
        {
            var refuelables = targetMap.listerThings.ThingsInGroup(ThingRequestGroup.Refuelable);
            for (int i = 0; i < refuelables.Count; i++)
            {
                Thing t = refuelables[i];
                if (t.IsForbidden(pawn)) continue;
                if (t.Faction != pawn.Faction) continue;

                CompRefuelable comp = t.TryGetComp<CompRefuelable>();
                if (comp == null || comp.IsFull) continue;
                if (!comp.allowAutoRefuel || !comp.ShouldAutoRefuelNow) continue;

                ThingFilter fuelFilter = comp.Props.fuelFilter;

                // 先找 pawn 当前层
                Thing fuel = GenClosest.ClosestThingReachable(
                    pawn.Position, pawnMap,
                    fuelFilter.BestThingRequest,
                    PathEndMode.ClosestTouch,
                    TraverseParms.For(pawn),
                    9999f,
                    f => !f.IsForbidden(pawn) && pawn.CanReserve(f)
                         && fuelFilter.Allows(f));

                if (fuel != null)
                    return MakeFetchJob(pawn, fuel.def, t, targetElev,
                        CrossLevelJobUtility.NeedType.Refuel);

                // 当前层没有 → 搜索其他层（三层情况）
                foreach (var (matMap, matElev) in otherMaps)
                {
                    if (matMap == targetMap) continue;
                    Thing remoteFuel = FindThingByFilter(matMap, pawn, fuelFilter);
                    if (remoteFuel == null) continue;

                    int nextElev = matElev > currentElev ? currentElev + 1 : currentElev - 1;
                    Building_Stairs stairs = CrossLevelJobUtility.FindStairsToElevation(
                        pawn, pawnMap, nextElev);
                    if (stairs == null) continue;

                    return MakeReverseFetchJob(pawn, remoteFuel.def, t, targetElev,
                        CrossLevelJobUtility.NeedType.Refuel, stairs);
                }
            }
            return null;
        }

        // ========== 制作/烹饪 (DoBill) ==========

        private static Job TryFetchForBill(Pawn pawn, Map pawnMap, Map targetMap, int targetElev,
            List<(Map map, int elevation)> otherMaps, int currentElev)
        {
            foreach (Building_WorkTable bench in targetMap.listerBuildings
                .AllBuildingsColonistOfClass<Building_WorkTable>())
            {
                if (bench.IsForbidden(pawn)) continue;
                if (!bench.CurrentlyUsableForBills()) continue;

                foreach (Bill bill in bench.BillStack)
                {
                    if (!bill.ShouldDoNow()) continue;
                    if (bill is Bill_Medical) continue;

                    RecipeDef recipe = bill.recipe;
                    if (recipe.ingredients == null || recipe.ingredients.Count == 0) continue;

                    for (int i = 0; i < recipe.ingredients.Count; i++)
                    {
                        IngredientCount ing = recipe.ingredients[i];

                        // 先找 pawn 当前层
                        Thing found = FindIngredientOnMap(pawn, pawnMap, ing, bill);
                        if (found != null)
                            return MakeFetchJob(pawn, found.def, bench, targetElev,
                                CrossLevelJobUtility.NeedType.Bill);

                        // 当前层没有 → 搜索其他层（三层情况）
                        foreach (var (matMap, matElev) in otherMaps)
                        {
                            if (matMap == targetMap) continue;
                            Thing remoteIng = FindIngredientByFilter(matMap, pawn, ing, bill);
                            if (remoteIng == null) continue;

                            int nextElev = matElev > currentElev
                                ? currentElev + 1 : currentElev - 1;
                            Building_Stairs stairs = CrossLevelJobUtility.FindStairsToElevation(
                                pawn, pawnMap, nextElev);
                            if (stairs == null) continue;

                            return MakeReverseFetchJob(pawn, remoteIng.def, bench, targetElev,
                                CrossLevelJobUtility.NeedType.Bill, stairs);
                        }
                    }
                }
            }
            return null;
        }

        private static Thing FindIngredientOnMap(Pawn pawn, Map map, IngredientCount ing, Bill bill)
        {
            ThingFilter filter = ing.filter;
            return GenClosest.ClosestThingReachable(
                pawn.Position, map,
                filter.BestThingRequest,
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                9999f,
                t => !t.IsForbidden(pawn) && pawn.CanReserve(t)
                     && filter.Allows(t) && bill.IsFixedOrAllowedIngredient(t));
        }

        // ========== 加油反向 ==========

        private static Job TryReverseFetchForRefuel(Pawn pawn, Map pawnMap,
            List<(Map map, int elevation)> otherMaps, int currentElev)
        {
            var refuelables = pawnMap.listerThings.ThingsInGroup(ThingRequestGroup.Refuelable);
            for (int i = 0; i < refuelables.Count; i++)
            {
                Thing t = refuelables[i];
                if (t.IsForbidden(pawn)) continue;
                if (t.Faction != pawn.Faction) continue;

                CompRefuelable comp = t.TryGetComp<CompRefuelable>();
                if (comp == null || comp.IsFull) continue;
                if (!comp.allowAutoRefuel || !comp.ShouldAutoRefuelNow) continue;

                ThingFilter fuelFilter = comp.Props.fuelFilter;

                // 本层有燃料就跳过，让原版处理
                Thing localFuel = GenClosest.ClosestThingReachable(
                    pawn.Position, pawnMap, fuelFilter.BestThingRequest,
                    PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f,
                    f => !f.IsForbidden(pawn) && pawn.CanReserve(f) && fuelFilter.Allows(f));
                if (localFuel != null) continue;

                // 搜索其他层的燃料
                foreach (var (otherMap, otherElev) in otherMaps)
                {
                    Thing remoteFuel = FindThingByFilter(otherMap, pawn, fuelFilter);
                    if (remoteFuel == null) continue;

                    int nextElev = otherElev > currentElev ? currentElev + 1 : currentElev - 1;
                    Building_Stairs stairs = CrossLevelJobUtility.FindStairsToElevation(
                        pawn, pawnMap, nextElev);
                    if (stairs == null) continue;

                    return MakeReverseFetchJob(pawn, remoteFuel.def, t, currentElev,
                        CrossLevelJobUtility.NeedType.Refuel, stairs);
                }
            }
            return null;
        }

        // ========== Bill 反向 ==========

        private static Job TryReverseFetchForBill(Pawn pawn, Map pawnMap,
            List<(Map map, int elevation)> otherMaps, int currentElev)
        {
            foreach (Building_WorkTable bench in pawnMap.listerBuildings
                .AllBuildingsColonistOfClass<Building_WorkTable>())
            {
                if (bench.IsForbidden(pawn)) continue;
                if (!bench.CurrentlyUsableForBills()) continue;

                foreach (Bill bill in bench.BillStack)
                {
                    if (!bill.ShouldDoNow()) continue;
                    if (bill is Bill_Medical) continue;

                    RecipeDef recipe = bill.recipe;
                    if (recipe.ingredients == null || recipe.ingredients.Count == 0) continue;

                    for (int j = 0; j < recipe.ingredients.Count; j++)
                    {
                        IngredientCount ing = recipe.ingredients[j];

                        // 本层有这个原料就跳过
                        Thing localIng = FindIngredientOnMap(pawn, pawnMap, ing, bill);
                        if (localIng != null) continue;

                        // 搜索其他层
                        foreach (var (otherMap, otherElev) in otherMaps)
                        {
                            Thing remoteIng = FindIngredientByFilter(otherMap, pawn, ing, bill);
                            if (remoteIng == null) continue;

                            int nextElev = otherElev > currentElev
                                ? currentElev + 1 : currentElev - 1;
                            Building_Stairs stairs = CrossLevelJobUtility.FindStairsToElevation(
                                pawn, pawnMap, nextElev);
                            if (stairs == null) continue;

                            return MakeReverseFetchJob(pawn, remoteIng.def, bench, currentElev,
                                CrossLevelJobUtility.NeedType.Bill, stairs);
                        }
                    }
                }
            }
            return null;
        }

        // ========== 医疗取药 ==========

        private static Job TryFetchForMedicine(Pawn pawn, Map pawnMap, Map targetMap, int targetElev,
            List<(Map map, int elevation)> otherMaps, int currentElev)
        {
            // 目标层有需要治疗的 pawn
            foreach (Pawn patient in targetMap.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if (!NeedsMedicineFromOtherFloor(patient, targetMap, pawn)) continue;

                // 先找 pawn 当前层的药
                Thing med = FindMedicineOnMap(pawnMap, pawn);
                if (med != null)
                    return MakeFetchJob(pawn, med.def, patient, targetElev,
                        CrossLevelJobUtility.NeedType.Medicine);

                // 三层：搜索其他层
                foreach (var (matMap, matElev) in otherMaps)
                {
                    if (matMap == targetMap) continue;
                    Thing remoteMed = FindMedicineOnOtherMap(matMap, pawn);
                    if (remoteMed == null) continue;

                    int nextElev = matElev > currentElev ? currentElev + 1 : currentElev - 1;
                    Building_Stairs stairs = CrossLevelJobUtility.FindStairsToElevation(
                        pawn, pawnMap, nextElev);
                    if (stairs == null) continue;

                    return MakeReverseFetchJob(pawn, remoteMed.def, patient, targetElev,
                        CrossLevelJobUtility.NeedType.Medicine, stairs);
                }
            }
            // 囚犯也需要治疗
            foreach (Pawn prisoner in targetMap.mapPawns.PrisonersOfColony)
            {
                if (!NeedsMedicineFromOtherFloor(prisoner, targetMap, pawn)) continue;

                Thing med = FindMedicineOnMap(pawnMap, pawn);
                if (med != null)
                    return MakeFetchJob(pawn, med.def, prisoner, targetElev,
                        CrossLevelJobUtility.NeedType.Medicine);

                foreach (var (matMap, matElev) in otherMaps)
                {
                    if (matMap == targetMap) continue;
                    Thing remoteMed = FindMedicineOnOtherMap(matMap, pawn);
                    if (remoteMed == null) continue;

                    int nextElev = matElev > currentElev ? currentElev + 1 : currentElev - 1;
                    Building_Stairs stairs = CrossLevelJobUtility.FindStairsToElevation(
                        pawn, pawnMap, nextElev);
                    if (stairs == null) continue;

                    return MakeReverseFetchJob(pawn, remoteMed.def, prisoner, targetElev,
                        CrossLevelJobUtility.NeedType.Medicine, stairs);
                }
            }
            return null;
        }

        // 医疗反向
        private static Job TryReverseFetchForMedicine(Pawn pawn, Map pawnMap,
            List<(Map map, int elevation)> otherMaps, int currentElev)
        {
            // 当前层有需要治疗的 pawn，其他层有药
            List<Pawn> patients = new List<Pawn>();
            patients.AddRange(pawnMap.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer));
            patients.AddRange(pawnMap.mapPawns.PrisonersOfColony);

            foreach (Pawn patient in patients)
            {
                if (!NeedsMedicineFromOtherFloor(patient, pawnMap, pawn)) continue;

                foreach (var (otherMap, otherElev) in otherMaps)
                {
                    Thing remoteMed = FindMedicineOnOtherMap(otherMap, pawn);
                    if (remoteMed == null) continue;

                    int nextElev = otherElev > currentElev ? currentElev + 1 : currentElev - 1;
                    Building_Stairs stairs = CrossLevelJobUtility.FindStairsToElevation(
                        pawn, pawnMap, nextElev);
                    if (stairs == null) continue;

                    return MakeReverseFetchJob(pawn, remoteMed.def, patient, currentElev,
                        CrossLevelJobUtility.NeedType.Medicine, stairs);
                }
            }
            return null;
        }

        private static bool NeedsMedicineFromOtherFloor(Pawn patient, Map patientMap, Pawn hauler)
        {
            if (!patient.Spawned || patient.Dead) return false;
            // 使用原版 API 判断是否需要治疗
            if (!HealthAIUtility.ShouldBeTendedNowByPlayer(patient)) return false;

            // 当前层没有药物才需要跨层取
            var meds = patientMap.listerThings.ThingsInGroup(ThingRequestGroup.Medicine);
            for (int i = 0; i < meds.Count; i++)
            {
                if (!meds[i].IsForbidden(hauler) && meds[i].stackCount > 0)
                    return false; // 本层有药，让原版处理
            }
            return true;
        }

        private static Thing FindMedicineOnMap(Map map, Pawn pawn)
        {
            return GenClosest.ClosestThingReachable(
                pawn.Position, map,
                ThingRequest.ForGroup(ThingRequestGroup.Medicine),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                9999f,
                t => !t.IsForbidden(pawn) && pawn.CanReserve(t) && t.stackCount > 0);
        }

        private static Thing FindMedicineOnOtherMap(Map map, Pawn pawn)
        {
            var meds = map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine);
            for (int i = 0; i < meds.Count; i++)
            {
                if (!meds[i].IsForbidden(pawn) && meds[i].stackCount > 0)
                    return meds[i];
            }
            return null;
        }

        // ========== 监管喂饭 ==========

        private static Job TryFetchForWardFeed(Pawn pawn, Map pawnMap, Map targetMap, int targetElev,
            List<(Map map, int elevation)> otherMaps, int currentElev)
        {
            foreach (Pawn prisoner in targetMap.mapPawns.PrisonersOfColony)
            {
                if (!NeedsFoodFromOtherFloor(prisoner, targetMap, pawn)) continue;

                Thing food = FindFoodOnMap(pawnMap, pawn);
                if (food != null)
                    return MakeFetchJob(pawn, food.def, prisoner, targetElev,
                        CrossLevelJobUtility.NeedType.WardFeed);

                // 三层
                foreach (var (matMap, matElev) in otherMaps)
                {
                    if (matMap == targetMap) continue;
                    Thing remoteFood = FindFoodOnOtherMap(matMap, pawn);
                    if (remoteFood == null) continue;

                    int nextElev = matElev > currentElev ? currentElev + 1 : currentElev - 1;
                    Building_Stairs stairs = CrossLevelJobUtility.FindStairsToElevation(
                        pawn, pawnMap, nextElev);
                    if (stairs == null) continue;

                    return MakeReverseFetchJob(pawn, remoteFood.def, prisoner, targetElev,
                        CrossLevelJobUtility.NeedType.WardFeed, stairs);
                }
            }
            return null;
        }

        // 监管喂饭反向
        private static Job TryReverseFetchForWardFeed(Pawn pawn, Map pawnMap,
            List<(Map map, int elevation)> otherMaps, int currentElev)
        {
            foreach (Pawn prisoner in pawnMap.mapPawns.PrisonersOfColony)
            {
                if (!NeedsFoodFromOtherFloor(prisoner, pawnMap, pawn)) continue;

                foreach (var (otherMap, otherElev) in otherMaps)
                {
                    Thing remoteFood = FindFoodOnOtherMap(otherMap, pawn);
                    if (remoteFood == null) continue;

                    int nextElev = otherElev > currentElev ? currentElev + 1 : currentElev - 1;
                    Building_Stairs stairs = CrossLevelJobUtility.FindStairsToElevation(
                        pawn, pawnMap, nextElev);
                    if (stairs == null) continue;

                    return MakeReverseFetchJob(pawn, remoteFood.def, prisoner, currentElev,
                        CrossLevelJobUtility.NeedType.WardFeed, stairs);
                }
            }
            return null;
        }

        private static bool NeedsFoodFromOtherFloor(Pawn prisoner, Map prisonerMap, Pawn hauler)
        {
            if (!prisoner.Spawned || prisoner.Dead) return false;
            // 使用原版 API 判断囚犯是否需要喂饭
            if (!WardenFeedUtility.ShouldBeFed(prisoner)) return false;

            // 当前层没有可用食物才需要跨层取
            var foods = prisonerMap.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree);
            for (int i = 0; i < foods.Count; i++)
            {
                Thing f = foods[i];
                if (!f.IsForbidden(hauler) && f.stackCount > 0 && f.IngestibleNow && f.def.ingestible != null)
                    return false; // 本层有食物
            }
            return true;
        }

        private static Thing FindFoodOnMap(Map map, Pawn pawn)
        {
            return GenClosest.ClosestThingReachable(
                pawn.Position, map,
                ThingRequest.ForGroup(ThingRequestGroup.FoodSourceNotPlantOrTree),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                9999f,
                t => !t.IsForbidden(pawn) && pawn.CanReserve(t)
                     && t.stackCount > 0 && t.IngestibleNow && t.def.ingestible != null);
        }

        private static Thing FindFoodOnOtherMap(Map map, Pawn pawn)
        {
            var foods = map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree);
            for (int i = 0; i < foods.Count; i++)
            {
                Thing f = foods[i];
                if (!f.IsForbidden(pawn) && f.stackCount > 0 && f.IngestibleNow && f.def.ingestible != null)
                    return f;
            }
            return null;
        }

        // ========== 喂食病人 ==========

        private static Job TryFetchForPatientFeed(Pawn pawn, Map pawnMap, Map targetMap, int targetElev,
            List<(Map map, int elevation)> otherMaps, int currentElev)
        {
            foreach (Pawn patient in targetMap.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if (!NeedsPatientFeedFromOtherFloor(patient, targetMap, pawn)) continue;

                Thing food = FindFoodOnMap(pawnMap, pawn);
                if (food != null)
                    return MakeFetchJob(pawn, food.def, patient, targetElev,
                        CrossLevelJobUtility.NeedType.PatientFeed);

                foreach (var (matMap, matElev) in otherMaps)
                {
                    if (matMap == targetMap) continue;
                    Thing remoteFood = FindFoodOnOtherMap(matMap, pawn);
                    if (remoteFood == null) continue;

                    int nextElev = matElev > currentElev ? currentElev + 1 : currentElev - 1;
                    Building_Stairs stairs = CrossLevelJobUtility.FindStairsToElevation(
                        pawn, pawnMap, nextElev);
                    if (stairs == null) continue;

                    return MakeReverseFetchJob(pawn, remoteFood.def, patient, targetElev,
                        CrossLevelJobUtility.NeedType.PatientFeed, stairs);
                }
            }
            return null;
        }

        private static Job TryReverseFetchForPatientFeed(Pawn pawn, Map pawnMap,
            List<(Map map, int elevation)> otherMaps, int currentElev)
        {
            foreach (Pawn patient in pawnMap.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if (!NeedsPatientFeedFromOtherFloor(patient, pawnMap, pawn)) continue;

                foreach (var (otherMap, otherElev) in otherMaps)
                {
                    Thing remoteFood = FindFoodOnOtherMap(otherMap, pawn);
                    if (remoteFood == null) continue;

                    int nextElev = otherElev > currentElev ? currentElev + 1 : currentElev - 1;
                    Building_Stairs stairs = CrossLevelJobUtility.FindStairsToElevation(
                        pawn, pawnMap, nextElev);
                    if (stairs == null) continue;

                    return MakeReverseFetchJob(pawn, remoteFood.def, patient, currentElev,
                        CrossLevelJobUtility.NeedType.PatientFeed, stairs);
                }
            }
            return null;
        }

        private static bool NeedsPatientFeedFromOtherFloor(Pawn patient, Map patientMap, Pawn hauler)
        {
            if (!patient.Spawned || patient.Dead) return false;
            if (patient.IsPrisoner) return false; // 囚犯由 WardFeed 处理
            if (!FoodUtility.ShouldBeFedBySomeone(patient)) return false;

            var foods = patientMap.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree);
            for (int i = 0; i < foods.Count; i++)
            {
                Thing f = foods[i];
                if (!f.IsForbidden(hauler) && f.stackCount > 0 && f.IngestibleNow && f.def.ingestible != null)
                    return false;
            }
            return true;
        }

        // ========== 喂食动物 ==========

        private static Job TryFetchForAnimalFeed(Pawn pawn, Map pawnMap, Map targetMap, int targetElev,
            List<(Map map, int elevation)> otherMaps, int currentElev)
        {
            foreach (Pawn animal in targetMap.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if (!NeedsAnimalFeedFromOtherFloor(animal, targetMap, pawn)) continue;

                Thing food = FindFoodOnMap(pawnMap, pawn);
                if (food != null)
                    return MakeFetchJob(pawn, food.def, animal, targetElev,
                        CrossLevelJobUtility.NeedType.AnimalFeed);

                foreach (var (matMap, matElev) in otherMaps)
                {
                    if (matMap == targetMap) continue;
                    Thing remoteFood = FindFoodOnOtherMap(matMap, pawn);
                    if (remoteFood == null) continue;

                    int nextElev = matElev > currentElev ? currentElev + 1 : currentElev - 1;
                    Building_Stairs stairs = CrossLevelJobUtility.FindStairsToElevation(
                        pawn, pawnMap, nextElev);
                    if (stairs == null) continue;

                    return MakeReverseFetchJob(pawn, remoteFood.def, animal, targetElev,
                        CrossLevelJobUtility.NeedType.AnimalFeed, stairs);
                }
            }
            return null;
        }

        private static Job TryReverseFetchForAnimalFeed(Pawn pawn, Map pawnMap,
            List<(Map map, int elevation)> otherMaps, int currentElev)
        {
            foreach (Pawn animal in pawnMap.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if (!NeedsAnimalFeedFromOtherFloor(animal, pawnMap, pawn)) continue;

                foreach (var (otherMap, otherElev) in otherMaps)
                {
                    Thing remoteFood = FindFoodOnOtherMap(otherMap, pawn);
                    if (remoteFood == null) continue;

                    int nextElev = otherElev > currentElev ? currentElev + 1 : currentElev - 1;
                    Building_Stairs stairs = CrossLevelJobUtility.FindStairsToElevation(
                        pawn, pawnMap, nextElev);
                    if (stairs == null) continue;

                    return MakeReverseFetchJob(pawn, remoteFood.def, animal, currentElev,
                        CrossLevelJobUtility.NeedType.AnimalFeed, stairs);
                }
            }
            return null;
        }

        private static bool NeedsAnimalFeedFromOtherFloor(Pawn animal, Map animalMap, Pawn hauler)
        {
            if (!animal.Spawned || animal.Dead) return false;
            if (!animal.RaceProps.Animal) return false;
            Need_Food foodNeed = animal.needs?.food;
            if (foodNeed == null || foodNeed.CurLevelPercentage > 0.3f) return false;

            var foods = animalMap.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree);
            for (int i = 0; i < foods.Count; i++)
            {
                Thing f = foods[i];
                if (!f.IsForbidden(hauler) && f.stackCount > 0 && f.IngestibleNow && f.def.ingestible != null)
                    return false;
            }
            return true;
        }

        // ========== 喂养婴儿 ==========

        private static Job TryFetchForBabyFeed(Pawn pawn, Map pawnMap, Map targetMap, int targetElev,
            List<(Map map, int elevation)> otherMaps, int currentElev)
        {
            foreach (Pawn baby in targetMap.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if (!NeedsBabyFeedFromOtherFloor(baby, targetMap, pawn)) continue;

                Thing food = FindFoodOnMap(pawnMap, pawn);
                if (food != null)
                    return MakeFetchJob(pawn, food.def, baby, targetElev,
                        CrossLevelJobUtility.NeedType.BabyFeed);

                foreach (var (matMap, matElev) in otherMaps)
                {
                    if (matMap == targetMap) continue;
                    Thing remoteFood = FindFoodOnOtherMap(matMap, pawn);
                    if (remoteFood == null) continue;

                    int nextElev = matElev > currentElev ? currentElev + 1 : currentElev - 1;
                    Building_Stairs stairs = CrossLevelJobUtility.FindStairsToElevation(
                        pawn, pawnMap, nextElev);
                    if (stairs == null) continue;

                    return MakeReverseFetchJob(pawn, remoteFood.def, baby, targetElev,
                        CrossLevelJobUtility.NeedType.BabyFeed, stairs);
                }
            }
            return null;
        }

        private static Job TryReverseFetchForBabyFeed(Pawn pawn, Map pawnMap,
            List<(Map map, int elevation)> otherMaps, int currentElev)
        {
            foreach (Pawn baby in pawnMap.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if (!NeedsBabyFeedFromOtherFloor(baby, pawnMap, pawn)) continue;

                foreach (var (otherMap, otherElev) in otherMaps)
                {
                    Thing remoteFood = FindFoodOnOtherMap(otherMap, pawn);
                    if (remoteFood == null) continue;

                    int nextElev = otherElev > currentElev ? currentElev + 1 : currentElev - 1;
                    Building_Stairs stairs = CrossLevelJobUtility.FindStairsToElevation(
                        pawn, pawnMap, nextElev);
                    if (stairs == null) continue;

                    return MakeReverseFetchJob(pawn, remoteFood.def, baby, currentElev,
                        CrossLevelJobUtility.NeedType.BabyFeed, stairs);
                }
            }
            return null;
        }

        private static bool NeedsBabyFeedFromOtherFloor(Pawn baby, Map babyMap, Pawn hauler)
        {
            if (!baby.Spawned || baby.Dead) return false;
            if (baby.DevelopmentalStage != DevelopmentalStage.Baby) return false;
            Need_Food foodNeed = baby.needs?.food;
            if (foodNeed == null || foodNeed.CurLevelPercentage > 0.3f) return false;

            var foods = babyMap.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree);
            for (int i = 0; i < foods.Count; i++)
            {
                Thing f = foods[i];
                if (!f.IsForbidden(hauler) && f.stackCount > 0 && f.IngestibleNow && f.def.ingestible != null)
                    return false;
            }
            return true;
        }

        // ========== 血原质 ==========

        private static Job TryFetchForHemogen(Pawn pawn, Map pawnMap, Map targetMap, int targetElev,
            List<(Map map, int elevation)> otherMaps, int currentElev)
        {
            foreach (Pawn target in targetMap.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if (!NeedsHemogenFromOtherFloor(target, targetMap, pawn)) continue;

                Thing pack = FindHemogenOnMap(pawnMap, pawn);
                if (pack != null)
                    return MakeFetchJob(pawn, pack.def, target, targetElev,
                        CrossLevelJobUtility.NeedType.Hemogen);

                foreach (var (matMap, matElev) in otherMaps)
                {
                    if (matMap == targetMap) continue;
                    Thing remotePack = FindHemogenOnOtherMap(matMap, pawn);
                    if (remotePack == null) continue;

                    int nextElev = matElev > currentElev ? currentElev + 1 : currentElev - 1;
                    Building_Stairs stairs = CrossLevelJobUtility.FindStairsToElevation(
                        pawn, pawnMap, nextElev);
                    if (stairs == null) continue;

                    return MakeReverseFetchJob(pawn, remotePack.def, target, targetElev,
                        CrossLevelJobUtility.NeedType.Hemogen, stairs);
                }
            }
            return null;
        }

        private static Job TryReverseFetchForHemogen(Pawn pawn, Map pawnMap,
            List<(Map map, int elevation)> otherMaps, int currentElev)
        {
            foreach (Pawn target in pawnMap.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if (!NeedsHemogenFromOtherFloor(target, pawnMap, pawn)) continue;

                foreach (var (otherMap, otherElev) in otherMaps)
                {
                    Thing remotePack = FindHemogenOnOtherMap(otherMap, pawn);
                    if (remotePack == null) continue;

                    int nextElev = otherElev > currentElev ? currentElev + 1 : currentElev - 1;
                    Building_Stairs stairs = CrossLevelJobUtility.FindStairsToElevation(
                        pawn, pawnMap, nextElev);
                    if (stairs == null) continue;

                    return MakeReverseFetchJob(pawn, remotePack.def, target, currentElev,
                        CrossLevelJobUtility.NeedType.Hemogen, stairs);
                }
            }
            return null;
        }

        private static bool NeedsHemogenFromOtherFloor(Pawn target, Map targetMap, Pawn hauler)
        {
            if (!target.Spawned || target.Dead) return false;
            if (target.genes == null) return false;
            Gene_Hemogen hemogen = target.genes.GetFirstGeneOfType<Gene_Hemogen>();
            if (hemogen == null) return false;
            if (hemogen.Value >= hemogen.targetValue) return false;

            var packs = targetMap.listerThings.ThingsOfDef(ThingDefOf.HemogenPack);
            for (int i = 0; i < packs.Count; i++)
            {
                if (!packs[i].IsForbidden(hauler) && packs[i].stackCount > 0)
                    return false;
            }
            return true;
        }

        private static Thing FindHemogenOnMap(Map map, Pawn pawn)
        {
            return GenClosest.ClosestThingReachable(
                pawn.Position, map,
                ThingRequest.ForDef(ThingDefOf.HemogenPack),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                9999f,
                t => !t.IsForbidden(pawn) && pawn.CanReserve(t) && t.stackCount > 0);
        }

        private static Thing FindHemogenOnOtherMap(Map map, Pawn pawn)
        {
            var packs = map.listerThings.ThingsOfDef(ThingDefOf.HemogenPack);
            for (int i = 0; i < packs.Count; i++)
            {
                if (!packs[i].IsForbidden(pawn) && packs[i].stackCount > 0)
                    return packs[i];
            }
            return null;
        }

        // ========== 通用工具 ==========

        private static Thing FindMaterialOnMap(Pawn pawn, Map map, ThingDef thingDef)
        {
            return GenClosest.ClosestThingReachable(
                pawn.Position, map,
                ThingRequest.ForDef(thingDef),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                9999f,
                t => !t.IsForbidden(pawn) && pawn.CanReserve(t) && t.stackCount > 0);
        }

        /// <summary>
        /// 在其他层地图上按 ThingDef 查找物品（不检查可达性，到达后再检查）。
        /// </summary>
        private static Thing FindThingOnOtherMap(Map map, Pawn pawn, ThingDef thingDef)
        {
            List<Thing> things = map.listerThings.ThingsOfDef(thingDef);
            for (int i = 0; i < things.Count; i++)
            {
                if (!things[i].IsForbidden(pawn) && things[i].stackCount > 0)
                    return things[i];
            }
            return null;
        }

        /// <summary>
        /// 三层通用：在 otherMaps 中搜索材料（按 ThingDef），找到后创建反向取材 job。
        /// </summary>
        private static Job TryFindMaterialOnOtherMaps(Pawn pawn, Map pawnMap, ThingDef thingDef,
            Thing target, int needElev, CrossLevelJobUtility.NeedType needType,
            List<(Map map, int elevation)> otherMaps, int currentElev)
        {
            foreach (var (matMap, matElev) in otherMaps)
            {
                if (matMap == target.Map) continue; // 跳过需求层
                Thing remoteMat = FindThingOnOtherMap(matMap, pawn, thingDef);
                if (remoteMat == null) continue;

                int nextElev = matElev > currentElev ? currentElev + 1 : currentElev - 1;
                Building_Stairs stairs = CrossLevelJobUtility.FindStairsToElevation(
                    pawn, pawnMap, nextElev);
                if (stairs == null) continue;

                return MakeReverseFetchJob(pawn, thingDef, target, needElev, needType, stairs);
            }
            return null;
        }

        private static Job MakeFetchJob(Pawn pawn, ThingDef materialDef, Thing target,
            int targetElev, CrossLevelJobUtility.NeedType needType)
        {
            CrossLevelJobUtility.StoreFetchData(pawn.thingIDNumber,
                new CrossLevelJobUtility.FetchData
                {
                    thingDef = materialDef,
                    target = target,
                    returnElevation = targetElev,
                    needType = needType
                });
            CrossLevelJobUtility.RecordRedirect(pawn);
            return JobMaker.MakeJob(MLF_JobDefOf.MLF_ReturnWithMaterial);
        }

        /// <summary>
        /// 反向取材 job：先走楼梯到材料层，再执行 MLF_ReturnWithMaterial 回来。
        /// </summary>
        private static Job MakeReverseFetchJob(Pawn pawn, ThingDef materialDef, Thing target,
            int returnElev, CrossLevelJobUtility.NeedType needType, Building_Stairs stairs)
        {
            CrossLevelJobUtility.StoreFetchData(pawn.thingIDNumber,
                new CrossLevelJobUtility.FetchData
                {
                    thingDef = materialDef,
                    target = target,
                    returnElevation = returnElev,
                    needType = needType
                });
            CrossLevelJobUtility.RecordRedirect(pawn);
            Job fetchJob = JobMaker.MakeJob(MLF_JobDefOf.MLF_ReturnWithMaterial);
            CrossLevelJobUtility.StoreDeferredJob(pawn, fetchJob);
            return JobMaker.MakeJob(MLF_JobDefOf.MLF_UseStairs, stairs);
        }

        /// <summary>
        /// 在其他层地图上按 ThingFilter 查找物品（不检查可达性，到达后再检查）。
        /// </summary>
        private static Thing FindThingByFilter(Map map, Pawn pawn, ThingFilter filter)
        {
            foreach (ThingDef def in filter.AllowedThingDefs)
            {
                List<Thing> things = map.listerThings.ThingsOfDef(def);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (!t.IsForbidden(pawn) && t.stackCount > 0 && filter.Allows(t))
                        return t;
                }
            }
            return null;
        }

        /// <summary>
        /// 在其他层地图上按 Bill 原料需求查找物品（不检查可达性）。
        /// </summary>
        private static Thing FindIngredientByFilter(Map map, Pawn pawn,
            IngredientCount ing, Bill bill)
        {
            ThingFilter filter = ing.filter;
            foreach (ThingDef def in filter.AllowedThingDefs)
            {
                List<Thing> things = map.listerThings.ThingsOfDef(def);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (!t.IsForbidden(pawn) && t.stackCount > 0
                        && filter.Allows(t) && bill.IsFixedOrAllowedIngredient(t))
                        return t;
                }
            }
            return null;
        }
    }
}
