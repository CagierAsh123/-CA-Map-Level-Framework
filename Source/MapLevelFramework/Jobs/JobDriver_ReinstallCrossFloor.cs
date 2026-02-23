using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using MapLevelFramework.CrossFloor;

namespace MapLevelFramework
{
    /// <summary>
    /// 跨层重新安装 JobDriver。
    /// 多段流程：走到建筑 → 拆卸 → 拿起 → 走楼梯 → 传送 → 交给原版安装。
    ///
    /// TargetA = 要拆卸的建筑（Building，在 pawn 当前层）
    /// TargetB = 楼梯（在 pawn 当前层）
    /// targetC = 编码 (x=destElevation, y=blueprintPos.x, z=blueprintPos.z)
    /// </summary>
    public class JobDriver_ReinstallCrossFloor : JobDriver
    {
        private Thing BuildingToReinstall => job.targetA.Thing;
        private Building_Stairs Stairs => (Building_Stairs)job.targetB.Thing;

        private int DestElevation => job.targetC.IsValid ? job.targetC.Cell.x : 0;
        private IntVec3 BlueprintPos => job.targetC.IsValid
            ? new IntVec3(job.targetC.Cell.y, 0, job.targetC.Cell.z)
            : IntVec3.Invalid;

        private float workLeft = -1f;
        private float totalWork = -1f;

        private static bool DebugLog =>
            MapLevelFrameworkMod.Settings?.debugPathfindingAndJob ?? false;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 注意：不能用 FailOnDestroyedNullOrForbidden(TargetIndex.A)
            // 因为建筑会在拆卸后被 MakeMinified 销毁，这是正常流程
            this.FailOnDespawnedOrNull(TargetIndex.B);

            // 1. 走到建筑
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            // 2. 拆卸建筑（对标原版 JobDriver_Uninstall / JobDriver_RemoveBuilding）
            Toil uninstall = ToilMaker.MakeToil("MLF_Uninstall");
            uninstall.initAction = delegate
            {
                Thing building = BuildingToReinstall;
                if (building == null) return;
                totalWork = building.def.building?.uninstallWork ?? 200f;
                if (totalWork <= 0f) totalWork = 200f;
                workLeft = totalWork;
                if (DebugLog)
                    Log.Message($"【MLF】跨层重装-{pawn.LabelShort}—开始拆卸 {building.LabelShort}, uninstallWork={totalWork}");
            };
            uninstall.tickAction = delegate
            {
                Thing building = BuildingToReinstall;
                if (building == null || building.Destroyed)
                {
                    if (DebugLog)
                        Log.Message($"【MLF】跨层重装-{pawn.LabelShort}—建筑已销毁，跳到下一步");
                    ReadyForNextToil();
                    return;
                }

                float speed = pawn.GetStatValue(StatDefOf.ConstructionSpeed) * 1.7f;
                workLeft -= speed;
                pawn.skills?.Learn(SkillDefOf.Construction, 0.25f);

                if (workLeft <= 0f)
                {
                    if (DebugLog)
                        Log.Message($"【MLF】跨层重装-{pawn.LabelShort}—拆卸工作完成，进入 minify 步骤");
                    ReadyForNextToil();
                }
            };
            uninstall.defaultCompleteMode = ToilCompleteMode.Never;
            uninstall.WithProgressBar(TargetIndex.A, () =>
            {
                if (totalWork <= 0f) return 1f;
                return 1f - workLeft / totalWork;
            });
            yield return uninstall;

