# Region 分段寻路设计

## 核心原则

**绝不把跨图 Thing 交给原版代码。**

原版整个 job 系统（Reserve、PathFinder、validator）假设 Thing 和 pawn 在同一张 Map。
违反这个假设 = 炸。之前三个 patch 全部因此被禁用。

## 架构：两层分离

```
┌─────────────────────────────────────────────┐
│  Region 层（跨图）                            │
│  "我知道目标在哪，通过楼梯可达"                   │
│  - CrossFloorReachabilityUtility              │
│  - GenClosestCrossFloor                       │
│  - RegionTraverserAcrossFloors                │
└──────────────────┬──────────────────────────┘
                   │ 找到跨图目标后
                   │ 不返回 Thing，返回 UseStairs job
                   ▼
┌─────────────────────────────────────────────┐
│  PathFinder 层（单图）                        │
│  "在当前 Map 内走到楼梯"                       │
│  - 原版 Pawn_PathFollower                     │
│  - 原版 PathFinder A*                         │
│  到达楼梯 → TransferPawn → 新 Map 内继续      │
└─────────────────────────────────────────────┘
```

## Region 连接模型

两张 Map 大小相同，楼梯在两层的 IntVec3 位置完全一致。

```
Map 1F                          Map 2F
┌───────────────────┐          ┌───────────────────┐
│ Region A  Region B│          │ Region X  Region Y│
│          ┌──┐     │          │          ┌──┐     │
│          │楼│     │          │          │楼│     │
│          │梯│     │          │          │梯│     │
│          └──┘     │          │          └──┘     │
│ Region C  Region D│          │ Region Z  Region W│
└───────────────────┘          └───────────────────┘
                  │                    ▲
                  └── 虚拟 Link ───────┘
                  （楼梯 Region 之间）
```

RegionTraverserAcrossFloors 的 BFS：
1. 标准 BFS：dequeue region → process → enqueue neighbors
2. 额外步骤：region 内有楼梯？→ 跳到目标层对应 Region → enqueue

## 可达性（Phase 3）

### CrossFloorReachabilityUtility.CanReach

已实现，逻辑正确：
1. 同 Map → 直接用原版 `Reachability.CanReach`
2. 不同 Map → 分段：pawn 能到楼梯？→ 楼梯目标层能到 dest？→ 递归多跳
3. 中间跳用无 pawn 的 `TraverseParms`（pawn 不在中间层）
4. 120 tick 缓存 + `working` 防递归

### Patch_Reachability_CrossFloor — 不启用

原版 `Reachability.CanReach` 的调用者太多，返回 true 后调用者会做同图操作。
可达性只在我们自己的代码中调用，不 patch 原版。

## 物品搜索（Phase 4）

### GenClosestCrossFloor.ClosestThingOnOtherFloors

已实现，逻辑正确：
1. 遍历所有楼层 Map
2. `listerThings.ThingsMatching(thingReq)` 找匹配物品
3. 跳过 Forbidden
4. `CrossFloorReachabilityUtility.CanReach` 检查可达
5. 估算距离（到楼梯 + 楼梯到目标 + 50f 惩罚）
6. 返回最近的

### Patch_GenClosest_CrossFloor — 不启用

返回跨图 Thing 给原版 = 炸。物品搜索只在我们的 JobGiver patch 中调用。

## 分段寻路（Phase 5）

### 流程

```
pawn 在 1F，job 目标在 3F:

1. JobGiver patch 调用 GenClosestCrossFloor 找到 3F 的目标
2. 不返回 3F 的 Thing，而是：
   a. 计算路径：1F → 楼梯A → 2F → 楼梯B → 3F
   b. 创建 UseStairs job（目标：1F 上通往 2F 的楼梯A）
   c. job.targetB = 目标楼层高程
3. pawn 在 1F 内寻路到楼梯A（原版 PathFinder）
4. 到达楼梯A → TransferPawn → 到 2F
5. UseStairs 完成 → ThinkTree 重新评估
6. JobGiver patch 再次调用 GenClosestCrossFloor
7. 目标仍在 3F → 创建 UseStairs（目标：2F 上通往 3F 的楼梯B）
8. 到达楼梯B → TransferPawn → 到 3F
9. ThinkTree 重新评估 → 原版 JobGiver 在 3F 找到目标 → 正常 job
```

### 关键：每次只走一跳

不需要预计算完整路径。每次 UseStairs 只走一层。
到达后 ThinkTree 重新评估：
- 目标在当前层 → 原版接管
- 目标在其他层 → 再走一跳

### Patch_PathFollower_CrossFloor — 不启用

在 StartPath 里调 StartJob 导致重入。分段寻路在 JobGiver 层面处理。

## Job 集成

### 统一入口：CrossFloorJobUtility

新建工具类，所有 JobGiver patch 共用：

