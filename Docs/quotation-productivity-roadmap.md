# 报价提效路线图（讨论稿）

> 本文档是 2026-05-25 关于"如何让报价员快速生成报价"的讨论沉淀。
> 用于跨日上下文延续——AI 助手在新会话里读这一份就能接着干活，不需要从零开始。
> 维护建议：每次讨论结束后追加一节 `## 讨论会话记录` 即可，不要重写全文。

---

## 1. 项目与用户场景速写

| 维度 | 现状 |
|---|---|
| 项目 | PanelFlow 报价系统，PowerBuilder → C# ASP.NET MVC（.NET 10）迁移；**数据库与历史 PB 系统共用** |
| 工作流 | 导入客户 BOM → 拆柜 → 填价 → 调整浮动/数量 → 复核 → 生成 → 发客户 |
| 业务量 | 每天 **3 份以内**报价；单份 **几百 → 几千**元件 |
| 单类型 | **几乎全是新单**，定制化项目控制柜，复购/复制场景极少 |
| 报价员瓶颈 | 不是录入速度，而是"几千元件量下无法掌控数据"——进度感、筛选、命中率、性能 |
| 技术栈 | ASP.NET MVC + EF Core + SQL Server 2022 + Bootstrap 5 + Handsontable + NPOI |
| 状态管理 | Session（不引 JWT） |
| 权限 | `IPermissionService` / `[RoleAuthorize]` |
| 当前分支 | `feature/auto-fill-price` |
| Handsontable License | `53ea8-c2678-49b80-cb40f-4dad4` |

---

## 2. 关键技术上下文（AI cheatsheet）

### 2.1 核心数据表与字段
| 表 | 关键字段 | 说明 |
|---|---|---|
| BJFAT | `fabh`（PK 方案编号）、`bjr`（报价人）、`dqzt`（状态：10=已成立） | 报价单主表 |
| BJB | `fabh + x_bm`（PK）、`x_lx`（11=元件行）、`x_mc`/`x_ggxh`/`x_dw`/`x_sccj`、`x_bj_dj`/`x_bjb_dj`/`x_bjb_bj`/`x_dj`、`x_fdds`、`x_sl`、`x_wzdh`、`x_bjb_datetime` | 元件清单。`x_bm` 长度 4 = 控制柜，长度 12 = 元件行 |
| STD_PRICE_HISTORY | `x_wzdh` 唯一键、`last_price`、`avg/min/max_price`、`avg_count`、`x_dw`、`x_sccj` | 历史价格聚合表，SQL Agent 每日 2:00 由 `SP_RefreshPriceHistory` 刷新 |
| STD_SPEC_ALIAS | `alias_wzdh` → `canonical_wzdh` | 别名映射表（**spec 已设计，0 实施**） |

### 2.2 核心算法/规则
- **NormalizeSpec**（C# `internal static`）/ **F_CleanString**（SQL UDF）：把规格型号标准化为指纹 `x_wzdh`。保留字符集：`a-z 0-9 U+4E00-U+9FFF μωΩ°±℃φ`，去括号内容、全角转半角、不可见字符过滤
- **金额公式**：`amount = x_bj_dj * (1 + x_fdds/100) * x_sl`；`x_dj = x_bj_dj * (1 + x_fdds/100)`
- **柜内"单价"列**显示的是 `x_bj_dj`（基础单价），不是 `x_dj`
- **元件使用控制柜**口径（Req 17）：仅按 `x_wzdh` 匹配，不参与价格/厂家/浮动比较；wzdh 为空时拒绝统计
- **价格异常着色**：浅灰=空/0、红=负、橙=偏离均价 ±20%
- **自动填价**：仅填 `x_bj_dj=0` 的行；保留已有非零价
- **数据库兼容**：禁止改历史表结构；新字段允许可空或默认值

### 2.3 业务生命周期约束
1. 报价 → 2. 合同 → 3. 生产准备 → 4. 采购 → 5. 装配 → 6. 质检 → 7. 用户带电（可选） → 8. 发货
- 禁止跳阶段；质检未过不得发货；发货=项目关闭→质保模块
- 关键字段计算口径必须全流程统一

