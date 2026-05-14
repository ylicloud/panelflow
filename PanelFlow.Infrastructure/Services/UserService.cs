using Microsoft.EntityFrameworkCore;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;
using PanelFlow.Infrastructure.Data;
using PanelFlow.Infrastructure.Entities;

namespace PanelFlow.Infrastructure.Services;

public class UserService : IUserService
{
    private readonly ApplicationDbContext _db;
    private readonly IPasswordCryptoService _crypto;

    public UserService(ApplicationDbContext db, IPasswordCryptoService crypto)
    {
        _db = db;
        _crypto = crypto;
    }

    public async Task<List<UserDto>> GetAllAsync()
    {
        return await _db.SysUsers
            .OrderBy(u => u.yhbh)
            .Select(u => new UserDto
            {
                Username = u.yhmcc.Trim(),
                DisplayName = u.yhname.Trim(),
                Role = u.js,
                IsEnabled = u.IsEnabled,
                Remark = u.beizhu ?? "",
                LastLogin = u.LastLogin
            })
            .ToListAsync();
    }

    public async Task<UserDto?> GetByUsernameAsync(string username)
    {
        var u = await _db.SysUsers.FirstOrDefaultAsync(x => x.yhmcc == username.Trim());
        if (u == null) return null;

        return new UserDto
        {
            Username = u.yhmcc.Trim(),
            DisplayName = u.yhname.Trim(),
            Role = u.js,
            IsEnabled = u.IsEnabled,
            Remark = u.beizhu?.Trim() ?? "",
            LastLogin = u.LastLogin
        };
    }

    public async Task<(bool Success, string Message)> CreateAsync(
        string username, string displayName, int role, string password, string remark)
    {
        username = username.Trim();
        if (string.IsNullOrWhiteSpace(username))
            return (false, "登录名不能为空");

        var exists = await _db.SysUsers.AnyAsync(u => u.yhmcc == username);
        if (exists)
            return (false, $"登录名 \"{username}\" 已存在");

        var now = DateTime.Now;
        var seed = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);

        var nextCode = await GetNextUserCodeAsync();

        var user = new SysUser
        {
            yhmcc = username,
            yhname = displayName.Trim(),
            kl = _crypto.Encrypt(password.Trim(), seed),
            kzhdrq = seed,
            js = role,
            IsEnabled = true,
            beizhu = remark?.Trim() ?? "",
            bmbm = "0101",
            yhbh = nextCode,
            yhlx = 1,
            syxz = 0,
            zgyhbh = ""
        };

        _db.SysUsers.Add(user);
        await _db.SaveChangesAsync();
        return (true, "用户创建成功");
    }

    public async Task<(bool Success, string Message)> UpdateAsync(
        string username, string displayName, int role, bool isEnabled, string remark)
    {
        var user = await _db.SysUsers.FirstOrDefaultAsync(x => x.yhmcc == username.Trim());
        if (user == null)
            return (false, "用户不存在");

        user.yhname = displayName.Trim();
        user.js = role;
        user.IsEnabled = isEnabled;
        user.beizhu = remark?.Trim() ?? "";

        _db.SysUsers.Update(user);
        await _db.SaveChangesAsync();
        return (true, "用户信息已更新");
    }

    public async Task<(bool Success, string Message)> ResetPasswordAsync(string username, string newPassword)
    {
        var user = await _db.SysUsers.FirstOrDefaultAsync(x => x.yhmcc == username.Trim());
        if (user == null)
            return (false, "用户不存在");

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Trim().Length < 4)
            return (false, "新密码长度不能少于 4 位");

        var now = DateTime.Now;
        var seed = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);

        user.kl = _crypto.Encrypt(newPassword.Trim(), seed);
        user.kzhdrq = seed;

        _db.SysUsers.Update(user);
        await _db.SaveChangesAsync();
        return (true, "密码已重置");
    }

    private async Task<string> GetNextUserCodeAsync()
    {
        var maxCode = await _db.SysUsers
            .Select(u => u.yhbh)
            .OrderByDescending(c => c)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(maxCode))
            return "00001";

        if (int.TryParse(maxCode.Trim(), out var num))
            return (num + 1).ToString("D5");

        return "00001";
    }
}
