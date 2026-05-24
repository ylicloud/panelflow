# Implementation Plan: 指纹别名匹配 (spec-alias-matching)

## Overview

在现有自动填价系统中引入二级匹配机制：当元件指纹在 STD_PRICE_HISTORY 中无直接匹配时，通过 STD_SPEC_ALIAS 别名表查找标准指纹再获取历史价格。实现顺序遵循 Model → Service → Controller → View → JS/CSS 架构分层。

## Tasks

- [ ] 1. 数据库与实体层搭建
  - [ ] 1.1 创建 STD_SPEC_ALIAS 建表 SQL 脚本
    - 在 `Docs/` 目录下创建 `create-std-spec-alias.sql`，包含建表语句、唯一索引（alias_wzdh）和非唯一索引（canonical_wzdh）
    - 字段定义严格按照设计文档：id INT IDENTITY, alias_wzdh NVARCHAR(400) NOT NULL, canonical_wzdh NVARCHAR(400) NOT NULL, created_by NVARCHAR(50), created_at DATETIME DEFAULT GETDATE(), remark NVARCHAR(200)
    - _Requirements: 1.1, 1.2, 1.3_

  - [ ] 1.2 创建 StdSpecAlias 实体类和 EF Core 配置
    - 在 `PanelFlow.Infrastructure/Entities/` 下创建 `StdSpecAlias.cs` 实体类
    - 在 `ApplicationDbContext.OnModelCreating` 中添加 EF Core 配置（表映射、索引、字段类型）
    - 添加 `DbSet<StdSpecAlias>` 到 `ApplicationDbContext`
    - _Requirements: 1.1, 1.2, 1.3_

  - [ ] 1.3 创建 DTO 模型类
    - 在 `PanelFlow.Core/Models/` 下创建：`PriceMatchResult.cs`、`PriceSearchResult.cs`、`UnmappedFingerprintDto.cs`、`SpecAliasDto.cs`、`BatchCreateResult.cs`
    - 所有 DTO 按设计文档中的字段定义
    - _Requirements: 2.7, 4.3, 7.2, 8.6, 8.5_

- [ ] 2. Service 层实现
  - [ ] 2.1 创建 ISpecAliasService 接口
    - 在 `PanelFlow.Core/Interfaces/` 下创建 `ISpecAliasService.cs`
    - 定义所有方法签名：ResolvePricesWithFallbackAsync、CreateAliasAsync、SearchPriceHistoryAsync、GetUnmappedFingerprintsAsync、GetAliasListAsync、DeleteAliasAsync、BatchCreateAliasAsync
    - _Requirements: 2.1, 2.2, 4.2, 5.1, 7.1, 8.4, 10.1, 10.4_

  - [ ] 2.2 实现 SpecAliasService — 二级匹配逻辑 (ResolvePricesWithFallbackAsync)
    - 在 `PanelFlow.Infrastructure/Services/` 下创建 `SpecAliasService.cs`
    - 实现批量二级匹配：先查 STD_PRICE_HISTORY 直接匹配，未命中的通过 STD_SPEC_ALIAS 查 canonical_wzdh，再批量查 STD_PRICE_HISTORY
    - 使用 IN 批量查询避免 N+1，直接匹配优先于别名匹配
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.6, 2.7_

  - [ ]* 2.3 编写属性测试 — 二级匹配正确性 (Property 1)
    - **Property 1: Two-level matching correctness**
    - **Validates: Requirements 2.1, 2.2, 2.3, 2.4**
    - 在 `PanelFlow.Web.Tests/` 下创建 `SpecAliasServicePropertyTests.cs`
    - 使用 FsCheck 生成随机指纹集合，验证直接匹配、别名匹配、无匹配三种情况的返回正确性

  - [ ]* 2.4 编写属性测试 — 直接匹配优先 (Property 2)
    - **Property 2: Direct match takes priority over alias**
    - **Validates: Requirements 2.3**
    - 验证当指纹同时存在于 STD_PRICE_HISTORY 和 STD_SPEC_ALIAS 时，返回直接匹配价格

  - [ ] 2.5 实现 SpecAliasService — 别名创建逻辑 (CreateAliasAsync)
    - 实现所有验证规则：空值检查、自引用检查、canonical_wzdh 存在性检查、alias_wzdh 重复检查、alias_wzdh 已有直接价格检查
    - 验证通过后插入 STD_SPEC_ALIAS 并返回匹配到的价格
    - _Requirements: 1.4, 1.5, 1.6, 1.7, 1.8, 5.1, 5.2, 5.3, 5.4, 5.7_

  - [ ]* 2.6 编写属性测试 — 别名创建验证规则 (Property 3, 4, 5, 6)
    - **Property 3: Alias creation validation — duplicate rejection**
    - **Property 4: Alias creation validation — canonical must exist in price history**
    - **Property 5: Alias creation validation — whitespace and self-reference rejection**
    - **Property 6: Alias creation validation — alias already has direct price**
    - **Validates: Requirements 1.4, 1.5, 1.6, 1.7, 1.8, 5.2, 5.3, 5.4**

  - [ ] 2.7 实现 SpecAliasService — 搜索与管理方法
    - 实现 SearchPriceHistoryAsync：按 ggxh/x_mc 模糊匹配，avg_count 降序，最多 50 条
    - 实现 GetUnmappedFingerprintsAsync：查询 BJB 中未映射指纹，支持分页和关键词筛选
    - 实现 GetAliasListAsync：分页查询已创建别名列表
    - 实现 DeleteAliasAsync：删除别名记录
    - 实现 BatchCreateAliasAsync：批量创建，跳过失败项，返回统计
    - _Requirements: 4.2, 4.4, 7.1, 7.2, 7.3, 7.5, 8.4, 8.5, 8.6, 8.7, 9.1, 9.2, 9.3, 9.4, 10.1, 10.2, 10.3, 10.4, 10.5_

  - [ ]* 2.8 编写属性测试 — 搜索与未映射查询 (Property 7, 8)
    - **Property 7: Search results correctness**
    - **Property 8: Unmapped fingerprint identification**
    - **Validates: Requirements 4.2, 4.4, 7.1, 7.3, 9.1, 9.3, 10.1**

  - [ ]* 2.9 编写属性测试 — 批量创建 (Property 9)
    - **Property 9: Batch assignment partial failure handling**
    - **Validates: Requirements 8.4, 8.5**

  - [ ] 2.10 注册 DI 服务
    - 在 `Program.cs` 中注册 `ISpecAliasService` → `SpecAliasService`
    - _Requirements: 2.5_

