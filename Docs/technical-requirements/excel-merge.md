# Excel 合并模块

## 1. 功能概述

将多个客户提供的 Excel 元件清单合并到一张统一的报价表格中，方便报价员快速整理。

- 菜单位置：`项目管理 -> Excel合并`
- 控制器：`PanelFlow.Web/Controllers/QuotationController.cs`
- 视图：`PanelFlow.Web/Views/Quotation/MergeExcel.cshtml`
- 前端脚本：`PanelFlow.Web/wwwroot/js/quotation-merge.js`
- 权限：管理员、报价员、生产管理人员

## 2. 业务流程

```
选择多个 Excel
       ↓
点击"合并"按钮
       ↓
后端逐个解析 Excel
       ↓
合并到 Handsontable 表格
       ↓
左侧目录树显示单元号（含重复/错误标记）
       ↓
用户可编辑表格
       ↓
点击"导出 Excel"下载合并结果
```

## 3. 输入 Excel 文件格式要求

### 3.1 必需的标题列

文件第 1 行必须同时包含以下 5 个列名：

| 列名 | 说明 |
|------|------|
| `名称` | 元件名称 |
| `型号规格` | 元件规格 |
| `数量` | 元件数量 |
| `厂商` 或 `生产厂家` | 元件生产厂家 |
| `备注` | 备注信息（可为空） |

**缺少任一列 → 文件被忽略，不参与合并**

### 3.2 数据规则

- 列顺序不要求固定（按列名匹配）
- 第 1 行为标题行
- 从第 2 行开始为数据行
- 名称、规格、数量都为空 → 跳过该行
- 单文件最多读取 5000 行

## 4. 合并后的表格结构

合并后的页面表格固定 8 列：

| 列号 | 列名 | 数据来源 |
|------|------|----------|
| 1 | 序号 | 全局递增（跨文件连续） |
| 2 | 单元号 | Excel 文件名（去除扩展名） |
| 3 | 名称 | Excel `名称` 列 |
| 4 | 规格 | Excel `型号规格` 列 |
| 5 | 单价 | 默认 `0.0` |
| 6 | 数量 | Excel `数量` 列 |
| 7 | 生产厂家 | Excel `厂商` 列 |
| 8 | 总价 | 默认 `0.0` |

### 4.1 单元号生成规则

每个 Excel 文件在合并表格中生成 1 个单元块：

- **单元首行**：单元号 = Excel 文件名（去扩展名），数量 = `1`，其他列空
- **元件行**：单元号空，填充名称/规格/数量/厂商

### 4.2 序号递增规则

- 全局连续递增（跨文件）
- 文件 1：序号 1～10
- 文件 2：序号 11～18（接续 11，不重新从 1 开始）
- 通过后端参数 `startSeqNo` 和返回值 `lastSeqNo` 实现

## 5. 后端接口

### 5.1 GET `/Quotation/MergeExcel`

返回合并页面视图。

### 5.2 POST `/Quotation/MergeExcelFile`

解析单个 Excel 文件。

**请求参数**：

| 参数 | 类型 | 说明 |
|------|------|------|
| file | IFormFile | Excel 文件（.xls/.xlsx） |
| startSeqNo | int | 起始序号 |

**响应**：

```json
{
  "success": true,
  "rows": [["1", "P101", "", "", "0.0", "1", "", "0.0"], ...],
  "rowCount": 25,
  "lastSeqNo": 25,
  "reachedLimit": false
}
```

**忽略响应**（缺少必需列）：

```json
{
  "success": false,
  "ignored": true,
  "message": "Excel 缺少必需列（名称/型号规格/数量/厂商/备注），已忽略"
}
```

### 5.3 POST `/Quotation/ExportMergedExcel`

导出合并后的表格为 Excel 文件。

**请求参数**（JSON）：

```json
{
  "rows": [["1", "P101", "", ...], ["2", "", "断路器", ...]]
}
```

**响应**：返回 `xlsx` 文件下载，文件名 `合并元件表_yyyyMMddHHmmss.xlsx`

## 6. 前端交互逻辑

### 6.1 页面布局

- 左侧：目录树（Excel 文件列表 / 合并后的单元号列表）
- 中间：可拖动分隔条，支持隐藏/显示目录树
- 右侧：Handsontable 表格（8 列）
- 底部按钮：`选择 Excel 文件`、`合并`、`导出 Excel`、`清空数据`

### 6.2 目录树标记

合并后目录树自动生成单元号节点，带状态标签：

| 标签 | 触发条件 | 样式 |
|------|---------|------|
| `重复` | 同一单元号出现多次 | 红色 badge |
| `错误` | 单元下任一行规格为空或数量为 0 | 黄色 badge |

### 6.3 节点点击

单击目录树节点 → 表格自动滚动并选中该单元号所在行。

### 6.4 离开页面提示

合并后未导出 → 用户离开页面（菜单导航/关闭）时弹出确认：

> 请及时导出保存合并后excel文件。确认离开吗？

实现细节：
- `beforeunload` 事件：拦截浏览器关闭/刷新
- `click` 事件捕获：拦截站内 `<a>` 链接
- 导出时临时禁用提示（`suppressLeaveCheck` 标志）
- 跳过下载链接、新窗口、锚点链接

### 6.5 合并结果消息

合并完成后状态栏显示：

> 选择 N 个文件，导入 X 个文件，忽略 Y 个文件

## 7. 主要校验规则

| 校验项 | 规则 |
|--------|------|
| 文件类型 | 仅支持 `.xls` / `.xlsx` |
| 文件大小 | 单文件不超过 5000 行 |
| 必需列 | 必须包含 5 个列：名称/型号规格/数量/厂商/备注 |
| 缺列处理 | 忽略该文件，不计入导入数 |
| 数据行 | 名称、规格、数量都为空则跳过 |

## 8. 关键代码位置

| 文件 | 说明 |
|------|------|
| `PanelFlow.Core/Services/PermissionService.cs` | 菜单注册（`项目管理 -> Excel合并`） |
| `PanelFlow.Web/Controllers/QuotationController.cs` | `MergeExcel` / `MergeExcelFile` / `ExportMergedExcel` |
| `PanelFlow.Web/Views/Quotation/MergeExcel.cshtml` | 页面视图 |
| `PanelFlow.Web/wwwroot/js/quotation-merge.js` | 前端交互逻辑 |

## 9. 审计日志

| 操作 | 是否记录 | 说明 |
|------|---------|------|
| 进入合并页面 | ❌ | 仅页面浏览，无业务影响 |
| 合并操作 | ❌ | 数据未落库，停留在前端 |
| 导出 Excel | ✅ | 数据流出系统，需追溯 |

**导出操作记录字段**：

| 字段 | 值 |
|------|------|
| ActionType | `ExportMergedExcel` |
| Module | `Quotation` |
| EntityName | `MergedExcel` |
| EntityId | 导出文件名 |
| AfterData | `{ fileName, rowCount, unitCount }` |

## 10. 后续待优化项

- 表格容量上限固定 5000 行，超大量数据需要分页或虚拟滚动
- 目录树重复检测仅基于单元号，未识别同名不同内容的情况
- 导出文件命名固定时间戳，未支持自定义文件名
- 未支持表格的撤销/重做（依赖 Handsontable 内置）
