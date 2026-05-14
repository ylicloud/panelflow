namespace PanelFlow.Infrastructure.Entities;

/// <summary>
/// 用户权限管理表 (YHQXGL)，字段映射与旧系统 User.cs 完全对齐
/// </summary>
public class SysUser
{
    /// <summary>登录账号 (yhmcc char10)，主键</summary>
    public string yhmcc { get; set; } = string.Empty;

    /// <summary>用户姓名/显示名 (yhname char20)</summary>
    public string yhname { get; set; } = string.Empty;

    /// <summary>密码密文 (kl char20)</summary>
    public string kl { get; set; } = string.Empty;

    /// <summary>用户类型 (yhlx int)，默认1</summary>
    public int yhlx { get; set; } = 1;

    /// <summary>使用性质 (syxz int)，默认0</summary>
    public int syxz { get; set; }

    /// <summary>备注 (beizhu varchar100)</summary>
    public string beizhu { get; set; } = string.Empty;

    /// <summary>部门编码 (bmbm char10)，默认"0101"</summary>
    public string bmbm { get; set; } = "0101";

    /// <summary>开账号日期/密码种子时间 (kzhdrq smalldatetime)</summary>
    public DateTime? kzhdrq { get; set; }

    /// <summary>主管用户编号 (zgyhbh char10)</summary>
    public string zgyhbh { get; set; } = string.Empty;

    /// <summary>用户编号，从00001递增 (yhbh char10)</summary>
    public string yhbh { get; set; } = string.Empty;

    /// <summary>角色 (js int)，对应 UserRole 枚举值</summary>
    public int js { get; set; } = 1;

    /// <summary>是否启用 (IsEnabled bit)，默认 true</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>最后登录时间 (LastLogin datetime)</summary>
    public DateTime? LastLogin { get; set; }
}
