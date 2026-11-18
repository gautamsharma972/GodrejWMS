using GodrejWMS.Application.Features.Pullout.Dtos;
using GodrejWMS.Domain.Enums;

namespace GodrejWMS.Application.Features.Pullout;

/// <summary>The data of a pullout's two-sheet download (Picklist + Loading Sheet), built once from the pullout detail so the Excel
/// download and the print view show exactly the same thing.</summary>
public sealed record PulloutDownloadPackage(
    IReadOnlyList<PulloutDownloadRowDto> Rows,
    PulloutLoadingSheetHeaderDto Header,
    IReadOnlyList<PulloutLoadingSheetRowDto> LoadingRows);

/// <summary>One label / value pair of the sheet header (blank labels keep the client's grid shape).</summary>
public sealed record PrintField(string Label, string Value);

public static class PulloutPrintSheets
{
    public static PulloutDownloadPackage FromDetail(PulloutDetailDto detail)
    {
        // One row per pick, like the Pick Result panel on the entry screen.
        var rows = detail.Lines
            .SelectMany(l => l.Picks.Select(p => new PulloutDownloadRowDto(
                l.MaterialNumber, l.MaterialDescription, p.QuantityBoxes, p.MfgMonthLabel, p.LocationCode, l.PlantName)))
            .ToList();
        var header = new PulloutLoadingSheetHeaderDto(
            detail.ReferenceNumber, detail.CreatedAt, detail.Lines.Select(l => l.PlantName).FirstOrDefault(p => p is not null),
            detail.HeaderFields, detail.WarehouseAddress);
        var loadingRows = detail.Lines
            .Select(l => new PulloutLoadingSheetRowDto(l.MaterialNumber, l.MaterialDescription, l.RequestedQuantityBoxes, l.Status, l.Priority))
            .ToList();
        return new PulloutDownloadPackage(rows, header, loadingRows);
    }

    public const string Company = "GODREJ CONSUMER PRODUCTS LTD";

    public static readonly string[] PicklistHeaders = ["Material Code", "Material Desc.", "Total Stock in CFB", "Mfg Month", "Pallet Position", "Plant Name"];

    public static readonly string[] LoadingHeaders = ["Code", "Description", "Del Qty", "Priority", "MRP", "PKD", "Load Qty", "Remarks"];

    public static string Address(PulloutLoadingSheetHeaderDto header) => string.IsNullOrWhiteSpace(header.WarehouseAddress) ? "NA" : header.WarehouseAddress;

    /// <summary>The transport block in the client's three label / value pairs per row (Transport, Vehicle no, Destination, Driver No on the left;
    /// Gate No, Plan Time, Date, Start / End Time on the right).</summary>
    public static IReadOnlyList<(PrintField Left, PrintField Middle, PrintField Right)> TransportRows(PulloutLoadingSheetHeaderDto header)
    {
        var f = header.Fields;
        static string V(string? value) => value?.Trim() ?? string.Empty;
        var date = !string.IsNullOrWhiteSpace(f?.DateText) ? f!.DateText! : header.CreatedAt.ToString("dd.MM.yyyy");
        return
        [
            (new("Transport", V(f?.TransportName)), new("GATE NO:", V(f?.GateNumber)), new("Plan Time:", V(f?.PlanTime))),
            (new("Vehicle no", V(f?.VehicleNumber)), new("", ""), new("Date", date)),
            (new("Destination", V(header.PlantName)), new("", ""), new("Start Time", V(f?.StartTime))),
            (new("Driver No", V(f?.DriverNumber)), new("", ""), new("End Time", V(f?.EndTime)))
        ];
    }

    /// <summary>Both sheets list rows alphabetically by material description, as the Excel download does.</summary>
    public static IReadOnlyList<string[]> PicklistRows(PulloutDownloadPackage package) =>
        package.Rows
            .OrderBy(r => r.MaterialDescription?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(r => new[] { r.MaterialNumber.ToString(), r.MaterialDescription ?? string.Empty, r.QuantityBoxes.ToString("0.###"), r.MfgMonthLabel, r.PalletPositionCode, r.PlantName ?? string.Empty })
            .ToList();

    public static IReadOnlyList<string[]> LoadingRows(PulloutDownloadPackage package) =>
        package.LoadingRows
            .OrderBy(r => r.MaterialDescription?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(r => new[] { r.MaterialNumber.ToString(), r.MaterialDescription ?? string.Empty, r.DelQtyBoxes.ToString("0"), r.Priority ?? string.Empty, "", "", "", Availability(r.Status) })
            .ToList();

    public static string Availability(AllocationStatus status) => status switch
    {
        AllocationStatus.Fulfilled => "Available",
        AllocationStatus.Partial => "Partial Available",
        _ => "Not available"
    };
}
