using System.Reflection;
using System.Text.RegularExpressions;
using GodrejWMS.Application.Features.Reports.Dtos;
using GodrejWMS.Application.Features.Reports.Queries;
using MediatR;

namespace GodrejWMS.Application.Features.Reports.Core;

public enum AnalyticsParamShape { None, DateRange, Search }

/// <summary>One of the specialized report queries that live outside the unified <see cref="ReportKind"/>
/// table (Inventory Aging, Valuation, FIFO-compliance checks, etc.) - the AI Assistant's second
/// routing target alongside <see cref="ReportKind"/>. <see cref="CreateRequest"/> builds the
/// concrete MediatR request from whatever the model filled in; <see cref="ExtractRows"/> pulls the
/// tabular rows out of that query's own DTO shape (a plain list, or the "Rows" property of a
/// summary DTO); <see cref="ExtraContext"/> optionally surfaces summary-level stats (averages,
/// counts) that aren't in the row list itself, for the AI's plain-English summary.</summary>
public sealed record AnalyticsToolDescriptor(
    string Name,
    string Description,
    AnalyticsParamShape ParamShape,
    Func<DateTimeOffset?, DateTimeOffset?, string?, IBaseRequest> CreateRequest,
    Func<object, IReadOnlyList<object>> ExtractRows,
    Func<object, string?>? ExtraContext = null);

public static class AnalyticsToolCatalog
{
    public static readonly IReadOnlyList<AnalyticsToolDescriptor> Tools =
    [
        new("InventoryAging",
            "Current stock bucketed by age since manufacture (0-30 / 31-60 / 61-90 / 90+ days), crossed with fast/slow-mover velocity - finds slow-moving stock that has sat too long.",
            AnalyticsParamShape.None,
            (_, _, _) => new GetInventoryAgingQuery(),
            raw => [.. ((IReadOnlyList<InventoryAgingRowDto>)raw)]),

        new("InventoryValuation",
            "Current stock valued at MRP price per SKU/material: total boxes and total value.",
            AnalyticsParamShape.Search,
            (_, _, search) => new GetInventoryValuationQuery(search),
            raw => [.. ((IReadOnlyList<InventoryValuationRowDto>)raw)]),

        new("WarehouseUtilizationByZone",
            "A simple zone-level percentage rollup of storage capacity utilization (locations, capacity, occupied, %) grouped by zone type (Fast/Reserve/Seasonal/DispatchNear). For a detailed rack/level breakdown instead, use the Capacity report kind.",
            AnalyticsParamShape.None,
            (_, _, _) => new GetWarehouseUtilizationByZoneQuery(),
            raw => [.. ((WarehouseUtilizationDto)raw).ByZone]),

        new("MovementReasonsBreakdown",
            "Internal stock movements grouped by reason (Rack Reorganization, Space Optimization, Stock Consolidation, Operational Requirement, Supervisor Correction) with counts and quantities.",
            AnalyticsParamShape.DateRange,
            (from, to, _) => new GetMovementReasonsBreakdownQuery(from, to),
            raw => [.. ((IReadOnlyList<MovementReasonBreakdownRowDto>)raw)]),

        new("MovementActivityByUser",
            "Internal stock movements grouped by which user performed them: counts and quantities per user.",
            AnalyticsParamShape.DateRange,
            (from, to, _) => new GetMovementActivityByUserQuery(from, to),
            raw => [.. ((IReadOnlyList<MovementUserActivityRowDto>)raw)]),

        new("MovementLocationChurn",
            "Storage locations with the most internal-movement activity (moves in vs. out) - finds hot/high-churn locations.",
            AnalyticsParamShape.DateRange,
            (from, to, _) => new GetMovementLocationChurnQuery(from, to),
            raw => [.. ((IReadOnlyList<MovementLocationChurnRowDto>)raw)]),

        new("PulloutLocationUtilization",
            "Which zones/location types pullout picks came from most often: pick counts and quantities.",
            AnalyticsParamShape.DateRange,
            (from, to, _) => new GetPulloutLocationUtilizationQuery(from, to),
            raw => [.. ((IReadOnlyList<PulloutLocationUtilizationRowDto>)raw)]),

        new("PulloutStockAgeAtPick",
            "How old (days since manufacture) stock was when picked for a pullout - a FIFO-compliance check. Includes average/median age across picks.",
            AnalyticsParamShape.DateRange,
            (from, to, _) => new GetPulloutStockAgeAtPickQuery(from, to),
            raw => [.. ((PulloutStockAgeSummaryDto)raw).Rows],
            raw =>
            {
                var s = (PulloutStockAgeSummaryDto)raw;
                return $"PickCount={s.PickCount}, AverageAgeDays={s.AverageAgeDays:0.#}, MedianAgeDays={s.MedianAgeDays:0.#}";
            }),

        new("InwardPutawayTurnaround",
            "How long (hours) between a GRN/inward being received and its put-away being confirmed - a speed/SLA metric. Includes average/median hours across GRNs.",
            AnalyticsParamShape.DateRange,
            (from, to, _) => new GetInwardPutawayTurnaroundQuery(from, to),
            raw => [.. ((InwardTurnaroundSummaryDto)raw).Rows],
            raw =>
            {
                var s = (InwardTurnaroundSummaryDto)raw;
                return $"GrnCount={s.GrnCount}, ConfirmedCount={s.ConfirmedCount}, AverageHours={s.AverageHours:0.#}, MedianHours={s.MedianHours:0.#}";
            }),

        new("InwardRejectionReasons",
            "Rejected GRNs (inward transactions) grouped by rejection reason: counts and total requested boxes.",
            AnalyticsParamShape.DateRange,
            (from, to, _) => new GetInwardRejectionReasonsQuery(from, to),
            raw => [.. ((IReadOnlyList<InwardRejectionReasonRowDto>)raw)])
    ];
}

