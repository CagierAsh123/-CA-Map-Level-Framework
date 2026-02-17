# 原版工作类型跨层兼容性

## 兼容标记说明

- ✅ 完全兼容 — 通过 TryCrossLevelScan 通用扫描或专用系统支持
- ✅+ 材料配送 — 额外支持跨层材料/燃料/原料配送（NeedType 系统）
- ⚠️ 部分兼容 — 基本功能可用，但特定场景（如跨层取材料）未覆盖
- ➖ 不适用 — 自身行为，无需跨层

## 跨层机制

| 机制 | 说明 |
|---|---|
| TryCrossLevelScan | 通用：临时传送 pawn 到其他层运行原版 WorkGiver，找到工作就走楼梯过去 |
| NeedType.Construction | 建造材料配送：蓝图/框架需要材料 → 跨层取材 → HaulToContainer |
| NeedType.Refuel | 加油配送：CompRefuelable 缺燃料 → 跨层取燃料 |
| NeedType.Bill | Bill 原料配送：工作台 Bill 缺原料 → 跨层取原料放工作台旁 |
| NeedType.Medicine | 医疗取药：病人需要治疗但当前层无药 → 跨层送药到病人旁 |
| NeedType.WardFeed | 监管喂饭：囚犯需要食物但当前层无食物 → 跨层送食物到囚犯旁 |
| Patch_ConstructDeliverResources | 建造反向取材：pawn 在蓝图层但材料在其他层 |
| Patch_CrossLevelNeeds | 需求系统：饥饿/疲劳/娱乐等跨层搜索满足 |
| WorkGiver_HaulAcrossLevel | 跨层搬运：检测其他层更优储物区 |

---

## 1. 灭火

| Job | 兼容 | 机制 |
|---|---|---|
| 扑灭火焰（紧急） | ✅ | TryCrossLevelScan |

## 2. 就医

| Job | 兼容 | 机制 |
|---|---|---|
| 去床上接受紧急治疗（紧急） | ✅ | Patch_CrossLevelNeeds |
| 去床上接受治疗 | ✅ | Patch_CrossLevelNeeds |

## 3. 医生

| Job | 兼容 | 机制 |
|---|---|---|
| 提供紧急治疗（紧急） | ✅+ | TryCrossLevelScan + NeedType.Medicine |
| 治疗病人 | ✅+ | TryCrossLevelScan + NeedType.Medicine |
| 治疗自己（紧急） | ➖ | 自身行为 |
| 治疗实体 | ✅ | TryCrossLevelScan |
| 治疗自己 | ➖ | 自身行为 |
| 喂食病人 | ⚠️ | TryCrossLevelScan（食物在其他层时不会跨层取） |
| 人类手术 | ✅+ | TryCrossLevelScan + NeedType.Medicine |
| 施用血原质 | ⚠️ | TryCrossLevelScan（血原质在其他层时不会跨层取） |
| 救援倒地的殖民者 | ✅ | TryCrossLevelScan |
| 治疗动物 | ✅+ | TryCrossLevelScan + NeedType.Medicine |
| 喂食动物 | ⚠️ | TryCrossLevelScan（食物在其他层时不会跨层取） |
| 动物手术 | ✅+ | TryCrossLevelScan + NeedType.Medicine |
| 把病人带到手术床上 | ✅ | TryCrossLevelScan |
| 看望病人 | ✅ | TryCrossLevelScan |
| 从实体身上提取活铁 | ✅ | TryCrossLevelScan |

## 4. 休养

| Job | 兼容 | 机制 |
|---|---|---|
| 卧床养病 | ✅ | Patch_CrossLevelNeeds |

## 5. 保育

| Job | 兼容 | 机制 |
|---|---|---|
| 教育孩子 | ✅ | TryCrossLevelScan |
| 带离婴儿 | ✅ | TryCrossLevelScan |
| 母乳喂养婴儿 | ✅ | TryCrossLevelScan |
| 与婴儿玩耍 | ✅ | TryCrossLevelScan |
| 喂养婴儿 | ⚠️ | TryCrossLevelScan（婴儿食物在其他层时不会跨层取） |
| 带给母亲 | ✅ | TryCrossLevelScan |

