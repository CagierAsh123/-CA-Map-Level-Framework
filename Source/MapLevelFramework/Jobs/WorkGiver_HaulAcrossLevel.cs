using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace MapLevelFramework
{
    /// <summary>
    /// 跨层搬运 WorkGiver - 当本层没有合适仓库时，搬到其他楼层的仓库。
    /// 优先级低于原版搬运，只在本层无法存放时触发。
    /// </summary>
    public class WorkGiver_HaulAcrossLevel : WorkGiver_Scanner
    {
        public override Danger MaxPathDanger(Pawn pawn)
        {
            return Danger.Deadly;
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling();
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling().Count == 0)
                return true;

            // 没有楼层系统就跳过
            Map map = pawn.Map;
            LevelManager mgr;
            if (LevelManager.IsLevelMap(map, out var parentMgr, out _))
                mgr = parentMgr;
            else
                mgr = LevelManager.GetManager(map);

            return mgr == null || mgr.LevelCount == 0;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, forced))
                return null;

            // 先检查本层是否有仓库（有的话让原版搬运处理）
            StoragePriority currentPriority = StoreUtility.CurrentStoragePriorityOf(t);
            IntVec3 localCell;
            IHaulDestination localDest;
            if (StoreUtility.TryFindBestBetterStorageFor(t, pawn, pawn.Map, currentPriority, pawn.Faction, out localCell, out localDest, true))
                return null; // 本层有更好的仓库，让原版处理

            // 本层没有合适仓库，检查其他楼层
            if (!CrossLevelHaulUtility.TryFindBetterStorageOnOtherLevel(t, pawn, out _, out _, out Building_Stairs stairs))
                return null;

            Job job = JobMaker.MakeJob(MLF_JobDefOf.MLF_HaulAcrossLevel, t, stairs);
            job.count = t.stackCount;
            return job;
        }
    }
}
