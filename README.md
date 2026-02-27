# 【CA】地图多层框架 Map Level Framework

RimWorld 垂直维度扩展框架。在单张地图上实现多层建筑（上层）和地下空间（地下层），所有层级共享同一坐标系，pawn 可通过楼梯在层级间移动。

## 核心特性

### 真实多层建筑
- 每个层级是独立的 PocketMap，拥有完整的原版系统（AI、需求、工作、寻路）
- 子地图与基地图同尺寸，坐标系完全一致，无需坐标转换
- 支持最多 18 层（受渲染深度缓冲约束）

### 智能跨层 AI
- **自动跨层工作**：pawn 自动发现其他楼层的工作（建造、清洁、搬运、制作等）并通过楼梯前往
- **跨层需求满足**：饥饿/疲劳/娱乐/药物 — 自动跨层搜索满足需求
- **跨层材料配送**：工作台缺原料时，pawn 自动从其他楼层搬运材料
  - 支持建造（蓝图/框架）、加油（燃料）、制作/烹饪（Bill 原料）
  - 智能材料匹配：尊重用户的材料过滤设置，优先选择非固定材料作为产物材质
  - 覆盖所有位置组合：正向取材、反向取材、三层取材
- **袭击者跨层**：敌人会通过楼梯追击殖民者到不同楼层

### 上层建筑（2F+）
- 放置上楼梯 → 自动扫描屋顶连通区域 → 创建对应层级
- 屋顶-地板双向同步：下层加屋顶 → 上层自动铺地板，上层拆地板 → 下层自动拆屋顶
- 叠加渲染：聚焦 3F 时同时渲染 2F 和 3F，基地图对应区域物体被过滤
- 多层遮挡：中间层被高层覆盖的建筑不会重复渲染

### 地下空间（B1-）
- 放置下楼梯 → 创建地下层（全图厚岩石顶 + 虚空地形）
- 初始可用区域为楼梯一格，周围 8 格生成原版岩石（花岗岩/大理石/石灰岩/砂岩/板岩 + 4% 矿脉）
- 挖掘岩石 → 动态扩展边界（8 方向生成新岩石）→ 无限拓展地下空间
- 聚焦地下层直接切换 CurrentMap，使用原版渲染管线

### 物理系统
- **坠落**：拆除下层屋顶 → 上层 things/pawns 掉落（pawn 受 10-35 钝伤，物品损 25-75% HP）
- **跳楼**：右键菜单从露天边缘跳下（15-40 钝伤）
- **跳楼精神崩溃**：极端崩溃时 pawn 会冲向楼层边缘跳下

### 完整的交互重定向
- 点击/拖选/右键菜单 → 自动重定向到聚焦层级
- 建造/区划/指定器 → 在子地图上操作
- 殖民者栏/工作面板/日程面板 → 包含所有层级的 pawn
- 警报系统 → 聚合所有层级的资源数据
- 物品可用性/配方计数/贸易 → 跨层聚合

## 核心架构

## 技术实现

### 层级系统
- LevelManager（MapComponent）管理所有层级的创建、切换、销毁
- LevelData 存储层级运行时数据（elevation、area、usableCells、activeSections）
- LevelMapParent（PocketMapParent）持有子地图与宿主管理器的关联
- 层级切换 UI（LevelSwitcherUI）支持快速聚焦不同楼层

### 跨层材料配送系统
统一的跨层材料搬运框架，支持三种需求类型：
- **建造（Construction）**：蓝图/框架需要材料 → 跨层取材 → HaulToContainer 交付
- **加油（Refuel）**：CompRefuelable 建筑缺燃料 → 跨层取燃料 → 放下 + 启动 Refuel job
- **制作/烹饪（Bill）**：工作台 Bill 缺原料 → 跨层取原料 → 放在工作台附近 → 直接 DoBill

**Bill 材料配送特性**：
- 单材料模式（carry）：手持材料走楼梯送达
- 多材料模式（inventory）：依次捡进背包 → 走楼梯 → 全部丢在工作台旁 → 直接 DoBill
- 智能材料匹配：
  - 窄 filter 优先（防止宽 filter 消耗窄 filter 需要的材料）
  - 尊重用户 `bill.ingredientFilter` 设置（FixedIngredient 豁免，符合原版设计）
  - 优先选择非 FixedIngredient 作为产物材质（Patch_DominantIngredient）
