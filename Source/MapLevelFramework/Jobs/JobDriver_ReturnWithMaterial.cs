using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MapLevelFramework
{
    /// <summary>
    /// 通用跨层材料搬运 JobDriver：
    /// 拿起材料 → 走到楼梯 → 转移到目标层 → 根据 NeedType 启动对应的原版交付 job。
    /// 搬运阶段通用，交付阶段按类型分发。
    /// </summary>
    public class JobDriver_ReturnWithMaterial : JobDriver
    {
        // TargetA = 材料（init toil 中设置）
        // TargetB = 返回楼梯（init toil 中设置）

        private CrossLevelJobUtility.FetchData fetchData;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 1. 初始化：从 FetchData 读取信息，找材料和返回楼梯
            Toil init = ToilMaker.MakeToil("MLF_InitFetch");
            init.initAction = delegate
            {
                if (!CrossLevelJobUtility.TryPopFetchData(pawn.thingIDNumber, out fetchData))
                {
                    EndJobWith(JobCondition.Errored);
                    return;
                }

                // 在当前地图找材料
                Thing material;
                if (fetchData.needType == CrossLevelJobUtility.NeedType.Refuel)
                {
                    // 加油：用燃料过滤器匹配
                    CompRefuelable comp = fetchData.target?.TryGetComp<CompRefuelable>();
                    if (comp == null)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    ThingFilter filter = comp.Props.fuelFilter;
                    material = GenClosest.ClosestThingReachable(
                        pawn.Position, pawn.Map,
                        filter.BestThingRequest,
                        PathEndMode.ClosestTouch,
                        TraverseParms.For(pawn),
                        9999f,
                        t => !t.IsForbidden(pawn) && pawn.CanReserve(t) && filter.Allows(t));
                }
                else if (fetchData.needType == CrossLevelJobUtility.NeedType.Medicine)
                {
                    // 医疗：找药物
                    material = GenClosest.ClosestThingReachable(
                        pawn.Position, pawn.Map,
                        ThingRequest.ForGroup(ThingRequestGroup.Medicine),
                        PathEndMode.ClosestTouch,
                        TraverseParms.For(pawn),
                        9999f,
                        t => !t.IsForbidden(pawn) && pawn.CanReserve(t) && t.stackCount > 0);
                }
                else if (fetchData.needType == CrossLevelJobUtility.NeedType.WardFeed
                    || fetchData.needType == CrossLevelJobUtility.NeedType.PatientFeed
                    || fetchData.needType == CrossLevelJobUtility.NeedType.AnimalFeed)
                {
                    // 喂食：找食物
                    material = GenClosest.ClosestThingReachable(
                        pawn.Position, pawn.Map,
                        ThingRequest.ForGroup(ThingRequestGroup.FoodSourceNotPlantOrTree),
                        PathEndMode.ClosestTouch,
                        TraverseParms.For(pawn),
                        9999f,
                        t => !t.IsForbidden(pawn) && pawn.CanReserve(t)
                             && t.stackCount > 0 && t.IngestibleNow && t.def.ingestible != null);
                }
                else if (fetchData.needType == CrossLevelJobUtility.NeedType.BabyFeed)
                {
                    // 喂养婴儿：找婴儿食物（按 thingDef 匹配，扫描时已确定）
                    material = GenClosest.ClosestThingReachable(
                        pawn.Position, pawn.Map,
                        ThingRequest.ForDef(fetchData.thingDef),
                        PathEndMode.ClosestTouch,
                        TraverseParms.For(pawn),
                        9999f,
                        t => !t.IsForbidden(pawn) && pawn.CanReserve(t) && t.stackCount > 0);
                }
                else if (fetchData.needType == CrossLevelJobUtility.NeedType.Hemogen)
                {
                    // 血原质：找血原质包（按 thingDef 匹配）
                    material = GenClosest.ClosestThingReachable(
                        pawn.Position, pawn.Map,
                        ThingRequest.ForDef(fetchData.thingDef),
                        PathEndMode.ClosestTouch,
                        TraverseParms.For(pawn),
                        9999f,
                        t => !t.IsForbidden(pawn) && pawn.CanReserve(t) && t.stackCount > 0);
                }
                else
                {
                    // 建造/Bill 等：按 thingDef 匹配
                    material = GenClosest.ClosestThingReachable(
                        pawn.Position, pawn.Map,
                        ThingRequest.ForDef(fetchData.thingDef),
                        PathEndMode.ClosestTouch,
                        TraverseParms.For(pawn),
                        9999f,
                        t => !t.IsForbidden(pawn) && pawn.CanReserve(t));
                }

                if (material == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // 找返回楼梯
                Building_Stairs returnStairs = CrossLevelJobUtility.FindStairsToElevation(
                    pawn, pawn.Map, fetchData.returnElevation);

                if (returnStairs == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                job.targetA = material;
                job.targetB = returnStairs;

                // 计算搬运数量
                int needed = GetNeededCount();
                int canCarry = pawn.carryTracker.MaxStackSpaceEver(material.def);
                job.count = UnityEngine.Mathf.Min(needed > 0 ? needed : material.stackCount,
                    canCarry, material.stackCount);
                if (job.count <= 0) job.count = 1;

                if (!pawn.Reserve(material, job, 1, -1, null, errorOnFailed: false))
                {
                    EndJobWith(JobCondition.Incompletable);
                }
            };
            init.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return init;

            // 2. 走到材料
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);

            // 3. 拿起材料
            yield return Toils_Haul.StartCarryThing(TargetIndex.A, false, true);

            // 4. 走到返回楼梯
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.OnCell);

            // 5. 转移到目标层并启动交付 job
            Toil transfer = ToilMaker.MakeToil("MLF_TransferAndDeliver");
            transfer.initAction = delegate
            {
                TransferAndDeliver();
            };
            transfer.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return transfer;
        }

        private void TransferAndDeliver()
        {
            Building_Stairs stairs = (Building_Stairs)job.targetB.Thing;
            if (!StairTransferUtility.TryGetTransferTarget(stairs, out Map destMap, out IntVec3 destPos))
                return;

            Thing carried = pawn.carryTracker.CarriedThing;
            if (carried == null) return;

            // 转移到目标层
            StairTransferUtility.TransferPawn(pawn, destMap, destPos);

            carried = pawn.carryTracker.CarriedThing;
            if (carried == null) return;

            Thing target = fetchData.target;

            // 目标还在吗？
            if (target == null || !target.Spawned || target.Map != destMap)
            {
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
                return;
            }

            // 根据 NeedType 启动对应的原版交付 job
            Job deliverJob = CreateDeliverJob(carried, target);
            if (deliverJob != null)
            {
                pawn.jobs.StartJob(deliverJob, JobCondition.None, null, false, true);
                return;
            }

            // 无法交付，放下材料（Bill/Refuel 可能已在 CreateDeliverJob 中放下）
            if (pawn.carryTracker.CarriedThing != null)
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
        }

        private Job CreateDeliverJob(Thing carried, Thing target)
        {
            switch (fetchData.needType)
            {
                case CrossLevelJobUtility.NeedType.Construction:
                    if (!pawn.CanReserve(target)) return null;
                    Job haulJob = JobMaker.MakeJob(JobDefOf.HaulToContainer);
                    haulJob.targetA = carried;
                    haulJob.targetB = target;
                    haulJob.count = carried.stackCount;
                    haulJob.haulMode = HaulMode.ToContainer;
                    return haulJob;

                case CrossLevelJobUtility.NeedType.Refuel:
                    if (!pawn.CanReserve(target)) return null;
                    // 先放下燃料，再启动原版 Refuel job
                    pawn.carryTracker.TryDropCarriedThing(
                        pawn.Position, ThingPlaceMode.Near, out Thing droppedFuel);
                    if (droppedFuel != null)
                    {
                        Job refuelJob = JobMaker.MakeJob(JobDefOf.Refuel, target, droppedFuel);
                        return refuelJob;
                    }
                    return null;

                case CrossLevelJobUtility.NeedType.Bill:
                    // 放在工作台附近，让原版 DoBill 下一轮扫描处理
                    pawn.carryTracker.TryDropCarriedThing(
                        target.Position, ThingPlaceMode.Near, out _);
                    return null;

                case CrossLevelJobUtility.NeedType.Medicine:
                    // 放在病人附近，让原版医生下一轮扫描时使用
                    pawn.carryTracker.TryDropCarriedThing(
                        target.Position, ThingPlaceMode.Near, out _);
                    return null;

                case CrossLevelJobUtility.NeedType.WardFeed:
                    // 放在囚犯附近，让原版看守下一轮扫描时喂饭
                    pawn.carryTracker.TryDropCarriedThing(
                        target.Position, ThingPlaceMode.Near, out _);
                    return null;

                case CrossLevelJobUtility.NeedType.PatientFeed:
                case CrossLevelJobUtility.NeedType.AnimalFeed:
                case CrossLevelJobUtility.NeedType.BabyFeed:
                case CrossLevelJobUtility.NeedType.Hemogen:
                    // 放在目标附近，让原版下一轮扫描处理
                    pawn.carryTracker.TryDropCarriedThing(
                        target.Position, ThingPlaceMode.Near, out _);
                    return null;

                default:
                    return null;
            }
        }

        private int GetNeededCount()
        {
            Thing target = fetchData.target;
            if (target == null) return -1;

            switch (fetchData.needType)
            {
                case CrossLevelJobUtility.NeedType.Construction:
                    IConstructible c = target as IConstructible;
                    if (c != null && fetchData.thingDef != null)
                        return c.ThingCountNeeded(fetchData.thingDef);
                    return -1;

                case CrossLevelJobUtility.NeedType.Refuel:
                    CompRefuelable comp = target.TryGetComp<CompRefuelable>();
                    if (comp != null)
                        return comp.GetFuelCountToFullyRefuel();
                    return -1;

                case CrossLevelJobUtility.NeedType.Bill:
                    // Bill 原料搬运：尽量多拿
                    return -1;

                case CrossLevelJobUtility.NeedType.Medicine:
                    // 药物：1 个就够一次治疗
                    return 1;

                case CrossLevelJobUtility.NeedType.WardFeed:
                    // 食物：1 份
                    return 1;

                case CrossLevelJobUtility.NeedType.PatientFeed:
                case CrossLevelJobUtility.NeedType.AnimalFeed:
                case CrossLevelJobUtility.NeedType.BabyFeed:
                    // 食物：1 份
                    return 1;

                case CrossLevelJobUtility.NeedType.Hemogen:
                    // 血原质包：1 个
                    return 1;

                default:
                    return -1;
            }
        }
    }
}
