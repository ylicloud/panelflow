# 合同状态字段重设计（兼容历史库）

## 设计目标

- 历史库 `dbo.contract` 只有合同内容字段，无状态字段。
- 新系统补充状态管理，但不能破坏原有数据兼容性。
- 状态编码与生产流程对齐，支持审计追踪和权限管控。

## 状态字典（建议）

`contract_status` 使用 `TINYINT`，保留分段编码，便于扩展：

- `10` 草稿：仅录入基础信息，尚未提交审批
- `20` 待审批：已提交，等待业务/财务审批
- `30` 审批通过：允许进入生产准备
- `40` 生产中：已进入生产执行域（具体看 `production_stage`）
- `50` 待发货：质检通过，等待出厂发货
- `60` 已完成：发货签收，合同履约完成
- `70` 已终止：报价或合同执行中止（需记录原因）
- `80` 已作废：录入错误或业务撤销（需记录原因）

> 说明：历史数据保持 `NULL`，表示“历史记录/未建状态”，由业务逐步补齐。

## 生产阶段字典（新增）

`production_stage` 使用 `TINYINT`，仅当 `contract_status=40` 时有效：

- `41` 采购阶段
- `42` 装配阶段
- `43` 质检阶段

`production_stage_status` 使用 `TINYINT`，表示阶段内部进度：

- `0` 未开始
- `1` 进行中
- `2` 已完成
- `3` 已跳过（必须填写原因）

## 状态流转规则（核心）

- 正向：`10 -> 20 -> 30 -> 40 -> 50 -> 60`
- 终止：`10/20/30/40/50 -> 70`
- 作废：`10/20 -> 80`
- 严禁：`NULL -> 60`、`20 -> 40`、`30 -> 60` 等跨阶段跳转
- 所有状态变更必须写审计表并记录操作人、原因、时间

生产阶段流转规则：

- 仅当 `contract_status=40` 时允许设置 `production_stage`
- 正向：`41 -> 42 -> 43`
- 默认每个阶段应经历：`0 -> 1 -> 2`
- 非法：`41 -> 43`、`0 -> 2`（跳过执行过程）等
- 若需跳过阶段，`production_stage_status=3` 且必须记录 `change_reason`

## SQL Server 2022 结构变更脚本

```sql
ALTER TABLE dbo.contract
ADD contract_status TINYINT NULL,
    production_stage TINYINT NULL,
    production_stage_status TINYINT NULL,
    status_updated_at DATETIME2(0) NULL,
    status_updated_by NVARCHAR(50) NULL;

ALTER TABLE dbo.contract
ADD CONSTRAINT CK_contract_status
CHECK (contract_status IS NULL OR contract_status IN (10,20,30,40,50,60,70,80));

ALTER TABLE dbo.contract
ADD CONSTRAINT CK_contract_production_stage
CHECK (
    production_stage IS NULL OR production_stage IN (41,42,43)
);

ALTER TABLE dbo.contract
ADD CONSTRAINT CK_contract_production_stage_status
CHECK (
    production_stage_status IS NULL OR production_stage_status IN (0,1,2,3)
);

ALTER TABLE dbo.contract
ADD CONSTRAINT CK_contract_stage_with_status40
CHECK (
    contract_status = 40
    OR (production_stage IS NULL AND production_stage_status IS NULL)
);

CREATE INDEX IX_contract_status
ON dbo.contract(contract_status);

CREATE INDEX IX_contract_production_stage
ON dbo.contract(contract_status, production_stage, production_stage_status);
```

## 状态审计表（必须）

```sql
CREATE TABLE dbo.contract_status_audit (
    id BIGINT IDENTITY(1,1) PRIMARY KEY,
    contract_id BIGINT NOT NULL,
    from_status TINYINT NULL,
    to_status TINYINT NOT NULL,
    from_stage TINYINT NULL,
    to_stage TINYINT NULL,
    from_stage_status TINYINT NULL,
    to_stage_status TINYINT NULL,
    change_reason NVARCHAR(200) NULL,
    changed_by NVARCHAR(50) NOT NULL,
    changed_at DATETIME2(0) NOT NULL
        CONSTRAINT DF_contract_status_audit_changed_at DEFAULT (SYSDATETIME())
);

CREATE INDEX IX_contract_status_audit_contract_id
ON dbo.contract_status_audit(contract_id, changed_at DESC);
```

