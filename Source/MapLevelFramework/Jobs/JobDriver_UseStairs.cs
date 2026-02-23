using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using MapLevelFramework.CrossFloor;

namespace MapLevelFramework
{
    /// <summary>
    /// 上下楼 JobDriver - 走到楼梯后将 pawn 转移到目标地图。
    /// 电梯模式：targetB 存储目标楼层 elevation（IntVec3.x），可直达任意楼层。
    /// 未设置 targetB 时回退到楼梯自身的 targetElevation（向后兼容）。
    /// 转移后消费意图：如果有跨层工作意图，直接执行目标 job。
    /// </summary>
    public class JobDriver_UseStairs : JobDriver
    {
        private Building_Stairs Stairs => (Building_Stairs)job.targetA.Thing;

        /// <summary>
        /// 获取目标楼层 elevation。优先用 job.targetB，否则用楼梯默认值。
        /// </summary>
        private int TargetElevation =>
            job.targetB.IsValid ? job.targetB.Cell.x : Stairs.targetElevation;

        /// <summary>
        /// 目的地提示（最终目标位置，如床的位置）。用于选择离目的地最近的传送器落点。
        /// </summary>
        private IntVec3 DestHint =>
            job.targetC.IsValid ? job.targetC.Cell : IntVec3.Invalid;

        private static bool DebugLog =>
            MapLevelFrameworkMod.Settings?.debugPathfindingAndJob ?? false;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 楼梯不需要预约，多个 pawn 可以同时使用
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);

            // Toil 1: 走到楼梯
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.OnCell);

            // Toil 2: 转移到目标地图
            Toil transfer = ToilMaker.MakeToil("MLF_Transfer");
            transfer.initAction = delegate
            {
                Building_Stairs stairs = Stairs;
                if (stairs == null) return;

                int targetElev = TargetElevation;
                IntVec3 destHint = DestHint;
                if (StairTransferUtility.TryGetTransferTarget(stairs, targetElev, out Map destMap, out IntVec3 destPos, destHint))
                {
                    if (DebugLog)
                    {
                        int fromElev = stairs.GetCurrentElevation();
                        Log.Message($"【MLF】寻路与job检测-{pawn.LabelShort}—执行UseStairs: {ElevLabel(fromElev)}→{ElevLabel(targetElev)}");
                    }
                    StairTransferUtility.TransferPawn(pawn, destMap, destPos);
                }
            };
            transfer.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return transfer;

            // Toil 3: 消费意图 — 到达后尝试执行跨层工作目标
            Toil consumeIntent = ToilMaker.MakeToil("MLF_ConsumeIntent");
            consumeIntent.initAction = delegate
            {
                if (!CrossFloorIntent.TryGet(pawn, out var intent))
                    return;

                CrossFloorIntent.Clear(pawn);

                // 确认 pawn 已到达意图的目标地图
                if (pawn.Map == null || pawn.Map.uniqueID != intent.destMapId)
                    return;

                // 在目标位置找 Thing
                Thing target = FindThingAt(pawn.Map, intent.targetPos, intent.targetDef);
                if (target == null)
                {
                    if (DebugLog)
                        Log.Message($"【MLF】寻路与job检测-{pawn.LabelShort}—意图消费: 目标已消失 ({intent.targetDef?.defName} at {intent.targetPos})");
                    return;
                }

                // 遍历 WorkGiver 创建 Job
                Job intentJob = TryCreateJobForThing(pawn, target);
                if (intentJob != null)
                {
                    if (DebugLog)
                        Log.Message($"【MLF】寻路与job检测-{pawn.LabelShort}—意图消费: 成功 → {intentJob.def.defName} on {target.LabelShort}");
                    pawn.jobs.StartJob(intentJob, JobCondition.None, null, false, true);
                }
                else if (DebugLog)
                {
                    Log.Message($"【MLF】寻路与job检测-{pawn.LabelShort}—意图消费: 无 WorkGiver 能处理 {target.LabelShort}，原版接管");
                }
            };
            consumeIntent.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return consumeIntent;
        }

        /// <summary>
        /// 在地图指定位置找匹配的 Thing。
        /// </summary>
        private static Thing FindThingAt(Map map, IntVec3 pos, ThingDef def)
        {
            if (!pos.InBounds(map)) return null;
            var things = map.thingGrid.ThingsListAtFast(pos);
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t.def == def && t.Spawned)
                    return t;
            }
            // 精确位置没找到，搜附近（目标可能被推了一格）
            foreach (IntVec3 cell in GenAdj.CellsAdjacent8Way(pos, Rot4.North, IntVec2.One))
            {
                if (!cell.InBounds(map)) continue;
                var nearby = map.thingGrid.ThingsListAtFast(cell);
                for (int i = 0; i < nearby.Count; i++)
                {
                    if (nearby[i].def == def && nearby[i].Spawned)
                        return nearby[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 遍历 pawn 的 WorkGiver 列表，找到能处理目标 Thing 的 WorkGiver 并创建 Job。
        /// </summary>
        private static Job TryCreateJobForThing(Pawn pawn, Thing target)
        {
            if (pawn.workSettings == null) return null;

            var allDefs = DefDatabase<WorkGiverDef>.AllDefsListForReading;
            for (int i = 0; i < allDefs.Count; i++)
            {
                WorkGiverDef wgDef = allDefs[i];
                if (wgDef.Worker == null) continue;
                if (wgDef.workType == null) continue;

                // pawn 没启用这个工作类型 → 跳过
                if (!pawn.workSettings.WorkIsActive(wgDef.workType)) continue;

                WorkGiver_Scanner scanner = wgDef.Worker as WorkGiver_Scanner;
                if (scanner == null) continue;

                try
                {
                    if (scanner.HasJobOnThing(pawn, target, false))
                    {
                        Job job = scanner.JobOnThing(pawn, target, false);
                        if (job != null) return job;
                    }
                }
                catch
                {
                    // 某些 WorkGiver 对非预期 Thing 可能抛异常，忽略
                }
            }
            return null;
        }

        private static string ElevLabel(int elev)
        {
            if (elev > 0) return $"{elev + 1}F";
            if (elev < 0) return $"B{-elev}";
            return "1F";
        }
    }
}
