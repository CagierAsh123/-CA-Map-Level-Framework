using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using MapLevelFramework.CrossFloor;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// Patch 原版建造 WorkGiver：当本层没材料时，搜索其他楼层并创建封装跨层投递 job。
    ///
    /// 触发条件：原版 JobOnThing 返回 null（本层没材料）
    /// 行为：搜索其他楼层的材料 → 创建 DeliverResourcesCrossFloor job
    /// </summary>
    [HarmonyPatch]
    public static class Patch_WorkGiver_Construct_CrossFloor
    {
        private static bool DebugLog =>
            MapLevelFrameworkMod.Settings?.debugPathfindingAndJob ?? false;

        // ========== Patch: WorkGiver_ConstructDeliverResourcesToBlueprints ==========

        [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToBlueprints),
            nameof(WorkGiver_ConstructDeliverResourcesToBlueprints.JobOnThing))]
        [HarmonyPostfix]
        public static void Postfix_Blueprints(
            ref Job __result, Pawn pawn, Thing t, bool forced)
        {
            if (__result != null) return;
            TryCrossFloorDeliver(ref __result, pawn, t);
        }

        // ========== Patch: WorkGiver_ConstructDeliverResourcesToFrames ==========

        [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToFrames),
            nameof(WorkGiver_ConstructDeliverResourcesToFrames.JobOnThing))]
        [HarmonyPostfix]
        public static void Postfix_Frames(
            ref Job __result, Pawn pawn, Thing t, bool forced)
        {
            if (__result != null) return;
            TryCrossFloorDeliver(ref __result, pawn, t);
        }

        // ========== 核心逻辑 ==========

        /// <summary>
        /// 原版返回 null 时，检查蓝图/框架缺什么材料，在其他楼层搜索。
        /// 找到 → 创建 DeliverResourcesCrossFloor job。
        /// </summary>
        private static void TryCrossFloorDeliver(ref Job result, Pawn pawn, Thing constructible)
        {
            if (pawn?.Map == null) return;
            if (!pawn.Map.IsPartOfFloorSystem()) return;

            IConstructible c = constructible as IConstructible;
            if (c == null) return;

            // Blueprint_Install 走专用跨层重装逻辑，不走材料投递
            if (constructible is Blueprint_Install bpInstall)
            {
                TryCrossFloorInstall(ref result, pawn, bpInstall);
                return;
            }

            // 检查蓝图/框架的基本条件（原版可能因为其他原因返回 null，如技能不足）
            if (constructible.Faction != pawn.Faction) return;
            if (!GenConstruct.CanConstruct(constructible, pawn, false, false, JobDefOf.HaulToContainer))
                return;

            // 收集缺少的材料
            List<ThingDefCountClass> costs = c.TotalMaterialCost();
            for (int i = 0; i < costs.Count; i++)
            {
                ThingDefCountClass need = costs[i];
                int needed = c.ThingCountNeeded(need.thingDef);
                if (needed <= 0) continue;

                // 本层有这个材料？那原版应该能处理，跳过
                if (pawn.Map.itemAvailability.ThingsAvailableAnywhere(
                        need.thingDef, needed, pawn))
                    continue;

                // 本层没有 → 搜索其他楼层
                Thing material = FindMaterialOnOtherFloors(
                    pawn, pawn.Map, need.thingDef, needed);
                if (material == null) continue;

                // 找到了！创建封装 job
                int materialElev = FloorMapUtility.GetMapElevation(material.Map);
                int destElev = FloorMapUtility.GetMapElevation(constructible.Map);

                // pawn 需要先去材料所在层拿材料
                // 如果材料在 pawn 当前层 → 直接创建 job
                // 如果材料在其他层 → 先 UseStairs 去拿（这里只处理材料在其他层的情况）
                Building_Stairs stairsToMaterial =
                    FloorMapUtility.FindStairsToFloor(pawn, pawn.Map, materialElev);
                if (stairsToMaterial == null) continue;

                // 编码蓝图位置到 targetC: (destElevation, blueprintPos.x, blueprintPos.z)
                IntVec3 encoded = new IntVec3(
                    destElev,
                    constructible.Position.x,
                    constructible.Position.z);

                // 创建两段 job：
                // 第一段：UseStairs 去材料层
                // 到达后由意图系统触发第二段（DeliverResourcesCrossFloor）
                //
                // 但更简单的方式：如果材料就在 pawn 当前层，直接创建 DeliverResourcesCrossFloor
                // 如果材料在其他层，先 UseStairs 过去，到达后原版会重新扫描

                if (material.Map == pawn.Map)
                {
                    // 材料在当前层，蓝图在其他层 → 直接封装 job
                    Building_Stairs stairsToDest =
                        FloorMapUtility.FindStairsToFloor(pawn, pawn.Map, destElev);
                    if (stairsToDest == null) continue;

                    int toCarry = Math.Min(needed, material.stackCount);
                    toCarry = Math.Min(toCarry,
                        pawn.carryTracker.MaxStackSpaceEver(material.def));

                    Job job = JobMaker.MakeJob(
                        MLF_JobDefOf.MLF_DeliverResourcesCrossFloor,
                        material, stairsToDest);
                    job.targetC = encoded;
                    job.count = toCarry;

                    if (DebugLog)
                        Log.Message($"【MLF】跨层建造-{pawn.LabelShort}—" +
                            $"封装投递: {material.LabelShort}x{toCarry} → " +
                            $"elev={destElev} at {constructible.Position}");

                    result = job;
                    return;
                }
                else
                {
                    // 材料在其他层 → UseStairs 去材料层
                    // 到达后原版 WorkGiver 重新扫描，此时材料在 pawn 当前层 → 走上面的分支
                    Job stairsJob = JobMaker.MakeJob(MLF_JobDefOf.MLF_UseStairs, stairsToMaterial);
                    stairsJob.targetB = new IntVec3(materialElev, 0, 0);

                    if (DebugLog)
                        Log.Message($"【MLF】跨层建造-{pawn.LabelShort}—" +
                            $"去取材料: UseStairs → elev={materialElev} 取{need.thingDef.defName}");

                    result = stairsJob;
                    return;
                }
            }
        }

        /// <summary>
        /// 跨层重装：Blueprint_Install 在 pawn 当前层，但要安装的建筑在其他层。
        /// 原版 InstallJob 因 CanReach 失败返回 null。
        /// 处理：pawn 先 UseStairs 去建筑所在层，到达后 TryScanCrossFloorReinstall 接管。
        /// </summary>
        private static void TryCrossFloorInstall(ref Job result, Pawn pawn, Blueprint_Install bpInstall)
        {
            Thing thingToInstall = GenConstruct.MiniToInstallOrBuildingToReinstall(bpInstall);
            if (thingToInstall?.Map == null || !thingToInstall.Spawned) return;

            // 建筑在 pawn 当前层 → 不是跨层问题，原版应该能处理
            if (thingToInstall.Map == pawn.Map) return;

            // 建筑在其他层 → pawn 先过去
            int targetElev = FloorMapUtility.GetMapElevation(thingToInstall.Map);
            Building_Stairs stairs = FloorMapUtility.FindStairsToFloor(pawn, pawn.Map, targetElev);
            if (stairs == null) return;

            // 设置意图：到达后找到建筑，TryScanCrossFloorReinstall 会创建 ReinstallCrossFloor job
            CrossFloorIntent.Set(pawn, thingToInstall.Map.uniqueID, thingToInstall.Position, thingToInstall.def);

            Job stairsJob = JobMaker.MakeJob(MLF_JobDefOf.MLF_UseStairs, stairs);
            stairsJob.targetB = new IntVec3(targetElev, 0, 0);

            if (DebugLog)
                Log.Message($"【MLF】跨层重装-{pawn.LabelShort}—" +
                    $"Blueprint_Install在本层，建筑在elev={targetElev}→UseStairs去拆");

            result = stairsJob;
        }

        /// <summary>
        /// 在其他楼层搜索指定材料。返回最近的可用材料。
        /// </summary>
        private static Thing FindMaterialOnOtherFloors(
            Pawn pawn, Map pawnMap, ThingDef matDef, int minCount)
        {
            Thing best = null;
            float bestDist = float.MaxValue;

            foreach (Map otherMap in pawnMap.BaseMapAndFloorMaps())
            {
                if (otherMap == pawnMap) continue;

                var things = otherMap.listerThings.ThingsOfDef(matDef);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (!t.Spawned) continue;
                    if (t.IsForbidden(pawn)) continue;

                    // 跨层可达性
                    if (!CrossFloorReachabilityUtility.CanReach(
                        pawnMap, pawn.Position, otherMap, t.Position,
                        PathEndMode.ClosestTouch,
                        TraverseParms.For(pawn, Danger.Deadly)))
                        continue;

                    float dist = GenClosestCrossFloor.EstimateCrossFloorDist(
                        pawn.Position, pawnMap, t.Position, otherMap);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = t;
                    }
                }
            }

            return best;
        }
    }
}