## 查询示例

```sql
-- 查询当前处于“生产中-采购阶段-进行中”的合同
SELECT c.*
FROM dbo.contract AS c
WHERE c.contract_status = 40
  AND c.production_stage = 41
  AND c.production_stage_status = 1;

-- 按阶段统计生产中合同数量
SELECT
    c.production_stage,
    COUNT(1) AS contract_count
FROM dbo.contract AS c
WHERE c.contract_status = 40
GROUP BY c.production_stage;
```

## 角色-动作-状态流转矩阵（建议）

说明：状态变更采用“动作驱动”，禁止前端直接编辑状态字段。

- 销售专员（或商务专员）
  - 动作：`SubmitContract`
  - 流转：`10 -> 20`
  - 约束：合同基础信息完整、金额口径校验通过
- 商务经理 / 财务经理 / 负责人
  - 动作：`ApproveContract`
  - 流转：`20 -> 30`
  - 约束：审批意见必填，金额确认通过
- 商务经理 / 财务经理 / 负责人
  - 动作：`RejectContract`
  - 流转：`20 -> 10`
  - 约束：驳回原因必填
- 计划经理 / 生产经理
  - 动作：`StartProduction`
  - 流转：`30 -> 40`
  - 约束：已创建生产任务，默认 `production_stage=41`、`production_stage_status=0`
- 采购主管
  - 动作：`StartProcurement`
  - 流转：`40(41,0) -> 40(41,1)`
  - 约束：采购计划已下达
- 采购主管
  - 动作：`CompleteProcurement`
  - 流转：`40(41,1) -> 40(41,2)`
  - 约束：关键物料到货并完成入库
- 车间主管 / 班组长
  - 动作：`StartAssembly`
  - 流转：`40(41,2) -> 40(42,1)`
  - 约束：BOM关键物料可用，领料已执行
- 车间主管 / 班组长
  - 动作：`CompleteAssembly`
  - 流转：`40(42,1) -> 40(42,2)`
  - 约束：装配记录完整
- 质检员
  - 动作：`StartInspection`
  - 流转：`40(42,2) -> 40(43,1)`
  - 约束：装配完成并提交质检
- 质检主管
  - 动作：`CompleteInspection`
  - 流转：`40(43,1) -> 40(43,2)`
  - 约束：质检通过；不通过时保持 `40` 并回退整改单
- 质检主管 / 发货放行权限角色
  - 动作：`ReleaseForShipment`
  - 流转：`40 -> 50`
  - 约束：质检已完成且放行
- 物流专员 / 仓储主管
  - 动作：`ConfirmDelivery`
  - 流转：`50 -> 60`
  - 约束：发货单存在且客户签收
- 销售经理 / 经营负责人
  - 动作：`TerminateContract`
  - 流转：`10/20/30/40/50 -> 70`
  - 约束：终止原因必填
- 销售经理 / 经营负责人
  - 动作：`VoidContract`
  - 流转：`10/20 -> 80`
  - 约束：作废原因必填

## Controller/Service 接口清单（建议）

### Controller（示例：`ContractWorkflowController`）

- `POST /api/contracts/{id}/submit` -> `SubmitContract`
- `POST /api/contracts/{id}/approve` -> `ApproveContract`
- `POST /api/contracts/{id}/reject` -> `RejectContract`
- `POST /api/contracts/{id}/start-production` -> `StartProduction`
- `POST /api/contracts/{id}/start-procurement` -> `StartProcurement`
- `POST /api/contracts/{id}/complete-procurement` -> `CompleteProcurement`
- `POST /api/contracts/{id}/start-assembly` -> `StartAssembly`
- `POST /api/contracts/{id}/complete-assembly` -> `CompleteAssembly`
- `POST /api/contracts/{id}/start-inspection` -> `StartInspection`
- `POST /api/contracts/{id}/complete-inspection` -> `CompleteInspection`
- `POST /api/contracts/{id}/release-for-shipment` -> `ReleaseForShipment`
- `POST /api/contracts/{id}/confirm-delivery` -> `ConfirmDelivery`
- `POST /api/contracts/{id}/terminate` -> `TerminateContract`
- `POST /api/contracts/{id}/void` -> `VoidContract`
- `GET /api/contracts/{id}/workflow` -> 查询当前状态/阶段/可执行动作
- `GET /api/contracts/{id}/audits` -> 查询状态变更审计

