# Requirements Document

## Introduction

自动填价功能基于历史报价数据，为当前报价单中的元件自动匹配并填充历史价格。该功能属于报价阶段（业务流程第1步）的辅助工具，帮助报价员快速完成单价填写，提高报价效率。

当前系统已有初步实现但无法正常工作，本次需求为完全重新设计和实现该功能。

## Glossary

- **报价单（Quotation）**：BJFAT 表中的一条记录，通过 `fabh`（方案编号）唯一标识
- **元件明细（BJB）**：报价单下的元件行项目，存储在 BJB 表中，通过 `fabh` + `x_bm` 唯一标识
- **控制柜（Cabinet）**：报价单中的一个逻辑分组单元，`x_bm` 为 4 位编码
- **元件行（Component Row）**：`x_bm` 长度为 12 且 `x_lx=11` 的 BJB 记录，代表一个具体元件
- **标准化指纹（x_wzdh）**：通过 NormalizeSpec（C#）或 F_CleanString（SQL）对规格型号进行标准化处理后的字符串，用于跨报价单匹配同一元件
- **历史价格表（STD_PRICE_HISTORY）**：按 x_wzdh 聚合的历史报价统计表，由定时任务每日刷新
- **填价页面（FillPrice Page）**：报价单的"填写报价"子页面，包含目录树和 Handsontable 表格
- **参考价格（Reference Price）**：从历史价格表中查询到的价格信息，供报价员参考
- **NormalizeSpec**：C# 端的规格型号标准化函数，去除括号内容、特殊符号、空格，全角转半角，统一小写

## Requirements

### Requirement 1: 历史价格数据准备

**User Story:** 作为系统管理员，我希望系统能自动维护历史价格聚合数据，以便报价员在填价时能快速获取参考价格。

#### Acceptance Criteria

1. THE STD_PRICE_HISTORY 表 SHALL 以 x_wzdh 为唯一键，存储每个唯一 x_wzdh 对应的最新报价（last_price）、最新来源方案编号（last_fabh）、最新报价时间（last_date）、原始规格型号（ggxh）、元件名称（x_mc）、计量单位（x_dw）、厂商（x_sccj）、近 5 年均价（avg_price）、近 5 年最低价（min_price）、近 5 年最高价（max_price）、样本数（avg_count）和最后刷新时间（updated_at）
2. THE SP_RefreshPriceHistory 存储过程 SHALL 从 BJB 表中筛选 x_lx=11 且 x_bjb_dj>0 且 x_wzdh 非空且所属报价单（BJFAT）dqzt=10（已成立）的元件记录进行聚合
3. THE SP_RefreshPriceHistory 存储过程 SHALL 按 x_wzdh 分组，取 fabh 降序的第一条记录的 x_ggxh、x_mc、x_dw、x_sccj、x_bjb_dj、fabh、x_bjb_datetime 作为最新报价信息
4. THE SP_RefreshPriceHistory 存储过程 SHALL 对每个 x_wzdh 分组中 x_bjb_datetime 在最近 5 年内或为 NULL 的记录，计算平均价格、最低价格、最高价格和样本数
5. WHEN 保存方案（SavePlan）写入 BJB 时，THE 系统 SHALL 对每个元件行使用 NormalizeSpec 函数处理 x_ggxh 字段并将结果写入 x_wzdh 字段
6. IF 元件行的 x_ggxh 为 NULL 或空字符串，THEN THE 系统 SHALL 将该行的 x_wzdh 设为 NULL 且不参与后续历史价格聚合
7. THE SQL Server Agent 定时任务 SHALL 每天凌晨 2:00 执行 SP_RefreshPriceHistory 存储过程
8. THE SP_RefreshPriceHistory 存储过程 SHALL 使用 MERGE 语句实现 upsert 逻辑，对已存在的 x_wzdh 更新所有聚合字段，对新增的 x_wzdh 插入完整记录

### Requirement 2: 自动填价查询

**User Story:** 作为报价员，我希望点击按钮后系统能自动查询当前报价单元件的历史价格，以便我快速了解哪些元件有历史报价可参考。

#### Acceptance Criteria

