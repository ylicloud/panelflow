# Fill Progress Dashboard (Mini Spec)

> Sprint 1 / 候选 B — 填价进度看板 + 异常聚合
> 类型：单文件 mini spec（requirements + design + tasks 合并）
> 适用范围：`Quotation/FillPrice` 页
> 关联：`.kiro/specs/auto-fill-price/`（在其基础上扩展，不修改既有 Req）

---

## 1. 问题与目标

### 1.1 报价员痛点
当一个报价单的元件量到 1000~3000 行时：
- 报价员**不知道整单还差多少没填价**（每柜单独看，无全局视图）
- 异常单元格（负数、偏离均价、缺规格）**散落在不同柜不同行**，必须逐柜翻阅才能发现
- 无快捷方式跳到"下一个待处理项"

### 1.2 目标
在 FillPrice 页提供 **常驻进度徽章** + **可展开的问题清单**，让报价员：
- 一眼看到 `已填价 X / Y（Z%）` 与 `异常 N` 全局指标
- 一键展开问题清单，按柜分组，**点击直接跳转到对应柜的对应行**
- `F3` 跳本柜"下一个未填价"

### 1.3 非目标
- ❌ 不做跨柜自动跳转（避免页面频繁切换的断裂感；放 Sprint 2）
- ❌ 不做异常的自动修复建议（属于"智能提示"另立功能）
- ❌ 不在汇总视图（rootSummaryMode）下显示进度看板（汇总本身就是全景）

---

## 2. 需求（Requirements）

### Req B-1: 全局进度徽章
1. THE 页面 SHALL 在工具栏右侧常驻显示进度徽章 `已填价 N / M（X%）`。
2. WHEN 报价单加载完成，THE 系统 SHALL 立即请求 `GetProjectFillProgress` 填充徽章。
3. WHEN 用户在柜体视图编辑单价后失焦，THE 系统 SHALL 重新统计**当前柜**的进度并增量更新徽章数值（不重查整单）。
4. WHEN 用户切换控制柜或调用自动填价或保存成功，THE 系统 SHALL 重新请求 `GetProjectFillProgress` 全量刷新徽章。
5. 进度计算口径：`x_lx=11`（元件行）且 `x_bj_dj > 0` 视为"已填价"；`x_bj_dj <= 0` 或为空视为"未填价"。
6. IF 整单元件行数为 0，THEN 徽章 SHALL 显示 `0 / 0（–）`，**不**显示百分比。

### Req B-2: 异常聚合徽章
1. THE 徽章组 SHALL 同时显示 `异常 K` 计数，K = 4 类异常之和：
   - **负数**：`x_bj_dj < 0`
   - **偏离均价**：`x_bj_dj > 0` 且 `STD_PRICE_HISTORY.avg_price > 0` 且 `|x_bj_dj − avg| / avg > 0.20`
   - **缺规格**：`x_wzdh` 为空白（无法做历史价匹配）
   - **零价**：`x_bj_dj = 0`（独立于"未填价"统计，仅在元件已录入名称/规格但价格未填时出现，作为"待填价"的具体落点）
2. WHEN K > 0，THE 异常徽章 SHALL 可点击；K = 0 时禁用点击且文字变灰。
3. 异常判定口径必须**与前端 `applyPriceAnomalyStyles` 完全一致**，统一由后端 SQL 计算返回（前端单元格着色逻辑不变，仅复用判定结果）。

### Req B-3: 问题清单抽屉
1. WHEN 用户点击异常徽章，THE 系统 SHALL 在表格右侧打开侧边抽屉 `#problem-list-drawer`。
2. THE 抽屉 SHALL 按"控制柜"分组列出所有问题项，每项含：柜名、行序号、元件名、规格、问题类型徽标（负/偏/缺规/零）、当前价格、参考均价（若有）。
3. WHEN 用户点击抽屉中某一项，THE 系统 SHALL 切换到对应控制柜，加载完成后滚动表格定位到对应行，并临时高亮 2 秒。
4. WHEN 用户在抽屉中切换分组折叠状态，状态 SHALL 仅在当前会话内有效（不持久化）。
5. WHEN 抽屉打开期间任何编辑导致进度刷新，THE 抽屉内容 SHALL 自动同步刷新。
6. THE 抽屉 SHALL 在 `summaryMode` 视图下隐藏（项目根汇总本身已是全景）。

