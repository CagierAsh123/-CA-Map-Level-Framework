using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using MapLevelFramework.CrossFloor;

namespace MapLevelFramework
{
    /// <summary>
    /// 跨层搬运 WorkGiver - 当本层没有合适仓库时，搬到其他楼层的仓库。
    /// 优先级低于原版搬运，只在本层无法存放时触发。
    /// </summary>
    public class WorkGiver_HaulAcrossLevel : WorkGiver_Scanner
    {
        public override Danger MaxPathDanger(Pawn pawn) => Danger.Deadly;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling();
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling().Count == 0)
                return true;
            return !pawn.Map.IsPartOfFloorSystem();
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, forced))
                return null;

            // 本层有更好仓库 → 让原版处理
            StoragePriority currentPriority = StoreUtility.CurrentStoragePriorityOf(t);
            if (StoreUtility.TryFindBestBetterStorageFor(t, pawn, pawn.Map,
                    currentPriority, pawn.Faction, out _, out _, true))
                return null;

            // 其他楼层找仓库
            if (!CrossLevelHaulUtility.TryFindBetterStorageOnOtherLevel(
                    t, pawn, out Map destMap, out _, out Building_Stairs stairs))
                return null;

            int destElev = FloorMapUtility.GetMapElevation(destMap);
            Job job = JobMaker.MakeJob(MLF_JobDefOf.MLF_HaulAcrossLevel, t, stairs);
            job.count = t.stackCount;
            job.targetC = new IntVec3(destElev, 0, 0);
            return job;
        }
    }
}
