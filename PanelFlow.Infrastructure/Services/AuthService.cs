using Microsoft.EntityFrameworkCore;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;
using PanelFlow.Core.Services;
using PanelFlow.Infrastructure.Data;

namespace PanelFlow.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly ApplicationDbContext _db;
    private readonly IPasswordCryptoService _crypto;

    public AuthService(ApplicationDbContext db, IPasswordCryptoService crypto)
    {
        _db = db;
        _crypto = crypto;
    }

    public async Task<LoginUser?> ValidateAsync(string userName, string password)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            return null;

        // yhmcc 是登录账号（主键）
        var user = await _db.SysUsers
            .FirstOrDefaultAsync(u => u.yhmcc == userName.Trim());

        if (user == null)
            return null;

        // 使用 kzhdrq 作为加密种子，为空时兜底 DateTime.Now（与旧系统一致）
        DateTime seed = user.kzhdrq ?? DateTime.Now;

        if (!_crypto.Verify(password.Trim(), user.kl.Trim(), seed))
            return null;

        // 登录成功，回写最后登录时间
        user.LastLogin = DateTime.Now;
        _db.SysUsers.Update(user);
        await _db.SaveChangesAsync();

        return new LoginUser
        {
            UserName = user.yhmcc.Trim(),
            DisplayName = user.yhname.Trim(),
            RoleName = RoleMapper.GetRoleName(user.js),  // 使用 js 字段映射角色
            UserType = user.yhlx,
            DeptCode = user.bmbm.Trim()
        };
    }
}
