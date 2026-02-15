using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MapLevelFramework
{
    /// <summary>
    /// 跳楼 JobDriver - pawn 走到露天边缘跳下去，转移到下层并受摔伤。
    /// TargetA = 跳下的目标格子（上层 OpenAir 旁边的可站立格子）
    /// </summary>
    public class JobDriver_JumpDown : JobDriver
    {
        private const float FallDamageMin = 15f;
        private const float FallDamageMax = 40f;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true; // 不需要预约
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 走到跳下点
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);

            // 跳！
            Toil jump = ToilMaker.MakeToil("MLF_JumpDown");
            jump.initAction = delegate
            {
                DoJump();
            };
            jump.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return jump;
        }

        private void DoJump()
        {
            Map upperMap = pawn.Map;
            IntVec3 jumpCell = job.targetA.Cell;

            if (!LevelManager.IsLevelMap(upperMap, out var mgr, out var levelData))
                return;

            // 找下层地图
            Map lowerMap = levelData.hostMap;
            if (lowerMap == null) return;

            if (!jumpCell.InBounds(lowerMap)) return;

            // 找落点（OpenAir 对应的下层格子）
            IntVec3 landingCell = JumpDownUtility.GetLandingCell(jumpCell, upperMap);
            if (!landingCell.IsValid || !landingCell.InBounds(lowerMap)) return;

            // 转移到下层落点
            StairTransferUtility.TransferPawn(pawn, lowerMap, landingCell);

            // 摔伤
            if (!pawn.Dead)
            {
                float dmg = Rand.Range(FallDamageMin, FallDamageMax);
                pawn.TakeDamage(new DamageInfo(
                    DamageDefOf.Blunt, dmg, 0f, -1f, null, null, null,
                    DamageInfo.SourceCategory.Collapse));
            }
        }
    }
}
