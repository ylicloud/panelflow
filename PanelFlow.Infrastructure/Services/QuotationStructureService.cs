using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;
using PanelFlow.Core.Rules;
using PanelFlow.Core.Services;
using PanelFlow.Infrastructure.Data;
using PanelFlow.Infrastructure.Entities;
using System.Globalization;
using System.Text.Json;

namespace PanelFlow.Infrastructure.Services;

public class QuotationStructureService : IQuotationStructureService
{
    private readonly ApplicationDbContext _db;
    private readonly IElementDictService _elementDictService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<QuotationStructureService> _logger;

    public QuotationStructureService(
        ApplicationDbContext db,
        IElementDictService elementDictService,
        IAuditLogService auditLogService,
        ILogger<QuotationStructureService> logger)
    {
        _db = db;
        _elementDictService = elementDictService;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<QuotationStructureDto?> GetTreeAsync(string fabh, string loginUserName, string loginRole)
    {
        var quotation = await LoadQuotationAsync(fabh);
        if (quotation == null) return null;

        var (roots, orphans) = await BuildTreeFromDbAsync(fabh);
        var canEdit = CanEditQuotation(quotation, loginUserName, loginRole);

        return new QuotationStructureDto
        {
            Fabh = fabh.Trim(),
            QuotationName = (quotation.famc ?? string.Empty).Trim(),
            Quoter = (quotation.bjr ?? string.Empty).Trim(),
            CurrentStatus = quotation.dqzt,
            CanEdit = canEdit && !QuotationEditRules.IsEstablished(quotation.dqzt),
            OrphanCount = orphans.Count,
            Tree = roots.Select(ToTreeDto).ToList()
        };
    }

    public async Task<StructureApplyResult> ApplyAsync(
        StructureApplyRequest request, string loginUserName, string loginRole)
    {
        var fabh = (request.Fabh ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(fabh))
        {
            return Fail("报价单编号不能为空");
        }

        var quotation = await LoadQuotationAsync(fabh);
        if (quotation == null)
        {
            return Fail("报价单不存在");
        }

        if (QuotationEditRules.IsEstablished(quotation.dqzt))
        {
            return Fail("报价单已成立，不可修改结构");
        }

        if (!CanEditQuotation(quotation, loginUserName, loginRole))
        {
            return Fail("仅报价人本人或管理员可修改该报价单结构");
        }

        if (request.Operations == null || request.Operations.Count == 0)
        {
            return Fail("未指定任何操作");
        }

        var (roots, orphans) = await BuildTreeFromDbAsync(fabh);
        var stats = new ApplyStats();

        foreach (var op in request.Operations)
        {
            var type = (op.Type ?? string.Empty).Trim();
            switch (type)
            {
                case "AddLevel1":
                    await ApplyAddLevel1Async(roots, op, stats);
                    break;
                case "AddLevel2":
                    await ApplyAddLevel2Async(roots, op, stats);
                    break;
                case "AddLevel3":
                    await ApplyAddLevel3Async(roots, op, stats);
                    break;
                case "RemoveLevel2ByDict":
                    await ApplyRemoveLevel2ByDictAsync(roots, op, stats);
                    break;
                case "RemoveLevel3ByDict":
                    await ApplyRemoveLevel3ByDictAsync(roots, op, stats);
                    break;
                case "Delete":
                    ApplyDelete(roots, op, stats);
                    break;
                case "Rename":
                    ApplyRename(roots, op, stats);
                    break;
                case "ReorderSiblings":
                    ApplyReorder(roots, op, stats);
                    break;
                default:
                    return Fail($"未知操作类型：{type}");
            }

            if (!string.IsNullOrEmpty(stats.Error))
            {
                return Fail(stats.Error);
            }
        }

        ReencodeTree(roots);
        var flatRows = FlattenTree(roots);
        flatRows.AddRange(orphans);

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $@"DELETE FROM BJB WHERE fabh = {fabh} AND x_bm NOT IN ('0', '9999')");

            foreach (var node in flatRows)
            {
                var d = node.Data;
                var code = node.EffectiveCode;
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO BJB
(fabh, x_bm, x_mc, x_dw, x_dj, x_fdds, x_sl, x_bj_fdds, x_bj_dj, x_bjb_bj, x_bjb_dj,
 x_bjb_datetime, x_bjb_fdds, x_wzfy, x_flbh, x_ggxh, x_sccj, x_key_ry, x_jsgsbh, x_bz, x_wzdh, x_lx, x_cgf)
VALUES
({fabh}, {code}, {node.Name.Trim()}, {d.Xdw}, {d.Xdj}, {d.Xfdds}, {d.Xsl}, {d.XbjFdds}, {d.XbjDj}, {d.XbjbBj}, {d.XbjbDj},
 {d.XbjbDatetime}, {d.XbjbFdds}, {d.Xwzfy}, {d.Xflbh}, {d.Xggxh}, {d.Xsccj}, {d.XkeyRy}, {d.Xjsgsbh}, {d.Xbz}, {d.Xwzdh}, {node.Xlx}, {d.Xcgf})");
            }

            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "ApplyStructure failed for {Fabh}", fabh);
            return Fail($"保存失败：{ex.Message}");
        }

        await _auditLogService.WriteAsync(new AuditLogEntry
        {
            ActionType = "ApplyStructure",
            Module = "Quotation",
            EntityName = "BJB",
            EntityId = fabh,
            UserName = loginUserName,
            RoleName = loginRole,
            IsSuccess = true,
            AfterData = JsonSerializer.Serialize(new
            {
                fabh,
                stats.AddedCount,
                stats.SkippedCount,
                stats.DeletedCount,
                stats.RenamedCount,
                stats.ReorderedCount,
                totalRows = flatRows.Count
            })
        });

        return new StructureApplyResult
        {
            Success = true,
            Message = BuildSummaryMessage(stats),
            AddedCount = stats.AddedCount,
            SkippedCount = stats.SkippedCount,
            DeletedCount = stats.DeletedCount,
            RenamedCount = stats.RenamedCount,
            ReorderedCount = stats.ReorderedCount,
            TotalRowsWritten = flatRows.Count
        };
    }

    private async Task ApplyAddLevel1Async(
        List<QuotationTreeNode> roots, StructureOperation op, ApplyStats stats)
    {
        var dictItems = await LoadDictItemsAsync(1, op.DictIds);
        if (dictItems.Count == 0)
        {
            stats.Error = "未选择有效的第1级扩展项";
            return;
        }

        foreach (var dict in dictItems)
        {
            if (roots.Any(r => string.Equals(r.Name.Trim(), dict.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                stats.SkippedCount++;
                continue;
            }

            roots.Add(CreateNodeFromDict(dict, parent: null!));
            stats.AddedCount++;
        }

        SortChildrenByDictOrder(roots, level: 1);
    }

    private async Task ApplyAddLevel2Async(
        List<QuotationTreeNode> roots, StructureOperation op, ApplyStats stats)
    {
        var dictItems = await LoadDictItemsAsync(2, op.DictIds);
        if (dictItems.Count == 0)
        {
            stats.Error = "未选择有效的第2级通用项";
            return;
        }

        foreach (var code in NormalizeCodes(op.TargetCodes))
        {
            var parent = FindNodeByCode(roots, code);
            if (parent == null || parent.Level != 1)
            {
                stats.Error = $"未找到第1级节点：{code}";
                return;
            }

            foreach (var dict in dictItems)
            {
                if (parent.Children.Any(c => c.Xlx == dict.Xlx
                    || string.Equals(c.Name.Trim(), dict.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    stats.SkippedCount++;
                    continue;
                }

                parent.Children.Add(CreateNodeFromDict(dict, parent));
                stats.AddedCount++;
            }

            SortChildrenByDictOrder(parent.Children, level: 2);
        }
    }

    private async Task ApplyAddLevel3Async(
        List<QuotationTreeNode> roots, StructureOperation op, ApplyStats stats)
    {
        var dictItems = await LoadDictItemsAsync(3, op.DictIds);
        if (dictItems.Count == 0)
        {
            stats.Error = "未选择有效的第3级通用项";
            return;
        }

        foreach (var code in NormalizeCodes(op.TargetCodes))
        {
            var parent = ResolveLevel3Parent(roots, code);
            if (parent == null)
            {
                stats.Error = $"未找到可挂入的第2级节点：{code}";
                return;
            }

            if (parent.Level != 2)
            {
                stats.Error = $"节点 {code} 不是第2级属性，无法挂入元件";
                return;
            }

            foreach (var dict in dictItems)
            {
                if (IsLevel3Duplicate(parent, dict))
                {
                    stats.SkippedCount++;
                    continue;
                }

                parent.Children.Add(CreateNodeFromDict(dict, parent));
                stats.AddedCount++;
            }

            SortChildrenByDictOrder(parent.Children, level: 3);
        }
    }

    private static QuotationTreeNode? ResolveLevel3Parent(List<QuotationTreeNode> roots, string code)
    {
        var node = FindNodeByCode(roots, code);
        if (node == null) return null;
        if (node.Level == 2) return node;

        if (node.Level == 1)
        {
            var device = node.Children.FirstOrDefault(c => c.Xlx == 1)
                ?? node.Children.FirstOrDefault(c => c.IsLockedDevice);
            return device;
        }

        return null;
    }

    private async Task ApplyRemoveLevel2ByDictAsync(
        List<QuotationTreeNode> roots, StructureOperation op, ApplyStats stats)
    {
        var dictItems = await LoadDictItemsAsync(2, op.DictIds);
        if (dictItems.Count == 0)
        {
            stats.Error = "未选择要移除的第2级属性";
            return;
        }

        foreach (var code in NormalizeCodes(op.TargetCodes))
        {
            var cabinet = FindNodeByCode(roots, code);
            if (cabinet == null || cabinet.Level != 1)
            {
                stats.Error = $"未找到第1级节点：{code}";
                return;
            }

            var cabinetLabel = cabinet.Name.Trim();

            foreach (var dict in dictItems)
            {
                var node = cabinet.Children.FirstOrDefault(c => c.Xlx == dict.Xlx);
                if (node == null)
                {
                    stats.SkippedCount++;
                    continue;
                }

                if (node.IsLockedDevice)
                {
                    stats.SkippedCount++;
                    stats.SkippedDetails.Add($"「{cabinetLabel}」器件为锁定项，已跳过");
                    continue;
                }

                if (node.Children.Count > 0)
                {
                    stats.SkippedCount++;
                    stats.SkippedDetails.Add(
                        $"「{cabinetLabel}」的「{node.Name.Trim()}」下仍有元件，已跳过");
                    continue;
                }

                cabinet.Children.Remove(node);
                stats.DeletedCount++;
            }
        }
    }

    private async Task ApplyRemoveLevel3ByDictAsync(
        List<QuotationTreeNode> roots, StructureOperation op, ApplyStats stats)
    {
        var dictItems = await LoadDictItemsAsync(3, op.DictIds);
        if (dictItems.Count == 0)
        {
            stats.Error = "未选择要移除的第3级元件";
            return;
        }

        foreach (var code in NormalizeCodes(op.TargetCodes))
        {
            var cabinet = FindNodeByCode(roots, code);
            if (cabinet == null || cabinet.Level != 1)
            {
                stats.Error = $"未找到第1级节点：{code}";
                return;
            }

            var device = cabinet.Children.FirstOrDefault(c => c.Xlx == 1)
                ?? cabinet.Children.FirstOrDefault(c => c.IsLockedDevice);
            if (device == null)
            {
                stats.SkippedCount++;
                stats.SkippedDetails.Add($"「{cabinet.Name.Trim()}」无器件节点，已跳过");
                continue;
            }

            foreach (var dict in dictItems)
            {
                var dictName = dict.Name.Trim();
                var dictGgxh = (dict.Ggxh ?? string.Empty).Trim();
                var toRemove = device.Children
                    .Where(c => string.Equals(c.Name.Trim(), dictName, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(c.Data.Xggxh.Trim(), dictGgxh, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (toRemove.Count == 0)
                {
                    stats.SkippedCount++;
                    continue;
                }

                foreach (var node in toRemove)
                {
                    device.Children.Remove(node);
                    stats.DeletedCount++;
                }
            }
        }
    }

    private static void ApplyDelete(List<QuotationTreeNode> roots, StructureOperation op, ApplyStats stats)
    {
        foreach (var code in NormalizeCodes(op.TargetCodes))
        {
            if (!TryFindParent(roots, code, out var parent, out var node))
            {
                stats.Error = $"未找到节点：{code}";
                return;
            }

            if (node.Level is not (2 or 3))
            {
                stats.Error = $"节点 {code} 不允许删除（仅第2/3级可删）";
                return;
            }

            if (node.IsLockedDevice)
            {
                stats.Error = "器件为锁定节点，不可删除";
                return;
            }

            if (node.Level == 2 && node.Children.Count > 0)
            {
                stats.Error = $"节点「{node.Name.Trim()}」下仍有元件，请先处理元件";
                return;
            }

            parent!.Children.Remove(node);
            stats.DeletedCount++;
        }
    }

    private static void ApplyRename(List<QuotationTreeNode> roots, StructureOperation op, ApplyStats stats)
    {
        var newName = (op.NewName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            stats.Error = "新名称不能为空";
            return;
        }

        foreach (var code in NormalizeCodes(op.TargetCodes))
        {
            var node = FindNodeByCode(roots, code);
            if (node == null)
            {
                stats.Error = $"未找到节点：{code}";
                return;
            }

            if (node.Level is not (2 or 3))
            {
                stats.Error = $"节点 {code} 不允许改名（仅第2/3级可改）";
                return;
            }

            node.Name = newName;
            stats.RenamedCount++;
        }
    }

    private static void ApplyReorder(List<QuotationTreeNode> roots, StructureOperation op, ApplyStats stats)
    {
        if (string.IsNullOrWhiteSpace(op.Reason))
        {
            stats.Error = "调整顺序必须填写理由";
            return;
        }

        var ordered = NormalizeCodes(op.OrderedCodes ?? []);
        if (ordered.Count == 0)
        {
            stats.Error = "排序列表为空";
            return;
        }

        var parentCode = (op.ParentCode ?? string.Empty).Trim();
        List<QuotationTreeNode> siblings;
        if (string.IsNullOrEmpty(parentCode))
        {
            siblings = roots;
        }
        else
        {
            var parent = FindNodeByCode(roots, parentCode);
            if (parent == null)
            {
                stats.Error = $"未找到父节点：{parentCode}";
                return;
            }

            siblings = parent.Children;
        }

        if (ordered.Count != siblings.Count)
        {
            stats.Error = "排序列表与同级节点数量不一致，请刷新后重试";
            return;
        }

        var byCode = siblings.ToDictionary(x => x.Xbm.Trim(), StringComparer.OrdinalIgnoreCase);
        if (!ordered.All(c => byCode.ContainsKey(c)))
        {
            stats.Error = "排序列表包含无效节点，请刷新后重试";
            return;
        }

        var locked = siblings.FirstOrDefault(x => x.IsLockedDevice);
        if (locked != null && !string.Equals(ordered[0], locked.Xbm.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            stats.Error = "器件为固定首位，不可调整";
            return;
        }

        var reordered = ordered.Select(c => byCode[c]).ToList();
        if (string.IsNullOrEmpty(parentCode))
        {
            roots.Clear();
            roots.AddRange(reordered);
        }
        else
        {
            var parent = FindNodeByCode(roots, parentCode)!;
            parent.Children = reordered;
        }

        stats.ReorderedCount++;
    }

    private async Task<List<ElementDictDto>> LoadDictItemsAsync(byte level, List<int> dictIds)
    {
        if (dictIds == null || dictIds.Count == 0) return [];

        var all = await _elementDictService.GetByLevelAsync(level, includeDisabled: false);
        var idSet = dictIds.ToHashSet();
        return all.Where(x => idSet.Contains(x.Id)).OrderBy(x => x.SortOrder).ToList();
    }

    /// <summary>
    /// 字典项 → BJB 行字段：Name→x_mc、Xlx→x_lx、Ggxh→x_ggxh、Amount→x_sl、DefaultUnit→x_dw；
    /// SortOrder 经同级排序 + 重编码写入 x_bm。
    /// </summary>
    private static QuotationTreeNode CreateNodeFromDict(ElementDictDto dict, QuotationTreeNode? parent)
    {
        var ggxh = (dict.Ggxh ?? string.Empty).Trim();
        var snapshot = new BjbRowSnapshot
        {
            Xdw = (dict.DefaultUnit ?? string.Empty).Trim(),
            Xsl = dict.Amount > 0 ? dict.Amount : 1m,
            Xggxh = ggxh
        };

        return new QuotationTreeNode
        {
            Xbm = "NEW",
            Name = dict.Name.Trim(),
            Xlx = dict.Xlx,
            DictSortOrder = dict.SortOrder,
            IsNew = true,
            Data = snapshot
        };
    }

    private static bool IsLevel3Duplicate(QuotationTreeNode parent, ElementDictDto dict)
    {
        var dictName = dict.Name.Trim();
        var dictGgxh = (dict.Ggxh ?? string.Empty).Trim();
        return parent.Children.Any(c =>
            string.Equals(c.Name.Trim(), dictName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(c.Data.Xggxh.Trim(), dictGgxh, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>按字典 SortOrder 重排同级节点，已有节点以 x_bm 后缀为序。</summary>
    private static void SortChildrenByDictOrder(List<QuotationTreeNode> children, int level)
    {
        if (children.Count <= 1) return;

        IEnumerable<QuotationTreeNode> ordered = level == 2
            ? children.OrderBy(n => n.IsLockedDevice ? 0 : 1).ThenBy(GetDictSortKey)
            : children.OrderBy(GetDictSortKey);

        var sorted = ordered.ToList();
        children.Clear();
        children.AddRange(sorted);
    }

    private static int GetDictSortKey(QuotationTreeNode node)
    {
        if (node.DictSortOrder > 0) return node.DictSortOrder;
        var code = node.EffectiveCode;
        return code.Length >= 4 && int.TryParse(code[^4..], NumberStyles.None, CultureInfo.InvariantCulture, out var seq)
            ? seq
            : int.MaxValue;
    }

    private static void ReencodeTree(List<QuotationTreeNode> roots)
    {
        ReencodeChildren(roots, string.Empty, 1);
    }

    private static void ReencodeChildren(List<QuotationTreeNode> nodes, string parentPrefix, int level)
    {
        var ordered = OrderSiblings(nodes, level);
        for (var i = 0; i < ordered.Count; i++)
        {
            var seq = (i + 1).ToString("D4", CultureInfo.InvariantCulture);
            var node = ordered[i];
            node.NewXbm = level == 1 ? seq : parentPrefix + seq;
            if (node.Children.Count > 0)
            {
                ReencodeChildren(node.Children, node.NewXbm, level + 1);
            }
        }

        nodes.Clear();
        nodes.AddRange(ordered);
    }

    private static List<QuotationTreeNode> OrderSiblings(List<QuotationTreeNode> nodes, int level)
    {
        if (level == 2)
        {
            var locked = nodes.Where(x => x.IsLockedDevice).ToList();
            var others = nodes.Where(x => !x.IsLockedDevice).ToList();
            locked.AddRange(others);
            return locked;
        }

        return nodes.ToList();
    }

    private static List<QuotationTreeNode> FlattenTree(List<QuotationTreeNode> roots)
    {
        var result = new List<QuotationTreeNode>();
        void Walk(QuotationTreeNode n)
        {
            result.Add(n);
            foreach (var c in n.Children) Walk(c);
        }

        foreach (var r in roots) Walk(r);
        return result;
    }

    private async Task<(List<QuotationTreeNode> Roots, List<QuotationTreeNode> Orphans)> BuildTreeFromDbAsync(string fabh)
    {
        var rows = await _db.Database.SqlQueryRaw<BjbFullRow>($@"
SELECT
    RTRIM(x_bm) AS Xbm,
    RTRIM(ISNULL(x_mc, '')) AS Xmc,
    RTRIM(ISNULL(x_dw, '')) AS Xdw,
    ISNULL(x_dj, 0) AS Xdj,
    ISNULL(x_fdds, 0) AS Xfdds,
    ISNULL(x_sl, 0) AS Xsl,
    ISNULL(x_bj_fdds, 0) AS XbjFdds,
    ISNULL(x_bj_dj, 0) AS XbjDj,
    ISNULL(x_bjb_bj, 0) AS XbjbBj,
    ISNULL(x_bjb_dj, 0) AS XbjbDj,
    x_bjb_datetime AS XbjbDatetime,
    ISNULL(x_bjb_fdds, 0) AS XbjbFdds,
    ISNULL(x_wzfy, 0) AS Xwzfy,
    RTRIM(ISNULL(x_flbh, '')) AS Xflbh,
    RTRIM(ISNULL(x_ggxh, '')) AS Xggxh,
    RTRIM(ISNULL(x_sccj, '')) AS Xsccj,
    RTRIM(ISNULL(x_key_ry, '')) AS XkeyRy,
    ISNULL(x_jsgsbh, 0) AS Xjsgsbh,
    RTRIM(ISNULL(x_bz, '')) AS Xbz,
    RTRIM(ISNULL(x_wzdh, '')) AS Xwzdh,
    ISNULL(x_lx, 0) AS Xlx,
    ISNULL(x_cgf, 1) AS Xcgf
FROM BJB
WHERE fabh = {{0}}
  AND RTRIM(x_bm) NOT IN ('0', '9999')", fabh.Trim()).ToListAsync();

        var nodesByCode = new Dictionary<string, QuotationTreeNode>(StringComparer.OrdinalIgnoreCase);
        var level1 = new List<QuotationTreeNode>();
        var level2 = new List<(QuotationTreeNode Node, string ParentCode)>();
        var level3 = new List<(QuotationTreeNode Node, string ParentCode)>();
        var orphans = new List<QuotationTreeNode>();

        foreach (var row in rows)
        {
            var code = (row.Xbm ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(code)) continue;

            var node = new QuotationTreeNode
            {
                Xbm = code,
                NewXbm = code,
                Name = (row.Xmc ?? string.Empty).Trim(),
                Xlx = row.Xlx,
                Data = row.ToSnapshot()
            };
            nodesByCode[code] = node;

            switch (code.Length)
            {
                case 4:
                    level1.Add(node);
                    break;
                case 8:
                    level2.Add((node, code[..4]));
                    break;
                case 12:
                    level3.Add((node, code[..8]));
                    break;
                default:
                    orphans.Add(node);
                    _logger.LogWarning("BJB orphan row fabh={Fabh} x_bm={Xbm} invalid length", fabh, code);
                    break;
            }
        }

        level1.Sort((a, b) => string.Compare(a.Xbm, b.Xbm, StringComparison.Ordinal));
        foreach (var (node, parentCode) in level2)
        {
            if (nodesByCode.TryGetValue(parentCode, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                orphans.Add(node);
                _logger.LogWarning("BJB orphan L2 fabh={Fabh} x_bm={Xbm} parent missing", fabh, node.Xbm);
            }
        }

        foreach (var parent in nodesByCode.Values.Where(n => n.Level == 1))
        {
            parent.Children.Sort((a, b) => string.Compare(a.Xbm, b.Xbm, StringComparison.Ordinal));
        }

        foreach (var (node, parentCode) in level3)
        {
            if (nodesByCode.TryGetValue(parentCode, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                orphans.Add(node);
                _logger.LogWarning("BJB orphan L3 fabh={Fabh} x_bm={Xbm} parent missing", fabh, node.Xbm);
            }
        }

        foreach (var parent in nodesByCode.Values.Where(n => n.Level == 2))
        {
            parent.Children.Sort((a, b) => string.Compare(a.Xbm, b.Xbm, StringComparison.Ordinal));
        }

        return (level1, orphans);
    }

    private async Task<BjfatQuotation?> LoadQuotationAsync(string fabh) =>
        await _db.BjfatQuotations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.fabh == fabh.Trim());

    private static bool CanEditQuotation(BjfatQuotation quotation, string loginUserName, string loginRole)
    {
        var isAdmin = string.Equals(loginRole.Trim(), RoleNames.Admin, StringComparison.OrdinalIgnoreCase);
        return isAdmin || QuotationEditRules.IsOwner(quotation.bjr, loginUserName);
    }

    private static QuotationStructureTreeNodeDto ToTreeDto(QuotationTreeNode node) => new()
    {
        Code = node.Xbm.Trim(),
        Name = string.IsNullOrWhiteSpace(node.Name) ? node.Xbm.Trim() : node.Name.Trim(),
        Xlx = node.Xlx,
        Level = node.Level,
        IsReadOnly = node.Level == 3,
        IsLocked = node.IsLockedDevice,
        Children = node.Children.Select(ToTreeDto).ToList()
    };

    private static QuotationTreeNode? FindNodeByCode(List<QuotationTreeNode> roots, string code)
    {
        foreach (var root in roots)
        {
            var found = FindNodeRecursive(root, code);
            if (found != null) return found;
        }

        return null;
    }

    private static QuotationTreeNode? FindNodeRecursive(QuotationTreeNode node, string code)
    {
        if (string.Equals(node.Xbm.Trim(), code, StringComparison.OrdinalIgnoreCase))
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var found = FindNodeRecursive(child, code);
            if (found != null) return found;
        }

        return null;
    }

    private static bool TryFindParent(
        List<QuotationTreeNode> roots, string code,
        out QuotationTreeNode? parent, out QuotationTreeNode? node)
    {
        parent = null;
        node = null;
        foreach (var root in roots)
        {
            if (TryFindParentRecursive(root, null, code, out parent, out node))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFindParentRecursive(
        QuotationTreeNode current, QuotationTreeNode? currentParent, string code,
        out QuotationTreeNode? parent, out QuotationTreeNode? node)
    {
        if (string.Equals(current.Xbm.Trim(), code, StringComparison.OrdinalIgnoreCase))
        {
            parent = currentParent;
            node = current;
            return true;
        }

        foreach (var child in current.Children)
        {
            if (TryFindParentRecursive(child, current, code, out parent, out node))
            {
                return true;
            }
        }

        parent = null;
        node = null;
        return false;
    }

    private static List<string> NormalizeCodes(IEnumerable<string> codes) =>
        codes.Select(x => (x ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static StructureApplyResult Fail(string message) =>
        new() { Success = false, Message = message };

    private static string BuildSummaryMessage(ApplyStats stats)
    {
        var parts = new List<string>();
        if (stats.AddedCount > 0) parts.Add($"新增 {stats.AddedCount} 项");
        if (stats.SkippedCount > 0) parts.Add($"跳过 {stats.SkippedCount} 项");
        if (stats.DeletedCount > 0) parts.Add($"删除 {stats.DeletedCount} 项");
        if (stats.RenamedCount > 0) parts.Add($"改名 {stats.RenamedCount} 项");
        if (stats.ReorderedCount > 0) parts.Add($"重排 {stats.ReorderedCount} 组");

        var msg = parts.Count > 0 ? string.Join("，", parts) + "。" : "结构已保存。";
        if (stats.SkippedDetails.Count > 0)
        {
            var detail = string.Join("；", stats.SkippedDetails.Distinct().Take(10));
            if (stats.SkippedDetails.Count > 10)
            {
                detail += $" 等共 {stats.SkippedDetails.Count} 条";
            }

            msg += " " + detail + "。";
        }

        return msg;
    }

    private sealed class ApplyStats
    {
        public int AddedCount { get; set; }
        public int SkippedCount { get; set; }
        public int DeletedCount { get; set; }
        public int RenamedCount { get; set; }
        public int ReorderedCount { get; set; }
        public List<string> SkippedDetails { get; } = [];
        public string? Error { get; set; }
    }

    private sealed class BjbFullRow
    {
        public string Xbm { get; set; } = string.Empty;
        public string Xmc { get; set; } = string.Empty;
        public string Xdw { get; set; } = string.Empty;
        public decimal Xdj { get; set; }
        public decimal Xfdds { get; set; }
        public decimal Xsl { get; set; }
        public decimal XbjFdds { get; set; }
        public decimal XbjDj { get; set; }
        public decimal XbjbBj { get; set; }
        public decimal XbjbDj { get; set; }
        public DateTime? XbjbDatetime { get; set; }
        public decimal XbjbFdds { get; set; }
        public decimal Xwzfy { get; set; }
        public string Xflbh { get; set; } = string.Empty;
        public string Xggxh { get; set; } = string.Empty;
        public string Xsccj { get; set; } = string.Empty;
        public string XkeyRy { get; set; } = string.Empty;
        public decimal Xjsgsbh { get; set; }
        public string Xbz { get; set; } = string.Empty;
        public string Xwzdh { get; set; } = string.Empty;
        public int Xlx { get; set; }
        public int Xcgf { get; set; }

        public BjbRowSnapshot ToSnapshot() => new()
        {
            Xdw = Xdw,
            Xdj = Xdj,
            Xfdds = Xfdds,
            Xsl = Xsl,
            XbjFdds = XbjFdds,
            XbjDj = XbjDj,
            XbjbBj = XbjbBj,
            XbjbDj = XbjbDj,
            XbjbDatetime = XbjbDatetime,
            XbjbFdds = XbjbFdds,
            Xwzfy = Xwzfy,
            Xflbh = Xflbh,
            Xggxh = Xggxh,
            Xsccj = Xsccj,
            XkeyRy = XkeyRy,
            Xjsgsbh = Xjsgsbh,
            Xbz = Xbz,
            Xwzdh = Xwzdh,
            Xcgf = Xcgf
        };
    }
}