- [ ] 3. Checkpoint — 确保 Service 层测试通过
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 4. Controller 层实现
  - [ ] 4.1 创建 SpecAliasController
    - 在 `PanelFlow.Web/Controllers/` 下创建 `SpecAliasController.cs`
    - 实现路由 `[Route("SpecAlias")]`
    - 实现 POST /SpecAlias/Create — 创建别名（含权限校验、ValidateAntiForgeryToken、返回价格信息）
    - 实现 GET /SpecAlias/Search — 搜索历史价格
    - 实现 POST /SpecAlias/Delete — 删除别名（仅管理员）
    - 实现 POST /SpecAlias/BatchCreate — 批量创建别名（仅管理员）
    - 实现 GET /SpecAlias/UnmappedList — 未映射指纹列表 API
    - 实现 GET /SpecAlias/AliasList — 已创建别名列表 API
    - 实现 GET /SpecAlias/Admin — 别名管理页面入口（仅管理员）
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 7.4, 8.7, 9.1, 9.5, 9.6, 10.1, 10.3, 10.4, 10.5, 10.6_

  - [ ] 4.2 修改 QuotationController — 集成二级匹配
    - 在 QuotationController 构造函数中注入 `ISpecAliasService`
    - 修改 `GetReferencePrice` 方法：在直接查询 STD_PRICE_HISTORY 后，对未命中的指纹调用 `ResolvePricesWithFallbackAsync`，合并结果
    - 修改 `AutoFillPriceFromHistory` 方法：同样集成二级匹配逻辑
    - 确保两个方法使用相同的匹配逻辑和返回格式
    - _Requirements: 2.5, 2.6, 2.7, 6.1, 6.2, 6.3_

  - [ ]* 4.3 编写属性测试 — 匹配结果一致性 (Property 10)
    - **Property 10: Matching result consistency**
    - **Validates: Requirements 2.5, 2.7**
    - 验证 AutoFillPriceFromHistory 和 GetReferencePrice 对相同指纹返回相同价格