/// <summary>Flattens either a <see cref="ReportRow"/> page (the unified Operational Reports shape)
/// or an <see cref="AnalyticsToolDescriptor"/> result (arbitrary DTO shape, via reflection) into
/// the same generic (columns, string-keyed rows) shape the AI Assistant page renders.</summary>
public static class AiResultFlattener
{
    private static readonly string[] ReportRowColumns =
        ["Warehouse", "Reference", "Date", "SKU", "Description", "Location", "Quantity", "Status"];

    public static (IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyDictionary<string, string>> Rows) FlattenReportRows(
        IReadOnlyList<ReportRow> items) => (ReportRowColumns, [.. items.Select(ToDictionary)]);

    private static IReadOnlyDictionary<string, string> ToDictionary(ReportRow r) => new Dictionary<string, string>
    {
        ["Warehouse"] = r.Warehouse,
        ["Reference"] = r.Reference,
        ["Date"] = r.Date?.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt") ?? "",
        ["SKU"] = r.Sku?.ToString() ?? "",
        ["Description"] = r.Description,
        ["Location"] = r.Location,
        ["Quantity"] = r.Quantity.ToString("0.###"),
        ["Status"] = r.Status
    };

    public static (IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyDictionary<string, string>> Rows) FlattenAnalyticsRows(
        IReadOnlyList<object> items)
    {
        if (items.Count == 0)
        {
            return ([], []);
        }

        var props = items[0].GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var columns = props.Select(Humanize).ToList();
        var rows = items
            .Select(item => (IReadOnlyDictionary<string, string>)props.ToDictionary(Humanize, p => FormatValue(p.GetValue(item))))
            .ToList();

        return (columns, rows);
    }

    private static string Humanize(PropertyInfo p) => Regex.Replace(p.Name, "(?<!^)([A-Z])", " $1");

    private static string FormatValue(object? value) => value switch
    {
        null => "",
        DateTimeOffset dto => dto.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt"),
        double d => d.ToString("0.##"),
        decimal m => m.ToString("0.###"),
        _ => value.ToString() ?? ""
    };
}
