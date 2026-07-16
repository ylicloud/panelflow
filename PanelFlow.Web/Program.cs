using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Services;
using PanelFlow.Infrastructure.Data;
using PanelFlow.Infrastructure.Security;
using PanelFlow.Infrastructure.Services;

// Ubuntu/Windows 上启用 GBK(936)，供 BJB char/varchar 字节长度校验使用
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

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
        sql => sql.CommandTimeout(30))
        .AddInterceptors(new SqlServerLegacySessionInterceptor()));

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
builder.Services.AddScoped<IElementDictService, ElementDictService>();
builder.Services.AddScoped<IQuotationStructureService, QuotationStructureService>();
builder.Services.AddScoped<IPriceHistoryService, PriceHistoryService>();
builder.Services.AddScoped<IPriceQueryService, PriceQueryService>();
builder.Services.AddScoped<IPurchaseService, PurchaseService>();
builder.Services.AddScoped<IQuotationSummaryService, QuotationSummaryService>();
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

// ===== 上传上限：报价单导入 Excel 等文件上传限制为 10 MB =====
// 显式声明全局 multipart 上限，避免框架默认 28MB 上限先于业务 10MB 校验生效。
// 其他上传入口若无单独限制将共用此值。
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 10L * 1024 * 1024);

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
