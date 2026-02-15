using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MapLevelFramework
{
    /// <summary>
    /// 上下楼 JobDriver - 走到楼梯后将 pawn 转移到目标地图。
    /// </summary>
    public class JobDriver_UseStairs : JobDriver
    {
        private Building_Stairs Stairs => (Building_Stairs)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 楼梯不需要独占预约，多个 pawn 可以同时使用
            return pawn.Reserve(job.targetA, job, 100, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);

            // 走到楼梯
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.OnCell);

            // 转移到目标地图
            Toil transfer = ToilMaker.MakeToil("MLF_Transfer");
            transfer.initAction = delegate
            {
                if (StairTransferUtility.TryGetTransferTarget(Stairs, out Map destMap, out IntVec3 destPos))
                {
                    StairTransferUtility.TransferPawn(pawn, destMap, destPos);
                }
            };
            transfer.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return transfer;
        }
    }
}
