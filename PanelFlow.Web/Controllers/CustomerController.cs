using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;
using PanelFlow.Core.Services;
using PanelFlow.Web.Extensions;
using PanelFlow.Web.Filters;
using System.ComponentModel.DataAnnotations;

namespace PanelFlow.Web.Controllers;

[RoleAuthorize(RoleNames.Admin, RoleNames.Quoter, RoleNames.ProductionManager)]
public class CustomerController : Controller
{
    private readonly ICustomerService _customerService;
    private readonly ICustomerContactService _customerContactService;
    private readonly IQuotationService _quotationService;

    public CustomerController(
        ICustomerService customerService,
        ICustomerContactService customerContactService,
        IQuotationService quotationService)
    {
        _customerService = customerService;
        _customerContactService = customerContactService;
        _quotationService = quotationService;
    }

    private string? GetCurrentUserName()
    {
        return HttpContext.Session.GetLoginUser()?.DisplayName;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? keyword)
    {
        ViewData["Title"] = "客户列表";
        ViewData["BreadcrumbTitle"] = "客户";

        var items = await _customerService.GetListAsync(keyword);
        return View(new CustomerListViewModel
        {
            Keyword = keyword?.Trim(),
            Items = items
        });
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewData["Title"] = "新建客户";
        ViewData["BreadcrumbTitle"] = "新建客户";
        return View(new CustomerEditViewModel
        {
            NewContact = new CustomerContactEditViewModel()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CustomerEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "新建客户";
            ViewData["BreadcrumbTitle"] = "新建客户";
            model.NewContact = new CustomerContactEditViewModel();
            return View(model);
        }

        var (success, message) = await _customerService.CreateAsync(ToDto(model), GetCurrentUserName());
        if (!success)
        {
            ModelState.AddModelError(string.Empty, message);
            ViewData["Title"] = "新建客户";
            ViewData["BreadcrumbTitle"] = "新建客户";
            model.NewContact = new CustomerContactEditViewModel();
            return View(model);
        }

        TempData["SuccessMessage"] = message;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        ViewData["Title"] = "编辑客户";
        ViewData["BreadcrumbTitle"] = "编辑客户";

        var dto = await _customerService.GetByCompanyNoAsync(id);
        if (dto == null)
        {
            TempData["ErrorMessage"] = "客户不存在";
            return RedirectToAction(nameof(Index));
        }

        return View(await BuildEditModelAsync(dto));
    }

    [HttpGet]
    public async Task<IActionResult> Details(string id)
    {
        ViewData["Title"] = "客户详情";
        ViewData["BreadcrumbTitle"] = "客户详情";

        var customer = await _customerService.GetByCompanyNoAsync(id);
        if (customer == null)
        {
            TempData["ErrorMessage"] = "客户不存在";
            return RedirectToAction(nameof(Index));
        }

        var contacts = await _customerContactService.GetByCompanyNoAsync(customer.CompanyNo);
        var quotations = await _quotationService.GetByCustomerNoAsync(customer.CompanyNo);

        return View(new CustomerDetailsViewModel
        {
            Customer = customer,
            Contacts = contacts,
            Quotations = quotations
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CustomerEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "编辑客户";
            ViewData["BreadcrumbTitle"] = "编辑客户";
            return View(await PopulateEditRelatedDataAsync(model));
        }

        var (success, message) = await _customerService.UpdateAsync(ToDto(model), GetCurrentUserName());
        if (!success)
        {
            ModelState.AddModelError(string.Empty, message);
            ViewData["Title"] = "编辑客户";
            ViewData["BreadcrumbTitle"] = "编辑客户";
            return View(await PopulateEditRelatedDataAsync(model));
        }

        TempData["SuccessMessage"] = message;
        return RedirectToAction(nameof(Edit), new { id = model.CompanyNo });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddContact(CustomerContactEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "联系人信息填写不完整";
            return RedirectToAction(nameof(Edit), new { id = model.CompanyNo });
        }

        var (success, message) = await _customerContactService.CreateAsync(ToContactDto(model));
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
        return RedirectToAction(nameof(Edit), new { id = model.CompanyNo });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateContact(CustomerContactEditViewModel model)
    {
        if (!ModelState.IsValid || model.Id <= 0)
        {
            TempData["ErrorMessage"] = "联系人信息填写不完整";
            return RedirectToAction(nameof(Edit), new { id = model.CompanyNo });
        }

        var (success, message) = await _customerContactService.UpdateAsync(ToContactDto(model));
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
        return RedirectToAction(nameof(Edit), new { id = model.CompanyNo });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteContact(string companyNo, int id)
    {
        var (success, message) = await _customerContactService.DeleteAsync(companyNo, id);
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
        return RedirectToAction(nameof(Edit), new { id = companyNo });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefaultContact(string companyNo, int id)
    {
        var (success, message) = await _customerContactService.SetDefaultAsync(companyNo, id);
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
        return RedirectToAction(nameof(Edit), new { id = companyNo });
    }

    private static CustomerDto ToDto(CustomerEditViewModel model)
    {
        return new CustomerDto
        {
            CompanyNo = model.CompanyNo,
            CompanyName = model.CompanyName,
            Alias = model.Alias ?? string.Empty,
            Contact = model.Contact ?? string.Empty,
            Phone = model.Phone ?? string.Empty,
            Remark = model.Remark ?? string.Empty
        };
    }

    private static CustomerContactDto ToContactDto(CustomerContactEditViewModel model)
    {
        return new CustomerContactDto
        {
            Id = model.Id,
            CompanyNo = model.CompanyNo,
            ContactName = model.ContactName,
            Phone = model.Phone ?? string.Empty,
            Email = model.Email ?? string.Empty,
            Title = model.Title ?? string.Empty,
            SortNo = model.SortNo,
            IsEnabled = model.IsEnabled
        };
    }

    private async Task<CustomerEditViewModel> BuildEditModelAsync(CustomerDto dto)
    {
        var model = new CustomerEditViewModel
        {
            CompanyNo = dto.CompanyNo,
            CompanyName = dto.CompanyName,
            Alias = dto.Alias,
            Contact = dto.Contact,
            Phone = dto.Phone,
            Remark = dto.Remark,
            NewContact = new CustomerContactEditViewModel
            {
                CompanyNo = dto.CompanyNo,
                SortNo = 100,
                IsEnabled = true
            }
        };
        return await PopulateEditRelatedDataAsync(model);
    }

    private async Task<CustomerEditViewModel> PopulateEditRelatedDataAsync(CustomerEditViewModel model)
    {
        model.Contacts = (await _customerContactService.GetByCompanyNoAsync(model.CompanyNo))
            .Select(x => new CustomerContactEditViewModel
            {
                Id = x.Id,
                CompanyNo = x.CompanyNo,
                ContactName = x.ContactName,
                Phone = x.Phone,
                Email = x.Email,
                Title = x.Title,
                IsDefault = x.IsDefault,
                SortNo = x.SortNo,
                IsEnabled = x.IsEnabled
            })
            .ToList();

        model.NewContact ??= new CustomerContactEditViewModel
        {
            CompanyNo = model.CompanyNo,
            SortNo = 100,
            IsEnabled = true
        };
        model.NewContact.CompanyNo = model.CompanyNo;
        return model;
    }
}

public class CustomerListViewModel
{
    public string? Keyword { get; set; }
    public List<CustomerDto> Items { get; set; } = [];
}

public class CustomerDetailsViewModel
{
    public CustomerDto Customer { get; set; } = new();
    public List<CustomerContactDto> Contacts { get; set; } = [];
    public List<QuotationDto> Quotations { get; set; } = [];

    public static string GetQuotationStatusText(int status)
    {
        return status switch
        {
            0 => "草稿",
            1 => "(无内容)",
            10 => "已成立",
            _ => status.ToString()
        };
    }
}

public class CustomerEditViewModel
{
    [Required(ErrorMessage = "请输入公司编号")]
    [StringLength(10, ErrorMessage = "公司编号最多 10 个字符")]
    [Display(Name = "公司编号")]
    public string CompanyNo { get; set; } = string.Empty;

    [Required(ErrorMessage = "请输入公司名称")]
    [StringLength(50, ErrorMessage = "公司名称最多 50 个字符")]
    [Display(Name = "公司名称")]
    public string CompanyName { get; set; } = string.Empty;

    [StringLength(10, ErrorMessage = "公司别名最多 10 个字符")]
    [Display(Name = "公司别名")]
    public string? Alias { get; set; }

    [StringLength(100, ErrorMessage = "联系人最多 100 个字符")]
    [Display(Name = "联系人")]
    public string? Contact { get; set; }

    [StringLength(40, ErrorMessage = "联系电话最多 40 个字符")]
    [Display(Name = "联系电话")]
    public string? Phone { get; set; }

    [StringLength(100, ErrorMessage = "备注最多 100 个字符")]
    [Display(Name = "备注")]
    public string? Remark { get; set; }

    [ValidateNever]
    public List<CustomerContactEditViewModel> Contacts { get; set; } = [];

    [ValidateNever]
    public CustomerContactEditViewModel NewContact { get; set; } = new();
}

public class CustomerContactEditViewModel
{
    public int Id { get; set; }

    [Required]
    public string CompanyNo { get; set; } = string.Empty;

    [Required(ErrorMessage = "请输入联系人")]
    [StringLength(100, ErrorMessage = "联系人最多 100 个字符")]
    [Display(Name = "联系人")]
    public string ContactName { get; set; } = string.Empty;

    [StringLength(40, ErrorMessage = "联系电话最多 40 个字符")]
    [Display(Name = "联系电话")]
    public string? Phone { get; set; }

    [StringLength(100, ErrorMessage = "邮箱最多 100 个字符")]
    [Display(Name = "邮箱")]
    public string? Email { get; set; }

    [StringLength(50, ErrorMessage = "职位最多 50 个字符")]
    [Display(Name = "职位")]
    public string? Title { get; set; }

    [Display(Name = "默认联系人")]
    public bool IsDefault { get; set; }

    [Display(Name = "排序")]
    public int SortNo { get; set; } = 100;

    [Display(Name = "启用")]
    public bool IsEnabled { get; set; } = true;
}
