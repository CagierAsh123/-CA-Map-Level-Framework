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
- 楼梯支持多 pawn 同时使用（maxPawns=100）

### 交互重定向（33 个 Harmony 补丁）
- 点击/拖选/右键菜单 → 重定向到聚焦层级的子地图
- 建造/区划/指定器 → 在子地图上操作
- 温度/天气/光照 → 子地图继承基地图环境
- 殖民者栏/工作面板/日程面板 → 包含所有层级的 pawn
- 警报系统 → 聚合所有层级的资源数据
- 事件系统 → 阻止袭击/虫害等在子地图触发
- 建造限制 → 只能在 usableCells 内放置建筑/地板
- 自动家园区域 → 子地图不自动扩展

### 渲染系统
- LevelRenderer：通过 Graphics.DrawMesh + Y 偏移叠加子地图内容
- 边缘阴影/日光阴影 → 聚焦时跳过基地图层级区域内的阴影
- 屋顶覆盖层 → 显示最高层级的屋顶数据
- Thing GUI 覆盖层 → 聚焦时跳过基地图层级区域内的标签

## 文件结构

```
Source/MapLevelFramework/
├── Buildings/       Building_Stairs（楼梯建筑）
├── Core/            LevelManager, LevelData, GenStep, RockFrontierUtility 等核心类
├── Jobs/            JobDriver_UseStairs, MLF_JobDefOf
├── Patches/         33 个 Harmony 补丁（交互重定向、渲染过滤、系统兼容）
├── Render/          LevelRenderer（叠加渲染器）
└── UI/              LevelSwitcherUI（层级切换界面）

Defs/
└── MLF_Defs.xml     BiomeDef, TerrainDef×3, GenStepDef×2, MapGeneratorDef×2,
                     WorldObjectDef, JobDef, ThingDef×2（上/下楼梯）
```

## 当前状态

大框架完成，进入细节完善和深度测试阶段。

### 待完善
- 跨层移动后 job 状态保留（当前 DeSpawn/Spawn 会丢失 job）
- usableCells 存档序列化
- 地下层光照系统（当前使用默认光照）
- 数据注入渲染方案 v2（替代当前叠加渲染，见 Docs/数据注入渲染方案.md）
- 兼容性补丁（Compat/ 目录预留）
- 更多楼梯样式和建筑类型

## 依赖

- RimWorld 1.6
- Harmony（通过 About.xml 声明依赖）

## 作者

Cagier.阳 (CA)
