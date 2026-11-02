using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;

namespace GodrejWMS.Application.Features.Inward;

public static class InwardDownloadMapper
{
    /// <summary>The whole two-sheet GRN workbook's data (Inward Download rows plus the Unloading Sheet).</summary>
    public static InwardDownloadDto PackageFromDetail(InwardDetailDto detail) => new(
        FromDetail(detail),
        new InwardDownloadHeaderDto(detail.ReferenceNumber, detail.VehicleNumber, detail.LrNumber,
            ResolveHeaderJson(detail.UploadHeaderJson, detail.VehicleNumber, detail.LrNumber, detail.ReceivedFrom, detail.CreatedAt)),
        detail.Lines
            .Select(l => new InwardUnloadingSheetRowDto(
                l.MaterialNumber.ToString(), l.MaterialDescription, l.RequestedQuantityBoxes, l.MfgMonthLabel,
                AllocationRemark(l.Status, detail.IsRejected)))
            .Concat((detail.UnsavedLines ?? []).Select(UnsavedRow))
            .ToList());

    public static InwardDownloadDto PackageFromResult(InwardResultDto result) => PackageFromResult(
        result,
        (result.UnsavedLines ?? []).Where(u => u.Kind == InwardUnsavedLineKind.UnknownMaterial).Select(u => u.CodeText).ToHashSet());

    private static InwardDownloadDto PackageFromResult(InwardResultDto result, HashSet<string> unknownCodes) => new(
        FromResult(result),
        new InwardDownloadHeaderDto(result.ReferenceNumber, result.VehicleNumber, result.LrNumber,
            ResolveHeaderJson(result.UploadHeaderJson, result.VehicleNumber, result.LrNumber, result.ReceivedFrom, result.CreatedAt)),
        result.Lines
            .Select(l => new InwardUnloadingSheetRowDto(
                l.MaterialNumber.ToString(), l.MaterialDescription == UnknownMaterialDescription ? string.Empty : l.MaterialDescription, l.RequestedQuantityBoxes, l.MfgMonthLabel,
                unknownCodes.Contains(l.MaterialNumber.ToString()) && l.Status == AllocationStatus.Failed
                    ? "Not allocated - " + (l.Remarks ?? "Material code not found in Material Master.")
                    : AllocationRemark(l.Status, result.IsRejected)))
            // Unknown-material lines are already in result.Lines (shown as failed); only unreadable rows are extra.
            .Concat((result.UnsavedLines ?? []).Where(u => u.Kind == InwardUnsavedLineKind.UnreadableRow).Select(UnsavedRow))
            .ToList());

    /// <summary>Only used on the on-screen result when the upload gave no name for a material that is not in the
    /// master; the Excel downloads show a blank in that case instead.</summary>
    public const string UnknownMaterialDescription = "(unknown material)";

    /// <summary>The name to show for an unsaved line: what the uploader typed, else a placeholder.</summary>
    public static string NameOrPlaceholder(string? uploadedDescription) =>
        string.IsNullOrWhiteSpace(uploadedDescription) ? UnknownMaterialDescription : uploadedDescription.Trim();

    private static InwardUnloadingSheetRowDto UnsavedRow(InwardUnsavedLineDto u) => new(
        u.CodeText,
        u.Description ?? string.Empty,                       // the name from the uploaded sheet; blank if none was given
        u.QuantityBoxes,
        string.Empty,
        "Not allocated - " + u.Reason);

