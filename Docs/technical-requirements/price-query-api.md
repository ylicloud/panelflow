# 元件价格查询 API — 需求与设计

## 1. 背景与目标

PanelFlow 已维护 `STD_PRICE_HISTORY` 聚合表（近 5 年历史报价汇总），并在填价流程中通过 `NormalizeSpec`（C# 版 `F_CleanString`）将原始型号转为 `x_wzdh` 指纹进行匹配。

本 API 供 **Cursor、脚本或其他内网程序** 按型号查询历史价格，避免重复手工查价，与填价页「引用历史报价」使用同一数据源与匹配口径。

## 2. 范围

### Phase 1（当前实现）

- 单条查询：`GET /api/price`
- 批量查询：`POST /api/price/batch`（最多 100 条）
- 型号预处理：`SpecNormalizer.Normalize` → `x_wzdh` → 查 `STD_PRICE_HISTORY`
- 内网免鉴权（网络层限制访问）

### Phase 2（后续）

- 别名回退：`STD_SPEC_ALIAS` 二级匹配（见 `.kiro/specs/spec-alias-matching/design.md`）
- API Key 鉴权（`X-Api-Key` Header）

## 3. 非目标

- 不写入 `BJB`，不触发自动填价
- 不替代 `PriceHistoryController` 的管理功能（剔除来源、刷新、属性维护等）
- 不提供模糊搜索（仅精确指纹匹配）

## 4. 数据依赖

| 依赖 | 说明 |
|------|------|
| `STD_PRICE_HISTORY` | 按 `x_wzdh` 聚合的历史价，见 `Docs/create-std-price-history.sql` |
| `SP_RefreshPriceHistory` | 每日刷新（建议凌晨 2:00），见 `Docs/SP_RefreshPriceHistory.sql` |
| 数据延迟 | 最多约 1 天（取决于定时 Job 是否正常运行） |

## 5. 匹配规则

### 5.1 型号预处理（`SpecNormalizer.Normalize`）

与 SQL `dbo.F_CleanString` 逻辑一致：

1. 转小写；去除 CR/LF/TAB/不间断空格/零宽空格
2. 全角转半角
3. 去掉括号及括号内内容
4. 仅保留 `a-z`、`0-9`、中文、`μωΩ°±℃φ`

输出为 `x_wzdh` 指纹，与 `STD_PRICE_HISTORY.x_wzdh` 唯一索引匹配。

### 5.2 查询参数优先级

- `spec`：原始型号，经 `Normalize` 后查库
- `wzdh`：已知指纹，跳过预处理直接查库
- 两者都传时 **优先 `wzdh`**

### 5.3 大小写

预处理结果为小写；查库使用精确匹配（库内 `x_wzdh` 亦为标准化小写）。

## 6. 接口契约

### 6.1 单条查询

**请求**

```
GET /api/price?spec={型号}
GET /api/price?wzdh={指纹}
```

**命中响应（200）**

```json
{
  "found": true,
  "inputSpec": "CJX2-0910 AC220V",
  "xWzdh": "cjx20910ac220v",
  "ggxh": "CJX2-0910 AC220V",
  "xMc": "交流接触器",
  "xDw": "台",
  "xSccj": "德力西",
  "lastPrice": 128.5000,
  "lastFabh": "BJ2025-0123",
  "lastDate": "2025-11-20T10:30:00",
  "avgPrice": 115.2000,
  "avgCount": 8,
  "minPrice": 98.0000,
  "maxPrice": 135.0000
}
```

**未命中响应（200）**

```json
{
  "found": false,
  "inputSpec": "UNKNOWN-MODEL",
  "xWzdh": "unknownmodel",
  "message": "历史价格表中无此型号指纹"
}
```

### 6.2 批量查询

**请求**

```
POST /api/price/batch
Content-Type: application/json

{
  "specs": ["CJX2-0910 AC220V", "DZ47-63 C16"]
}
```

