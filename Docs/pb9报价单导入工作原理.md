# PB9 报价单导入工作原理（`pb9-w_drdc`）

## 1. 功能目标

该段 PowerBuilder 9.0 代码用于将 Excel 报价表导入到数据库 `BJB` 表，并自动构建报价树结构（单元 + 固定子节点 + 器件明细）。

源代码文件：`pb9code/pb9-w_drdc.txt`

---

## 2. 执行入口与前置校验

按钮点击后按以下顺序执行：

1. 检查当前报价单是否已有明细：`if fab.rowcount() > 0`
   - 若已有数据，直接提示“该报价单已经有数据，不能导入”，并终止。
2. 弹出文件选择框，仅允许 `*.xls`。
3. 通过 OLE 启动 Excel：`ConnectToNewObject("excel.application")`
   - 若 Excel 启动失败，提示并终止。
4. 打开选中的 Excel 第 1 个工作表。

> 这意味着旧逻辑是“空报价单一次性导入”，不支持覆盖导入。

---

## 3. Excel 模板的核心约定

代码实际读取的是前 7 列，关键列如下：

- 第2列：单元名称（用于判断是否切换到新单元）
- 第3列：器件名称（`x_mc`）
- 第4列：规格型号（`x_ggxh`）
- 第5列：单价（`x_dj`）
- 第6列：数量（`x_sl`）
- 第7列：厂家（该段代码未写入，留空）

结束条件：

- 当某一行 **1~7列全部为 NULL** 时，认为数据结束，停止循环。

---

## 4. 导入主流程（算法）

## 4.1 初始化

- `J = 2`：从第 2 行开始读数据。
- `curdy_xh = 1`：当前单元序号（4位编码）。
- `CURDY_MC = Cells(2,2)`：读取首行单元名称。
- `CUR_SJ = 当前日期时间`：写入 `x_bjb_datetime`。

若 `CURDY_MC` 为空，则判定模板错误并退出。

## 4.2 先创建第一个单元骨架

先写入 6 条基础节点（同一个单元编码前缀）：

1. `0001`：单元节点（`x_lx=1`，名称=单元名）
2. `00010001`：器件（`x_lx=1`）
3. `00010002`：辅料（`x_lx=12`）
4. `00010003`：壳体（`x_lx=13`）
5. `00010004`：安装（`x_lx=14`）
6. `00010005`：包装（`x_lx=15`）

> 其中示例“0001”会被替换成当前单元序号 `STRING(curdy_xh,"0000")`。

## 4.3 行循环处理（`do while FLAG = false`）

每行先取第2列到 `TEMP_STR`，然后分支：

- **分支A：仍属于当前单元**
  - 条件：`TEMP_STR = CURDY_MC`，或第2列为空。
  - 若第3、4列都为空，则跳过该行。
  - 否则新增一条器件明细：
    - 编码：`单元4位 + "0001" + 器件序号4位`
    - `x_mc <- 第3列`
    - `x_ggxh <- 第4列`
    - `x_dj <- 第5列(数值)`
    - `x_sl <- 第6列(数值)`
    - `x_lx = 11`
    - `x_bj_dj/x_bjb_bj/x_bjb_dj` 同步写入单价
  - 行号 `J` 加 1，继续。

- **分支B：遇到新单元**
  - 条件：`TEMP_STR <> CURDY_MC` 且非空。
  - `curdy_xh += 1`，更新 `CURDY_MC = TEMP_STR`。
  - 重新插入该新单元的 6 条骨架节点（同 4.2）。
  - 器件序号计数 `I` 归零，下一轮继续读取器件行。

---

## 5. 编码体系（BJB.x_bm）

该逻辑形成固定分层编码：

- 单元：`####`（4位）
- 单元固定子类：`####0001~0005`
- 器件明细：`####0001####`

示例（第 2 个单元第 3 个器件）：

- 单元：`0002`
- 器件目录：`00020001`
- 器件明细：`000200010003`

这套规则与当前 C# 实现中“单元 + 固定节点 + 器件明细”的建模方式是一致的。

---

## 6. 数据类型处理细节

代码对 Excel 单元格做了类型分支：

- 第3/4列（名称/规格）：
  - 字符串则 `trim`
  - 非字符串则转 `string(...)`
- 第5/6列（单价/数量）：
  - 空值 => 0
  - 字符串 => 0（不会尝试解析字符串数字）
  - 数值类型 => 直接写入

> 因此旧系统对“文本数字”容错较弱，要求 Excel 单价、数量单元格最好是数值格式。

---

## 7. 提交与界面刷新

循环结束后执行：

