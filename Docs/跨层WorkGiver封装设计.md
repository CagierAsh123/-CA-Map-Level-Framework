# 跨层 WorkGiver 封装设计

## 核心原则

**不改原版 WorkGiver 管线，在外围做封装。**

原版建造流程是单地图闭环：
```
WorkGiver_ConstructDeliverResourcesToBlueprints.JobOnThing(blueprint)
  → ResourceDeliverJobFor(pawn, blueprint)
    → ThingsAvailableAnywhere(need.thingDef, count, pawn)  // pawn.Map 内检查
    → GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ...)  // 同地图搜索
    → HaulToContainer(material → blueprint)

WorkGiver_ConstructFinishFrames.JobOnThing(frame)
  → frame.IsCompleted() → FinishFrame
```

跨层系统的职责：**让材料和蓝图出现在同一层，然后原版自然接管。**

## 封装方案：全程手持 + 交接原版

### 流程

```
JobDriver_DeliverResourcesCrossFloor:
  1. GotoThing(material)        — 走到材料（当前层）
  2. StartCarryThing            — 拿起来（材料在手里）
  3. GotoThing(stairs)          — 走到楼梯
  4. Transfer                   — 传送到目标层（材料跟着走）
  5. 在目标层找到蓝图/框架
  6. StartJob(HaulToContainer)  — 交给原版完成投递
     ↓ 原版接管
     pawn 手里有材料 → JumpIf 跳过拾取 → GotoBuild → 塞进框架
```

### 为什么安全

| 保障 | 说明 |
|------|------|
| 材料不丢失 | 全程在 pawn 手里，不会被抢走或搬进仓库 |
| 无分配间隙 | 到达目标层后立即 StartJob(HaulToContainer)，不等下一 tick |
| 原版兼容 | HaulToContainer 的 JumpIf(IsCarryingThing) 天然支持手持投递 |
| 蓝图取消 | HaulToContainer 的 FailOn 检测到 → 终止 → 材料掉在目标层（不浪费） |
| 中途打断 | 战斗/饥饿打断 → 材料掉在当前位置 → 不消失 |

## 双触发点设计

原版 WorkGiver 只扫描 pawn.Map 上的蓝图/框架，所以需要两个触发点覆盖两种场景：

### 场景 A：蓝图在 pawn 当前层，材料在其他层

触发点：`Patch_WorkGiver_Construct_CrossFloor`（Postfix on JobOnThing）

```
原版扫描 pawn.Map 上的蓝图 → 找不到材料 → 返回 null
  ↓ Postfix 介入
搜索其他楼层的材料
  → 材料在其他层 → UseStairs 去材料层
  → 到达材料层后 → 场景 B 接管
```

### 场景 B：材料在 pawn 当前层，蓝图在其他层

触发点：`Patch_JobGiver_Work_CrossFloor` 的 P1（TryCreateCrossFloorDeliverJob）

```
pawn 本层无工作 → P1 扫描其他层的蓝图/框架
  → 发现其他层蓝图缺材料，本层有
  → 创建 DeliverResourcesCrossFloor job
  → pawn 拿材料 → 走楼梯 → 传送 → HaulToContainer 投递
```

### 完整循环示例

```
1F 有木头，3F 有蓝图需要木头：

pawn 在 1F → P1 发现 3F 蓝图缺木头，1F 有木头
  → DeliverResourcesCrossFloor: 拿木头 → 走楼梯 → 到 3F → HaulToContainer

pawn 在 3F → WorkGiver 扫描蓝图 → 没材料 → Postfix 发现 1F 有木头
  → UseStairs 去 1F → 到 1F 后 P1 接管 → DeliverResourcesCrossFloor
```

## P3 智能过滤规则

P3（去有工作的楼层）必须过滤掉"去了也做不了"的情况：

```
允许跨层的工作类型：
  ✅ 清洁（不需要材料）
  ✅ 开采（不需要材料）
  ✅ 拆除（不需要材料）
  ✅ 打磨（不需要材料）
  ✅ 建造 — 仅当目标层材料已齐（frame.IsCompleted() 或蓝图的材料在目标层）
  ✅ FinishFrame（框架已完成）
  ✅ 灭火（紧急）
  ✅ 医疗（紧急）

  ❌ 建造 — 目标层有蓝图但缺材料（应由 P1/封装 job 先搬材料）
  ❌ 搬运 — 目标层有待搬运物品但本层也有（优先本层）
```

## 删除/替换的旧逻辑

| 旧代码 | 替换为 |
|--------|--------|
| P1 TryCreateHaulToStairsJob | P1 TryCreateCrossFloorDeliverJob（封装 job） |
| P2 TryCreateFetchMaterialJob | 删除（Postfix + P1 已覆盖） |
| P3 TryCreateGoToWorkFloorJob | 保留但加智能过滤 |
| TryFindHigherPriorityWork | 保留（跨层优先级比较仍需要） |
| CollectMaterialNeeds 系列 | 删除（不再需要自己收集需求） |
| FindMaterialOnMap | 删除（改用原版 GenClosest） |

## 新增文件

```
Jobs/JobDriver_DeliverResourcesCrossFloor.cs   — 封装 JobDriver
Patches/Patch_WorkGiver_Construct_CrossFloor.cs — WorkGiver Postfix
Defs/MLF_Defs.xml                              — 新增 JobDef
Jobs/MLF_JobDefOf.cs                           — 新增引用
```

## 原版关键代码参考

### ResourceDeliverJobFor 失败条件（返回 null 的原因）

```
1. ThingsAvailableAnywhere 返回 false → 本地图没有该材料（或全被禁止）
2. GenClosest.ClosestThingReachable 返回 null → 有材料但不可达
3. missingResources 非空 → 缺少一种或多种材料
```

我们的 Postfix 在这些情况下介入，搜索其他楼层。

### HaulToContainer 手持跳转

```csharp
// JobDriver_HaulToContainer.MakeNewToils() 第一行：
yield return Toils_Jump.JumpIf(jumpIfAlsoCollectingNextTarget,
    () => this.pawn.IsCarryingThing(this.ThingToCarry));
// pawn 已经在搬运 → 跳过 Goto + PickUp → 直接走到蓝图投递
```

### ItemAvailability 缓存

```csharp
// 每 tick 清理一次缓存
public void Tick() { this.cachedResults.Clear(); }
// 所以材料 Spawn 后下一 tick 就可见
```
