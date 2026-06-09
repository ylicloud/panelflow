# Requirements Document

## Introduction

自动填价功能基于历史报价数据，为当前报价单中的元件自动匹配并填充历史价格。该功能属于报价阶段（业务流程第1步）的辅助工具，帮助报价员快速完成单价填写，提高报价效率。

当前系统已有初步实现但无法正常工作，本次需求为完全重新设计和实现该功能。

填价页面同时承担元件数据的编辑职责（增删改控制柜和元件），与 Excel 导入功能形成"导入创建 + 页面编辑"的分工模式。

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
- **金额（Amount）**：由公式 `x_bj_dj * (1 + x_fdds / 100) * x_sl` 计算得出的元件总价
- **浮动点数（x_fdds）**：价格浮动系数，用于在基础单价上增加百分比调整
- **全量替换保存**：保存时先删除该方案下所有旧元件记录，再批量插入页面中的最新数据（在一个事务中）

## Requirements

### Requirement 1: 历史价格数据准备

**User Story:** 作为系统管理员，我希望系统能自动维护历史价格聚合数据，以便报价员在填价时能快速获取参考价格。

#### Acceptance Criteria

1. THE STD_PRICE_HISTORY 表 SHALL 以 x_wzdh 为唯一键，存储每个唯一 x_wzdh 对应的最新报价（last_price）、最新来源方案编号（last_fabh）、最新报价时间（last_date）、原始规格型号（ggxh）、元件名称（x_mc）、计量单位（x_dw）、厂商（x_sccj）、近 5 年均价（avg_price）、近 5 年最低价（min_price）、近 5 年最高价（max_price）、样本数（avg_count）和最后刷新时间（updated_at）
2. THE SP_RefreshPriceHistory 存储过程 SHALL 从 BJB 表中筛选 x_lx=11 且 x_bj_dj>0 且 x_wzdh 非空且所属报价单（BJFAT）dqzt=10（已成立）的元件记录进行聚合
3. THE SP_RefreshPriceHistory 存储过程 SHALL 按 x_wzdh 分组，在 x_bjb_datetime 非空且在近 5 年内的记录中取 fabh 降序的第一条，将其 x_ggxh、x_mc、x_dw、x_sccj、x_bj_dj、fabh、x_bjb_datetime 作为最新报价信息
4. THE SP_RefreshPriceHistory 存储过程 SHALL 仅对 x_bjb_datetime 非空且在最近 5 年内的记录计算平均价格、最低价格、最高价格和样本数（排除早期无报价日期的陈旧数据）
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
5. THE 系统 SHALL 返回匹配结果，包含每个 x_wzdh 对应的最新价格（last_price）、均价（avg_price）、最低价（min_price）、最高价（max_price）、样本数（avg_count）、单位（x_dw）和厂商（x_sccj）
6. IF 当前报价单无任何 x_bm 长度为 12 且 x_lx=11 且 x_ggxh 非空的元件行，THEN THE 系统 SHALL 返回空匹配结果且不报错
7. IF 批量查询 STD_PRICE_HISTORY 过程中发生数据库异常，THEN THE 系统 SHALL 返回错误信息并记录日志，不修改任何现有数据

### Requirement 3: 前端自动填充逻辑

**User Story:** 作为报价员，我希望系统能将查询到的历史价格自动填入表格中单价为 0 的元件行，以便我只需关注无历史记录的元件。

#### Acceptance Criteria

1. WHEN 历史价格查询返回成功时，THE 前端 SHALL 遍历当前 Handsontable 表格中所有元件行（x_bm 长度为 12 的行），按每行的 x_wzdh 与返回的历史价格数据进行匹配
2. IF 某元件行的当前单价为 0、null 或空值，且该行 x_wzdh 匹配到历史价格，THEN THE 前端 SHALL 将历史价格（last_price）填入该行的单价列（x_bj_dj）
3. IF 某元件行的当前单位为 null 或空字符串，且该行 x_wzdh 匹配到历史单位，THEN THE 前端 SHALL 将历史单位填入该行的单位列
4. IF 某元件行的当前厂家为 null 或空字符串，且该行 x_wzdh 匹配到历史厂家，THEN THE 前端 SHALL 将历史厂家填入该行的厂家列
5. THE 前端 SHALL 保留已手动填写的单价（大于 0 的值），不进行覆盖
6. WHEN 自动填充完成后，THE 前端 SHALL 在信息栏显示匹配统计信息，格式为"已匹配 M/N 个元件的历史报价，X 个元件无历史记录"，其中 M 为匹配数量、N 为元件总数、X 为未匹配数量
7. IF 某元件行的 x_wzdh 为空或 null，THEN THE 前端 SHALL 将该行计入未匹配数量，不进行填充
8. WHEN 自动填充修改表格单元格数据时，THE 前端 SHALL 通过 Handsontable 的 setDataAtCell 方法写入，以触发表格的数据变更事件供后续保存流程识别
9. WHEN 单价（x_bj_dj）被自动填充后，THE 前端 SHALL 自动重新计算该行的金额列（公式：x_bj_dj * (1 + x_fdds / 100) * x_sl）

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

