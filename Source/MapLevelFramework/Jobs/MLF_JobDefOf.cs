using RimWorld;
using Verse;

namespace MapLevelFramework
{
    [DefOf]
    public static class MLF_JobDefOf
    {
        public static JobDef MLF_UseStairs;

        static MLF_JobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MLF_JobDefOf));
        }
    }
}