## 6. 处理

| Job | 兼容 | 机制 |
|---|---|---|
| 开关 | ✅ | TryCrossLevelScan |
| 释放囚犯 | ✅ | TryCrossLevelScan |
| 打开容器 | ✅ | TryCrossLevelScan |
| 取出燃料 | ✅ | TryCrossLevelScan |
| 改变树精类型 | ✅ | TryCrossLevelScan |
| 取出颅骨 | ✅ | TryCrossLevelScan |

## 7. 监管

| Job | 兼容 | 机制 |
|---|---|---|
| 审问身份 | ✅ | TryCrossLevelScan |
| 处决实体 | ✅ | TryCrossLevelScan |
| 处决囚犯 | ✅ | TryCrossLevelScan |
| 处决有罪的殖民者 | ✅ | TryCrossLevelScan |
| 处决奴隶 | ✅ | TryCrossLevelScan |
| 释放奴隶 | ✅ | TryCrossLevelScan |
| 释放囚犯 | ✅ | TryCrossLevelScan |
| 奴役囚犯 | ✅ | TryCrossLevelScan |
| 抑制实体活跃度 | ✅ | TryCrossLevelScan |
| 把囚犯带到床上 | ✅ | TryCrossLevelScan |
| 给囚犯喂食 | ✅+ | TryCrossLevelScan + NeedType.WardFeed |
| 囚禁奴隶 | ✅ | TryCrossLevelScan |
| 教化囚犯 | ✅ | TryCrossLevelScan |
| 给犯人提供血原质 | ⚠️ | TryCrossLevelScan（血原质在其他层时不会跨层取） |
| 给犯人送餐 | ✅+ | TryCrossLevelScan + NeedType.WardFeed |
| 镇压奴隶 | ✅ | TryCrossLevelScan |
| 释放实体 | ✅ | TryCrossLevelScan |
| 和囚犯聊天 | ✅ | TryCrossLevelScan |

## 8. 驯兽

| Job | 兼容 | 机制 |
|---|---|---|
| 牵引迁徙中的动物 | ✅ | TryCrossLevelScan |
| 喂养动物 | ⚠️ | TryCrossLevelScan（食物在其他层时不会跨层取） |
| 牵引动物 | ✅ | TryCrossLevelScan |
| 宰杀动物 | ✅ | TryCrossLevelScan |
| 放生 | ✅ | TryCrossLevelScan |
| 收取动物产物 | ✅ | TryCrossLevelScan |
| 给动物剪毛 | ✅ | TryCrossLevelScan |
| 驯服动物 | ✅ | TryCrossLevelScan |
| 训练动物 | ✅ | TryCrossLevelScan |
| 分配圈养的动物 | ✅ | TryCrossLevelScan |

## 9. 烹饪

| Job | 兼容 | 机制 |
|---|---|---|
| 在炉灶烹饪食物 | ✅+ | TryCrossLevelScan + NeedType.Bill |
| 在篝火烹饪食物 | ✅+ | TryCrossLevelScan + NeedType.Bill |
| 屠宰尸体 | ✅+ | TryCrossLevelScan + NeedType.Bill |
| 在酿造台酿酒 | ✅+ | TryCrossLevelScan + NeedType.Bill |

## 10. 狩猎

| Job | 兼容 | 机制 |
|---|---|---|
| 狩猎 | ✅ | TryCrossLevelScan |

## 11. 建造

