using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MapLevelFramework
{
    /// <summary>
    /// 上下楼 JobDriver - 走到楼梯后将 pawn 转移到目标地图。
    /// 电梯模式：targetB 存储目标楼层 elevation（IntVec3.x），可直达任意楼层。
    /// 未设置 targetB 时回退到楼梯自身的 targetElevation（向后兼容）。
    /// </summary>
    public class JobDriver_UseStairs : JobDriver
    {
        private Building_Stairs Stairs => (Building_Stairs)job.targetA.Thing;

        /// <summary>
        /// 获取目标楼层 elevation。优先用 job.targetB，否则用楼梯默认值。
        /// </summary>
        private int TargetElevation =>
            job.targetB.IsValid ? job.targetB.Cell.x : Stairs.targetElevation;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 楼梯不需要预约，多个 pawn 可以同时使用
            return true;
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
                Building_Stairs stairs = Stairs;
                if (stairs == null) return;

                int targetElev = TargetElevation;
                if (StairTransferUtility.TryGetTransferTarget(stairs, targetElev, out Map destMap, out IntVec3 destPos))
                {
                    if (MapLevelFrameworkMod.Settings?.debugPathfindingAndJob ?? false)
                    {
                        int fromElev = stairs.GetCurrentElevation();
                        string fromLabel = fromElev > 0 ? $"{fromElev + 1}F" : fromElev < 0 ? $"B{-fromElev}" : "1F";
                        string toLabel = targetElev > 0 ? $"{targetElev + 1}F" : targetElev < 0 ? $"B{-targetElev}" : "1F";
                        Log.Message($"【MLF】寻路与job检测-{pawn.LabelShort}—执行UseStairs: {fromLabel}→{toLabel}");
                    }
                    StairTransferUtility.TransferPawn(pawn, destMap, destPos);
                }
            };
            transfer.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return transfer;
        }
    }
}
