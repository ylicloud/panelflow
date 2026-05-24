using System.ComponentModel.DataAnnotations;

namespace PanelFlow.Web.Models.Quotation;

public class AutoFillPriceRequest
{
    [Required]
    public string Fabh { get; set; } = string.Empty;
}