### Req B-4: 快捷键 F3
1. WHEN 用户在柜体视图按 `F3`，THE 系统 SHALL 选中当前柜内"下一个未填价行"的单价单元格，并滚动到可视区域。
2. IF 当前柜没有未填价行，THE 系统 SHALL 在 InfoBar 提示 `本柜已全部填价。按 F4 跳到下一个未填价柜`（F4 行为放 Sprint 2，本期 spec 不实现 F4，仅提示文案）。
3. 快捷键 SHALL 仅在 Handsontable 持有焦点时生效，不与浏览器原生 F3 搜索冲突（通过 Handsontable 的 `beforeKeyDown` 钩子拦截）。

### Req B-5: 性能与可用性
1. THE `GetProjectFillProgress` 端点 SHALL 在 2000 行规模下 P95 响应时间 < 500ms。
2. THE 抽屉首屏 SHALL 仅渲染前 200 条问题项；超过部分提供"加载更多"按钮，避免长列表卡顿。
3. WHEN 后端请求失败，THE 徽章 SHALL 显示 `统计失败` 且抽屉禁用点击；不阻塞表格编辑。

---

## 3. 设计（Design）

### 3.1 后端

#### 3.1.1 新增端点
```
GET /Quotation/GetProjectFillProgress?id={fabh}
```

**返回 DTO**
```csharp
public sealed class ProjectFillProgressDto
{
    public int TotalRows { get; init; }      // 元件行总数 (x_lx = 11)
    public int FilledRows { get; init; }     // 已填价 (x_bj_dj > 0)
    public int UnfilledRows { get; init; }   // 未填价 (x_bj_dj <= 0 或空)
    public AnomalyCountDto Anomalies { get; init; } = new();
    public IReadOnlyList<ProblemItemDto> Problems { get; init; } = [];
}

public sealed class AnomalyCountDto
{
    public int Negative { get; init; }
    public int Deviation { get; init; }
    public int MissingSpec { get; init; }
    public int ZeroPrice { get; init; }
    public int Total => Negative + Deviation + MissingSpec + ZeroPrice;
}

public sealed class ProblemItemDto
{
    public string CabinetCode { get; init; } = "";
    public string CabinetName { get; init; } = "";
    public int RowSeq { get; init; }                 // 在柜内的序号 (按 x_bm 字典序)
    public string Name { get; init; } = "";
    public string Spec { get; init; } = "";
    public decimal CurrentPrice { get; init; }
    public decimal? AvgPrice { get; init; }
    public string IssueType { get; init; } = "";     // negative / deviation / missing_spec / zero_price
}
```

#### 3.1.2 SQL 核心
```sql
WITH cabinets AS (
  SELECT LEFT(LTRIM(RTRIM(x_bm)), 4) AS cab_code, MAX(x_mc) AS cab_name
  FROM BJB
  WHERE fabh = @fabh AND LEN(LTRIM(RTRIM(x_bm))) = 4
  GROUP BY LEFT(LTRIM(RTRIM(x_bm)), 4)
),
components AS (
  SELECT b.x_bm, LEFT(LTRIM(RTRIM(b.x_bm)), 4) AS cab_code,
         b.x_mc, b.x_ggxh, b.x_bj_dj, b.x_wzdh,
         h.avg_price, h.avg_count
  FROM BJB b
  LEFT JOIN STD_PRICE_HISTORY h
    ON h.x_wzdh = b.x_wzdh AND h.avg_count > 0
  WHERE b.fabh = @fabh AND b.x_lx = 11
)
SELECT c.*, ca.cab_name
FROM components c
LEFT JOIN cabinets ca ON ca.cab_code = c.cab_code;
```

后端在 C# 侧做异常归类与计数（避免 SQL UDF 复杂化）；序号 `RowSeq` 在 C# 内按 `x_bm` 字典序枚举生成。