    /// <summary>The seven header rows to repeat at the top of the workbook. A GRN uploaded after the rows were
    /// being saved returns them exactly as stored. An earlier uploaded GRN (it has a vehicle, LR or Received
    /// From but no stored rows - the original file is gone) gets a block rebuilt in the same layout from the
    /// fields that were kept; everything that was never stored stays blank. A GRN entered by hand has no upload
    /// to repeat and returns null.</summary>
    public static string? ResolveHeaderJson(string? storedJson, string? vehicle, string? lr, string? receivedFrom, DateTimeOffset createdAt)
    {
        if (InwardUploadHeaderBlock.TryParse(storedJson) is not null)
        {
            return storedJson;
        }

        if (string.IsNullOrWhiteSpace(vehicle) && string.IsNullOrWhiteSpace(lr) && string.IsNullOrWhiteSpace(receivedFrom))
        {
            return null;
        }

        static string Text(string? value) => value?.Trim() ?? string.Empty;
        return new InwardUploadHeaderBlock(
        [
            ["UNLOADING SHEET", "", "", "", "", ""],
            ["Invoice No:-", "", "LR NO :-", Text(lr), "", ""],
            ["Invoice Date:-", "", "Vehicle No:-", Text(vehicle), "Plan", ""],
            ["", "", "", "", "", ""],
            ["TRANSPORT", "", "Unloading Check List", "", "Received from:", Text(receivedFrom)],
            ["DOCK No", "", "Start Time :-", "", "Sup Name:", ""],
            ["Date:-", createdAt.ToLocalTime().ToString("dd/MM/yy"), "End Time :-", "", "", ""]
        ], ["A1:F1"]).ToJson();
    }

    /// <summary>The Unloading Sheet's per-line verdict on whether locations were found for the line.</summary>
    public static string AllocationRemark(AllocationStatus status, bool isRejected) =>
        isRejected
            ? "GRN rejected"
            : status switch
            {
                AllocationStatus.Fulfilled => "Fully allocated",
                AllocationStatus.Partial => "Partially allocated",
                _ => "Not allocated"
            };

    public static IReadOnlyList<InwardDownloadRow> FromDetail(InwardDetailDto detail) =>
        detail.Lines
            .SelectMany(l => Rows(
                detail.ReferenceNumber, l.MaterialNumber, l.MaterialDescription, l.MfgMonthLabel,
                l.RequestedQuantityBoxes, l.AllocatedQuantityBoxes, l.Status,
                l.Putaways.Select(p => (p.LocationCode, p.QuantityBoxes, p.IsConfirmed, (int?)p.Id)), detail.IsRejected,
                detail.VehicleNumber, detail.LrNumber))
            .ToList();

    public static IReadOnlyList<InwardDownloadRow> FromResult(InwardResultDto result) =>
        result.Lines
            .SelectMany(l => Rows(
                result.ReferenceNumber, l.MaterialNumber, l.MaterialDescription, l.MfgMonthLabel,
                l.RequestedQuantityBoxes, l.AllocatedQuantityBoxes, l.Status,
                l.AllocatedTo.Select(a => (a.LocationCode, a.QuantityBoxes, a.IsConfirmed, a.PutawayId)), result.IsRejected,
                result.VehicleNumber, result.LrNumber))
            .ToList();

    private static IEnumerable<InwardDownloadRow> Rows(
        string reference, long materialNumber, string description, string mfgMonthLabel,
        decimal requested, decimal allocated, AllocationStatus status,
        IEnumerable<(string LocationCode, decimal QuantityBoxes, bool IsConfirmed, int? PutawayId)> locations, bool isRejected,
        string? vehicleNumber, string? lrNumber)
    {
        var placed = locations.ToList();
        if (placed.Count == 0)
        {
            yield return new InwardDownloadRow(reference, materialNumber, description, mfgMonthLabel,
                requested, allocated, status.ToString(), string.Empty, 0m, isRejected ? "Rejected" : "No location",
                VehicleNumber: vehicleNumber, LrNumber: lrNumber);
            yield break;
        }

        foreach (var (locationCode, quantity, isConfirmed, putawayId) in placed)
        {
            yield return new InwardDownloadRow(reference, materialNumber, description, mfgMonthLabel,
                requested, allocated, status.ToString(), locationCode, quantity,
                isRejected ? "Rejected" : isConfirmed ? "Confirmed" : "Reserved", putawayId, vehicleNumber, lrNumber);
        }
    }
}
