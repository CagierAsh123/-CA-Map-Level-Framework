using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// 跨层级工作扫描 - 让 pawn 在当前地图找不到工作时，自动去其他楼层找工作。
    /// 使用 CrossLevelJobUtility 共用的跨层扫描逻辑。
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Work), "TryIssueJobPackage")]
    public static class Patch_CrossLevelJobScan
    {
        public static void Postfix(
            ref ThinkResult __result,
            JobGiver_Work __instance,
            Pawn pawn,
            JobIssueParams jobParams)
        {
            if (CrossLevelJobUtility.Scanning) return;
            if (__result != ThinkResult.NoJob) return;
            if (pawn?.Map == null || !pawn.Spawned) return;

            Job stairJob = CrossLevelJobUtility.TryCrossLevelScan(pawn, () =>
            {
                ThinkResult result = __instance.TryIssueJobPackage(pawn, jobParams);
                return result != ThinkResult.NoJob ? result.Job : null;
            });

            if (stairJob != null)
            {
                __result = new ThinkResult(stairJob, __instance, null, false);
            }
        }
    }
}
