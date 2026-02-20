using HarmonyLib;
using Verse;
using Verse.AI;

namespace MapLevelFramework.CrossFloor
{
    /// <summary>
    /// [已禁用] PathFollower 拦截导致 "started 10 jobs in one tick" 无限循环：
    /// GenClosest 返回跨层 Thing → WorkGiver 创建 job → PathFollower 拦截 → UseStairs →
    /// 转移后 AI 重新分配同一 job → 再次拦截 → 循环。
    /// 跨层工作改由 Patch_JobGiver_Work_CrossFloor 在 job 分配层面处理。
    /// </summary>
    // [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
    public static class Patch_PathFollower_CrossFloor
    {
        public static bool Prefix(
            Pawn_PathFollower __instance,
            LocalTargetInfo dest,
            Pawn ___pawn)
        {
            if (___pawn == null) return true;
            if (___pawn.CurJob == null) return true;

            // 确定目标所在的地图
            Map destMap = null;
            if (dest.HasThing && dest.Thing != null)
            {
                destMap = dest.Thing.MapHeld;
            }

            if (destMap == null) return true;
            if (destMap == ___pawn.Map) return true;

            // 确认属于同一楼层系统
            Map pawnMap = ___pawn.Map;
            if (!pawnMap.IsPartOfFloorSystem()) return true;
            if (pawnMap.GetBaseMap() != destMap.GetBaseMap()) return true;

            // 已经在走楼梯了，不要再拦截
            if (___pawn.CurJobDef == MLF_JobDefOf.MLF_UseStairs) return true;

            // 电梯模式：直达目标楼层
            int destElev = FloorMapUtility.GetMapElevation(destMap);

            Building_Stairs stairs = FloorMapUtility.FindStairsToFloor(___pawn, pawnMap, destElev);
            if (stairs == null) return true; // 找不到楼梯，让原版处理（会失败）

            // 中断当前 job，开始走楼梯
            // 走楼梯完成后，AI 会重新分配 job，GenClosest 会再次找到目标
            Job stairJob = JobMaker.MakeJob(MLF_JobDefOf.MLF_UseStairs, stairs);
            stairJob.targetB = new IntVec3(destElev, 0, 0);
            ___pawn.jobs.StartJob(stairJob, JobCondition.InterruptForced);
            return false;
        }
    }
}
