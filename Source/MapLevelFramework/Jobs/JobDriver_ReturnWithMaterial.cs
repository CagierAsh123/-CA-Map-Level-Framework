using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MapLevelFramework
{
    /// <summary>
    /// 跨层取材料 JobDriver - pawn 已转移到材料所在楼层，
    /// 拿起材料 → 走到返回楼梯 → 转移回原层 → 送到蓝图/框架。
    /// 由 Patch_ConstructDeliverResources 创建，通过 MLF_UseStairs 延迟触发。
    /// </summary>
    public class JobDriver_ReturnWithMaterial : JobDriver
    {
        // TargetA = 材料（init toil 中设置）
        // TargetB = 返回楼梯（init toil 中设置）

        private CrossLevelJobUtility.FetchData fetchData;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 目标在 init toil 中动态设置，此处无需预约
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
                Thing material = GenClosest.ClosestThingReachable(
                    pawn.Position, pawn.Map,
                    ThingRequest.ForDef(fetchData.thingDef),
                    PathEndMode.ClosestTouch,
                    TraverseParms.For(pawn),
                    9999f,
                    t => !t.IsForbidden(pawn) && pawn.CanReserve(t));

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

            // 5. 转移回原层并送到蓝图
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

            // 转移回原层
            StairTransferUtility.TransferPawn(pawn, destMap, destPos);

            carried = pawn.carryTracker.CarriedThing;
            if (carried == null) return;

            // 检查蓝图/框架是否还在
            Thing frame = fetchData.frame;
            if (frame != null && frame.Spawned && frame.Map == destMap
                && pawn.CanReserve(frame))
            {
                Job haulJob = JobMaker.MakeJob(JobDefOf.HaulToContainer);
                haulJob.targetA = carried;
                haulJob.targetB = frame;
                haulJob.count = carried.stackCount;
                haulJob.haulMode = HaulMode.ToContainer;
                pawn.jobs.StartJob(haulJob, JobCondition.None, null, false, true);
                return;
            }

            // 蓝图已消失，放下材料
            pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
        }
    }
}
