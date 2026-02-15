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
            return pawn.Reserve(job.targetA, job, 100, 1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);

            // 走到楼梯
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.OnCell);

            // 转移到目标地图并恢复延迟 job
            Toil transfer = ToilMaker.MakeToil("MLF_Transfer");
            transfer.initAction = delegate
            {
                if (StairTransferUtility.TryGetTransferTarget(Stairs, out Map destMap, out IntVec3 destPos))
                {
                    StairTransferUtility.TransferPawn(pawn, destMap, destPos);

                    // 恢复跨层扫描时找到的 job
                    if (CrossLevelJobUtility.TryPopDeferredJob(pawn, out Job deferredJob))
                    {
                        // 验证 job 目标仍然有效
                        if (ValidateDeferredJob(deferredJob, destMap))
                        {
                            pawn.jobs.StartJob(deferredJob, JobCondition.None, null, false, true);
                        }
                    }
                }
            };
            transfer.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return transfer;
        }

        /// <summary>
        /// 验证延迟 job 的目标在目标地图上仍然有效。
        /// 扫描和实际转移之间可能经过多个 tick，目标可能已被销毁/移动。
        /// </summary>
        private bool ValidateDeferredJob(Job job, Map destMap)
        {
            if (job == null) return false;

            // 检查 thing 目标是否仍然 spawned 且在正确的地图上
            for (int i = 0; i < 3; i++)
            {
                LocalTargetInfo target = i switch
                {
                    0 => job.targetA,
                    1 => job.targetB,
                    _ => job.targetC
                };
                if (target.HasThing && target.Thing != null)
                {
                    if (!target.Thing.Spawned || target.Thing.Map != destMap)
                        return false;
                }
            }
            return true;
        }
    }
}