1. THE 填价页面 SHALL 在表格中始终显示"参考价格"只读列，位于单价列之后
2. WHEN 加载控制柜元件数据时，THE 系统 SHALL 同时向后端请求当前表格中所有元件的参考价格数据（STD_PRICE_HISTORY 中的 last_price），并填充到参考价格列
3. THE 参考价格列 SHALL 为只读状态，使用浅绿色背景区分于可编辑列，且单元格不可被编辑或选中进行输入
4. IF 某元件行在 STD_PRICE_HISTORY 中无匹配记录，THEN THE 参考价格列 SHALL 在该行显示为空（不显示 0）
5. WHEN 报价员将鼠标悬停在参考价格单元格上时，THE 系统 SHALL 显示 tooltip 浮层，内容包含该元件的均价（avg_price）、最低价（min_price）、最高价（max_price）和样本数（avg_count）
6. IF 某元件行在 STD_PRICE_HISTORY 中无匹配记录，THEN THE tooltip SHALL 不显示
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

### Requirement 9: 价格异常检测与提醒

**User Story:** 作为报价员，我希望系统能自动检测异常价格并以颜色标记提醒我，以便我及时发现和修正错误价格。

#### Acceptance Criteria

1. WHEN 表格中元件行的单价（x_bj_dj）为 0 或空值时，THE 前端 SHALL 将该行单价单元格背景设为浅灰色，提示未填写
2. WHEN 表格中元件行的单价（x_bj_dj）为负数时，THE 前端 SHALL 将该行单价单元格背景设为红色，提示必须修正
3. WHEN 表格中元件行的单价（x_bj_dj）偏离该元件历史均价（avg_price）超过 ±20% 时，THE 前端 SHALL 将该行单价单元格背景设为橙色，提示价格偏离较大
4. THE 偏离检测 SHALL 使用公式 `|x_bj_dj - avg_price| / avg_price > 0.2` 进行判断；IF avg_price 为 0 或无历史记录，THEN THE 系统 SHALL 不进行偏离检测
5. WHEN 报价员将鼠标悬停在异常标记的单价单元格上时，THE 系统 SHALL 显示 tooltip 说明异常原因（如"价格为负数，请修正"或"偏离历史均价 ¥X.XX 超过 20%"）
6. THE 价格异常检测 SHALL 在以下时机触发：元件数据加载完成后、单价单元格值变更后、自动填价完成后

### Requirement 10: 价格数据校验

**User Story:** 作为系统管理员，我希望系统能阻止负数价格写入数据库，以确保报价数据的准确性和干净度。

#### Acceptance Criteria

1. WHEN 保存方案（SavePlan）时，THE 系统 SHALL 校验所有元件行的单价（x_bj_dj）不为负数
2. IF 存在任何元件行的 x_bj_dj 为负数，THEN THE 系统 SHALL 拒绝保存并返回错误信息，列出负价格元件的名称和编号
3. THE 前端 SHALL 在保存前进行客户端校验，IF 检测到负数价格，THEN THE 前端 SHALL 阻止提交并在信息栏显示错误提示，指明哪些元件价格为负
4. WHEN 保存方案写入 BJB 时，THE 系统 SHALL 对每个元件行计算 x_dj 字段值，公式为 `x_bj_dj * (1 + x_fdds / 100)`，并将计算结果写入 x_dj 字段
5. IF 元件行的 x_fdds 为 NULL，THEN THE 系统 SHALL 将 x_fdds 视为 0 进行计算（即 x_dj = x_bj_dj）

### Requirement 11: 金额列显示

**User Story:** 作为报价员，我希望在表格中能看到每个元件的金额（总价），以便快速了解各元件的费用占比。

#### Acceptance Criteria

