using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CSharpOA.Core.Enums;

namespace CSharpOA.Core.Models
{
    [Table("YHQXGL")]
    public class User
    {
        // 用户登录名
        [Key]
        [Column("yhmcc")]
        [StringLength(10)]
        public string Username { get; set; } = string.Empty;

        // 用户姓名
        [Column("yhname")]
        [StringLength(20)]
        public string DisplayName { get; set; } = string.Empty;

        // 密码
        [Column("kl")]
        [StringLength(20)]
        public string EncryptedPassword { get; set; } = string.Empty;

        // 用户类型 (未知字段,页面上不显示)
        [Column("yhlx")]
        public int UserType { get; set; } = 1;

        // 使用性质(未知字段,页面上不显示)
        [Column("syxz")]
        public int UseType { get; set; } = 0;

        // 备注
        [Column("beizhu")]
        [StringLength(100)]
        public string Remark { get; set; } = string.Empty;  

        // 部门编码:默认为0101        
        [Column("bmbm")]
        [StringLength(10)]
        public string DepartmentCode { get; set; } = "0101";

        // 开账户日期
        [Column("kzhdrq")]
        public DateTime? CreateTime { get; set; }

        // 主管用户编号
        [Column("zgyhbh")]
        [StringLength(10)]
        public string SupervisorUsername { get; set; } = string.Empty;
        // 用户编号: 从00001开始,每次加1
        [Column("yhbh")]
        [StringLength(10)]
        public string UserCode { get; set; } = string.Empty;


        // 用户角色:默认为普通用户1,管理员0
        [Column("js")]
        public UserRole Role { get; set; } = UserRole.Viewer;

        // 是否启用:默认为1,启用
        [Column("IsEnabled")]
        public bool IsEnabled { get; set; } = true;
        // 最后登录时间
        [Column("LastLogin")]
        public DateTime? LastLogin { get; set; }



    }
}