1. WHEN 报价员点击"引用历史报价"按钮时，THE 系统 SHALL 查询当前报价单中所有 x_bm 长度为 12 且 x_lx=11 的元件行的规格型号（x_ggxh）
2. IF 某元件行的 x_wzdh 字段为空或 NULL，THEN THE 系统 SHALL 使用 NormalizeSpec 函数对该元件的 x_ggxh 实时计算 x_wzdh；若 x_wzdh 已有存储值则直接使用该存储值
3. IF 某元件行的 x_ggxh 为空或 NULL，THEN THE 系统 SHALL 跳过该元件，不纳入批量查询集合
4. THE 系统 SHALL 使用计算得到的 x_wzdh 集合批量查询 STD_PRICE_HISTORY 表，获取匹配的历史价格，并在 30 秒内返回结果
5. THE 系统 SHALL 返回匹配结果，包含每个 x_wzdh 对应的最新价格（last_price）、单位（x_dw）和厂商（x_sccj）
6. IF 当前报价单无任何 x_bm 长度为 12 且 x_lx=11 且 x_ggxh 非空的元件行，THEN THE 系统 SHALL 返回空匹配结果且不报错
7. IF 批量查询 STD_PRICE_HISTORY 过程中发生数据库异常，THEN THE 系统 SHALL 返回错误信息并记录日志，不修改任何现有数据

### Requirement 3: 前端自动填充逻辑

**User Story:** 作为报价员，我希望系统能将查询到的历史价格自动填入表格中单价为 0 的元件行，以便我只需关注无历史记录的元件。

#### Acceptance Criteria

1. WHEN 历史价格查询返回成功时，THE 前端 SHALL 遍历当前 Handsontable 表格中所有元件行（x_bm 长度为 12 的行），按每行的 x_wzdh 与返回的历史价格数据进行匹配
2. IF 某元件行的当前单价为 0、null 或空值，且该行 x_wzdh 匹配到历史价格，THEN THE 前端 SHALL 将历史价格（last_price）填入该行的单价列
3. IF 某元件行的当前单位为 null 或空字符串，且该行 x_wzdh 匹配到历史单位，THEN THE 前端 SHALL 将历史单位填入该行的单位列
4. IF 某元件行的当前厂家为 null 或空字符串，且该行 x_wzdh 匹配到历史厂家，THEN THE 前端 SHALL 将历史厂家填入该行的厂家列
5. THE 前端 SHALL 保留已手动填写的单价（大于 0 的值），不进行覆盖
6. WHEN 自动填充完成后，THE 前端 SHALL 在信息栏显示匹配统计信息，格式为"已匹配 M/N 个元件的历史报价，X 个元件无历史记录"，其中 M 为匹配数量、N 为元件总数、X 为未匹配数量
7. IF 某元件行的 x_wzdh 为空或 null，THEN THE 前端 SHALL 将该行计入未匹配数量，不进行填充
8. WHEN 自动填充修改表格单元格数据时，THE 前端 SHALL 通过 Handsontable 的 setDataAtCell 方法写入，以触发表格的数据变更事件供后续保存流程识别

### Requirement 4: 填价页面交互

**User Story:** 作为报价员，我希望填价页面提供清晰的操作引导和状态反馈，以便我能高效完成报价工作。

#### Acceptance Criteria

1. THE 填价页面 SHALL 在工具栏显示"引用历史报价"按钮
2. WHILE 未加载任何控制柜元件数据时，IF 报价员点击"引用历史报价"按钮，THEN THE 系统 SHALL 在信息栏显示提示信息，指示需先点击左侧控制柜节点加载元件数据
3. WHILE 自动填价请求正在执行时，THE 系统 SHALL 禁用"引用历史报价"按钮并在信息栏显示"正在匹配历史报价..."提示
4. WHEN 自动填价请求完成后，THE 系统 SHALL 恢复按钮可用状态并清除"正在匹配历史报价..."提示
5. IF 自动填价请求失败，THEN THE 系统 SHALL 清除加载提示、在信息栏显示错误信息并恢复按钮可用状态
6. IF 自动填价请求在 30 秒内未收到响应，THEN THE 系统 SHALL 中止请求、在信息栏显示超时错误信息并恢复按钮可用状态
7. THE "引用历史报价"按钮 SHALL 支持重复点击（幂等操作），每次执行以最新查询结果为准，仅填充单价为 0 的元件行，不覆盖已有非零单价

### Requirement 5: 自动填价数据持久化

**User Story:** 作为报价员，我希望自动填充的价格能保存到数据库，以便下次打开报价单时仍能看到已填写的价格。

#### Acceptance Criteria

1. WHEN 报价员触发自动填价操作时，THE 系统 SHALL 在 BJB 表中批量更新当前报价单（fabh）下所有满足条件的元件行（x_lx=11 且 x_wzdh 非空且 x_bjb_dj=0）的 x_bjb_dj、x_bjb_bj、x_bj_dj 字段为 STD_PRICE_HISTORY 中对应 x_wzdh 的 last_price 值
2. IF 元件行的 x_bjb_dj 字段当前值不为 0，THEN THE 系统 SHALL 跳过该行不做更新，保留已有价格
3. THE 系统 SHALL 在一个数据库事务中完成所有价格更新操作，事务超时时间不超过 30 秒
4. WHEN 批量更新成功完成后，THE 系统 SHALL 返回操作结果，包含匹配元件数量、实际更新数量和未匹配数量
5. IF 批量更新过程中发生错误，THEN THE 系统 SHALL 回滚事务，不保留任何部分更新结果，并返回包含失败原因的错误信息