1. THE 填价表格 SHALL 包含"金额"只读列，位于数量列之后
2. THE 金额列 SHALL 按公式 `x_bj_dj * (1 + x_fdds / 100) * x_sl` 自动计算显示，其中 x_bj_dj 为单价、x_fdds 为浮动点数、x_sl 为数量
3. WHEN 单价（x_bj_dj）、浮动点数（x_fdds）或数量（x_sl）任一值发生变化时，THE 前端 SHALL 立即重新计算并更新该行的金额列
4. IF x_bj_dj、x_fdds 或 x_sl 中任一值为 null 或空，THEN THE 系统 SHALL 将该值视为 0 参与计算
5. THE 金额列 SHALL 为只读状态，使用浅蓝色背景区分于可编辑列，显示格式保留 2 位小数

### Requirement 12: 元件数据全量保存

**User Story:** 作为报价员，我希望在页面上对控制柜和元件的增删改操作能正确保存到数据库，以便数据始终与页面一致。

#### Acceptance Criteria

1. THE 填价页面 SHALL 将加载到页面中的元件数据与数据库完全独立管理，用户在页面上的增删改操作仅影响前端内存数据
2. WHEN 报价员点击保存按钮时，THE 系统 SHALL 在一个数据库事务中执行以下操作：先删除该方案（fabh）下 BJB 表中所有现有元件记录，再批量插入页面中的最新完整元件清单
3. THE 系统 SHALL 在插入时按页面中的顺序重新生成元件编号（x_bm），确保编号连续递增
4. IF 删除或插入过程中发生错误，THEN THE 系统 SHALL 回滚整个事务，保留数据库中的原始数据不变，并返回错误信息
5. WHEN 保存成功后，THE 系统 SHALL 返回成功信息并在信息栏显示"保存成功"提示
6. THE 保存操作 SHALL 同时对每个元件行计算并写入 x_wzdh（NormalizeSpec 处理 x_ggxh）和 x_dj（公式 x_bj_dj * (1 + x_fdds / 100)）字段

### Requirement 13: 控制柜和元件编辑

**User Story:** 作为报价员，我希望能在填价页面上直接增加、修改和删除控制柜及其内的元件，以便灵活调整报价方案内容。

#### Acceptance Criteria

1. THE 填价页面 SHALL 支持在左侧目录树中新增控制柜节点，新增时自动分配下一个可用的 4 位编码
2. THE 填价页面 SHALL 支持在左侧目录树中删除控制柜节点；WHEN 删除控制柜时，THE 系统 SHALL 同时删除该控制柜下的所有元件行（仅从前端内存中移除，保存时才写入数据库）
3. THE 填价页面 SHALL 支持在当前选中的控制柜下新增元件行（在表格末尾追加空行）
4. THE 填价页面 SHALL 支持删除表格中选中的元件行
5. THE 填价页面 SHALL 支持对元件行的名称、规格型号、单位、数量、单价、浮动点数、厂商等字段进行编辑
6. WHEN 用户进行增删改操作后未保存时，THE 系统 SHALL 在页面标题或信息栏显示"未保存"状态标记，提醒用户保存

### Requirement 14: 控制柜拖拽排序

**User Story:** 作为报价员，我希望能通过拖拽调整控制柜的顺序，以便按照实际需要重新排列报价方案中的控制柜。

#### Acceptance Criteria

1. THE 左侧目录树 SHALL 支持通过鼠标拖拽控制柜节点来调整控制柜之间的顺序
2. WHEN 拖拽完成后，THE 系统 SHALL 更新前端内存中所有控制柜及其下属元件的编号（x_bm），确保编号按新顺序连续递增
3. THE 拖拽操作 SHALL 仅允许同级节点之间的排序调整，不允许将控制柜拖入另一个控制柜内部
4. WHEN 拖拽排序后，THE 系统 SHALL 在页面标记"未保存"状态，排序结果在用户点击保存后才写入数据库
5. THE 拖拽过程中 SHALL 显示视觉反馈（如拖拽占位符和插入位置指示线），帮助用户确认放置位置

### Requirement 15: 根节点元件汇总视图

**User Story:** 作为报价员，我希望点击目录树根节点时能看到所有元件的汇总信息，以便快速了解整个报价方案的元件概况和总金额。

#### Acceptance Criteria

1. WHEN 报价员点击左侧目录树的根节点时，THE 系统 SHALL 在右侧表格区域显示所有元件的汇总视图（只读）
2. THE 汇总视图 SHALL 按元件名称（x_mc）、规格型号（x_ggxh）、单价（x_dj）进行分组，显示每组的合计数量（SUM(x_sl)）
3. THE 汇总视图 SHALL 显示以下列：元件名称、规格型号、单价、合计数量、金额小计（单价 * 合计数量）
4. THE 汇总视图 SHALL 在表格底部显示总金额合计行
5. THE 汇总视图 SHALL 为只读状态，所有单元格不可编辑
6. WHEN 报价员从根节点切换到某个控制柜节点时，THE 系统 SHALL 切换回该控制柜的可编辑元件明细视图