#### 3.1.3 Controller
新增 `QuotationController.GetProjectFillProgress(string id)` 方法：
- `[HttpGet]` + `[RoleAuthorize]`（与现有 `GetProjectComponentSummary` 同权限）
- 返回 `Json(new { success = true, data = dto })`
- 异常返回 `{ success = false, message = "..." }`

### 3.2 前端

#### 3.2.1 DOM 调整（`FillPrice.cshtml`）
工具栏右侧 `<div class="d-flex align-items-center gap-2 flex-wrap">` 内、`@if (Model.CanEdit)` 之前插入：

```html
<div id="fill-progress-badge" class="d-inline-flex align-items-center gap-2 small">
    <span class="badge bg-info-subtle text-info-emphasis border border-info-subtle" title="已填价 / 总元件数">
        <i class="bi bi-list-check me-1"></i>
        <span id="progress-filled">0</span> / <span id="progress-total">0</span>
        （<span id="progress-percent">0%</span>）
    </span>
    <button id="problem-list-toggle" type="button"
            class="btn btn-sm btn-outline-warning"
            title="点击查看问题清单" disabled>
        <i class="bi bi-exclamation-triangle me-1"></i>异常 <span id="anomaly-count">0</span>
    </button>
</div>
```

表格右侧（与 `#component-usage-panel` 同级）新增抽屉容器：

```html
<div id="problem-list-drawer" class="problem-list-drawer d-none">
    <div class="drawer-header">
        <span class="fw-semibold">问题清单</span>
        <button type="button" class="btn-close" id="problem-list-close" aria-label="关闭"></button>
    </div>
    <div id="problem-list-body" class="drawer-body"></div>
</div>
```

CSS（追加到 `<style>`）：抽屉 `position: fixed; right: 0; top: 56px; bottom: 0; width: 360px; box-shadow: -2px 0 8px rgba(0,0,0,.1); background: #fff; z-index: 1040;`，移动端 100% 宽。

#### 3.2.2 JS 模块（`quotation-fill-price.js`）
新增封装：

```javascript
const progressApi = {
    fetch: async () => { /* GET projectFillProgressUrl, 失败返回 null */ },
    render: (dto) => { /* 更新徽章 DOM */ },
    incrementLocalFilled: (delta) => { /* 增量本柜统计 */ }
};

const problemDrawer = {
    open: () => {},
    close: () => {},
    render: (problems) => { /* 按 cabinetCode 分组渲染 */ },
    jumpTo: async (cabinetCode, rowSeq) => {
        await loadCabinetComponents(cabinetCode);
        hot.selectCell(rowSeq - 1, colPrice());
        hot.scrollViewportTo(rowSeq - 1, colPrice());
        flashRow(rowSeq - 1);
    }
};
```

挂载点：
- 页面加载结束 → `progressApi.fetch().then(progressApi.render)`
- `loadCabinetComponents` 成功后 → 增量算本柜 + 全局再 fetch
- `hot.addHook("afterChange")` 中价格列变更 → `progressApi.incrementLocalFilled`（仅当从无到有/有到无翻转才动）；并 setTimeout 500ms 防抖后做一次全局 fetch
- 自动填价成功后 → `progressApi.fetch()`
- 保存成功后 → `progressApi.fetch()`

F3 快捷键：在现有 `hot.updateSettings({ beforeKeyDown: ... })` 中扩展，捕获 keyCode 114（F3），找下一个 `x_bj_dj <= 0` 的行调 `selectCell`。

#### 3.2.3 隐藏规则
- `summaryMode === true` 时，`#fill-progress-badge` 和抽屉均 `d-none`
- 退出 `summaryMode` 时恢复显示

### 3.3 数据流图
```
┌────────────┐  GET /GetProjectFillProgress  ┌──────────────┐
│ FillPrice  │ ─────────────────────────────▶│ Quotation    │
│ 页（JS）   │ ◀──── ProjectFillProgressDto ─│ Controller   │
└─────┬──────┘                                └─────┬────────┘
      │ render badge / drawer                       │ SQL JOIN
      ▼                                             ▼
  徽章/抽屉DOM                                BJB ⋈ STD_PRICE_HISTORY
```

