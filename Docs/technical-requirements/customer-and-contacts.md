# 技术需求：客户与联系人

> 从 `Docs/technical-logic.md` 拆出，包含客户主表 `KHYLB` 与联系人表 `KHYLB_CONTACT` 及兼容说明。  
> 字段映射：`Docs/database-mapping.md` 第 4、5 节。

---

## 1. 客户管理

### 1.1 功能入口

- 菜单位置：`项目管理 -> 客户`
- 控制器：`PanelFlow.Web/Controllers/CustomerController.cs`
- 服务：`PanelFlow.Infrastructure/Services/CustomerService.cs`
- 主表：`KHYLB`

### 1.2 功能范围

- 客户列表
- 客户搜索
- 新建客户
- 编辑客户

### 1.3 客户列表逻辑

入口方法：

- `CustomerController.Index()`

当前支持：

- 搜索字段：
  - 公司名称 `gsmc`
  - 公司别名 `gsld`
  - 联系人 `lxr`
  - 电话 `lxdh`
- 排序规则：
  - 按 `updated_at` 倒序
  - 再按 `gsbh` 升序

### 1.4 新建客户逻辑

入口方法：

- `CustomerController.Create()` `GET/POST`

处理流程：

1. 页面提交客户主信息
2. Controller 转换为 `CustomerDto`
3. `CustomerService.CreateAsync()` 执行业务校验：
   - 公司编号不能为空
   - 公司名称不能为空
   - 公司编号不能重复
   - 公司名称不能重复
   - 公司别名非空时不能重复
4. 通过后写入 `KHYLB`
5. 创建时同步写入：
   - `created_at`
   - `updated_at`

### 1.5 编辑客户逻辑

入口方法：

- `CustomerController.Edit()` `GET/POST`

处理流程：

1. 根据 `gsbh` 读取客户
2. 加载客户主信息和联系人信息
3. 更新客户时调用 `CustomerService.UpdateAsync()`
4. Service 执行业务校验：
   - 公司编号不能为空
   - 公司名称不能为空
   - 公司名称不能与其他客户重复
   - 公司别名非空时不能与其他客户重复
5. 更新 `KHYLB`
6. 刷新 `updated_at`

### 1.6 客户管理兼容策略

旧系统仍使用 `KHYLB.lxr` 和 `KHYLB.lxdh` 作为联系人字段，但原表只能保存单个联系人。

新系统当前策略：

- `KHYLB` 存客户主信息
- `KHYLB.lxr`、`KHYLB.lxdh` 不再由用户直接编辑
- 这两个字段作为“默认联系人镜像字段”
- 默认联系人来自新表 `KHYLB_CONTACT`

### 1.7 客户管理关键规则

- `gsmc` 不能为空
- `gsmc` 在应用层禁止重复
- `gsld` 非空时在应用层禁止重复
- 客户列表优先展示最近维护的数据

### 1.8 字段映射索引

客户管理字段映射详见：

- `Docs/database-mapping.md` 的“4. 客户管理字段映射”

---

## 2. 联系人管理

### 2.1 功能入口

联系人功能目前集成在客户编辑页中，不单独建页面。

- 控制器：`PanelFlow.Web/Controllers/CustomerController.cs`
- 服务：`PanelFlow.Infrastructure/Services/CustomerContactService.cs`
- 联系人表：`KHYLB_CONTACT`

### 2.2 联系人表用途

该表是新表，用于解决旧系统联系人只能保留最后一位的问题。

设计目标：

- 一个客户可维护多个联系人
- 支持默认联系人
- 支持联系人排序
- 支持回写旧系统兼容字段

### 2.3 已使用的联系人字段

当前程序按以下字段实现：

- `Id`
- `gsbh`
- `lxr`
- `lxdh`
- `email`
- `zw`
- `is_default`
- `sort_no`
- `is_enabled`
- `created_at`
- `updated_at`

### 2.4 联系人列表逻辑

入口方法：

- `CustomerContactService.GetByCompanyNoAsync()`

排序规则：

1. 默认联系人优先
2. `sort_no` 升序
3. `Id` 升序

### 2.5 新增联系人逻辑

入口方法：

- `CustomerController.AddContact()`
- `CustomerContactService.CreateAsync()`

处理流程：