### Requirement 6: 参考价格列显示

**User Story:** 作为报价员，我希望在填价表格中能看到历史参考价格列，以便在手动填价时有价格参考依据。

#### Acceptance Criteria

1. THE 填价页面 SHALL 在工具栏区域提供"显示参考价格"复选框
2. WHEN 报价员勾选"显示参考价格"时，THE 系统 SHALL 向后端请求当前表格中所有元件的参考价格数据，并在单价列后插入一列"参考价格"只读列，显示该元件在 STD_PRICE_HISTORY 中的 last_price 值
3. WHEN 报价员取消勾选"显示参考价格"时，THE 表格 SHALL 移除参考价格列，且不影响其他列的数据和表格滚动位置
4. WHILE 未加载控制柜元件数据时，THE "显示参考价格"复选框 SHALL 处于禁用状态
5. THE 参考价格列 SHALL 为只读状态，使用浅绿色背景区分于可编辑列，且单元格不可被编辑或选中进行输入
6. IF 某元件行在 STD_PRICE_HISTORY 中无匹配记录，THEN THE 参考价格列 SHALL 在该行显示为空（不显示 0）
7. WHILE 参考价格数据正在加载时，THE 系统 SHALL 在参考价格列中显示加载状态指示，加载完成后替换为实际价格值

### Requirement 7: 权限控制

**User Story:** 作为系统管理员，我希望自动填价功能受到权限控制，以防止未授权用户修改报价数据。

#### Acceptance Criteria

1. THE 自动填价接口 SHALL 要求用户已登录且具有报价权限（报价单报价人本人或管理员角色）
2. IF 未登录用户请求自动填价接口，THEN THE 系统 SHALL 返回未授权响应并重定向至登录页面
3. IF 已登录用户请求自动填价接口但非该报价单报价人（BJFAT.bjr）且非管理员角色，THEN THE 系统 SHALL 返回 403 状态码及错误信息指明权限不足
4. THE 自动填价接口 SHALL 启用防 CSRF 保护（ValidateAntiForgeryToken）
5. IF 报价单状态为已成立（dqzt=10）时用户请求自动填价接口，THEN THE 系统 SHALL 拒绝请求并返回错误信息指明已成立的报价单不允许修改价格
6. WHILE 当前用户非报价单报价人且非管理员时，THE 填价页面 SHALL 隐藏"引用历史报价"按钮和保存按钮

### Requirement 8: 标准化指纹匹配算法

**User Story:** 作为报价员，我希望系统能准确匹配不同报价单中的同一元件，即使规格型号的书写格式略有差异。

#### Acceptance Criteria

1. IF 输入字符串为 NULL 或空白（仅含空格、制表符等），THEN THE NormalizeSpec 函数 SHALL 返回空字符串（C#）或 NULL（SQL），不进行后续处理
2. THE NormalizeSpec 函数 SHALL 按以下固定顺序处理输入：先转为小写，再去除不可见字符（CR、LF、TAB、不间断空格 U+00A0、零宽空格 U+200B），再全角转半角，再去除括号及其内容，最后过滤非保留字符
3. THE NormalizeSpec 函数 SHALL 去除所有括号及括号内的内容，支持嵌套括号；IF 左括号无匹配的右括号，THEN THE 函数 SHALL 丢弃该孤立左括号字符但继续正常处理后续字符；IF 右括号无匹配的左括号（深度为0），THEN THE 函数 SHALL 丢弃该孤立右括号字符
4. THE NormalizeSpec 函数 SHALL 将全角字符（U+FF01 至 U+FF5E）转换为对应半角字符（Unicode 值减 65248），全角空格（U+3000）转换为半角空格
5. THE NormalizeSpec 函数 SHALL 仅保留以下字符：ASCII 字母（a-z）、ASCII 数字（0-9）、CJK 统一汉字（U+4E00 至 U+9FFF）、以及单位符号集合（μ、ω、Ω、°、±、℃、φ），其余字符（空格、标点、其他符号）全部丢弃
6. THE NormalizeSpec 函数（C#）与 F_CleanString 函数（SQL）SHALL 对相同输入产生相同输出；输入最大长度为 400 个字符（与 SQL 函数参数一致）
7. IF NormalizeSpec 处理后结果为空字符串，THEN THE 系统 SHALL 将该元件视为无有效指纹，不参与历史价格匹配
