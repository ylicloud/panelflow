using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Services;
using PanelFlow.Infrastructure.Data;
using PanelFlow.Infrastructure.Security;
using PanelFlow.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "未配置 ConnectionStrings:DefaultConnection。请在 PanelFlow.Web 目录执行：dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" \"<你的连接字符串>\"（开发环境）；部署环境请使用环境变量 ConnectionStrings__DefaultConnection 或服务器上的 appsettings（勿将密钥提交到 Git）。");
}

// ===== 数据库 =====
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        connectionString,
        sql => sql.CommandTimeout(30)));

// ===== Data Protection 密钥持久化（解决部署后防伪令牌失效问题） =====
var keysDir = Path.Combine(builder.Environment.ContentRootPath, "keys");
Directory.CreateDirectory(keysDir);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
    .SetApplicationName("PanelFlow");

// ===== 业务服务 =====
builder.Services.AddScoped<IPasswordCryptoService, PbCryptoHelper>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IManufacturingContractService, ManufacturingContractService>();
builder.Services.AddScoped<IQuotationService, QuotationService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<ICustomerContactService, CustomerContactService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddSingleton<IPermissionService, PermissionService>();

// ===== Session =====
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = ".PanelFlow.Session";
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// ===== MVC =====
builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

// frp 反向代理转发头：让 ASP.NET 识别原始请求的 HTTPS 协议和客户端 IP
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
});

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// ===== 预热数据库连接池 =====
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        await db.Database.OpenConnectionAsync();
        await db.Database.CloseConnectionAsync();
    }
    catch
    {
        // 启动时预热失败不影响应用启动，后续请求会重试
    }
}

app.Run();