---

## 3. 现有 spec 库盘点（截至 2026-05-25）

`.kiro/specs/` 下共 5 个 feature spec：

| Spec | 范围 | 实施状态 | 备注 |
|---|---|---|---|
| **auto-fill-price** | 自动填价、参考价列、异常着色、汇总视图、控制柜 CRUD、拖拽排序 | ✅ 已完成（含今天补的 Req 16/17） | 详见 `.kiro/specs/auto-fill-price/` |
| **spec-alias-matching** | 别名表 + 二级匹配（精确→别名回退）+ 填价页关联 UI + 管理后台 | ⏳ **设计完整，0 任务实施** | **下一个最有价值的功能** |
| **merge-excel-multisheet** | 单文件多 sheet 合并 | ✅ 已完成 | |
| **import-components-redesign** | ImportComponents 页重设计 | 🚧 大部分完成，剩前端属性测试（Task 6） | |
| **cabinet-batch-edit** | MergeExcel 页柜批量编辑（壳体类型/尺寸/通用元件） | 🚧 实施中（前 6 个任务已完成） | |

---

## 4. 本日工作总结（2026-05-25）

### 4.1 FillPrice 页 5 个 bug 修复（用户报告）

| # | 现象 | 根因 | 修复 |
|---|---|---|---|
| 1 | "显示参考价格"复选框始终选中且禁用 | Req 6.1 要求始终显示参考价格列，复选框已无意义 | 移除复选框 |
| 2 | "引用历史价格"按钮无效 | `priceMap` 默认 `Dictionary` 大小写敏感，但 SQL Server 默认排序规则大小写不敏感 → DB 返回不同大小写 → 前端 `prices[wzdh]` 查找失败 | 后端用 `OrdinalIgnoreCase` 比较器；返回字典 key 改用**输入** wzdh |
| 3 | 底部按钮被状态栏遮挡 | `.price-table-pane` 没用 flex 列布局，`#hot-container` 100% 高度把按钮挤出可视区 | flex 列重构，hot-container `flex: 1 1 auto`，按钮 `flex-shrink: 0` |
| 4 | "保存数据"按钮不可点击 | 仅在 `summaryMode && summaryDirty` 时启用；柜内全量保存功能在前后端均未实现 | **维持现状**（仅根汇总视图启用）。柜内全量保存是未完成的 Req 12，需另立任务 |
| 5 | "元件用于哪些控制柜"任意元件都显示无匹配 | **设计层 bug**：原按六字段全等匹配（含价格），同型号在不同柜不同定价就匹配失败；后改 `x_bj_dj` 仍是错的口径 | **重构为按 `x_wzdh` 匹配**（Req 17）。wzdh 为空时拒绝统计 |

### 4.2 FillPrice 页 UI 重组（用户要求 5 件事）

1. 删除顶部独立标题栏；报价单号、未保存徽章、返回列表按钮全部并入工具栏
2. 工具栏下新增**颜色图例区**（仅柜体视图显示）：浅灰/红/橙/浅绿/浅蓝 5 个色块及含义
3. 表格上方新增**"当前节点"状态栏** + **"合计金额"实时显示**
4. 分离两类高亮：
   - `tree-node-link-selected`（强对比蓝底白字）= 用户主动选中的柜，持久
   - `tree-node-link-usage`（淡蓝底）= 元件使用提示的柜，临时
   - 两类可同时存在，互不覆盖
5. 合计金额触发时机：数据加载/单价变更/数量变更/浮动变更/增删行/自动填价完成

### 4.3 文档更新

- `.kiro/specs/auto-fill-price/requirements.md` 新增 **Req 16（页面布局与状态提示）**、**Req 17（元件使用控制柜匹配口径）**
- `.kiro/specs/auto-fill-price/design.md` 新增 `GetProjectComponentUsage` 章节、Property 13/14、Error Handling 表更新
- `.kiro/specs/auto-fill-price/tasks.md` 新增 **Task 12（UI 调整与字段口径修复，5 子任务全部完成）** 与 **Task 13（属性测试补充，未做）**

