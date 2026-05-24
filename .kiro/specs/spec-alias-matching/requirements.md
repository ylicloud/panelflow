# Requirements Document

## Introduction

指纹别名匹配功能是对现有自动填价功能的增强。当前系统通过 NormalizeSpec 函数将元件规格型号（x_ggxh）标准化为指纹（x_wzdh），再用指纹查询 STD_PRICE_HISTORY 获取历史价格。然而，同一物理元件可能被不同用户以不同方式书写（如 "ic65n c 2a/2p"、"ic65n 2p 2a"、"i-c65n c2 2p"），经 NormalizeSpec 处理后产生不同的指纹，导致自动填价无法匹配。

本功能通过引入别名表（STD_SPEC_ALIAS），将变体指纹映射到标准指纹，实现"精确匹配失败 → 别名回退查找"的二级匹配机制。同时提供 UI 让报价员在填价页面手动创建别名关联，以及管理员批量管理别名的后台页面。

## Glossary

- **标准化指纹（x_wzdh）**：通过 NormalizeSpec（C#）或 F_CleanString（SQL）对规格型号进行标准化处理后的字符串，用于跨报价单匹配同一元件
- **变体指纹（alias_wzdh）**：经 NormalizeSpec 处理后产生的、与标准指纹不同但实际代表同一物理元件的指纹字符串
- **标准指纹（canonical_wzdh）**：在 STD_PRICE_HISTORY 表中已存在的、被选定为标准参照的指纹字符串
- **别名表（STD_SPEC_ALIAS）**：存储变体指纹到标准指纹映射关系的数据库表
- **别名映射（Alias Mapping）**：一条从 alias_wzdh 到 canonical_wzdh 的关联记录
- **填价页面（FillPrice Page）**：报价单的"填写报价"子页面，包含目录树和 Handsontable 表格
- **参考价格（Reference Price）**：从 STD_PRICE_HISTORY 中查询到的历史价格信息
- **关联按钮（Link Button）**：填价页面中参考价格为空的元件行上显示的操作按钮，用于手动创建别名映射
- **搜索对话框（Search Dialog）**：点击关联按钮后弹出的模态窗口，用于搜索 STD_PRICE_HISTORY 并选择目标标准指纹
- **未映射指纹（Unmapped Fingerprint）**：在 BJB 表中出现但在 STD_PRICE_HISTORY 和 STD_SPEC_ALIAS 中均无匹配记录的指纹
- **别名管理页面（Alias Admin Page）**：管理员使用的后台页面，用于批量查看和管理别名映射

## Requirements

### Requirement 1: 别名表数据结构

**User Story:** 作为系统管理员，我希望系统有一张别名映射表，以便将不同写法的元件指纹关联到同一标准指纹，解决自动填价匹配失败的问题。

#### Acceptance Criteria

1. THE STD_SPEC_ALIAS 表 SHALL 包含以下字段：自增主键（id）、变体指纹（alias_wzdh, NVARCHAR(400), NOT NULL）、标准指纹（canonical_wzdh, NVARCHAR(400), NOT NULL）、创建人（created_by, NVARCHAR(50)）、创建时间（created_at, DATETIME, DEFAULT GETDATE()）、备注（remark, NVARCHAR(200)）
2. THE STD_SPEC_ALIAS 表 SHALL 在 alias_wzdh 列上建立唯一索引，确保每个变体指纹仅映射到一个标准指纹
3. THE STD_SPEC_ALIAS 表 SHALL 在 canonical_wzdh 列上建立非唯一索引，以支持按标准指纹查询所有关联的变体指纹
4. IF 插入的 alias_wzdh 与已有记录重复，THEN THE 系统 SHALL 拒绝插入并返回错误信息指明该变体指纹已存在映射，原有记录保持不变
5. IF canonical_wzdh 在 STD_PRICE_HISTORY 表中不存在对应的 x_wzdh 值，THEN THE 系统 SHALL 通过应用层校验拒绝创建该别名映射并返回错误信息指明目标标准指纹在历史价格表中不存在
6. IF 插入的 alias_wzdh 或 canonical_wzdh 为空字符串或仅含空白字符，THEN THE 系统 SHALL 拒绝创建并返回错误信息指明指纹值不能为空
7. IF 插入的 alias_wzdh 与 canonical_wzdh 值相同，THEN THE 系统 SHALL 拒绝创建并返回错误信息指明变体指纹不能与标准指纹相同
8. IF 插入的 alias_wzdh 已存在于 STD_PRICE_HISTORY 表的 x_wzdh 中（即该指纹已有直接匹配的历史价格），THEN THE 系统 SHALL 拒绝创建并返回错误信息指明该指纹已有直接价格记录无需创建别名

