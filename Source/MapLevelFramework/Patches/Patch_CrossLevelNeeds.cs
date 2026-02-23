using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using MapLevelFramework.CrossFloor;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// 跨层需求处理 — 当 pawn 在当前层满足不了需求时，走楼梯去有资源的楼层。
    /// 使用 CrossFloorJobUtility 统一入口。
    /// 需求类不设置意图（可替代：任何食物/床/娱乐设施都行）。
    /// </summary>

    // ========== 困了找床 ==========
    [HarmonyPatch(typeof(JobGiver_GetRest), "TryGiveJob")]
    public static class Patch_CrossLevel_GetRest
    {
        public static void Postfix(ref Job __result, Pawn pawn)
        {
            if (pawn?.Map == null || !pawn.IsColonist) return;
            if (!pawn.Map.IsPartOfFloorSystem()) return;

            Need_Rest rest = pawn.needs?.rest;
            if (rest == null) return;

            // 有自己的床在其他楼层 → 去自己的床
            Building_Bed ownedBed = pawn.ownership?.OwnedBed;
            if (ownedBed != null && ownedBed.Map != pawn.Map
                && ownedBed.Map.IsPartOfFloorSystem())
            {
                // 原版给了 job（说明 pawn 该休息了，包括管制时间 Sleep）或者 pawn 已经 Tired
                if (__result != null || rest.CurCategory >= RestCategory.Tired)
                {
                    Job stairJob = CrossFloorJobUtility.TryGoToMap(pawn, ownedBed.Map, ownedBed.Position);
                    if (stairJob != null)
                    {
                        CrossLevelNeedsUtility.LogNeed(pawn, "休息",
                            $"自己的床在{FloorMapUtility.GetMapElevation(ownedBed.Map)}F，" +
                            $"去睡觉 (category={rest.CurCategory}, level={rest.CurLevelPercentage:P0})");
                        __result = stairJob;
                        return;
                    }
                }
            }

            // 当前层找到了真正的床（不是睡地板） → 不跨层
            if (__result != null && __result.targetA.HasThing
                && __result.targetA.Thing is Building_Bed)
                return;

            // 判断是否需要去睡觉：
            //   - 原版给了 job（包括管制 Sleep 时的地板睡觉 job）
            //   - pawn 已经 Tired
            //   - 管制时间是 Sleep
            bool shouldSleep = __result != null
                || rest.CurCategory >= RestCategory.Tired;
            if (!shouldSleep && pawn.timetable != null)
                shouldSleep = pawn.timetable.CurrentAssignment == TimeAssignmentDefOf.Sleep;
            if (!shouldSleep)
                return;

            // 精确搜索其他楼层的床
            Job job = CrossFloorJobUtility.FindNeedAcrossFloors(
                pawn,
                ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial),
                PathEndMode.OnCell,
                TraverseParms.For(pawn),
                9999f,
                t => t is Building_Bed bed && !bed.Medical && !bed.ForPrisoners);
            if (job != null)
            {
                CrossLevelNeedsUtility.LogNeed(pawn, "休息",
                    $"本层无床，跨层找床 (category={rest.CurCategory}, level={rest.CurLevelPercentage:P0}," +
                    $" schedule={pawn.timetable?.CurrentAssignment?.label ?? "?"})");
                __result = job;
            }
        }
    }

    // ========== 饿了找食物 ==========
    [HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
    public static class Patch_CrossLevel_GetFood
    {
        public static void Postfix(ref Job __result, Pawn pawn)
        {
            if (pawn?.Map == null || !pawn.IsColonist) return;
            if (!pawn.Map.IsPartOfFloorSystem()) return;

            // 原版找到了 → 不跨层
            if (__result != null) return;

            // 只在 UrgentlyHungry 或 Starving 时才跨层找食物。
            // 普通饥饿（Hungry）让 PrioritySorter 自然 fallthrough 到 Work。
            Need_Food food = pawn.needs?.food;
            if (food == null) return;
            if (food.CurCategory < HungerCategory.UrgentlyHungry) return;

            // 精确搜索其他楼层的食物
            Job job = CrossFloorJobUtility.FindNeedAcrossFloors(
                pawn,
                ThingRequest.ForGroup(ThingRequestGroup.FoodSourceNotPlantOrTree),
                PathEndMode.OnCell,
                TraverseParms.For(pawn),
                9999f);
            if (job != null)
            {
                CrossLevelNeedsUtility.LogNeed(pawn, "食物",
                    $"紧急饥饿跨层 (category={food.CurCategory}, level={food.CurLevelPercentage:P0})");
                __result = job;
            }
        }
    }

    // ========== 找娱乐 ==========
    [HarmonyPatch(typeof(JobGiver_GetJoy), "TryGiveJob")]
    public static class Patch_CrossLevel_GetJoy
    {
        public static void Postfix(ref Job __result, Pawn pawn)
        {
            if (pawn?.Map == null || !pawn.IsColonist) return;
            if (!pawn.Map.IsPartOfFloorSystem()) return;

            // 原版找到了 → 不跨层
            if (__result != null) return;

            // 只在 joy 非常低时才跨层
            Need_Joy joy = pawn.needs?.joy;
            if (joy == null) return;
            if (joy.CurLevelPercentage > 0.15f) return;

            // 精确搜索其他楼层的娱乐设施
            Job job = CrossFloorJobUtility.FindNeedAcrossFloors(
                pawn,
                ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial),
                PathEndMode.OnCell,
                TraverseParms.For(pawn),
                9999f,
                t => t.def.building != null && t.def.building.joyKind != null);
            if (job != null)
            {
                CrossLevelNeedsUtility.LogNeed(pawn, "娱乐",
                    $"娱乐极低跨层 (level={joy.CurLevelPercentage:P0})");
                __result = job;
            }
        }
    }

    internal static class CrossLevelNeedsUtility
    {
        public static void LogNeed(Pawn pawn, string needType, string reason)
        {
            if (!(MapLevelFrameworkMod.Settings?.debugPathfindingAndJob ?? false)) return;
            int elev = FloorMapUtility.GetMapElevation(pawn.Map);
            Log.Message($"【MLF】寻路与job检测-{pawn.LabelShort}—需求跨层: {needType}，原因: {reason}，当前楼层: {elev}F");
        }
    }
}