### 4.4 关键代码变更点

| 文件 | 变更 |
|---|---|
| `PanelFlow.Web/Controllers/QuotationController.cs` | `AutoFillPriceFromHistory`：priceMap 大小写不敏感 + 返回 prices 用输入 wzdh 为 key；`GetProjectComponentUsage` 签名改为 `(id, wzdh)`，匹配按 `x_wzdh`，wzdh 空时拒绝统计，返回多带 priceMin/priceMax/vendors |
| `PanelFlow.Web/Views/Quotation/FillPrice.cshtml` | 删标题栏、重组工具栏、加图例区、加状态栏；CSS flex 列布局；新增 `.tree-node-link-selected` 类 |
| `PanelFlow.Web/wwwroot/js/quotation-fill-price.js` | 删 `identityFromHotRow` / `fetchComponentUsageByIdentity` / `loadProjectComponentUsage`；新增 `displayInfoFromHotRow` / `fetchComponentUsageByWzdh` / `setSelectedTreeNode` / `recalcTotalAmount` / `updateCabinetStatusBar` / `formatUsagePriceRange` |

---

## 5. 候选功能优先级（基于场景倒推）

> 用户场景：每份 **几百到几千元件**、**全新单为主**。
> ROI 排序按"省时间 + 降错误 + 提质量"的综合判断。

### 第一梯队：千级表"可控感"（强烈推荐 Sprint 1 做）

#### A. 元件模糊匹配 / 智能联想 ★★★★★
**对应 spec**：`spec-alias-matching`（设计完整、0 任务实施）
**核心价值**：解决"几千个元件里有相当一部分历史报过价但因写法不同就匹配不上"的痛点
**实施起点**：直接执行该 spec 的 Task 1-8，从数据库与实体层做起

#### B. 填价进度看板 + 异常聚合 ★★★★★
**对应 spec**：无（**新需求，需立 spec**）
**核心价值**：千级表必备的"我做到哪了"的可控感
**实施要点**：
- 工具栏右上固定徽章 `已填价 1234 / 2500（49%）` + `异常：N 负、M 偏离、K 无规格`
- 点击徽章 → 侧边"问题清单"，可点跳转到对应行
- `F3` 快捷键跳"下一个未填价"
**预估**：1-2 天

#### C. 元件搜索 / 表格筛选 ★★★★★
**对应 spec**：无（**新需求，需立 spec**）
**核心价值**：千级表里翻不动元件
**实施要点**：
- 表格上方搜索框（名称/规格/厂家 keyword）
- 快捷筛选按钮：「只看未填价」「只看异常」「只看某厂家」
- 用 Handsontable `filters` 插件 + 自定义 UI
**预估**：1 天

### 第二梯队：导入分柜效率

#### D. Excel 智能拆柜导入 ★★★★
**对应 spec**：**待确认**——可能与现有 `import-components-redesign` 或 `cabinet-batch-edit` 有重叠
**核心价值**：客户 BOM 通常已经按"位号/盘号/区域"列分组；自动拆柜免去手工拆分
**实施前置**：先看 `import-components-redesign` 和 `cabinet-batch-edit` 是否已经覆盖类似能力

#### E. 大表剪贴板粘贴 + 列映射 ★★★
**对应 spec**：无
**核心价值**：外部 Excel 列序千差万别，原生粘贴不便
**预估**：2 天

### 第三梯队：千级表性能与健壮性

#### F. Handsontable 性能加固 + 保存批量化 ★★★
- 确认 Handsontable 虚拟滚动选项
- `SavePlan` 改为批量 INSERT 或 `SqlBulkCopy`
- 自动填价端点改批量 UPDATE
**先决条件**：用户提供 2000 行级的实测耗时基线

#### G. 自动保存 / 草稿持久化 ★★★
- 每 N 分钟写 `BJB_DRAFT` 草稿表
- 重新进入页面检测草稿 → "是否恢复"
**预估**：3-4 天

### 第四梯队（暂缓）