| Job | 兼容 | 机制 |
|---|---|---|
| 替换损坏的零部件 | ✅ | TryCrossLevelScan |
| 卸载建筑 | ✅ | TryCrossLevelScan |
| 建造屋顶 | ✅ | TryCrossLevelScan |
| 移除屋顶 | ✅ | TryCrossLevelScan |
| 拆除蓝图上的建筑 | ✅ | TryCrossLevelScan |
| 建造布置好的框架 | ✅+ | TryCrossLevelScan + NeedType.Construction |
| 搬运材料至框架 | ✅+ | NeedType.Construction + Patch_ConstructDeliverResources |
| 搬运材料至蓝图 | ✅+ | NeedType.Construction + Patch_ConstructDeliverResources |
| 收容 | ✅ | TryCrossLevelScan |
| 拆除建筑 | ✅ | TryCrossLevelScan |
| 修理受损建筑 | ✅ | TryCrossLevelScan |
| 移除地板 | ✅ | TryCrossLevelScan |
| 移除地基 | ✅ | TryCrossLevelScan |
| 打磨地面 | ✅ | TryCrossLevelScan |
| 打磨墙壁 | ✅ | TryCrossLevelScan |

## 12. 种植

| Job | 兼容 | 机制 |
|---|---|---|
| 收获作物 | ✅ | TryCrossLevelScan |
| 播撒种子 | ✅ | TryCrossLevelScan |
| 栽植树木 | ✅ | TryCrossLevelScan |
| 播种作物 | ✅ | TryCrossLevelScan |

## 13. 开采

| Job | 兼容 | 机制 |
|---|---|---|
| 采矿 | ✅ | TryCrossLevelScan |
| 钻探 | ✅ | TryCrossLevelScan |

## 14. 割除

| Job | 兼容 | 机制 |
|---|---|---|
| 掘起树木 | ✅ | TryCrossLevelScan |
| 修剪母树 | ✅ | TryCrossLevelScan |
| 割除植物 | ✅ | TryCrossLevelScan |

## 15. 锻造

| Job | 兼容 | 机制 |
|---|---|---|
| 在编译器制作 | ✅+ | TryCrossLevelScan + NeedType.Bill |
| 在机械培育器制作 | ✅+ | TryCrossLevelScan + NeedType.Bill |
| 维修机械族 | ✅ | TryCrossLevelScan |
| 锻造武器 | ✅+ | TryCrossLevelScan + NeedType.Bill |
| 完成机械加工台的清单 | ✅+ | TryCrossLevelScan + NeedType.Bill |
| 在活铁塑造台制作 | ✅+ | TryCrossLevelScan + NeedType.Bill |
| 装配物品 | ✅+ | TryCrossLevelScan + NeedType.Bill |

## 16. 缝制

| Job | 兼容 | 机制 |
|---|---|---|
| 制作衣物 | ✅+ | TryCrossLevelScan + NeedType.Bill |

## 17. 艺术

| Job | 兼容 | 机制 |
|---|---|---|
| 移除涂料 | ✅ | TryCrossLevelScan |
| 粉饰建筑 | ✅ | TryCrossLevelScan |
| 粉饰地板 | ✅ | TryCrossLevelScan |
| 雕刻 | ✅+ | TryCrossLevelScan + NeedType.Bill |

## 18. 制作

| Job | 兼容 | 机制 |
|---|---|---|
| 完成手工加工点的清单 | ✅+ | TryCrossLevelScan + NeedType.Bill |
| 操作精炼设备 | ✅+ | TryCrossLevelScan + NeedType.Bill |
| 合成药物 | ✅+ | TryCrossLevelScan + NeedType.Bill |
| 切割石砖 | ✅+ | TryCrossLevelScan + NeedType.Bill |
| 熔炼物品 | ✅+ | TryCrossLevelScan + NeedType.Bill |
| 在血清实验室制作物品 | ✅+ | TryCrossLevelScan + NeedType.Bill |

## 19. 钓鱼

| Job | 兼容 | 机制 |
|---|---|---|
| 钓鱼 | ✅ | TryCrossLevelScan |

## 20. 搬运

