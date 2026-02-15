using Verse;
using Verse.AI;

namespace MapLevelFramework
{
    /// <summary>
    /// 跳楼精神崩溃条件检查 - pawn 必须在上层且附近有可跳格子。
    /// </summary>
    public class MentalStateWorker_JumpOff : MentalStateWorker
    {
        public override bool StateCanOccur(Pawn pawn)
        {
            if (!base.StateCanOccur(pawn)) return false;
            if (pawn?.Map == null || !pawn.Spawned) return false;

            // 必须在上层地图
            if (!LevelManager.IsLevelMap(pawn.Map, out _, out _))
                return false;

            // 附近必须有可跳的格子
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(pawn.Position, 15f, true))
            {
                if (cell.InBounds(pawn.Map) && JumpDownUtility.CanJumpDownAt(cell, pawn.Map))
                    return true;
            }
            return false;
        }
    }
}
