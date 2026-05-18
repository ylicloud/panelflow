---
inclusion: always
---

# 项目核心规则

本项目是将 PowerBuilder 系统业务功能移植到 C# ASP.NET MVC 的重构项目。
数据库与历史系统共用，但**不拷贝 PowerBuilder 代码**，使用现代 C# 最佳实践重新实现。

## 架构分层

- **MVC** 仅负责展示与协调
- **CSharpOA.Core** 承载业务逻辑与 Service
- **CSharpOA.Infrastructure** 承载数据访问

## 技术栈决策

- 状态管理：Session（暂不引入 JWT）
- 权限方案：`IPermissionService` / `[RoleAuthorize]`
- Excel 导入：NPOI
- 编码规范详见 `coding-standards.mdc`，数据库兼容详见 `db-compatibility.mdc`

## 交付一致性要求（AI 必须遵守）

- 修改代码时**只改动必要的文件**，不要顺手修改无关代码。
- 优先按照 `Model → Service → Controller → View → JS/CSS` 的顺序实施功能。
- 每次回复应先列出修改的文件清单，再说明主要变更和业务影响。
- 涉及业务流程的修改，必须参考 `business-workflow.mdc` 中的生命周期规则。
- 如存在风险或未验证项，必须明确说明。
