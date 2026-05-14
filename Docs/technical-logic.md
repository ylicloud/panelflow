# PanelFlow 技术逻辑文档

## 1. 文档目的

本文件用于记录当前系统已实现功能的程序运行逻辑，方便后续开发、排错、交接和回顾。

配套文档：

- 字段映射与表关系：`Docs/database-mapping.md`
- **按模块拆分的技术需求（详细规则与流程）**：`Docs/technical-requirements/`
  - `contract.md` — 制造合同
  - `customer-and-contacts.md` — 客户与联系人
  - `quotation.md` — 报价单

建议后续按以下原则持续维护本文件：

- 新增业务模块时，在 `technical-requirements/` 下增加或扩充对应文件，并在本节配套列表中登记
- 业务规则发生变化时，同步更新对应技术需求文件与本总览
- 重要的数据兼容策略、事务处理、回写逻辑必须记录

---

## 2. 当前涉及模块

当前文档体系覆盖以下模块（细则见各技术需求文件）：

1. 合同管理（制造合同 `XMYLB`）
2. 客户管理（`KHYLB`）
3. 联系人管理（`KHYLB_CONTACT`，与客户编辑页集成）
4. 报价单（`BJFAT`、`BJB`）

---

## 3. 系统实现分层

当前项目按以下分层实现：

- `PanelFlow.Web`
  - Controller 接收请求
  - View 负责页面展示与表单提交
- `PanelFlow.Core`
  - DTO、接口定义
- `PanelFlow.Infrastructure`
  - EF 实体映射
  - Service 业务逻辑与数据库操作

典型调用链：

`View -> Controller -> Core Interface -> Infrastructure Service -> ApplicationDbContext -> SQL Server`

---

## 4. 技术需求文档索引

本文件保留**总览**与**跨模块原则**；各模块的详细业务规则、流程与约束放在 `Docs/technical-requirements/`，按主题单独维护。

| 主题 | 文件 |
|------|------|
| 制造合同（`XMYLB`） | [`technical-requirements/contract.md`](technical-requirements/contract.md) |
| 客户与联系人（`KHYLB`、`KHYLB_CONTACT`） | [`technical-requirements/customer-and-contacts.md`](technical-requirements/customer-and-contacts.md) |
| 报价单（`BJFAT`、`BJB`） | [`technical-requirements/quotation.md`](technical-requirements/quotation.md) |

---

## 5. 一页式运行与兼容总原则

### 5.1 新旧系统兼容总原则

1. 新系统优先使用结构化新表
2. 旧系统依赖的旧字段不立即删除
3. 通过“镜像字段回写”保证旧系统继续运行
4. 能在应用层校验的规则先放应用层，避免过早破坏旧库兼容性

客户、联系人及镜像字段的细则见：`Docs/technical-requirements/customer-and-contacts.md`。

合同删除联动报价表状态等细则见：`Docs/technical-requirements/contract.md`。

---

## 6. 后续维护建议

建议未来继续补充以下内容：

1. 每个模块对应的数据库字段说明（可与 `database-mapping.md` 交叉引用）
2. 每个模块的页面截图或流程图
3. 常见故障及排查方式
4. 与旧系统的差异点
5. 审计日志覆盖范围说明
6. 与 `Docs/database-mapping.md`、各 `technical-requirements/*.md` 的同步更新

---

## 7. 文档维护建议

每次功能完成后，建议同步补写**对应技术需求文件**与本总览，至少包含：

- 功能入口
- 使用的数据表
- Controller / Service 位置
- 关键校验规则
- 事务与联动逻辑
- 与旧系统的兼容处理

这样后续查看某块功能时，不需要每次都重新读代码才能理解运行逻辑。