```csharp
public static class CrossFloorJobUtility
{
    /// <summary>
    /// 尝试为跨层目标创建 UseStairs job。
    /// 如果目标在其他楼层且可达，返回走楼梯的 job。
    /// 如果目标在当前层或不可达，返回 null。
    /// </summary>
    public static Job TryMakeStairsJobForTarget(
        Pawn pawn, Thing target)
    {
        if (target == null) return null;
        if (target.Map == pawn.Map) return null;  // 同层，原版处理
        if (!pawn.Map.IsPartOfFloorSystem()) return null;

        int destElev = FloorMapUtility.GetMapElevation(target.Map);
        var stairs = FloorMapUtility.FindStairsToFloor(pawn, pawn.Map, destElev);
        if (stairs == null) return null;

        Job job = JobMaker.MakeJob(MLF_JobDefOf.MLF_UseStairs, stairs);
        job.targetB = new IntVec3(destElev, 0, 0);
        return job;
    }

    /// <summary>
    /// 在所有楼层搜索满足条件的最近物品。
    /// 返回 Thing（可能跨图）+ 对应的 UseStairs job。
    /// 调用者不应把返回的 Thing 交给原版代码。
    /// </summary>
    public static (Thing thing, Job stairsJob) FindThingAcrossFloors(
        Pawn pawn, ThingRequest req,
        PathEndMode peMode, TraverseParms tp,
        float maxDist, Predicate<Thing> validator)
    {
        Thing found = GenClosestCrossFloor.ClosestThingOnOtherFloors(
            pawn.Position, pawn.Map, req, peMode, tp, maxDist, validator);
        if (found == null) return (null, null);

        Job stairsJob = TryMakeStairsJobForTarget(pawn, found);
        return (found, stairsJob);
    }
}
```

### 各 JobGiver 的集成方式

#### 食物（JobGiver_GetFood）

```
Postfix on TryGiveJob:
  if __result != null → 原版找到了，不管
  if 饥饿度 < UrgentlyHungry → 不跨层（让 PrioritySorter fallthrough 到 Work）
  调用 FindThingAcrossFloors(pawn, FoodSourceNotPlantOrTree, ...)
  if found → __result = stairsJob
```

#### 休息（JobGiver_GetRest）

```
Postfix on TryGiveJob:
  if 疲劳度 < VeryTired → 不跨层
  检查自己的床是否在其他楼层 → TryMakeStairsJobForTarget
  否则 → 搜索其他楼层的空床
```

#### 娱乐（JobGiver_GetJoy）

```
Postfix on TryGiveJob:
  if joy > 15% → 不跨层
  搜索其他楼层的娱乐设施
```

#### 工作（JobGiver_Work）— 已有

`Patch_JobGiver_Work_CrossFloor` 已经在 job 层面处理。
重构后可以复用 CrossFloorJobUtility。

#### 搬运（WorkGiver_HaulAcrossLevel）— 已有

已经用 UseStairs 模式。保持不变。

## 需求阈值

跨层有代价（走楼梯耗时），不应该轻微需求就跨层。

| 需求 | 跨层阈值 | 原因 |
|------|----------|------|
| 食物 | UrgentlyHungry (< 12%) | 普通饥饿让 PrioritySorter fallthrough 到 Work |
| 休息 | VeryTired (< 14%) | 轻微困倦继续工作 |
| 娱乐 | joy < 15% | 娱乐优先级最低 |
| 工作 | 无阈值（已有 600 tick 冷却） | 工作是主要跨层动机 |

## 不做的事

1. ❌ 不 patch `Reachability.CanReach` — 调用者太多，返回 true 后做同图操作
2. ❌ 不 patch `GenClosest.ClosestThingReachable` — 返回跨图 Thing 给原版
3. ❌ 不 patch `Pawn_PathFollower.StartPath` — StartPath 里调 StartJob 重入
4. ❌ 不把跨图 Thing 作为 job.targetA — 原版会对它做 Reserve/PathFind
5. ❌ 不在一次 UseStairs 中走多跳 — 每次只走一层，到达后重新评估

## 实现步骤

### Step 1：创建 CrossFloorJobUtility
- `TryMakeStairsJobForTarget(pawn, thing)`
- `FindThingAcrossFloors(pawn, req, ...)`
- 复用现有 `GenClosestCrossFloor` 和 `CrossFloorReachabilityUtility`

### Step 2：重写 Patch_CrossLevelNeeds
- 食物/休息/娱乐统一用 CrossFloorJobUtility
- 加需求阈值
- 删除旧的 HasFood/HasAvailableBed/HasJoySource 简单检查
- 改用 GenClosestCrossFloor 精确搜索

### Step 3：重构 Patch_JobGiver_Work_CrossFloor
- P3（去其他楼层找工作）改用 CrossFloorJobUtility
- P1/P2（跨层材料搬运）保持不变（已经是物理搬运模式）

### Step 4：清理
- 删除被禁用的三个 patch 文件（不是注释掉，是删除）
- 或保留但标记为 `[Obsolete]` 参考用

## 验证场景

1. 1F pawn 饿了（UrgentlyHungry），食物在 2F → 走楼梯去 2F 吃
2. 1F pawn 轻微饿（Hungry），工作在 2F → 去 2F 工作，不被食物拉回
3. 2F pawn 工作完，轻微饿 → 回 1F 闲逛时原版自然找到 1F 的食物
4. 1F→3F 多跳：pawn 自动经过 2F 到达 3F
5. 拆楼梯后：跨层不可达，pawn 不尝试
6. 100+ pawn 无明显卡顿
