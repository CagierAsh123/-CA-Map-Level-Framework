using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MapLevelFramework
{
    /// <summary>
    /// 跨层电力传输管理器。
    /// 挂在所有地图上（MapComponent 自动注册），但只在基地图上工作。
    /// 每 60 tick 扫描所有楼梯对，根据两侧电网盈亏设置楼梯的 PowerOutput。
    /// </summary>
    public class PowerRelayManager : MapComponent
    {
        private const int UpdateInterval = 60;

        public PowerRelayManager(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            // 只在基地图上工作
            if (LevelManager.IsLevelMap(map, out _, out _)) return;

            if (Find.TickManager.TicksGame % UpdateInterval != 0) return;

            var mgr = LevelManager.GetManager(map);
            if (mgr == null || mgr.LevelCount == 0) return;

            UpdateAllPowerNets(mgr);
        }

        private void UpdateAllPowerNets(LevelManager mgr)
        {
            // 收集所有楼梯对：(stairA on mapA, stairB on mapB)
            // 楼梯对的定义：同一 position，一个在基地图/子地图，另一个在目标层级子地图
            foreach (var level in mgr.AllLevels)
            {
                Map levelMap = level.LevelMap;
                if (levelMap == null) continue;

                // 获取该层级地图上的所有楼梯
                var things = levelMap.listerThings.AllThings;
                for (int i = 0; i < things.Count; i++)
                {
                    if (!(things[i] is Building_Stairs stairOnLevel)) continue;

                    var compB = stairOnLevel.CompPowerTrader;
                    if (compB == null) continue;

                    // 找到配对楼梯：在 stairOnLevel.targetElevation 对应的地图上，
                    // 同一 position 的楼梯
                    Map pairMap = GetMapForElevation(mgr, stairOnLevel.targetElevation);
                    if (pairMap == null) continue;

                    Building_Stairs pairStair = FindStairAt(pairMap, stairOnLevel.Position);
                    if (pairStair == null) continue;

                    var compA = pairStair.CompPowerTrader;
                    if (compA == null) continue;

                    // 避免重复处理：只处理 level.elevation < targetElevation 的方向
                    if (level.elevation > stairOnLevel.targetElevation) continue;

                    UpdatePair(compA, compB);
                }
            }

            // 也处理基地图上的楼梯
            var baseThings = map.listerThings.AllThings;
            for (int i = 0; i < baseThings.Count; i++)
            {
                if (!(baseThings[i] is Building_Stairs stairOnBase)) continue;

                var compA = stairOnBase.CompPowerTrader;
                if (compA == null) continue;

                int targetElev = stairOnBase.targetElevation;
                if (targetElev == 0) continue;

                var levelData = mgr.GetLevel(targetElev);
                if (levelData?.LevelMap == null) continue;

                Building_Stairs pairStair = FindStairAt(levelData.LevelMap, stairOnBase.Position);
                if (pairStair == null) continue;

                var compB = pairStair.CompPowerTrader;
                if (compB == null) continue;

                UpdatePair(compA, compB);
            }
        }

        private Map GetMapForElevation(LevelManager mgr, int elevation)
        {
            if (elevation == 0) return map; // 地面层 = 基地图
            var level = mgr.GetLevel(elevation);
            return level?.LevelMap;
        }

        private static Building_Stairs FindStairAt(Map m, IntVec3 pos)
        {
            if (m == null || !pos.InBounds(m)) return null;
            var things = m.thingGrid.ThingsListAtFast(pos);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Building_Stairs s) return s;
            }
            return null;
        }

        /// <summary>
        /// 更新一对楼梯的电力传输。
        /// compA 和 compB 分别在不同地图上。
        /// </summary>
        private static void UpdatePair(CompPowerTrader compA, CompPowerTrader compB)
        {
            var netA = compA.PowerNet;
            var netB = compB.PowerNet;

            if (netA == null || netB == null)
            {
                compA.powerOutputInt = 0f;
                compB.powerOutputInt = 0f;
                return;
            }

            // 计算各自电网的净功率（排除楼梯自身贡献）
            // PowerOutput 单位是 W，正 = 产出，负 = 消耗
            float gainA = ComputeNetPower(netA, compA);
            float gainB = ComputeNetPower(netB, compB);

            if (gainA > 0f && gainB < 0f)
            {
                // A 盈余，B 亏损 → A 消耗，B 产出
                float transfer = UnityEngine.Mathf.Min(gainA, -gainB);
                compA.powerOutputInt = -transfer;
                compB.powerOutputInt = transfer;
            }
            else if (gainA < 0f && gainB > 0f)
            {
                // A 亏损，B 盈余 → A 产出，B 消耗
                float transfer = UnityEngine.Mathf.Min(-gainA, gainB);
                compA.powerOutputInt = transfer;
                compB.powerOutputInt = -transfer;
            }
            else
            {
                // 两侧同向（都盈余或都亏损）→ 空闲
                compA.powerOutputInt = 0f;
                compB.powerOutputInt = 0f;
            }
        }

        /// <summary>
        /// 计算电网的净功率（W），排除指定的 comp。
        /// 正 = 盈余，负 = 亏损。
        /// </summary>
        private static float ComputeNetPower(PowerNet net, CompPowerTrader exclude)
        {
            float total = 0f;
            for (int i = 0; i < net.powerComps.Count; i++)
            {
                var comp = net.powerComps[i];
                if (comp == exclude) continue;
                if (comp.PowerOn)
                    total += comp.PowerOutput;
            }
            return total;
        }
    }
}