### Requirement 2: 二级匹配逻辑

**User Story:** 作为报价员，我希望系统在精确匹配失败时能通过别名表回退查找历史价格，以便更多元件能自动获取参考价格。

#### Acceptance Criteria

1. WHEN 系统使用 x_wzdh 查询 STD_PRICE_HISTORY 未找到匹配记录时，THE 系统 SHALL 使用该 x_wzdh 作为 alias_wzdh 查询 STD_SPEC_ALIAS 表获取对应的 canonical_wzdh
2. WHEN STD_SPEC_ALIAS 查询返回 canonical_wzdh 时，THE 系统 SHALL 使用该 canonical_wzdh 再次查询 STD_PRICE_HISTORY 获取历史价格；IF 该 canonical_wzdh 在 STD_PRICE_HISTORY 中不存在（记录已被删除或刷新移除），THEN THE 系统 SHALL 将该元件视为无匹配，返回空结果
3. IF x_wzdh 在 STD_PRICE_HISTORY 中直接匹配成功，THEN THE 系统 SHALL 使用直接匹配结果，不查询 STD_SPEC_ALIAS 表
4. IF x_wzdh 在 STD_PRICE_HISTORY 和 STD_SPEC_ALIAS 中均无匹配记录，THEN THE 系统 SHALL 返回空结果（无参考价格）
5. THE 二级匹配逻辑 SHALL 同时应用于以下场景：自动填价批量查询（AutoFillPriceFromHistory）、参考价格列加载（GetReferencePrice），两个场景使用相同的匹配顺序（先直接匹配，再别名回退）和相同的返回字段（last_price、avg_price）
6. THE 批量查询 SHALL 将所有未直接匹配的 x_wzdh 集合通过一次 IN 查询获取 STD_SPEC_ALIAS 中的 canonical_wzdh 映射，再将获取的 canonical_wzdh 集合通过一次 IN 查询获取 STD_PRICE_HISTORY 中的历史价格，单次批量查询的元件数量上限为当前报价单 BJB 中 x_lx=11 且 x_wzdh 非空的全部行数
7. THE 二级匹配结果 SHALL 与直接匹配结果返回相同的数据结构（包含 last_price、avg_price），调用方无需区分价格来源是直接匹配还是别名匹配

### Requirement 3: 填价页面关联按钮

**User Story:** 作为报价员，我希望在填价表格中看到哪些元件没有参考价格，并能快速为其创建别名关联，以便下次自动匹配。

#### Acceptance Criteria

1. WHEN 填价表格中某元件行的参考价格列为空（无直接匹配且无别名匹配）且该行 x_wzdh 非空时，THE 前端 SHALL 在该行参考价格单元格中显示"关联"按钮
2. IF 元件行的 x_wzdh 为空或 NULL，THEN THE 前端 SHALL 不显示"关联"按钮（因为无有效指纹无法创建映射）
3. WHEN 报价员点击"关联"按钮时，THE 系统 SHALL 打开搜索对话框，并将当前元件的名称（x_mc）与规格型号（x_ggxh）以空格拼接为一个字符串预填入搜索框作为默认搜索关键词
4. IF 当前用户非报价单报价人且非管理员，THEN THE 前端 SHALL 不显示"关联"按钮
5. WHEN 别名创建成功后，THE 前端 SHALL 将该行的参考价格列更新为匹配到的历史最新价格（last_price）并移除"关联"按钮
6. IF 别名创建请求失败（网络错误或服务端返回错误），THEN THE 前端 SHALL 保留"关联"按钮不变，并在搜索对话框内显示错误信息

### Requirement 4: 搜索对话框

**User Story:** 作为报价员，我希望通过搜索对话框能快速找到目标元件的历史价格记录，并选择其作为别名映射的目标。

#### Acceptance Criteria