| Job | 兼容 | 机制 |
|---|---|---|
| 捕获实体 | ✅ | TryCrossLevelScan |
| 整备炮塔 | ✅ | TryCrossLevelScan |
| 补充燃料 | ✅+ | NeedType.Refuel |
| 卸下货物 | ✅ | TryCrossLevelScan |
| 装载远行队 | ✅ | TryCrossLevelScan |
| 储存基因组 | ✅ | TryCrossLevelScan |
| 装载运输舱 | ✅ | TryCrossLevelScan |
| 清空污染物容器 | ✅ | TryCrossLevelScan |
| 搬运至塑形舱 | ✅ | TryCrossLevelScan |
| 带到培育舱 | ✅ | TryCrossLevelScan |
| 搬运到地图外 | ✅ | TryCrossLevelScan |
| 剥光衣物 | ✅ | TryCrossLevelScan |
| 搬运尸体 | ✅ | TryCrossLevelScan |
| 带到基因提取器 | ✅ | TryCrossLevelScan |
| 搬到充电站 | ✅ | TryCrossLevelScan |
| 带去次核扫描 | ✅ | TryCrossLevelScan |
| 搬运资源 | ✅ | TryCrossLevelScan + WorkGiver_HaulAcrossLevel |
| 搬到垃圾分解器 | ✅ | TryCrossLevelScan |
| 跨层搬运 | ✅+ | WorkGiver_HaulAcrossLevel（MLF 专用） |
| 移送实体 | ✅ | TryCrossLevelScan |
| 装填进料口 | ✅ | TryCrossLevelScan |
| 火化尸体或衣物 | ✅+ | TryCrossLevelScan + NeedType.Bill |
| 在篝火工作 | ✅+ | TryCrossLevelScan + NeedType.Bill |
| 从发酵桶中取出啤酒 | ✅ | TryCrossLevelScan |
| 清空卵箱 | ✅ | TryCrossLevelScan |
| 取出活铁采集器产物 | ✅ | TryCrossLevelScan |
| 填充发酵桶 | ✅ | TryCrossLevelScan |
| 搬运物品 | ✅ | TryCrossLevelScan + WorkGiver_HaulAcrossLevel |
| 向建筑框架运送物资 | ✅+ | NeedType.Construction + Patch_ConstructDeliverResources |
| 向建筑蓝图运送物资 | ✅+ | NeedType.Construction + Patch_ConstructDeliverResources |
| 合并物品 | ✅ | TryCrossLevelScan |

## 21. 清洁

| Job | 兼容 | 机制 |
|---|---|---|
| 清除积雪 | ✅ | TryCrossLevelScan |
| 清理污渍 | ✅ | TryCrossLevelScan |
| 清理污染 | ✅ | TryCrossLevelScan |

## 22. 调查

| Job | 兼容 | 机制 |
|---|---|---|
| 调查 | ✅ | TryCrossLevelScan |

## 23. 研究

| Job | 兼容 | 机制 |
|---|---|---|
| 骇入建筑 | ✅ | TryCrossLevelScan |
| 创建异种胚芽 | ✅ | TryCrossLevelScan |
| 调查超凡结构 | ✅ | TryCrossLevelScan |
| 研究科技 | ✅ | TryCrossLevelScan |
| 操作远距离矿物扫描仪 | ✅ | TryCrossLevelScan |
| 操作地质扫描仪 | ✅ | TryCrossLevelScan |

---

## 统计

| 状态 | 数量 |
|---|---|
| ✅ 完全兼容 | 95 |
| ✅+ 材料配送 | 27 |
| ⚠️ 部分兼容 | 5 |
| ➖ 不适用 | 2 |

## ⚠️ 部分兼容详情

以下 job 基本功能可用（pawn 能跨层去目标），但「目标在 A 层 + 所需物品在 B 层」时不会跨层取物品：

| Job | 缺失 | 可能的 NeedType |
|---|---|---|
| 喂食病人 | 食物跨层取 | NeedType.PatientFeed |
| 喂食动物 | 食物跨层取 | NeedType.AnimalFeed |
| 喂养婴儿 | 婴儿食物跨层取 | NeedType.BabyFeed |
| 施用血原质 | 血原质跨层取 | NeedType.Hemogen |
| 给犯人提供血原质 | 血原质跨层取 | NeedType.Hemogen |
