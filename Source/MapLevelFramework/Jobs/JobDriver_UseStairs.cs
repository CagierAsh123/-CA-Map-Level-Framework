using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MapLevelFramework
{
    /// <summary>
    /// 上下楼 JobDriver - 走到楼梯后将 pawn 转移到目标地图。
    /// </summary>
    public class JobDriver_UseStairs : JobDriver
    {
        private Building_Stairs Stairs => (Building_Stairs)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 楼梯不需要预约，多个 pawn 可以同时使用
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);

            // 走到楼梯
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.OnCell);

            // 转移到目标地图并恢复延迟 job
            Toil transfer = ToilMaker.MakeToil("MLF_Transfer");
            transfer.initAction = delegate
            {
                if (StairTransferUtility.TryGetTransferTarget(Stairs, out Map destMap, out IntVec3 destPos))
                {
                    StairTransferUtility.TransferPawn(pawn, destMap, destPos);

                    // 恢复跨层扫描时找到的 job
                    if (CrossLevelJobUtility.TryPopDeferredJob(pawn, out Job deferredJob))
                    {
                        bool started = false;
                        if (ValidateDeferredJob(deferredJob, destMap))
                        {
                            try
                            {
                                pawn.jobs.StartJob(deferredJob, JobCondition.None, null, false, true);
                                started = true;
                            }
                            catch (Exception ex)
                            {
                                Log.Warning($"[MLF] 延迟 job 启动失败: {ex.Message}");
                            }
                        }

                        // 延迟 job 失败 → 走楼梯回原来的楼层
                        if (!started)
                        {
                            TryReturnToOrigin(pawn, Stairs);
                        }
                    }
                }
            };
            transfer.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return transfer;
        }

        /// <summary>
        /// 验证延迟 job 的目标在目标地图上仍然有效。
        /// 扫描和实际转移之间可能经过多个 tick，目标可能已被销毁/移动。
        /// </summary>
        private bool ValidateDeferredJob(Job job, Map destMap)
        {
            if (job == null) return false;

            // 检查 thing 目标是否仍然 spawned、在正确的地图上、且可预约
            for (int i = 0; i < 3; i++)
            {
                LocalTargetInfo target = i switch
                {
                    0 => job.targetA,
                    1 => job.targetB,
                    _ => job.targetC
                };
                if (target.HasThing && target.Thing != null)
                {
                    if (!target.Thing.Spawned || target.Thing.Map != destMap)
                        return false;
                    // 检查是否已被其他 pawn 预约（避免多人抢同一物品）
                    if (!pawn.CanReserve(target.Thing))
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 延迟 job 失败时，找到回去的楼梯让 pawn 走回原来的楼层。
        /// 楼梯是双向的：用来的那个楼梯反向走就能回去。
        /// </summary>
        private void TryReturnToOrigin(Pawn p, Building_Stairs arrivedFrom)
        {
            // arrivedFrom 是出发层的楼梯，对应的目标层楼梯就在 pawn 当前位置附近
            // 找当前地图上同位置/最近的楼梯走回去
            Map currentMap = p.Map;
            if (currentMap == null) return;

            var allStairs = currentMap.listerBuildings.AllBuildingsColonistOfClass<Building_Stairs>();
            Building_Stairs best = null;
            float bestDist = float.MaxValue;
            foreach (var s in allStairs)
            {
                if (!s.Spawned) continue;
                // 楼梯必须通往来的方向（即 arrivedFrom 所在的地图）
                if (!StairTransferUtility.TryGetTransferTarget(s, out Map targetMap, out _)) continue;
                if (targetMap != arrivedFrom.Map) continue;

                float dist = s.Position.DistanceToSquared(p.Position);
                if (dist < bestDist)
                {
                    best = s;
                    bestDist = dist;
                }
            }

            if (best != null)
            {
                try
                {
                    Job returnJob = JobMaker.MakeJob(MLF_JobDefOf.MLF_UseStairs, best);
                    p.jobs.StartJob(returnJob, JobCondition.None, null, false, true);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[MLF] 返回原楼层失败: {ex.Message}");
                }
            }
        }
    }
}
