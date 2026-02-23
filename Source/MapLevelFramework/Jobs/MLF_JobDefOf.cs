using RimWorld;
using Verse;

namespace MapLevelFramework
{
    [DefOf]
    public static class MLF_JobDefOf
    {
        public static JobDef MLF_UseStairs;
        public static JobDef MLF_JumpDown;
        public static JobDef MLF_HaulToStairs;
        public static JobDef MLF_HaulAcrossLevel;
        public static JobDef MLF_DeliverResourcesCrossFloor;
        public static JobDef MLF_ReinstallCrossFloor;

        static MLF_JobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MLF_JobDefOf));
        }
    }
}
