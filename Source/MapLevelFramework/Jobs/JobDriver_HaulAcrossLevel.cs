using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MapLevelFramework
{
    /// <summary>
    /// 跨层搬运 JobDriver - 拿起物品 → 走到楼梯 → 转移 → 在目标层放到仓库。
    /// TargetA = 要搬运的物品
    /// TargetB = 楼梯
    /// </summary>
    public class JobDriver_HaulAcrossLevel : JobDriver
    {
        private Thing Item => job.targetA.Thing;
        private Building_Stairs Stairs => (Building_Stairs)job.targetB.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 只预约物品，楼梯不需要预约
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDespawnedOrNull(TargetIndex.B);

            // 1. 走到物品
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);

            // 2. 拿起物品
            yield return Toils_Haul.StartCarryThing(TargetIndex.A, false, true);

            // 3. 走到楼梯
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.OnCell);

            // 4. 转移到目标层并放到仓库
            Toil transfer = ToilMaker.MakeToil("MLF_HaulTransfer");
            transfer.initAction = delegate
            {
                TransferAndHaul();
            };
            transfer.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return transfer;
        }

        private void TransferAndHaul()
        {
            if (!StairTransferUtility.TryGetTransferTarget(Stairs, out Map destMap, out IntVec3 destPos))
                return;

            Thing carried = pawn.carryTracker.CarriedThing;
            if (carried == null) return;

            // 转移 pawn（携带物品一起走）
            StairTransferUtility.TransferPawn(pawn, destMap, destPos);

            // 转移后，在目标地图找仓库并创建搬运 job
            carried = pawn.carryTracker.CarriedThing;
            if (carried == null) return;

            StoragePriority currentPriority = StoragePriority.Unstored;
            IntVec3 storeCell;
            IHaulDestination haulDest;

            if (StoreUtility.TryFindBestBetterStorageFor(carried, pawn, destMap, currentPriority, pawn.Faction, out storeCell, out haulDest, true))
            {
                Job haulJob = HaulAIUtility.HaulToCellStorageJob(pawn, carried, storeCell, false);
                if (haulJob != null)
                {
                    pawn.jobs.StartJob(haulJob, JobCondition.None, null, false, true);
                    return;
                }
            }

            // 找不到仓库就直接放下
            if (carried.Spawned)
            {
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
            }
        }
    }
}