### 3.4 Property（属性测试要点）
- **P-B1**：进度合法性 — `FilledRows + UnfilledRows == TotalRows`
- **P-B2**：异常归类互斥性 — 任意一行至多归入 `negative / zero_price` 之一（不重复计数）；`deviation` 与 `missing_spec` 可与其它共存
- **P-B3**：进度幂等性 — 同一 fabh 连续调用两次，DTO 完全相等
- **P-B4**：性能边界 — 2000 行构造数据 P95 < 500ms

---

## 4. 任务（Tasks）

### Wave 1 — 后端
- [ ] **B-T1** 新增 `ProjectFillProgressDto / AnomalyCountDto / ProblemItemDto` 到 `PanelFlow.Web/Models/Quotation/`
- [ ] **B-T2** 在 `QuotationController` 新增 `GetProjectFillProgress(string id)`，含 SQL JOIN + C# 端归类计数 + 按 `x_bm` 字典序生成 `RowSeq`
- [ ] **B-T3** 单元测试覆盖：进度计算（含全空/全有/部分填）、4 类异常归类、空报价单边界

### Wave 2 — 前端
- [ ] **B-T4** `FillPrice.cshtml`：工具栏插入进度徽章 + 异常按钮，body 末尾插入抽屉容器，新增 CSS
- [ ] **B-T5** `quotation-fill-price.js`：新增 `progressApi` 模块，挂载到加载/编辑/切柜/自动填价/保存的生命周期
- [ ] **B-T6** `quotation-fill-price.js`：新增 `problemDrawer` 模块，按柜分组渲染，跳转 + 行高亮
- [ ] **B-T7** F3 快捷键 + InfoBar 提示（无下一项时）

### Wave 3 — 属性测试与验收
- [ ] **B-T8** 属性测试 P-B1 / P-B2 / P-B3 / P-B4 编码
- [ ] **B-T9** 手工冒烟：用一份 1000+ 行报价单测进度准确性、抽屉跳转、F3、隐藏规则

### 依赖图
```
B-T1 ──▶ B-T2 ──▶ B-T3
              │
              ▼
            B-T4 ──▶ B-T5 ──▶ B-T6 ──▶ B-T7
                                            │
                                            ▼
                                          B-T8 ──▶ B-T9
```

预估总工时：**1.5 天**（Wave 1 半天，Wave 2 一天，Wave 3 收尾半天）。

---

## 5. 风险与未决项

| # | 风险 / 未决 | 处置 |
|---|---|---|
| 1 | "偏离均价"判定需 LEFT JOIN STD_PRICE_HISTORY，大表性能未实测 | B-T3 同时基准测试；超 500ms 则改为后台预计算或拆 RAW 视图 |
| 2 | F3 与浏览器原生搜索冲突 | 仅在 Handsontable 持焦时拦截；测试 Chrome/Edge/Firefox |
| 3 | 抽屉宽度在小屏遮挡表格 | 移动端转 100% 宽 + 半屏遮罩，PC ≥ 1280 用 360px 固定宽 |
| 4 | 跨柜跳转放 Sprint 2 后用户是否接受 | spec 文案明确"按 F4 跳到下一个未填价柜"作为引导，用户实际反馈后再上 F4 |

---

## 6. 验收脚本（人工冒烟）

1. 打开任意 ≥ 100 元件行的报价单
2. 工具栏右侧应立即出现 `已填价 X / Y（Z%）` 与 `异常 K`
3. 修改任意单价 → 失焦 → 徽章数值应更新
4. 点击 `异常 K` → 右侧抽屉打开，按柜分组列出问题
5. 点击抽屉某一条 → 切换到对应柜 + 行高亮 2 秒
6. 按 F3 → 跳到本柜下一个未填价行
7. 进入项目根汇总视图 → 徽章与抽屉应隐藏
8. 退出汇总视图 → 徽章与抽屉恢复
