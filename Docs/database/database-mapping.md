# PanelFlow 数据映射文档

## 1. 文档目的

本文件专门记录“代码字段 <-> 数据库字段”的映射关系，作为 `technical-logic.md` 与 `Docs/technical-requirements/` 的配套文档。

使用建议：

- 查业务流程：优先看 `technical-logic.md`（总览）或 `Docs/technical-requirements/` 下对应模块文件
- 查字段落库：优先看本文件

---

## 2. 模块与主表

- 合同管理：`XMYLB`
- 客户管理：`KHYLB`
- 联系人管理：`KHYLB_CONTACT`

---

## 3. 合同管理字段映射

### 3.1 DTO 与数据库映射

DTO：`PanelFlow.Core/Models/ManufacturingContractDto.cs`

- `ContractNo` -> `XMYLB.xmbh`
- `ProjectName` -> `XMYLB.xmmc`
- `LegacyContractNo` -> `XMYLB.hth_1`
- `SignDate` -> `XMYLB.qdsj`
- `Owner` -> `XMYLB.fzr`
- `ContractContent` -> `XMYLB.htnr`
- `DeliveryDate` -> `XMYLB.jhsj`
- `TotalAmount` -> `XMYLB.zhtje`
- `CustomerNo` -> `XMYLB.khbh`
- `SignCompany` -> `XMYLB.qydw`
- `QuotationPlanNo` -> `XMYLB.bjd_fabh`
- `CurrentStatus` -> `XMYLB.dqzt`
- `Remark` -> `XMYLB.beizhu`

### 3.2 删除联动

删除合同时，若 `bjd_fabh` 有值，会联动：

- `BJFAT.dqzt = 0`

---

## 4. 客户管理字段映射

### 4.1 DTO 与数据库映射

DTO：`PanelFlow.Core/Models/CustomerDto.cs`

- `CompanyNo` -> `KHYLB.gsbh`
- `CompanyName` -> `KHYLB.gsmc`
- `Alias` -> `KHYLB.gsld`
- `Contact` -> `KHYLB.lxr`
- `Phone` -> `KHYLB.lxdh`
- `Remark` -> `KHYLB.beizhu`
- `CreatedAt` -> `KHYLB.created_at`
- `UpdatedAt` -> `KHYLB.updated_at`

### 4.2 兼容说明

- `KHYLB.lxr`、`KHYLB.lxdh` 作为默认联系人镜像字段保留
- 这两个字段由联系人模块自动回写，不作为客户主表单的人工输入主来源

---

## 5. 联系人管理字段映射

### 5.1 DTO 与数据库映射

DTO：`PanelFlow.Core/Models/CustomerContactDto.cs`

- `Id` -> `KHYLB_CONTACT.Id`
- `CompanyNo` -> `KHYLB_CONTACT.gsbh`
- `ContactName` -> `KHYLB_CONTACT.lxr`
- `Phone` -> `KHYLB_CONTACT.lxdh`
- `Email` -> `KHYLB_CONTACT.email`
- `Title` -> `KHYLB_CONTACT.zw`
- `IsDefault` -> `KHYLB_CONTACT.is_default`
- `SortNo` -> `KHYLB_CONTACT.sort_no`
- `IsEnabled` -> `KHYLB_CONTACT.is_enabled`
- `CreatedAt` -> `KHYLB_CONTACT.created_at`
- `UpdatedAt` -> `KHYLB_CONTACT.updated_at`

### 5.2 业务关联

- `KHYLB_CONTACT.gsbh` 关联 `KHYLB.gsbh`
- 默认联系人变更时，回写 `KHYLB.lxr/lxdh`

---

## 6. 规则摘要

- 合同编号：应用层唯一校验
- 客户公司名：应用层唯一校验
- 客户别名：非空时应用层唯一校验
- 联系人：同客户下 `联系人 + 联系电话` 唯一
- 联系人默认：同客户下仅一个默认联系人（由服务层保证）

---

## 7. 维护建议

每次涉及字段调整时，需同步更新以下文件：

1. `PanelFlow.Infrastructure/Data/ApplicationDbContext.cs`（实体映射）
2. 对应 DTO（`PanelFlow.Core/Models/...`）
3. Service 读写映射逻辑（`PanelFlow.Infrastructure/Services/...`）
4. 本文档 `Docs/database-mapping.md`

---

## 8. 报价单管理字段映射（2026-04-28）

### 8.1 DTO 与数据库映射

DTO：`PanelFlow.Core/Models/QuotationDto.cs`

- `QuotationNo` -> `BJFAT.fabh`
- `CreatedAt` -> `BJFAT.fasj`
- `QuotationName` -> `BJFAT.famc`
- `PlanModelNo` -> `BJFAT.famxbh`
- `Quoter` -> `BJFAT.bjr`
- `Remark` -> `BJFAT.bz`
- `CustomerNo` -> `BJFAT.khbh`
- `PlanType` -> `BJFAT.falx`
- `CurrentStatus` -> `BJFAT.dqzt`

页面展示补充：

- `CustomerName` 为展示字段，不直接落 `BJFAT`
- 列表中客户名称展示为：`KHYLB.gsmc(KHYLB.gsld)`（别名非空时）
- 编辑页中客户名称、别名回显来源为：
  - `BJFAT.khbh = KHYLB.gsbh` 后分别读取 `KHYLB.gsmc`、`KHYLB.gsld`

### 8.2 新建默认值映射

新建报价单时由后端强制赋值：

- `BJFAT.fasj = DateTime.Now`
- `BJFAT.famxbh = 0`
- `BJFAT.falx = 1`
- `BJFAT.dqzt = 1`（空报价单；列表展示「(无内容)」，见 `technical-requirements/quotation.md` 第 2、3 节）
- `BJFAT.bjr = 当前登录用户名`

### 8.3 新建联动写入 BJB

创建成功前，在同一事务中除 `BJFAT` 外还需写入 `BJB` 两条初始化记录：

1. 初始化行一：
   - `BJB.fabh = BJFAT.fabh`
   - `BJB.x_bm = 0`
2. 初始化行二：
   - `BJB.fabh = BJFAT.fabh`
   - `BJB.x_bm = 9999`
   - `BJB.x_mc = 总计`

其余 `BJB` 字段按默认值（空字符串/0/NULL）写入。

### 8.4 删除映射与顺序

删除报价单时（仅本人且 `dqzt≠10`，与列表及服务端 `DeleteAsync` 一致）在同一事务按顺序执行：

1. 删除 `BJFAT` 中 `fabh=报价单编号` 的记录
2. 删除 `BJB` 中 `fabh=报价单编号` 的记录

### 8.5 条件规则映射

- 修改 / 删除 /「报价」按钮显示条件（一致）：
  - `BJFAT.bjr == 登录用户名`
  - `BJFAT.dqzt !== 10`（已成立不可进行上述操作）
- 删除接口 `QuotationService.DeleteAsync` 与上表一致：本人且 `dqzt != 10`