1. THE 搜索对话框 SHALL 包含一个搜索输入框和一个结果列表区域，以模态窗口形式显示
2. WHEN 报价员输入关键词并触发搜索时，THE 系统 SHALL 在 STD_PRICE_HISTORY 表中按 ggxh（原始规格型号）、x_mc（元件名称）字段进行模糊匹配（LIKE '%keyword%'），返回匹配结果
3. THE 搜索结果列表 SHALL 显示以下信息：元件名称（x_mc）、原始规格型号（ggxh）、最新价格（last_price）、均价（avg_price）、厂商（x_sccj）、样本数（avg_count）
4. THE 搜索结果 SHALL 按样本数（avg_count）降序排列，最多返回 50 条记录
5. WHEN 报价员点击搜索结果中的某一行时，THE 系统 SHALL 将当前元件的 x_wzdh 作为 alias_wzdh、选中行的 x_wzdh 作为 canonical_wzdh 创建别名映射
6. IF 创建别名映射成功，THEN THE 搜索对话框 SHALL 关闭，并在信息栏显示"别名关联创建成功"提示
7. IF 创建别名映射失败（如 alias_wzdh 已存在映射），THEN THE 搜索对话框 SHALL 保持打开状态，并在对话框内显示错误信息
8. THE 搜索对话框 SHALL 支持按 Escape 键或点击遮罩层关闭，关闭时不创建任何映射
9. WHILE 搜索请求正在执行时，THE 搜索对话框 SHALL 显示加载指示器并禁用搜索按钮

### Requirement 5: 别名创建接口

**User Story:** 作为系统开发者，我希望有一个安全的后端接口用于创建别名映射，以确保数据完整性和权限控制。

#### Acceptance Criteria

1. THE 别名创建接口 SHALL 接收以下参数：alias_wzdh（变体指纹）、canonical_wzdh（标准指纹）、remark（备注，可选）
2. WHEN 接收到创建请求时，THE 系统 SHALL 校验 canonical_wzdh 在 STD_PRICE_HISTORY 表中存在；IF 不存在，THEN THE 系统 SHALL 返回 400 状态码及错误信息"目标标准指纹在历史价格表中不存在"
3. WHEN 接收到创建请求时，THE 系统 SHALL 校验 alias_wzdh 不等于 canonical_wzdh；IF 相等，THEN THE 系统 SHALL 返回 400 状态码及错误信息"变体指纹不能与标准指纹相同"
4. WHEN 接收到创建请求时，THE 系统 SHALL 校验 alias_wzdh 在 STD_SPEC_ALIAS 表中不存在；IF 已存在，THEN THE 系统 SHALL 返回 409 状态码及错误信息"该变体指纹已存在别名映射"
5. THE 别名创建接口 SHALL 要求用户已登录且具有报价权限（报价单报价人本人或管理员角色）
6. THE 别名创建接口 SHALL 启用防 CSRF 保护（ValidateAntiForgeryToken）
7. WHEN 别名映射创建成功时，THE 系统 SHALL 将当前登录用户名写入 created_by 字段，并返回创建成功的响应及匹配到的历史价格信息（last_price、avg_price）

### Requirement 6: 别名全局生效

**User Story:** 作为报价员，我希望创建的别名映射能全局生效，以便任何报价单中出现相同变体指纹的元件都能自动匹配到历史价格。

#### Acceptance Criteria

1. WHEN 别名映射创建成功后，THE 系统 SHALL 在后续所有报价单的自动填价和参考价格查询中使用该映射
2. THE 别名映射 SHALL 不限于创建时所在的报价单，任何报价单中 x_wzdh 等于 alias_wzdh 的元件行均可通过该映射获取历史价格
3. WHEN 新的报价单执行自动填价时，THE 系统 SHALL 同时使用直接匹配和别名匹配两种方式查找历史价格
4. IF STD_PRICE_HISTORY 中的标准指纹记录被 SP_RefreshPriceHistory 更新（价格变化），THEN THE 通过别名映射关联的元件 SHALL 在下次查询时自动获取更新后的价格

### Requirement 7: 别名管理页面 - 未映射指纹列表

**User Story:** 作为管理员，我希望能看到所有未映射的指纹及其出现频率，以便优先处理高频出现的变体指纹，提高整体匹配率。

#### Acceptance Criteria