1. `commit using sqlca;`
2. 断开并销毁 Excel OLE 对象
3. `fab.retrieve(curbjdbh)` 重新加载报价数据
4. `loadfamx(num)` 重新装载报价模型树
5. `dw_1.retrieve(curbjdbh,"",1)` 刷新数据窗口
6. 再次 `commit`
7. 弹窗提示“报价单导入成功”

---

## 8. 对后续 C# 迁移的参考要点

1. **禁止重复导入**：仅空报价单允许导入（与 PB 一致）。
2. **严格保留编码规则**：`单元4位 + 固定0001~0005 + 器件0001####`。
3. **固定节点必须生成**：器件/辅料/壳体/安装/包装。
4. **结束规则**：连续空行策略可优化，但要兼容“空行即结束”的历史行为。
5. **字段映射一致性**：`x_dj/x_sl/x_ggxh/x_lx/x_bjb_*` 的赋值口径不要变。
6. **事务一致性**：建议在 C# 中保持整次导入的单事务提交。

---

## 9. PB9 字段 -> C# 参数/模型对照表

以下对照用于实现/联调时快速核对。  
（注：C# 名称以当前项目 `QuotationController.SavePlan` 与 `BuildRowsFromTable` 的参数语义为准）

- **上下文主键**
  - `PB9: curbjdbh`（当前报价单编号） -> `C#: QuotationPlanSaveRequest.Fabh`

- **Excel 行输入（按列）**
  - `PB9: Cells(J,2)` 单元名 -> `C#: treeNodeNames[]`（目录预览后生成的单元节点名）
  - `PB9: Cells(J,3)` 器件名称 -> `C#: ComponentSourceRow.Name`
  - `PB9: Cells(J,4)` 规格型号 -> `C#: ComponentSourceRow.Spec`
  - `PB9: Cells(J,5)` 单价 -> `C#: ComponentSourceRow.Price`
  - `PB9: Cells(J,6)` 数量 -> `C#: ComponentSourceRow.Qty`
  - `PB9: Cells(J,7)` 厂家（PB段落未写入） -> `C#: ComponentSourceRow.Vendor`（当前 C# 已支持）

- **编码与层级**
  - `PB9: curdy_xh`（单元序号） -> `C#: unitSeq`
  - `PB9: CURDY_BH = STRING(curdy_xh,"0000")` -> `C#: currentUnitCode = unitSeq.ToString("D4")`
  - `PB9: CURDY_BH + "0001" + string(i,"0000")` -> `C#: componentCode = $"{currentUnitCode}0001{componentSeq:D4}"`

- **BJB 字段映射（器件明细 x_lx=11）**
  - `PB9: x_bm` -> `C#: BjbWriteRow.Xbm`
  - `PB9: x_mc` -> `C#: BjbWriteRow.Xmc`
  - `PB9: x_ggxh` -> `C#: BjbWriteRow.Xggxh`
  - `PB9: x_sccj`（PB示例置空） -> `C#: BjbWriteRow.Xsccj`
  - `PB9: x_dj` -> `C#: BjbWriteRow.Xdj`
  - `PB9: x_sl` -> `C#: BjbWriteRow.Xsl`
  - `PB9: x_bj_dj` -> `C#: BjbWriteRow.XbjDj`
  - `PB9: x_bjb_bj` -> `C#: BjbWriteRow.XbjbBj`
  - `PB9: x_bjb_dj` -> `C#: BjbWriteRow.XbjbDj`
  - `PB9: x_lx=11` -> `C#: BjbWriteRow.Xlx=11`

- **固定节点映射（每单元自动创建）**
  - `PB9: ####0001 / 器件 / x_lx=1` -> `C#: CreateFixedNode(..., "0001", "器件", 1)`
  - `PB9: ####0002 / 辅料 / x_lx=12` -> `C#: CreateFixedNode(..., "0002", "辅料", 12)`
  - `PB9: ####0003 / 壳体 / x_lx=13` -> `C#: CreateFixedNode(..., "0003", "壳体", 13)`
  - `PB9: ####0004 / 安装 / x_lx=14` -> `C#: CreateFixedNode(..., "0004", "安装", 14)`
  - `PB9: ####0005 / 包装 / x_lx=15` -> `C#: CreateFixedNode(..., "0005", "包装", 15)`

- **默认值差异（迁移注意）**
  - `PB9` 固定节点 `x_sl` 多处写 `1`；当前 `C#` 固定节点 `Xsl` 为 `0`（`CreateFixedNode`）。
  - 若后续要做“与历史行为完全一致”，需评估是否将固定节点数量改回 `1`，并验证对汇总/报价计算的影响。

