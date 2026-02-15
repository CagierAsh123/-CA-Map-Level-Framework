using RimWorld;
using Verse;
using Verse.AI;

namespace MapLevelFramework
{
    /// <summary>
    /// 跳楼精神崩溃状态 - pawn 走到露天边缘跳下去。
    /// 跳完后（不在上层了）自动恢复。
    /// </summary>
    public class MentalState_JumpOff : MentalState
    {
        public IntVec3 targetCell = IntVec3.Invalid;

        public override void PostStart(string reason)
        {
            base.PostStart(reason);
            targetCell = FindBestJumpCell();
        }

        public override void MentalStateTick(int delta)
        {
            base.MentalStateTick(delta);

            // 跳完后不在上层了 → 自动恢复
            if (pawn.Spawned && !LevelManager.IsLevelMap(pawn.Map, out _, out _))
            {
                RecoverFromState();
            }
        }

        private IntVec3 FindBestJumpCell()
        {
            if (pawn?.Map == null) return IntVec3.Invalid;

            IntVec3 best = IntVec3.Invalid;
            float bestDist = float.MaxValue;

            // 搜索附近可跳的格子
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(pawn.Position, 30f, true))
            {
                if (!cell.InBounds(pawn.Map)) continue;
                if (!JumpDownUtility.CanJumpDownAt(cell, pawn.Map)) continue;
                if (!pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) continue;

                float dist = cell.DistanceToSquared(pawn.Position);
                if (dist < bestDist)
                {
                    best = cell;
                    bestDist = dist;
                }
            }
            return best;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref targetCell, "targetCell", IntVec3.Invalid);
        }

        public override RandomSocialMode SocialModeMax()
        {
            return RandomSocialMode.Off;
        }
    }
}