- 防盗机制：dropped 材料 Forbid，DoBill 时 Unforbid
- 部分材料运输：某些 ingredient 无法满足时不会阻止其他 ingredient 的运输

核心组件：
- Patch_JobGiver_Work_CrossFloor：三阶段跨层工作扫描（P1/P2/P3）
- Patch_WorkGiver_Construct_CrossFloor：建造专用跨层取材
- JobDriver_DeliverResourcesCrossFloor：通用跨层投递 Driver
- Patch_DominantIngredient：产物材质选择优化

### 渲染系统
- LevelRenderer：通过 Graphics.DrawMesh + Y 偏移叠加子地图内容
- 跳过特定 SectionLayer：雾战、雪、黑暗、光照、电力覆盖层
- 边缘阴影/日光阴影 → 聚焦时跳过基地图层级区域内的阴影
- 屋顶覆盖层 → 显示最高层级的屋顶数据
- Thing GUI 覆盖层 → 聚焦时跳过基地图层级区域内的标签

### 跨层电力系统
- PowerRelayManager：跨层电网中继管理
- CompPowerBatteryRelay：电池中继组件，连接不同楼层的电网

### DLC 兼容
- Odyssey（逆重飞船）：起飞时捕获层级地图数据（things/pawns/地形/屋顶），降落后自动恢复

## 文件结构

```
Source/MapLevelFramework/
├── AI/              MentalState_JumpOff, JobGiver_JumpOff, MentalStateWorker_JumpOff
├── Buildings/       Building_Stairs（楼梯建筑）
├── Compat/          Odyssey 逆重飞船兼容
├── Core/            LevelManager, LevelData, StairTransferUtility, StairsCache,
│                    JumpDownUtility, RockFrontierUtility, LevelMapParent 等
├── CrossFloor/      FloorMapUtility, CrossFloorIntent, CrossFloorReachabilityUtility 等
├── Jobs/            JobDriver_UseStairs, JobDriver_DeliverResourcesCrossFloor,
│                    JobDriver_JumpDown, JobGiver_AssaultAcrossLevel, MLF_JobDefOf
├── Patches/         50+ Harmony 补丁（交互重定向、渲染过滤、跨层AI、跨层材料配送）
├── Power/           PowerRelayManager, CompPowerBatteryRelay（跨层电力）
├── Render/          LevelRenderer（叠加渲染器）
└── UI/              LevelSwitcherUI（层级切换界面）

Defs/
└── MLF_Defs.xml     BiomeDef, TerrainDef×3, GenStepDef×2, MapGeneratorDef×2,
                     WorldObjectDef, JobDef×3, MentalStateDef, MentalBreakDef,
                     ThingDef×2（上/下楼梯）

Patches/
├── MLF_RaiderDuties.xml     袭击者 DutyDef 注入
└── MLF_MentalStates.xml     跳楼精神崩溃 ThinkTree 注入
```

## 开发状态

核心系统完成，跨层 AI 和跨层材料配送系统完成，进入 374-mod 环境深度测试阶段。

80+ C# 源文件，50+ Harmony 补丁，覆盖层级管理、渲染、AI、物理、交互重定向、跨层材料配送、跨层电力全链路。

### 已完成
- ✅ 上层建筑系统（屋顶-地板同步、叠加渲染、多层遮挡）
- ✅ 地下空间系统（动态边界扩展、无限拓展）
- ✅ 跨层 AI（需求、工作、袭击者）
- ✅ 跨层材料配送（建造、加油、Bill 原料）
- ✅ 智能材料匹配（窄 filter 优先、ingredientFilter 尊重、产物材质优化）
- ✅ 跨层电力系统（电网中继、电池中继）
- ✅ 物理系统（坠落、跳楼、精神崩溃）
- ✅ 完整交互重定向（42+ 补丁）
- ✅ Odyssey DLC 兼容

### 待完善
- 地下层光照系统（当前使用默认光照）
- 更多楼梯样式和建筑类型
- 社交互动/灭火/动物管理跨层（低优先级）

## 依赖

- RimWorld 1.6
- Harmony（通过 About.xml 声明依赖）
- Odyssey DLC（可选，逆重飞船兼容）

## 作者

Cagier.阳 (CA)
