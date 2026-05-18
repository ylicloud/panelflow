---
inclusion: always
---

# 前端开发规范

## Razor 视图

- 必须使用**强类型** `@model`，禁止滥用 `ViewBag` 或 `dynamic`。
- 表单提交必须使用 Tag Helper 并包含防伪令牌（`@Html.AntiForgeryToken()`）。

## JavaScript

- 代码必须放在 `wwwroot/js/` 目录下，**禁止**在 `.cshtml` 中书写大量内联脚本。
- 异步请求必须正确处理成功和失败情况，并给出明确的用户提示。

## CSS

- 按页面或功能拆分文件，避免滥用 `!important`。
- **新增** CSS 文件中的类名使用统一前缀（如 `oa-`）；已有文件中的类名不强制重命名。

## 移动端与 PWA

- 本系统支持**手机浏览器访问**；可使用 `manifest.webmanifest`、主题色等改善「添加到主屏幕」体验（**未使用 Service Worker**，无离线缓存层）。
- 所有新增页面必须在移动端可用，禁止出现横向滚动条或元素溢出。
- `viewport` meta 必须包含 `width=device-width, initial-scale=1.0`。
- 新增独立页面（不使用 `_Layout`）时，按需包含与 `_Layout` 一致的 viewport、manifest 等 meta，**不要求**注册 Service Worker。
- 必须使用 `env(safe-area-inset-*)` 适配带刘海/圆角的手机屏幕。

**核心目标**：保持前端代码整洁、用户体验一致、移动端可用。
