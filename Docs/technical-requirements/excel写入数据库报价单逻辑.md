# Excel 写入数据库报价单逻辑

## 概述
将前端表格数据转换为树形目录结构，并保存到数据库 `BJB` 表中。

## 前端发送数据
- `fabh`: 方案编号
- `tableJson`: 表格数据，格式为 `List<List<string>>`，每行包含 8 列 (c1 到 c8)

## 后端处理流程

### 1. 权限验证
- 检查用户角色是否管理员或者报价员
- 验证方案是否存在于 `BJFAT` 表
- 验证方案创建者为当前用户 (`bjfat.bjr == currentUser`)

### 2. 数据解析
- 反序列化 `tableJson` 为 `List<List<string>>`
- 调用 `BuildRowsFromTable(fabh, tableRows)` 构建 `BJB` 实体列表

### 3. 表格解析逻辑 (`BuildRowsFromTable`)
- 遍历每一行：
  - 如果 c2 (单元名称) 有值：创建单元节点和固定子节点
  - 如果 c3/c4/c5/c6/c7 (元件信息) 有值：创建元件节点

### 4. 单元节点生成
- 单元编码：`unitSeq` 从 1 开始，格式 `0001`, `0002`, ...
- 单元行：`x_lx = 1`
- 固定子节点：
  - `unitCode + "0001"`: 器件 (`x_lx = 1`)
  - `unitCode + "0002"`: 辅料 (`x_lx = 12`)
  - `unitCode + "0003"`: 壳体 (`x_lx = 13`)
  - `unitCode + "0004"`: 安装 (`x_lx = 14`)
  - `unitCode + "0005"`: 包装 (`x_lx = 15`)
- 字段映射
  - `x_bm`: unitCode
  - `x_mc`: c2 (单元名称)
  - `x_lx`: 1
  - `x_sl`: 1
  - 其它字段使用默认值

### 5. 元件节点生成
- 元件编码：`unitSeq + "0001" + componentSeq`，例如 `000100010001`
- `x_lx = 11` (器件类型)
- 字段映射：
  - `x_mc`: c3 (元件名称)
  - `x_ggxh`: c4 (规格)
  - `x_dj`: c5 (单价，解析为 decimal)
  - `x_sl`: c6 (数量，解析为 decimal)
  - `x_sccj`: c7 (厂商)

### 6. 数据库操作
- 开启事务
- 删除旧记录：`WHERE fabh = targetFabh AND x_bm NOT IN ('0', '9999')`
- 批量插入新记录
- 提交事务

## BJB 实体字段映射
- `fabh`: 方案编号
- `x_bm`: 树形编码 (4位单元或12位元件)
- `x_mc`: 名称
- `x_ggxh`: 规格
- `x_dw`: 单位
- `x_dj`: 单价
- `x_sl`: 数量
- `x_fdds`: 报价浮动
- `x_sccj`: 厂商
- `x_bj_dj`: 报价单价 (等于 x_dj)
- `x_bjb_bj`: 市场价 (等于 x_dj)
- `x_bjb_dj`: 报价表单价 (等于 x_dj)

- `x_lx`: 类型 (1=单元, 11=器件, 12=辅料, 13=壳体, 14=安装, 15=包装)
- `x_cgf`: 采购 (固定为 1)
- 其他字段：默认值或空

## 错误处理
- 方案编号为空：返回错误
- 无编辑权限：返回错误
- 表格数据格式错误：返回错误
- 表格为空：返回错误
- 数据库操作失败：事务回滚

## 返回结果
- 成功：`{ success: true, message: "保存成功，共写入 X 条记录。" }`
- 失败：`{ success: false, message: "错误信息" }`