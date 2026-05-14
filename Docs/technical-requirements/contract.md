# 技术需求：制造合同管理

> 从 `Docs/technical-logic.md` 拆出，仅包含合同（`XMYLB`）相关逻辑。  
> 字段映射：`Docs/database-mapping.md` 第 3 节。

---

## 1. 功能入口

- 菜单位置：`项目管理 -> 制造合同`
- 控制器：`PanelFlow.Web/Controllers/ContractController.cs`
- 服务：`PanelFlow.Infrastructure/Services/ManufacturingContractService.cs`
- 主表：`XMYLB`

## 2. 功能范围

- 合同列表
- 新建合同
- 编辑合同
- 删除合同
- 审计日志记录

## 3. 列表逻辑

入口方法：

- `ContractController.Index()`

核心逻辑：

- 接收 `keyword`、`includeHistory`、`page`、`pageSize`
- 调用 `IManufacturingContractService.GetListAsync()`
- 默认只显示近 3 年合同
- 可按合同编号、项目名称、合同内容模糊查询
- 列表按 `qdsj` 倒序，再按合同编号倒序

## 4. 新建逻辑

入口方法：

- `ContractController.Create()` `GET/POST`

处理流程：

1. 页面提交表单到 `Create(ContractCreateViewModel)`
2. Controller 将 ViewModel 转为 `ManufacturingContractDto`
3. 调用 `ManufacturingContractService.CreateAsync()`
4. Service 校验合同编号不能为空、不能重复
5. 写入 `XMYLB`
6. Controller 调用审计服务写入操作日志
7. 成功后跳转回列表页

## 5. 编辑逻辑

入口方法：

- `ContractController.Edit()` `GET/POST`

处理流程：

1. 根据合同编号读取原数据
2. 将表单内容映射为 DTO
3. 调用 `ManufacturingContractService.UpdateAsync()`
4. Service 校验合同是否存在
5. 更新 `XMYLB` 关键字段
6. Controller 记录审计日志（保留修改前后数据）

## 6. 删除逻辑

入口方法：

- `ContractController.Delete()`

处理流程：

1. 先读取合同原数据用于审计
2. `ManufacturingContractService.DeleteAsync()` 开启事务
3. 如果合同关联了报价方案号 `bjd_fabh`
   - 先执行 SQL：
     - 将 `BJFAT.dqzt` 重置为 `0`
4. 删除 `XMYLB` 中对应合同记录
5. 提交事务
6. 写入审计日志

## 7. 合同管理关键规则

- 合同编号必须唯一
- 删除合同时需要联动报价表状态（见第 6 节）
- 所有新增、编辑、删除操作都写审计日志
- 当前合同权限由 `[RoleAuthorize(RoleNames.Admin, RoleNames.Quoter)]` 控制

## 8. 字段映射索引

合同管理字段映射详见：

- `Docs/database-mapping.md` 的“3. 合同管理字段映射”

## 9. 一页式运行流程（合同）

1. 用户从菜单进入制造合同页面
2. Controller 接收筛选、分页条件
3. Service 从 `XMYLB` 查询并返回 DTO
4. View 展示列表或编辑表单
5. 用户提交新增、编辑、删除请求
6. Service 执行校验和数据库写入
7. 删除时额外联动 `BJFAT.dqzt`（见第 6 节）
8. Controller 写审计日志
