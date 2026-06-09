# Implementation Plan: quotation-element-maintenance

## Overview

按三阶段增量实现。Phase 1 = 通用项字典主数据 + 导入去硬编码；Phase 2 = 报价单结构维护（TreeReencode）；Phase 3（低优先）= 第 1 级通用项接入。汇总报表不在本规格。

所有写操作：异步 + 事务 + 防伪 + 审计；不改历史表结构。

---

## Phase 1：通用项字典 + 导入去硬编码

- [ ] 1. 数据库建表与种子
  - 新建 `Docs/create-element-dict.sql`：`STD_ELEMENT_DICT` 建表 + 索引 + 第 2 级 8 类种子（前 5 类 `IsDefaultOnImport=1`，器件 `IsLocked=1`）
  - _Requirements: 1, 7, 9_

- [ ] 2. 实体与 DbContext 映射
  - 新增 `PanelFlow.Infrastructure/Entities/StdElementDict.cs`
  - `ApplicationDbContext`：注册 `DbSet<StdElementDict>` + `OnModelCreating` 映射
  - _Requirements: 1_

- [ ] 3. DTO 与服务接口
  - 新增 `PanelFlow.Core/Models/ElementDictDto.cs`（含 `DataAnnotations`）
  - 新增 `PanelFlow.Core/Interfaces/IElementDictService.cs`
  - _Requirements: 1, 2_

- [ ] 4. 字典服务实现
  - 新增 `PanelFlow.Infrastructure/Services/ElementDictService.cs`：`GetByLevelAsync`/`CreateAsync`/`UpdateAsync`/`ToggleEnableAsync`/`ReorderAsync`
  - `ReorderAsync`：器件锁定守卫、理由必填、写 `SYS_AUDIT_LOG`（`ActionType=ReorderElementDict`）
  - `Program.cs` 注册 `IElementDictService`
  - _Requirements: 1, 2, 8_

- [ ] 5. 字典维护控制器
  - 新增 `PanelFlow.Web/Controllers/ElementDictController.cs`：`Index` + `GetByLevel`/`Create`/`Update`/`ToggleEnable`/`Reorder`（防伪）
  - `[RoleAuthorize(Admin, Quoter, ProductionManager)]`
  - _Requirements: 1, 2, 8_

- [ ] 6. 字典维护视图与前端
  - 新增 `Views/ElementDict/Index.cshtml`（三级页签 + 表格 + 新增/编辑 Modal）
  - 新增 `wwwroot/js/element-dict.js`（加载、CRUD、拖拽排序 + 理由弹框）
  - _Requirements: 1, 2_

- [ ] 7. 菜单注册
  - `PermissionService.BuildAllMenus()` 项目管理下新增"通用项字典"
  - _Requirements: 1.1_

- [ ] 8. 导入去硬编码
  - 改造 `QuotationController.BuildRowsFromTable`：默认第 2 级节点改由参数注入（来自字典 `Level=2 且 IsDefaultOnImport=1 且 IsEnabled=1`），`SavePlan` 预读字典传入
  - 同步更新 `PanelFlow.Web.Tests` 中 `BuildRowsFromTable` PBT
  - _Requirements: 7_

- [ ] 9. Phase 1 检查点
  - `dotnet build` 通过；字典页可增删改查与排序；导入默认行为与改造前等价（PBT 通过）

---

## Phase 2：报价单结构维护（TreeReencode）

- [ ] 10. 结构模型与服务接口
  - 新增 `PanelFlow.Core/Models/QuotationTreeNode.cs`、`QuotationStructureDto`、`StructureApplyRequest/Result`
  - 新增 `PanelFlow.Core/Interfaces/IQuotationStructureService.cs`
  - _Requirements: 3, 4, 5, 6_

- [ ] 11. TreeReencodeService 核心
  - 新增 `PanelFlow.Infrastructure/Services/QuotationStructureService.cs`
  - [ ] 11.1 构树（4/8/12 分层，脏数据归游离行保留）
  - [ ] 11.2 结构操作：挂入（字段映射 + 幂等）/删除（器件锁定 + 有元件守卫）/改名/排序（器件首位守卫）
  - [ ] 11.3 DFS 按位置重编码（父前缀 + D4 序号，器件固定 0001）
  - [ ] 11.4 事务写回（DELETE 保留 0/9999 + 重新 INSERT，保留计价字段）
  - `Program.cs` 注册
  - _Requirements: 4, 5, 6_

- [ ] 12. 结构维护控制器
  - `QuotationController` 新增 `GET StructureMaintain`、`GET GetQuotationTree`、`POST ApplyStructure`（防伪 + 权限 dqzt!=10 + 本人/管理员 + 审计 `ActionType=ApplyStructure`）
  - _Requirements: 3, 4, 5, 8_

- [ ] 13. 结构维护视图与前端
  - 新增 `Views/Quotation/StructureMaintain.cshtml`、`wwwroot/js/quotation-structure.js`、`wwwroot/css/quotation-structure.css`
  - 选单 → 树（多选、元件只读）→ 挂入/删除/改名/排序 → 提交刷新
  - _Requirements: 3, 4, 5_

- [ ] 14. 菜单注册
  - 项目管理下新增"报价单结构维护"
  - _Requirements: 3.1_

- [ ]* 15. TreeReencode 属性/集成测试
  - 编码连续性、器件首位、子树跟随、计价保留、挂入幂等；dqzt==10 拒绝；审计断言
  - _Requirements: 6_

- [ ] 16. Phase 2 检查点
  - `dotnet build` + 测试通过；端到端：选单→挂入第2/3级→写回 BJB 正确

### Phase 2 增补（结构维护批量操作增强）

- [x] 17. 目录树全部展开/折叠 + 单节点折叠箭头 + 默认折叠到第1级
- [x] 18. 控制柜快速选择：全选/反选 + Shift 范围选
- [x] 19. 按控制柜批量挂入 L2+L3（一次 ApplyStructure 提交 AddLevel2 + AddLevel3）
- [x] 20. 按控制柜批量移除：`RemoveLevel2ByDict`（有元件跳过）/ `RemoveLevel3ByDict`（名称+规格匹配）
- [x] 21. 修复无法新增第1级：树顶报价单名称根节点 + `AddLevel1` + L1 字典种子
- [x] 22. 结果摘要展示跳过明细（含被跳过柜名）

---

## Phase 3（低优先）：第 1 级通用项接入

- [ ] 17. 第 1 级费用项清单与字典种子（与用户核对运费/保费/侧板/备件/附件）
- [ ] 18. 结构维护页支持在报价单根下挂入第 1 级通用项（4 位节点，沿用不区分类型）
  - _Requirements: 4（扩展到 Level=1）_

---

## Notes

- 标注 `*` 为可选测试任务，可后置。
- `BuildRowsFromTable` 保持 `internal static` 纯函数，默认列表以参数注入，确保 PBT 可测。
- 写回 BJB 的 INSERT 列清单与写法沿用现有 `SavePlan`。
- x_lx ↔ 汇总槽位上限（约 10）在字典页与文档提示。
