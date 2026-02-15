using Verse;
using Verse.AI;

namespace MapLevelFramework
{
    /// <summary>
    /// 跳楼 JobGiver - 精神崩溃时给 pawn 跳楼 job。
    /// 从 MentalState_JumpOff 读取目标格子。
    /// </summary>
    public class JobGiver_JumpOff : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var state = pawn.MentalState as MentalState_JumpOff;
            if (state == null || !state.targetCell.IsValid)
                return null;

            if (!state.targetCell.InBounds(pawn.Map))
                return null;

            if (!pawn.CanReach(state.targetCell, PathEndMode.OnCell, Danger.Deadly))
                return null;

            return JobMaker.MakeJob(MLF_JobDefOf.MLF_JumpDown, state.targetCell);
        }
    }
}
