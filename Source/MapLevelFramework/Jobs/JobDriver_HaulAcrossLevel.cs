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
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDespawnedOrNull(TargetIndex.B);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A, false, true);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.OnCell);

            Toil transfer = ToilMaker.MakeToil("MLF_HaulTransfer");
            transfer.initAction = delegate { TransferAndHaul(); };
            transfer.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return transfer;
        }

        private void TransferAndHaul()
        {
            if (!StairTransferUtility.TryGetTransferTarget(Stairs, out Map destMap, out IntVec3 destPos))
                return;

            Thing carried = pawn.carryTracker.CarriedThing;
            if (carried == null) return;

            StairTransferUtility.TransferPawn(pawn, destMap, destPos);

            carried = pawn.carryTracker.CarriedThing;
            if (carried == null) return;

            StoragePriority currentPriority = StoragePriority.Unstored;
            if (StoreUtility.TryFindBestBetterStorageFor(carried, pawn, destMap,
                    currentPriority, pawn.Faction, out IntVec3 storeCell, out _, true))
            {
                Job haulJob = HaulAIUtility.HaulToCellStorageJob(pawn, carried, storeCell, false);
                if (haulJob != null)
                {
                    pawn.jobs.StartJob(haulJob, JobCondition.None, null, false, true);
                    return;
                }
            }

            if (carried.Spawned)
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
        }
    }
}
