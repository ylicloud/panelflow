using Microsoft.EntityFrameworkCore;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;
using PanelFlow.Infrastructure.Data;
using PanelFlow.Infrastructure.Entities;

namespace PanelFlow.Infrastructure.Services;

public class CustomerContactService : ICustomerContactService
{
    private readonly ApplicationDbContext _db;

    public CustomerContactService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<CustomerContactDto>> GetByCompanyNoAsync(string companyNo)
    {
        var key = Safe(companyNo, 10);
        return await _db.KhylbCustomerContacts
            .AsNoTracking()
            .Where(x => x.gsbh == key)
            .OrderByDescending(x => x.is_default)
            .ThenBy(x => x.sort_no)
            .ThenBy(x => x.Id)
            .Select(x => ToDto(x))
            .ToListAsync();
    }

    public async Task<(bool Success, string Message)> CreateAsync(CustomerContactDto dto)
    {
        var companyNo = Safe(dto.CompanyNo, 10);
        var contactName = Safe(dto.ContactName, 100);
        var phone = Safe(dto.Phone, 40);
        var email = Safe(dto.Email, 100);
        var title = Safe(dto.Title, 50);

        if (string.IsNullOrWhiteSpace(companyNo))
            return (false, "公司编号不能为空");
        if (string.IsNullOrWhiteSpace(contactName))
            return (false, "联系人不能为空");

        var customer = await _db.KhylbCustomers.FirstOrDefaultAsync(x => x.gsbh == companyNo);
        if (customer == null)
            return (false, "客户不存在");

        var duplicate = await HasDuplicateAsync(companyNo, contactName, phone, null);
        if (duplicate)
            return (false, "该客户下已存在相同联系人和联系电话");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var now = DateTime.Now;
            var existingCount = await _db.KhylbCustomerContacts.CountAsync(x => x.gsbh == companyNo);
            var isFirst = existingCount == 0;

            var entity = new KhylbCustomerContact
            {
                gsbh = companyNo,
                lxr = contactName,
                lxdh = phone,
                email = email,
                zw = title,
                is_default = isFirst,
                sort_no = dto.SortNo <= 0 ? 100 : dto.SortNo,
                is_enabled = dto.IsEnabled,
                created_at = now,
                updated_at = now
            };

            _db.KhylbCustomerContacts.Add(entity);
            await _db.SaveChangesAsync();

            if (isFirst)
            {
                await SyncDefaultContactToCustomerAsync(companyNo, entity);
            }

            await tx.CommitAsync();
            return (true, "联系人创建成功");
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<(bool Success, string Message)> UpdateAsync(CustomerContactDto dto)
    {
        var companyNo = Safe(dto.CompanyNo, 10);
        var contactName = Safe(dto.ContactName, 100);
        var phone = Safe(dto.Phone, 40);
        var email = Safe(dto.Email, 100);
        var title = Safe(dto.Title, 50);

        if (string.IsNullOrWhiteSpace(companyNo))
            return (false, "公司编号不能为空");
        if (dto.Id <= 0)
            return (false, "联系人参数无效");
        if (string.IsNullOrWhiteSpace(contactName))
            return (false, "联系人不能为空");

        var duplicate = await HasDuplicateAsync(companyNo, contactName, phone, dto.Id);
        if (duplicate)
            return (false, "该客户下已存在相同联系人和联系电话");

        var entity = await _db.KhylbCustomerContacts.FirstOrDefaultAsync(x => x.gsbh == companyNo && x.Id == dto.Id);
        if (entity == null)
            return (false, "联系人不存在");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            entity.lxr = contactName;
            entity.lxdh = phone;
            entity.email = email;
            entity.zw = title;
            entity.sort_no = dto.SortNo <= 0 ? 100 : dto.SortNo;
            entity.is_enabled = dto.IsEnabled;
            entity.updated_at = DateTime.Now;

            _db.KhylbCustomerContacts.Update(entity);
            await _db.SaveChangesAsync();

            if (entity.is_default)
            {
                await SyncDefaultContactToCustomerAsync(companyNo, entity);
            }

            await tx.CommitAsync();
            return (true, "联系人信息已更新");
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<(bool Success, string Message)> DeleteAsync(string companyNo, int id)
    {
        var key = Safe(companyNo, 10);
        if (string.IsNullOrWhiteSpace(key) || id <= 0)
            return (false, "联系人参数无效");

        var entity = await _db.KhylbCustomerContacts.FirstOrDefaultAsync(x => x.gsbh == key && x.Id == id);
        if (entity == null)
            return (false, "联系人不存在");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var wasDefault = entity.is_default;
            _db.KhylbCustomerContacts.Remove(entity);
            await _db.SaveChangesAsync();

            if (wasDefault)
            {
                var nextDefault = await _db.KhylbCustomerContacts
                    .Where(x => x.gsbh == key)
                    .OrderBy(x => x.sort_no)
                    .ThenBy(x => x.Id)
                    .FirstOrDefaultAsync();

                if (nextDefault == null)
                {
                    await ClearDefaultContactOnCustomerAsync(key);
                }
                else
                {
                    nextDefault.is_default = true;
                    nextDefault.updated_at = DateTime.Now;
                    await _db.SaveChangesAsync();
                    await SyncDefaultContactToCustomerAsync(key, nextDefault);
                }
            }

            await tx.CommitAsync();
            return (true, "联系人已删除");
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<(bool Success, string Message)> SetDefaultAsync(string companyNo, int id)
    {
        var key = Safe(companyNo, 10);
        if (string.IsNullOrWhiteSpace(key) || id <= 0)
            return (false, "联系人参数无效");

        var entity = await _db.KhylbCustomerContacts.FirstOrDefaultAsync(x => x.gsbh == key && x.Id == id);
        if (entity == null)
            return (false, "联系人不存在");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var contacts = await _db.KhylbCustomerContacts.Where(x => x.gsbh == key).ToListAsync();
            foreach (var item in contacts)
            {
                item.is_default = item.Id == id;
                item.updated_at = DateTime.Now;
            }

            await _db.SaveChangesAsync();
            await SyncDefaultContactToCustomerAsync(key, entity);
            await tx.CommitAsync();
            return (true, "默认联系人已更新");
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private async Task<bool> HasDuplicateAsync(string companyNo, string contactName, string phone, int? excludeId)
    {
        return await _db.KhylbCustomerContacts.AnyAsync(x =>
            x.gsbh == companyNo &&
            x.lxr == contactName &&
            (x.lxdh ?? string.Empty) == phone &&
            (!excludeId.HasValue || x.Id != excludeId.Value));
    }

    private async Task SyncDefaultContactToCustomerAsync(string companyNo, KhylbCustomerContact entity)
    {
        var customer = await _db.KhylbCustomers.FirstOrDefaultAsync(x => x.gsbh == companyNo);
        if (customer == null)
            return;

        customer.lxr = entity.lxr;
        customer.lxdh = entity.lxdh;
        customer.updated_at = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    private async Task ClearDefaultContactOnCustomerAsync(string companyNo)
    {
        var customer = await _db.KhylbCustomers.FirstOrDefaultAsync(x => x.gsbh == companyNo);
        if (customer == null)
            return;

        customer.lxr = string.Empty;
        customer.lxdh = string.Empty;
        customer.updated_at = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    private static CustomerContactDto ToDto(KhylbCustomerContact x)
    {
        return new CustomerContactDto
        {
            Id = x.Id,
            CompanyNo = SafeTrim(x.gsbh),
            ContactName = SafeTrim(x.lxr),
            Phone = SafeTrim(x.lxdh),
            Email = SafeTrim(x.email),
            Title = SafeTrim(x.zw),
            IsDefault = x.is_default,
            SortNo = x.sort_no,
            IsEnabled = x.is_enabled,
            CreatedAt = x.created_at,
            UpdatedAt = x.updated_at
        };
    }

    private static string Safe(string? value, int maxLen)
    {
        var v = (value ?? string.Empty).Trim();
        return v.Length <= maxLen ? v : v[..maxLen];
    }

    private static string SafeTrim(string? value) => value?.Trim() ?? string.Empty;
}