可选字段 `wzdhList`：已知指纹列表，与 `specs` 合并去重后批量查询。

**响应（200）**

```json
{
  "total": 2,
  "foundCount": 1,
  "items": [
    { "found": true, "inputSpec": "CJX2-0910 AC220V", "xWzdh": "cjx20910ac220v", "lastPrice": 128.5, ... },
    { "found": false, "inputSpec": "DZ47-63 C16", "xWzdh": "dz4763c16", "message": "历史价格表中无此型号指纹" }
  ]
}
```

### 6.3 错误码

| HTTP | 场景 |
|------|------|
| 200 | 查询成功（含 `found=false`） |
| 400 | `spec`/`wzdh` 均为空；批量为空或超过 100 条 |
| 500 | 数据库异常 |

### 6.4 价格字段说明

| 字段 | 含义 |
|------|------|
| `lastPrice` | 最新报价（与填价「引用历史报价」一致） |
| `avgPrice` | 近 5 年均价 |
| `minPrice` / `maxPrice` | 近 5 年最低/最高价 |
| `avgCount` | 参与均价计算的样本数 |

## 7. 安全

- **Phase 1**：接口不使用 `[RoleAuthorize]`（Session 会导致 API 重定向登录页）
- **必须**在网络层限制仅内网可访问 `/api/price*`（IIS IP 限制、防火墙、frp 内网绑定等）
- **Phase 2**：可增加 `X-Api-Key` + `appsettings` 配置，不改变 JSON 契约

## 8. 架构决策

接口放在 **PanelFlow.Web** 同一项目：

- 复用 `ApplicationDbContext`、`StdPriceHistory` 实体与 `SpecNormalizer`
- 与 `AutoFillPriceFromHistory` / `GetReferencePrice` 价格口径一致
- 共用部署与连接池，运维简单

**影响**：API 与 MVC 同进程；高频调用共享资源（单次索引查询，影响通常可忽略）。

独立拆项目仅当需公网暴露、独立扩缩容或与主站完全不同的鉴权策略时考虑。

## 9. 实现清单

| 层 | 文件 |
|----|------|
| Core | `Utilities/SpecNormalizer.cs` |
| Core | `Interfaces/IPriceQueryService.cs` |
| Core | `Models/PriceQueryResultDto.cs`、`PriceBatchQueryRequest.cs` |
| Infrastructure | `Services/PriceQueryService.cs` |
| Web | `Controllers/PriceQueryController.cs` |
| Web | `Program.cs` 注册 `IPriceQueryService` |
| Web | `QuotationController` 改用 `SpecNormalizer.Normalize` |

## 10. 测试计划

| 用例 | 预期 |
|------|------|
| 已知型号（库中存在） | `found=true`，返回 `lastPrice` 等 |
| 全角/括号型号 | 归一化后与库内 `x_wzdh` 匹配 |
| 不存在型号 | `found=false`，`message` 有说明 |
| 空 `spec`/`wzdh` | HTTP 400 |
| 批量 2 条（1 命中 1 未命中） | `foundCount=1`，`items` 长度 2 |
| 批量超过 100 条 | HTTP 400 |

## 11. 调用示例

```bash
# 单条（按型号）
curl "http://localhost:5000/api/price?spec=CJX2-0910%20AC220V"

# 单条（按指纹）
curl "http://localhost:5000/api/price?wzdh=cjx20910ac220v"

# 批量
curl -X POST "http://localhost:5000/api/price/batch" \
  -H "Content-Type: application/json" \
  -d "{\"specs\":[\"CJX2-0910 AC220V\",\"UNKNOWN\"]}"
```

PowerShell：

```powershell
Invoke-RestMethod -Uri "http://localhost:5000/api/price?spec=CJX2-0910 AC220V"
```

## 12. 风险

- 定时 Job 未运行 → 返回数据可能过期
- 内网免鉴权 → 务必在网络层收口
- 别名匹配未实现 → Phase 1 仅直接 `x_wzdh` 匹配
