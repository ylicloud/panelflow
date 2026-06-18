using Microsoft.AspNetCore.Mvc;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;

namespace PanelFlow.Web.Controllers;

/// <summary>
/// 元件历史价格查询 API（内网免鉴权，须在反向代理/防火墙层限制访问）。
/// 详见 Docs/technical-requirements/price-query-api.md
/// </summary>
[ApiController]
[Route("api/price")]
public class PriceQueryController : ControllerBase
{
    private readonly IPriceQueryService _priceQueryService;
    private readonly ILogger<PriceQueryController> _logger;

    public PriceQueryController(IPriceQueryService priceQueryService, ILogger<PriceQueryController> logger)
    {
        _priceQueryService = priceQueryService;
        _logger = logger;
    }

    /// <summary>按型号或指纹查询单条历史价格。</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? spec, [FromQuery] string? wzdh)
    {
        if (string.IsNullOrWhiteSpace(spec) && string.IsNullOrWhiteSpace(wzdh))
            return BadRequest(new { message = "请提供 spec（型号）或 wzdh（指纹）参数" });

        try
        {
            var result = await _priceQueryService.QueryBySpecAsync(spec, wzdh);
            if (!result.Found && result.Message == "型号或指纹不能为空")
                return BadRequest(new { message = result.Message });

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "价格查询失败。spec={Spec}, wzdh={Wzdh}", spec, wzdh);
            return StatusCode(500, new { message = "查询失败，请稍后重试" });
        }
    }

    /// <summary>批量查询历史价格（最多 100 条）。</summary>
    [HttpPost("batch")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Batch([FromBody] PriceBatchQueryRequest? request)
    {
        if (request == null)
            return BadRequest(new { message = "请求体不能为空" });

        var hasSpecs = request.Specs?.Any(s => !string.IsNullOrWhiteSpace(s)) == true;
        var hasWzdh = request.WzdhList?.Any(s => !string.IsNullOrWhiteSpace(s)) == true;
        if (!hasSpecs && !hasWzdh)
            return BadRequest(new { message = "请提供 specs 或 wzdhList" });

        try
        {
            var result = await _priceQueryService.QueryBatchAsync(request);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量价格查询失败");
            return StatusCode(500, new { message = "查询失败，请稍后重试" });
        }
    }
}