### Service（示例：`IContractWorkflowService`）

- `Task SubmitContractAsync(long contractId, string operatorId, string? reason)`
- `Task ApproveContractAsync(long contractId, string operatorId, string? reason)`
- `Task RejectContractAsync(long contractId, string operatorId, string reason)`
- `Task StartProductionAsync(long contractId, string operatorId, string? reason)`
- `Task StartProcurementAsync(long contractId, string operatorId, string? reason)`
- `Task CompleteProcurementAsync(long contractId, string operatorId, string? reason)`
- `Task StartAssemblyAsync(long contractId, string operatorId, string? reason)`
- `Task CompleteAssemblyAsync(long contractId, string operatorId, string? reason)`
- `Task StartInspectionAsync(long contractId, string operatorId, string? reason)`
- `Task CompleteInspectionAsync(long contractId, string operatorId, string? reason)`
- `Task ReleaseForShipmentAsync(long contractId, string operatorId, string? reason)`
- `Task ConfirmDeliveryAsync(long contractId, string operatorId, string? reason)`
- `Task TerminateContractAsync(long contractId, string operatorId, string reason)`
- `Task VoidContractAsync(long contractId, string operatorId, string reason)`
- `Task<ContractWorkflowDto> GetWorkflowAsync(long contractId, string operatorId)`
- `Task<IReadOnlyList<ContractStatusAuditDto>> GetAuditLogsAsync(long contractId)`

### Service 内部强制校验（必须）

- 统一入口校验角色权限（`IPermissionService` + `[RoleAuthorize]`）
- 统一状态机校验：`CanTransition(from, to, stage, stageStatus, role)`
- 统一前置条件校验：如“质检未完成不得放行发货”
- 状态更新与审计写入同事务提交
- 所有回退/终止/作废必须有 `reason`

## 合同-生产-库房边界与联动规则（参考）

### 边界定义

- 合同模块：管理履约里程碑（`contract_status`），回答“合同走到哪一步”
- 生产模块：管理执行阶段（`production_stage`/`production_stage_status`），回答“生产做到哪一步”
- 库房模块：管理物料实物流转，回答“货物是否真实可用且可追溯”

### 不混用原则

- 入库/出库/盘点/报损属于库房模块，不直接写入合同状态字段
- 合同状态不记录每一笔库存动作，只消费“可放行/不可放行”等结果信号
- 生产阶段推进依赖库房结果，但不替代库房明细台账

### 联动规则（建议）

- 采购完成后，关键元件必须完成入库，方可进入装配开始
- 装配领料时必须校验可用库存，出库失败则禁止推进装配状态
- 质检完成后才允许执行发货放行（`40 -> 50`）
- 发货确认后更新合同完成（`50 -> 60`），库房侧完成对应出库闭环
- 盘点差异、报损等库存异常不直接改合同状态，但应触发预警和审批流程

### 系统实现建议

- 通过应用服务编排联动，不通过数据库触发器硬编码业务流转
- 在工作流动作中调用库存可用性检查接口（只读）
- 对关键动作建立幂等控制，避免重复提交造成重复扣减或重复流转
- 联动失败时整体回滚并给出业务提示（事务一致性）

## 历史数据回填建议

为保证兼容性，第一阶段不强制回填：

```sql
-- 历史记录保持 NULL，不影响旧流程
-- 如需逐步治理，可按业务规则分批回填：
-- UPDATE dbo.contract
-- SET contract_status = 30
-- WHERE contract_status IS NULL AND approve_flag = 1;
```

建议在业务层展示时做兜底映射：

- `NULL` 显示为“历史合同（未建状态）”
- 首次编辑或审批时再写入具体状态
- `contract_status=40` 且 `production_stage IS NULL` 时，前端显示“生产中（待分派阶段）”

## 回归验证清单

- 新增字段后，历史合同查询/编辑不报错
- 仅允许状态值在约束枚举内
- 非法跳转被业务层拦截并提示
- 每次状态变更都写入 `dbo.contract_status_audit`
- `70/80` 状态必须填写变更原因
- `contract_status!=40` 时，`production_stage`/`production_stage_status` 必须为空
- `production_stage_status=3`（已跳过）时，必须填写跳过原因


