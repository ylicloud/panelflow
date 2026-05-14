using Microsoft.EntityFrameworkCore;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;
using PanelFlow.Infrastructure.Data;
using PanelFlow.Infrastructure.Entities;

namespace PanelFlow.Infrastructure.Services;

public class CustomerService : ICustomerService
{
    private readonly ApplicationDbContext _db;

    public CustomerService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<CustomerDto>> GetListAsync(string? keyword)
    {
        var query = _db.KhylbCustomers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var q = keyword.Trim();
            // 各字段 Trim 后与界面 SafeTrim 一致，避免首尾空格导致搜不到
            query = query.Where(x =>
                x.gsbh.Trim().Contains(q) ||
                x.gsmc.Trim().Contains(q) ||
                x.gsld.Trim().Contains(q) ||
                x.lxr.Trim().Contains(q) ||
                x.lxdh.Trim().Contains(q));
        }

        return await query
            .OrderByDescending(x => x.updated_at ?? DateTime.MinValue)
            .ThenBy(x => x.gsbh)
            .Select(x => new CustomerDto
            {
                CompanyName = SafeTrim(x.gsmc),
                CompanyNo = SafeTrim(x.gsbh),
                Alias = SafeTrim(x.gsld),
                Contact = SafeTrim(x.lxr),
                Phone = SafeTrim(x.lxdh),
                Remark = SafeTrim(x.beizhu),
                CreatedAt = x.created_at,
                UpdatedAt = x.updated_at
            })
            .ToListAsync();
    }

    public async Task<CustomerDto?> GetByCompanyNoAsync(string companyNo)
    {
        if (string.IsNullOrWhiteSpace(companyNo))
            return null;

        var key = companyNo.Trim();
        var entity = await _db.KhylbCustomers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.gsbh == key);
        return entity == null ? null : ToDto(entity);
    }

    public async Task<(bool Success, string Message)> CreateAsync(CustomerDto dto)
    {
        var companyNo = Safe(dto.CompanyNo, 10);
        var companyName = Safe(dto.CompanyName, 50);
        var alias = Safe(dto.Alias, 10);
        if (string.IsNullOrWhiteSpace(companyNo))
            return (false, "公司编号不能为空");
        if (string.IsNullOrWhiteSpace(companyName))
            return (false, "公司名称不能为空");

        var exists = await _db.KhylbCustomers.AnyAsync(x => x.gsbh == companyNo);
        if (exists)
            return (false, $"公司编号 \"{companyNo}\" 已存在");

        var duplicateName = await _db.KhylbCustomers.AnyAsync(x => x.gsmc == companyName);
        if (duplicateName)
            return (false, $"公司名称 \"{companyName}\" 已存在");

        if (!string.IsNullOrWhiteSpace(alias))
        {
            var duplicateAlias = await _db.KhylbCustomers.AnyAsync(x => x.gsld == alias);
            if (duplicateAlias)
                return (false, $"公司别名 \"{alias}\" 已存在");
        }

        var now = DateTime.Now;
        var entity = new KhylbCustomer
        {
            gsbh = companyNo,
            gsmc = companyName,
            gsld = alias,
            lxr = string.Empty,
            lxdh = string.Empty,
            beizhu = Safe(dto.Remark, 100),
            created_at = now,
            updated_at = now
        };

        _db.KhylbCustomers.Add(entity);
        await _db.SaveChangesAsync();
        return (true, "客户创建成功");
    }

    public async Task<(bool Success, string Message)> UpdateAsync(CustomerDto dto)
    {
        var companyNo = Safe(dto.CompanyNo, 10);
        var companyName = Safe(dto.CompanyName, 50);
        var alias = Safe(dto.Alias, 10);
        if (string.IsNullOrWhiteSpace(companyNo))
            return (false, "公司编号不能为空");
        if (string.IsNullOrWhiteSpace(companyName))
            return (false, "公司名称不能为空");

        var entity = await _db.KhylbCustomers.FirstOrDefaultAsync(x => x.gsbh == companyNo);
        if (entity == null)
            return (false, "客户不存在");

        var duplicateName = await _db.KhylbCustomers
            .AnyAsync(x => x.gsbh != companyNo && x.gsmc == companyName);
        if (duplicateName)
            return (false, $"公司名称 \"{companyName}\" 已存在");

        if (!string.IsNullOrWhiteSpace(alias))
        {
            var duplicateAlias = await _db.KhylbCustomers
                .AnyAsync(x => x.gsbh != companyNo && x.gsld == alias);
            if (duplicateAlias)
                return (false, $"公司别名 \"{alias}\" 已存在");
        }

        entity.gsmc = companyName;
        entity.gsld = alias;
        entity.beizhu = Safe(dto.Remark, 100);
        entity.updated_at = DateTime.Now;

        _db.KhylbCustomers.Update(entity);
        await _db.SaveChangesAsync();
        return (true, "客户信息已更新");
    }

    private static string SafeTrim(string? value) => value?.Trim() ?? string.Empty;

    private static string Safe(string? value, int maxLen)
    {
        var v = (value ?? string.Empty).Trim();
        return v.Length <= maxLen ? v : v[..maxLen];
    }

    private static CustomerDto ToDto(KhylbCustomer x)
    {
        return new CustomerDto
        {
            CompanyName = SafeTrim(x.gsmc),
            CompanyNo = SafeTrim(x.gsbh),
            Alias = SafeTrim(x.gsld),
            Contact = SafeTrim(x.lxr),
            Phone = SafeTrim(x.lxdh),
            Remark = SafeTrim(x.beizhu),
            CreatedAt = x.created_at,
            UpdatedAt = x.updated_at
        };
    }
}