            // 3. 拆卸完成 → 生成 MinifiedThing 并拿起
            Toil minifyAndPickup = ToilMaker.MakeToil("MLF_MinifyAndPickup");
            minifyAndPickup.initAction = delegate
            {
                Thing building = BuildingToReinstall;
                if (DebugLog)
                    Log.Message($"【MLF】跨层重装-{pawn.LabelShort}—minifyAndPickup: building={building?.LabelShort ?? "null"}, destroyed={building?.Destroyed}");

                if (building == null || building.Destroyed)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                Map buildingMap = building.Map;
                IntVec3 buildingPos = building.Position;

                if (building.def.Minifiable)
                {
                    MinifiedThing mini = building.MakeMinified(DestroyMode.Vanish);
                    if (mini != null)
                    {
                        // MakeMinified 返回未 Spawn 的 MinifiedThing，直接放入 carryTracker
                        // 不能先 Spawn 再 Carry，因为 Spawn 会把 mini 放入地图容器，
                        // TryStartCarry 无法从容器中转移
                        if (pawn.carryTracker.TryStartCarry(mini))
                        {
                            if (DebugLog)
                                Log.Message($"【MLF】跨层重装-{pawn.LabelShort}—拿起成功 {mini.LabelShort}，前往楼梯");
                            return;
                        }
                        // 拿不起来（太重等），放到地上
                        GenSpawn.Spawn(mini, buildingPos, buildingMap);
                        if (DebugLog)
                            Log.Warning($"【MLF】跨层重装-{pawn.LabelShort}—无法拿起{mini.LabelShort}，已放到地上");
                    }
                }
                else
                {
                    Log.Warning($"【MLF】跨层重装-{pawn.LabelShort}—建筑{building.LabelShort}不可 minify");
                }
                EndJobWith(JobCondition.Incompletable);
            };
            minifyAndPickup.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return minifyAndPickup;

            // 4. 走到楼梯
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.OnCell);

            // 5. 传送到目标层 + 交给原版安装
            Toil transferAndInstall = ToilMaker.MakeToil("MLF_TransferAndInstall");
            transferAndInstall.initAction = delegate
            {
                Building_Stairs stairs = Stairs;
                if (stairs == null) return;

                Thing carried = pawn.carryTracker.CarriedThing;
                if (carried == null) return;

                int destElev = DestElevation;
                IntVec3 bpPos = BlueprintPos;

                if (!StairTransferUtility.TryGetTransferTarget(
                        stairs, destElev, out Map destMap, out IntVec3 destPos))
                {
                    if (DebugLog)
                        Log.Message($"【MLF】跨层重装-{pawn.LabelShort}—传送失败: 无法到达 elev={destElev}");
                    return;
                }

                if (DebugLog)
                    Log.Message($"【MLF】跨层重装-{pawn.LabelShort}—传送: {pawn.Map.uniqueID}→{destMap.uniqueID}, 携带{carried.LabelShort}");

                // 传送 pawn（携带物品自动保留）
                StairTransferUtility.TransferPawn(pawn, destMap, destPos);

                // 在目标层找 Blueprint_Install
                Blueprint_Install blueprint = FindInstallBlueprintAt(destMap, bpPos);
                if (blueprint == null)
                {
                    if (DebugLog)
                        Log.Message($"【MLF】跨层重装-{pawn.LabelShort}—目标位置无安装蓝图 at {bpPos}，丢下物品");
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
                    return;
                }

                // 创建原版 HaulToContainer job 完成安装
                Thing carriedNow = pawn.carryTracker.CarriedThing;
                if (carriedNow == null) return;

                Job haulJob = JobMaker.MakeJob(JobDefOf.HaulToContainer);
                haulJob.targetA = carriedNow;
                haulJob.targetB = blueprint;
                haulJob.targetC = blueprint;
                haulJob.count = 1;
                haulJob.haulMode = HaulMode.ToContainer;

                if (DebugLog)
                    Log.Message($"【MLF】跨层重装-{pawn.LabelShort}—交接原版安装: {carriedNow.LabelShort}→{blueprint.LabelShort}");

                pawn.jobs.StartJob(haulJob, JobCondition.None, null, false, true);
            };
            transferAndInstall.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return transferAndInstall;
        }

        /// <summary>
        /// 在目标地图指定位置找 Blueprint_Install。
        /// </summary>
        private static Blueprint_Install FindInstallBlueprintAt(Map map, IntVec3 pos)
        {
            if (!pos.InBounds(map)) return null;

            var things = map.thingGrid.ThingsListAtFast(pos);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Blueprint_Install bp && bp.Spawned)
                    return bp;
            }

            // 蓝图可能偏移一格
            foreach (IntVec3 cell in GenAdj.CellsAdjacent8Way(pos, Rot4.North, IntVec2.One))
            {
                if (!cell.InBounds(map)) continue;
                var nearby = map.thingGrid.ThingsListAtFast(cell);
                for (int i = 0; i < nearby.Count; i++)
                {
                    if (nearby[i] is Blueprint_Install bp && bp.Spawned)
                        return bp;
                }
            }

            return null;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref workLeft, "workLeft", -1f);
            Scribe_Values.Look(ref totalWork, "totalWork", -1f);
        }
    }
}