### Requirement 16: 填价页面布局与状态提示

**User Story:** 作为报价员，我希望填价页面有清晰的当前节点信息、合计金额和颜色含义说明，且页面留给表格的显示面积尽量大，以便高效地完成报价工作。

#### Acceptance Criteria

1. THE 填价页面 SHALL 在右侧表格区域上方设置"当前节点状态栏"，显示当前查看的节点名称（柜体视图：控制柜名+编号；根节点视图：项目名+"项目汇总"标记；未选择时：占位文字）
2. THE 填价页面 SHALL 在左侧目录树中对用户主动选中的节点使用持久的高亮样式（`tree-node-link-selected`，强对比背景），与"元件使用控制柜"的临时提示高亮（`tree-node-link-usage`）使用不同的 CSS 类，互不覆盖
3. THE 当前节点状态栏 SHALL 同时显示该节点下所有元件的"合计金额"（保留 2 位小数，前缀 ¥）；金额计算口径为：柜体视图累加当前表格的"金额"列；根节点汇总视图累加"金额小计"列（不含合计自身行）；金额随单价、浮动点数、数量、增删行、自动填价等操作实时刷新
4. THE 填价页面 SHALL 在顶部工具栏的颜色图例区（仅柜体视图显示）展示以下色块及含义：浅灰=单价为空/0、红色=单价为负、橙色=单价偏离均价±20%、浅绿=参考价格列、浅蓝=金额列
5. THE 填价页面 SHALL 移除独立的标题栏，将"报价单号""未保存"提示和"返回列表"按钮整合到唯一的工具栏中，以释放表格垂直显示空间
6. WHEN 报价员从一个控制柜切换到另一个，OR 进入根节点汇总视图时，THE 系统 SHALL 同步更新当前节点状态栏的节点名称与合计金额；并将持久选中高亮迁移到新节点

### Requirement 17: 元件使用控制柜的匹配口径

**User Story:** 作为报价员，我希望点击右侧表格中的某个元件时，下方能正确显示该元件被哪些控制柜使用，以便核对元件去重与跨柜复用情况。

#### Acceptance Criteria

1. WHEN 报价员在柜体视图中选中某一元件行，THE 系统 SHALL 以该行的**标准化指纹（x_wzdh）**作为唯一识别字段，查询本报价单（fabh）下使用同一型号的所有控制柜
2. THE 元件使用匹配 SHALL **仅按 x_wzdh 比对**，不参与 价格（x_bj_dj / x_dj）、浮动率（x_fdds）、厂家（x_sccj）、单位（x_dw） 等字段的比较——同一型号在不同控制柜中可能出现不同定价或来自不同厂家，仍应被识别为同一元件
3. THE 元件使用匹配 SHALL 与历史价格匹配（STD_PRICE_HISTORY）使用完全相同的指纹口径，确保"被使用的柜"与"参考价命中的元件"语义一致
4. IF 元件行 DB 中 x_wzdh 字段为空，THEN THE 系统 SHALL 使用 NormalizeSpec(x_ggxh) 实时计算指纹作为兜底，与 GetCabinetComponents 的兜底逻辑一致
5. THE 匹配结果 SHALL 按控制柜编码（x_bm 前 4 位）分组，列出每个使用控制柜的：编码、名称、合计数量（SUM(x_sl)）、单价区间（同柜下该型号的最小/最大 x_bj_dj）、厂家集合（去重并按字典序排列）
6. IF 当前元件行的 x_wzdh 为空（即 x_ggxh 也为空、NormalizeSpec 后仍为空），THEN THE 系统 SHALL **拒绝统计使用情况**，在提示面板中明确显示"该元件未填规格型号，无法识别使用情况"——型号未填则不存在"是不是同一元件"的判断依据
7. IF 经过 wzdh 匹配后没有任何控制柜（包含当前柜）使用该型号，THEN THE 系统 SHALL 在提示面板中显示"未找到使用该元件的控制柜"
8. THE 元件使用提示面板 SHALL 在左侧目录树中以"`tree-node-link-usage`"样式高亮被匹配到的控制柜节点，且不覆盖当前用户已选中的节点（`tree-node-link-selected`）

