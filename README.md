# 【CA】地图多层框架 Map Level Framework

RimWorld 垂直维度扩展框架。在单张地图上实现多层建筑（上层）和地下空间（地下层），所有层级共享同一坐标系，pawn 可通过楼梯在层级间移动。

## 核心架构

- 每个层级是一张真实的 PocketMap，拥有完整的原版系统（AI、需求、工作、寻路）
- 子地图与基地图同尺寸，坐标系完全一致，无需坐标转换
- 上层（elevation > 0）：保持基地图为 CurrentMap，叠加渲染子地图内容
- 地下层（elevation < 0）：直接切换 CurrentMap 到子地图，使用原版渲染管线
- 最大 18 层（受渲染深度缓冲约束）

## 已实现功能

### 层级系统
- LevelManager（MapComponent）管理所有层级的创建、切换、销毁
- LevelData 存储层级运行时数据（elevation、area、usableCells、activeSections）
- LevelMapParent（PocketMapParent）持有子地图与宿主管理器的关联
- 层级切换 UI（LevelSwitcherUI）支持快速聚焦不同楼层

### 上层（2F+）
- 放置上楼梯 → FloodFill 扫描屋顶连通区域 → 创建对应层级
- 屋顶-地板双向同步（下层加屋顶 → 上层铺地板，上层拆地板 → 下层拆屋顶）
- 叠加渲染：聚焦 3F 时渲染 [2F, 3F]，基地图对应区域物体被过滤
- 多层遮挡：中间层被高层覆盖的建筑不烘焙进 mesh

### 地下层（B1-）
- 放置下楼梯 → 创建地下层（全图厚岩石顶 + MLF_OpenAir）
- 初始可用区域为楼梯一格，周围 8 格生成原版岩石（花岗岩/大理石/石灰岩/砂岩/板岩 + 4% 矿脉）
- 挖掘岩石 → 动态扩展边界（8 方向生成新岩石）→ 无限拓展地下空间
- 聚焦地下层直接切换 CurrentMap，原版管线原生渲染
- 地下层不参与屋顶-地板同步

### Pawn 跨层移动
- Building_Stairs 提供右键菜单（上楼/下楼）
- JobDriver_UseStairs：走到楼梯 → StairTransferUtility 跨图转移
- 跨层移动后自动恢复 deferred job（跨层找食物/找床/工作等）

### 跨层 AI
- 需求系统：饥饿/疲劳/娱乐/药物/血源/死眠 — 跨层搜索满足需求
- 工作系统：所有 WorkGiver_Scanner 工作类型跨层扫描
- 跨层搬运：WorkGiver_HaulAcrossLevel 检测其他层更优储物区 → JobDriver_HaulAcrossLevel 搬运
- 袭击者 AI：JobGiver_AssaultAcrossLevel 让敌人通过楼梯跨层进攻

### 物理系统
- 坠落：拆除下层屋顶 → 上层 things/pawns 掉落（pawn 受 10-35 钝伤，物品损 25-75% HP）
- 跳楼：右键菜单从露天边缘跳下（JobDriver_JumpDown，15-40 钝伤）
- 跳楼精神崩溃：MentalState_JumpOff（极端崩溃，pawn 冲向楼层边缘跳下）

### 交互重定向（33+ Harmony 补丁）
- 点击/拖选/右键菜单 → 重定向到聚焦层级的子地图
- 建造/区划/指定器 → 在子地图上操作
- 温度/天气/光照 → 子地图继承基地图环境
- 殖民者栏/工作面板/日程面板 → 包含所有层级的 pawn
- 警报系统 → 聚合所有层级的资源数据
- 事件系统 → 阻止袭击/虫害等在子地图触发
- 建造限制 → 只能在 usableCells 内放置建筑/地板
- 自动家园区域 → 子地图不自动扩展

### DLC 兼容
- Odyssey（逆重飞船）：起飞时捕获层级地图数据（things/pawns/地形/屋顶），降落后自动恢复

### 渲染系统
- LevelRenderer：通过 Graphics.DrawMesh + Y 偏移叠加子地图内容
- 边缘阴影/日光阴影 → 聚焦时跳过基地图层级区域内的阴影
- 屋顶覆盖层 → 显示最高层级的屋顶数据
- Thing GUI 覆盖层 → 聚焦时跳过基地图层级区域内的标签

## 文件结构

```
Source/MapLevelFramework/
├── AI/              MentalState_JumpOff, JobGiver_JumpOff, MentalStateWorker_JumpOff
├── Buildings/       Building_Stairs（楼梯建筑）
├── Compat/          Odyssey 逆重飞船兼容（GravshipLevelStorage, MLF_GravshipManager, Patch_GravshipCompat）
├── Core/            LevelManager, LevelData, StairTransferUtility, CrossLevelJobUtility,
│                    CrossLevelHaulUtility, JumpDownUtility, GenStep, RockFrontierUtility 等
├── Jobs/            JobDriver_UseStairs, JobDriver_HaulAcrossLevel, JobDriver_JumpDown,
│                    JobGiver_AssaultAcrossLevel, WorkGiver_HaulAcrossLevel, MLF_JobDefOf
├── Patches/         33+ Harmony 补丁（交互重定向、渲染过滤、跨层AI、系统兼容）
├── Render/          LevelRenderer（叠加渲染器）
└── UI/              LevelSwitcherUI（层级切换界面）

Defs/
└── MLF_Defs.xml     BiomeDef, TerrainDef×3, GenStepDef×2, MapGeneratorDef×2,
                     WorldObjectDef, JobDef×3, WorkGiverDef, MentalStateDef,
                     MentalBreakDef, ThingDef×2（上/下楼梯）

Patches/
├── MLF_RaiderDuties.xml     袭击者 DutyDef 注入
└── MLF_MentalStates.xml     跳楼精神崩溃 ThinkTree 注入
```

## 当前状态

核心系统完成，AI 跨层交互完成，进入 374-mod 环境深度测试阶段。

### 待完善
- 地下层光照系统（当前使用默认光照）
- 数据注入渲染方案 v2（替代当前叠加渲染，见 Docs/数据注入渲染方案.md）
- 更多楼梯样式和建筑类型
- 社交互动/灭火/动物管理跨层（低优先级）

## 依赖

- RimWorld 1.6
- Harmony（通过 About.xml 声明依赖）
- Odyssey DLC（可选，逆重飞船兼容）

## 作者

Cagier.阳 (CA)