1. 接收联系人表单
2. 校验：
   - 公司编号不能为空
   - 联系人不能为空
   - 客户必须存在
   - 同一客户下，`联系人 + 联系电话` 不能重复
3. 开启事务
4. 判断该客户当前联系人数量
5. 如果是第一个联系人：
   - 自动设为默认联系人 `is_default = true`
6. 写入 `KHYLB_CONTACT`
7. 如果是默认联系人：
   - 同步回写 `KHYLB.lxr`
   - 同步回写 `KHYLB.lxdh`
   - 更新客户 `updated_at`
8. 提交事务

### 2.6 编辑联系人逻辑

入口方法：

- `CustomerController.UpdateContact()`
- `CustomerContactService.UpdateAsync()`

处理流程：

1. 根据 `companyNo + Id` 找到联系人
2. 校验：
   - 联系人不能为空
   - 同一客户下不能出现相同 `联系人 + 联系电话`
3. 更新联系人字段
4. 刷新联系人 `updated_at`
5. 如果当前联系人是默认联系人：
   - 回写 `KHYLB.lxr`
   - 回写 `KHYLB.lxdh`
   - 刷新客户 `updated_at`

### 2.7 删除联系人逻辑

入口方法：

- `CustomerController.DeleteContact()`
- `CustomerContactService.DeleteAsync()`

处理流程：

1. 根据 `companyNo + Id` 找到联系人
2. 开启事务
3. 删除联系人记录
4. 若删除的是默认联系人：
   - 自动查找下一个联系人
   - 按 `sort_no`、`Id` 顺序选出新的默认联系人
5. 如果还有联系人：
   - 设新的默认联系人
   - 回写 `KHYLB.lxr/lxdh`
6. 如果已无联系人：
   - 清空 `KHYLB.lxr`
   - 清空 `KHYLB.lxdh`
7. 提交事务

### 2.8 设为默认联系人逻辑

入口方法：

- `CustomerController.SetDefaultContact()`
- `CustomerContactService.SetDefaultAsync()`

处理流程：

1. 根据 `companyNo + Id` 找到目标联系人
2. 开启事务
3. 将该客户下全部联系人重新计算默认标记：
   - 当前联系人 `is_default = true`
   - 其他联系人 `is_default = false`
4. 更新所有联系人 `updated_at`
5. 回写 `KHYLB.lxr/lxdh`
6. 提交事务

### 2.9 联系人管理关键规则

- 联系人表是主数据来源
- `KHYLB.lxr/lxdh` 只是默认联系人镜像字段
- 第一个联系人自动成为默认联系人
- 同一客户下联系人不能重复
- 默认联系人变化后必须同步回写客户表

### 2.10 字段映射索引

联系人管理字段映射详见：

- `Docs/database-mapping.md` 的“5. 联系人管理字段映射”

---

## 3. 一页式运行流程

### 3.1 客户管理流程

1. 用户从菜单进入客户列表
2. Controller 接收关键字查询条件
3. Service 从 `KHYLB` 查询客户
4. 按 `updated_at` 倒序返回结果
5. 用户可新建客户或进入编辑页
6. 新建/编辑时先校验公司编号、公司名称、公司别名
7. 通过后写入 `KHYLB`
8. 更新时间字段同步刷新

### 3.2 联系人管理流程

1. 用户进入客户编辑页
2. Controller 同时加载客户主信息与联系人列表
3. 用户在编辑页右侧新增联系人，或在下方列表编辑/删除联系人
4. Service 对 `KHYLB_CONTACT` 执行新增、编辑、删除、设默认
5. 若联系人默认状态变化，自动回写 `KHYLB.lxr/lxdh`
6. 保证旧系统继续可通过客户表读取默认联系人

---

## 4. 兼容性说明（客户 / 联系人）

### 4.1 旧系统兼容

由于 PowerBuilder 旧系统仍在使用：

- 不直接删除 `KHYLB` 原有联系人字段
- 不把联系人完全迁出后废弃旧字段
- 改用“新表为主、旧字段镜像”的兼容模式

### 4.2 客户维护时间字段

客户表当前使用：

- `created_at`
- `updated_at`

用途：

- 新建客户时写入创建时间和更新时间
- 编辑客户时刷新更新时间
- 客户列表按更新时间倒序排列