1. THE 别名管理页面 SHALL 显示所有未映射指纹的列表，未映射指纹定义为：在 BJB 表中出现（x_lx=11, x_wzdh 非空）但在 STD_PRICE_HISTORY 和 STD_SPEC_ALIAS（alias_wzdh）中均无匹配记录的 x_wzdh
2. THE 未映射指纹列表 SHALL 显示以下信息：指纹值（x_wzdh）、原始规格型号样本（取任一对应的 x_ggxh）、元件名称样本（取任一对应的 x_mc）、出现次数（在 BJB 中的记录数）
3. THE 未映射指纹列表 SHALL 按出现次数降序排列，支持分页显示（每页 50 条）
4. THE 别名管理页面 SHALL 仅管理员角色可访问；IF 非管理员用户访问，THEN THE 系统 SHALL 返回 403 状态码或重定向至无权限提示页面
5. THE 未映射指纹列表 SHALL 支持按元件名称或规格型号关键词筛选

### Requirement 8: 别名管理页面 - 批量指派

**User Story:** 作为管理员，我希望能在管理页面中为未映射指纹批量创建别名映射，以便高效地提升系统匹配率。

#### Acceptance Criteria

1. THE 别名管理页面 SHALL 在每行未映射指纹旁显示"指派"按钮，点击后打开与填价页面相同的搜索对话框
2. WHEN 管理员通过搜索对话框选择目标标准指纹后，THE 系统 SHALL 创建别名映射（alias_wzdh = 当前行指纹，canonical_wzdh = 选中目标的 x_wzdh）
3. WHEN 别名映射创建成功后，THE 系统 SHALL 将该行从未映射列表中移除（或标记为已映射）
4. THE 别名管理页面 SHALL 支持多选未映射指纹行，并提供"批量指派"功能，将多个变体指纹映射到同一个标准指纹
5. WHEN 批量指派时，THE 系统 SHALL 对每个选中的变体指纹逐一创建别名映射；IF 某条创建失败，THEN THE 系统 SHALL 跳过该条并继续处理剩余项，最终返回成功数和失败数的统计
6. THE 别名管理页面 SHALL 显示已创建的别名映射列表（支持分页），包含：变体指纹、标准指纹、对应的原始规格型号、创建人、创建时间
7. THE 已创建别名列表 SHALL 支持删除操作；WHEN 管理员删除某条别名映射时，THE 系统 SHALL 从 STD_SPEC_ALIAS 表中移除该记录

### Requirement 9: 别名搜索接口

**User Story:** 作为系统开发者，我希望有一个后端接口用于搜索 STD_PRICE_HISTORY，以支持搜索对话框的数据查询需求。

#### Acceptance Criteria

1. THE 搜索接口 SHALL 接收关键词参数（keyword），在 STD_PRICE_HISTORY 表中按 ggxh 和 x_mc 字段进行模糊匹配
2. THE 搜索接口 SHALL 返回匹配结果列表，每条记录包含：x_wzdh、x_mc、ggxh、last_price、avg_price、x_sccj、avg_count
3. THE 搜索结果 SHALL 按 avg_count 降序排列，最多返回 50 条记录
4. IF 关键词为空或仅含空白字符，THEN THE 搜索接口 SHALL 返回空结果列表
5. THE 搜索接口 SHALL 要求用户已登录；IF 未登录，THEN THE 系统 SHALL 返回 401 状态码
6. THE 搜索接口 SHALL 对关键词进行 SQL 注入防护，使用参数化查询

### Requirement 10: 别名管理数据接口

**User Story:** 作为系统开发者，我希望有后端接口支持别名管理页面的数据查询和操作需求。

#### Acceptance Criteria

1. THE 未映射指纹查询接口 SHALL 返回分页结果，包含：指纹值、原始规格型号样本、元件名称样本、出现次数，按出现次数降序排列
2. THE 未映射指纹查询接口 SHALL 支持按关键词筛选（匹配 x_mc 或 x_ggxh）
3. THE 已创建别名查询接口 SHALL 返回分页结果，包含：id、alias_wzdh、canonical_wzdh、对应的原始规格型号（从 STD_PRICE_HISTORY 获取 ggxh）、创建人、创建时间
4. THE 别名删除接口 SHALL 接收别名记录 id，从 STD_SPEC_ALIAS 表中删除对应记录
5. IF 删除的别名记录不存在，THEN THE 系统 SHALL 返回 404 状态码
6. THE 别名管理相关接口 SHALL 仅管理员角色可访问；IF 非管理员用户请求，THEN THE 系统 SHALL 返回 403 状态码

