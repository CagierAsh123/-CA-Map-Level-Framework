using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// 跨层级需求扫描 - 让 pawn 在当前地图满足不了需求时，自动去其他楼层找。
    /// 覆盖 P0（生存）和 P2（生活质量）需求。
    /// </summary>

    // ========== P0: 生存需求 ==========

    /// <summary>饿了找食物</summary>
    [HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
    public static class Patch_CrossLevel_GetFood
    {
        private static readonly MethodInfo method =
            AccessTools.Method(typeof(JobGiver_GetFood), "TryGiveJob");

        public static void Postfix(ref Job __result, JobGiver_GetFood __instance, Pawn pawn)
        {
            if (__result != null) return;
            if (CrossLevelJobUtility.Scanning) return;

            __result = CrossLevelJobUtility.TryCrossLevelScan(pawn, () =>
            {
                Job job = (Job)method.Invoke(__instance, new object[] { pawn });
                return job != null;
            });
        }
    }

    /// <summary>困了找床</summary>
    [HarmonyPatch(typeof(JobGiver_GetRest), "TryGiveJob")]
    public static class Patch_CrossLevel_GetRest
    {
        private static readonly MethodInfo method =
            AccessTools.Method(typeof(JobGiver_GetRest), "TryGiveJob");

        public static void Postfix(ref Job __result, JobGiver_GetRest __instance, Pawn pawn)
        {
            if (__result != null) return;
            if (CrossLevelJobUtility.Scanning) return;

            __result = CrossLevelJobUtility.TryCrossLevelScan(pawn, () =>
            {
                Job job = (Job)method.Invoke(__instance, new object[] { pawn });
                return job != null;
            });
        }
    }

    /// <summary>出门前打包食物</summary>
    [HarmonyPatch(typeof(JobGiver_PackFood), "TryGiveJob")]
    public static class Patch_CrossLevel_PackFood
    {
        private static readonly MethodInfo method =
            AccessTools.Method(typeof(JobGiver_PackFood), "TryGiveJob");

        public static void Postfix(ref Job __result, JobGiver_PackFood __instance, Pawn pawn)
        {
            if (__result != null) return;
            if (CrossLevelJobUtility.Scanning) return;

            __result = CrossLevelJobUtility.TryCrossLevelScan(pawn, () =>
            {
                Job job = (Job)method.Invoke(__instance, new object[] { pawn });
                return job != null;
            });
        }
    }

    // ========== P2: 生活质量 ==========

    /// <summary>找娱乐</summary>
    [HarmonyPatch(typeof(JobGiver_GetJoy), "TryGiveJob")]
    public static class Patch_CrossLevel_GetJoy
    {
        private static readonly MethodInfo method =
            AccessTools.Method(typeof(JobGiver_GetJoy), "TryGiveJob");

        public static void Postfix(ref Job __result, JobGiver_GetJoy __instance, Pawn pawn)
        {
            if (__result != null) return;
            if (CrossLevelJobUtility.Scanning) return;

            __result = CrossLevelJobUtility.TryCrossLevelScan(pawn, () =>
            {
                Job job = (Job)method.Invoke(__instance, new object[] { pawn });
                return job != null;
            });
        }
    }

    /// <summary>药物/化学需求</summary>
    [HarmonyPatch(typeof(JobGiver_SatisfyChemicalNeed), "TryGiveJob")]
    public static class Patch_CrossLevel_SatisfyChemicalNeed
    {
        private static readonly MethodInfo method =
            AccessTools.Method(typeof(JobGiver_SatisfyChemicalNeed), "TryGiveJob");

        public static void Postfix(ref Job __result, JobGiver_SatisfyChemicalNeed __instance, Pawn pawn)
        {
            if (__result != null) return;
            if (CrossLevelJobUtility.Scanning) return;

            __result = CrossLevelJobUtility.TryCrossLevelScan(pawn, () =>
            {
                Job job = (Job)method.Invoke(__instance, new object[] { pawn });
                return job != null;
            });
        }
    }

    /// <summary>血源需求（Biotech DLC）</summary>
    [HarmonyPatch(typeof(JobGiver_GetHemogen), "TryGiveJob")]
    public static class Patch_CrossLevel_GetHemogen
    {
        private static readonly MethodInfo method =
            AccessTools.Method(typeof(JobGiver_GetHemogen), "TryGiveJob");

        public static void Postfix(ref Job __result, JobGiver_GetHemogen __instance, Pawn pawn)
        {
            if (__result != null) return;
            if (CrossLevelJobUtility.Scanning) return;

            __result = CrossLevelJobUtility.TryCrossLevelScan(pawn, () =>
            {
                Job job = (Job)method.Invoke(__instance, new object[] { pawn });
                return job != null;
            });
        }
    }

    /// <summary>死眠需求（Biotech DLC）</summary>
    [HarmonyPatch(typeof(JobGiver_GetDeathrest), "TryGiveJob")]
    public static class Patch_CrossLevel_GetDeathrest
    {
        private static readonly MethodInfo method =
            AccessTools.Method(typeof(JobGiver_GetDeathrest), "TryGiveJob");

        public static void Postfix(ref Job __result, JobGiver_GetDeathrest __instance, Pawn pawn)
        {
            if (__result != null) return;
            if (CrossLevelJobUtility.Scanning) return;

            __result = CrossLevelJobUtility.TryCrossLevelScan(pawn, () =>
            {
                Job job = (Job)method.Invoke(__instance, new object[] { pawn });
                return job != null;
            });
        }
    }
}