- [ ] 5. Checkpoint — 确保 Controller 层编译通过
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 6. 前端实现 — 填价页面别名关联
  - [ ] 6.1 修改 FillPrice.cshtml — 添加搜索对话框 HTML 结构
    - 在填价页面底部添加模态对话框 HTML（搜索输入框、结果列表区域、加载指示器、错误信息区域）
    - 对话框支持 Escape 键和遮罩层点击关闭
    - _Requirements: 4.1, 4.8_

  - [ ] 6.2 创建 fill-price-alias.js — 别名关联交互逻辑
    - 在 `wwwroot/js/` 下创建 `fill-price-alias.js`
    - 实现"关联"按钮渲染逻辑：参考价格为空且 x_wzdh 非空时显示按钮
    - 实现按钮点击打开搜索对话框，预填 x_mc + x_ggxh 作为搜索关键词
    - 实现搜索请求（调用 GET /SpecAlias/Search）、加载状态、结果渲染
    - 实现结果行点击创建别名（调用 POST /SpecAlias/Create）
    - 创建成功后更新参考价格列、移除关联按钮
    - 创建失败时保留对话框并显示错误信息
    - 权限控制：非报价人且非管理员时不显示关联按钮
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 4.2, 4.3, 4.5, 4.6, 4.7, 4.9_

  - [ ] 6.3 创建 fill-price-alias.css — 搜索对话框样式
    - 在 `wwwroot/css/` 下创建 `fill-price-alias.css`
    - 模态对话框样式、搜索结果列表样式、关联按钮样式、加载指示器样式
    - 使用 `oa-` 前缀命名类名
    - 确保移动端适配（无横向滚动、safe-area-inset 适配）
    - _Requirements: 3.1, 4.1, 4.9_

- [ ] 7. 前端实现 — 别名管理页面
  - [ ] 7.1 创建 Admin.cshtml — 别名管理页面视图
    - 在 `PanelFlow.Web/Views/SpecAlias/` 下创建 `Admin.cshtml`
    - 使用强类型 @model，包含两个 Tab：未映射指纹列表、已创建别名列表
    - 未映射列表：显示指纹值、规格型号样本、名称样本、出现次数，支持分页和关键词筛选
    - 已创建别名列表：显示变体指纹、标准指纹、规格型号、创建人、创建时间，支持分页和删除
    - 未映射列表每行显示"指派"按钮，支持多选和"批量指派"按钮
    - 复用填价页面的搜索对话框组件
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7_

  - [ ] 7.2 创建 alias-admin.js — 管理页面交互逻辑
    - 在 `wwwroot/js/` 下创建 `alias-admin.js`
    - 实现未映射列表加载（调用 GET /SpecAlias/UnmappedList）、分页、筛选
    - 实现已创建别名列表加载（调用 GET /SpecAlias/AliasList）、分页
    - 实现单条指派（打开搜索对话框 → 选择 → 创建别名 → 从列表移除）
    - 实现多选 + 批量指派（调用 POST /SpecAlias/BatchCreate）
    - 实现删除别名（调用 POST /SpecAlias/Delete）
    - _Requirements: 7.3, 7.5, 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7_

  - [ ] 7.3 创建 alias-admin.css — 管理页面样式
    - 在 `wwwroot/css/` 下创建 `alias-admin.css`
    - Tab 切换样式、列表表格样式、分页控件样式、多选复选框样式
    - 使用 `oa-` 前缀，移动端适配
    - _Requirements: 7.2, 7.3, 8.6_

- [ ] 8. Final checkpoint — 确保所有测试通过
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties defined in the design document
- Unit tests validate specific examples and edge cases
- 实现顺序遵循项目规范：Model → Service → Controller → View → JS/CSS
- Service 层使用 EF Core InMemory provider 进行属性测试
- 前端 JavaScript 禁止内联在 .cshtml 中，必须放在 wwwroot/js/ 目录
- 所有写操作必须启用 ValidateAntiForgeryToken

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.3"] },
    { "id": 1, "tasks": ["1.2"] },
    { "id": 2, "tasks": ["2.1"] },
    { "id": 3, "tasks": ["2.2", "2.5", "2.7"] },
    { "id": 4, "tasks": ["2.3", "2.4", "2.6", "2.8", "2.9", "2.10"] },
    { "id": 5, "tasks": ["4.1", "4.2"] },
    { "id": 6, "tasks": ["4.3"] },
    { "id": 7, "tasks": ["6.1", "6.2", "6.3", "7.1", "7.2", "7.3"] }
  ]
}
```