- 同型号价格一致性提示
- 批量浮动率 / 整柜调价（部分已被 `cabinet-batch-edit` 覆盖）
- 历史价时段加权
- 撤销/重做、快捷键

### 已降级（用户场景不适用）

- ~~基于历史报价整单复制~~（用户场景几乎不复购，价值小）
- ~~柜模板/跨项目柜复用~~（定制化项目柜不复用）
- ~~多人协作 / 审批流~~（每天 3 份内，单人足够）

---

## 6. 推荐 Sprint 1（约 1 周）

按 ROI 顺序：

1. **B. 进度看板 + 异常聚合**（1-2 天）— 立即给报价员可控感
2. **C. 表格搜索 / 筛选**（1 天）— 千级表必备
3. **A. 别名匹配（spec-alias-matching）第一阶段：数据层 + Service 层 + 自动填价集成**（2-3 天）— 长期价值最大，越用越准

预期收益：千级元件场景下报价员时间 **节省 30%-50%**。

完成后再评估 Sprint 2 走向（性能加固 / 草稿持久化 / Excel 智能拆柜）。

---

## 7. 仍待用户确认的开放问题

| # | 问题 | 用途 |
|---|---|---|
| 1 | 现在做一份 **2000 行**的报价，从开始到发出大致几个小时？ | 量化基线，便于评估优化收益 |
| 2 | 元件未匹配到历史价的占比大致多少？1000 个里有几百个还要手填？ | 决定 spec-alias-matching 价值密度 |
| 3 | 当前 MergeExcel / ImportComponents 页是如何把客户 BOM 拆到不同柜的？手工逐行？还是按列自动？ | 决定是否需要做 Excel 智能拆柜（功能 D） |
| 4 | 是否需要"柜内全量保存"功能（Req 12 实质未实现）？现在保存数据按钮在柜体视图永远不可点击 | 决定要不要立"FillPrice 全量保存" spec |
| 5 | 现在 2000 行级别的 SavePlan 大致耗时多少？是否经常超时？ | 决定 Sprint 性能加固优先级 |

---

## 8. 讨论会话记录

### 2026-05-25（首日讨论）

**用户提的 5 个 FillPrice bug 已全部修复**（见 §4.1-4.2）。

**用户提问"还有哪些功能可以让报价员更快生成报价？"**
→ AI 给出三个维度的功能清单（省时间 / 降错误 / 提质量），覆盖 16 项候选。

**用户回答场景细节**：
- 每天 ≤ 3 份；每份 几百~几千 元件
- 全新单为主，复购极少，控制柜定制化

**AI 据此重新排序优先级**（即 §5 的当前版本），并锁定 Sprint 1（§6）。

**遗留**：用户要求把讨论沉淀成文件（即本文档），明日继续。

---

### 模板（下次讨论结束追加用）

```markdown
### YYYY-MM-DD（讨论主题）

**用户输入**：
- ...

**结论 / 决定**：
- ...

**新增 todo / 修改优先级**：
- ...
```

---

## 附录：明日 AI 接手指引

如果明天新开会话，请按以下顺序读：

1. **本文档**（一站式总览）
2. `.cursor/rules/project-core.mdc` 与同目录其它 `.mdc`（强制规则）
3. `.kiro/specs/auto-fill-price/{requirements,design,tasks}.md`（已实施的最大功能基线）
4. `.kiro/specs/spec-alias-matching/{requirements,design,tasks}.md`（**Sprint 1 候选#A 的设计书**）
5. 看用户当前问题对准本文档第 5/6 节，直接给到对应 spec 即可开始干活

**强制约束提醒**：
- 不改历史表结构；新字段允许可空
- 所有 DB 操作必须 `async`，不许 `.Result/.Wait()`
- 写操作必须带防伪令牌
- 编码规范看 `.cursor/rules/coding-standards.mdc`
- 数据库兼容看 `.cursor/rules/db-compatibility.mdc`
- 业务生命周期看 `.cursor/rules/business-workflow.mdc`
- 前端规范看 `.cursor/rules/frontend-delivery.mdc`
