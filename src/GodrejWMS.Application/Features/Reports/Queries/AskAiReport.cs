using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Reports.Core;
using MediatR;

namespace GodrejWMS.Application.Features.Reports.Queries;

/// <summary>AI Assistant tab: an admin asks a plain-English question; the LLM only ever chooses
/// among the app's existing report queries - a <see cref="ReportKind"/> (Operational Reports) or a
/// named <see cref="AnalyticsToolCatalog"/> entry (Inventory Aging, FIFO-compliance checks, etc.) -
/// and fills in the filter fields. It never generates or runs arbitrary SQL.</summary>
public sealed record AskAiReportQuery(string Question) : IRequest<AiReportAnswer>;

public sealed record AiReportAnswer(string Target, string Reasoning, string Summary,
    IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyDictionary<string, string>> Rows, int TotalCount);

public sealed class AskAiReportHandler(IAiAssistantService ai, ISender mediator, IDateTimeProvider clock)
    : IRequestHandler<AskAiReportQuery, AiReportAnswer>
{
    public async Task<AiReportAnswer> Handle(AskAiReportQuery request, CancellationToken cancellationToken)
    {
        // Brand-count/list questions are common master-data questions. Route these deterministically
        // so a small local model cannot misclassify "brands" as a Current Stock reference filter.
        var plan = IsBrandMasterQuestion(request.Question)
            ? new AiReportPlan("BrandMaster", new ReportFilter(), "The question asks about Brand Master data.")
            : await ai.PlanReportQueryAsync(request.Question, clock.UtcNow, cancellationToken);

        if (Enum.TryParse<ReportKind>(plan.Name, ignoreCase: true, out var kind) && Enum.IsDefined(kind))
        {
            var filter = NormalizeOperationalFilter(kind, plan.Filter);
            var page = await mediator.Send(
                new GetOperationalReportQuery(kind, filter, 1, 50, "reference", false), cancellationToken);
            var (columns, rows) = AiResultFlattener.FlattenReportRows(page.Items);
            var summary = await ai.SummarizeAsync(request.Question, kind.ToString(), rows, page.TotalCount, null, cancellationToken);
            return new AiReportAnswer(kind.ToString(), plan.Reasoning, summary, columns, rows, page.TotalCount);
        }

        var tool = AnalyticsToolCatalog.Tools.FirstOrDefault(t => t.Name.Equals(plan.Name, StringComparison.OrdinalIgnoreCase))
            ?? throw new AiAssistantUnavailableException($"The AI assistant picked an unrecognized report (\"{plan.Name}\"). Try rephrasing the question.");

        var toolRequest = tool.CreateRequest(plan.Filter.From, plan.Filter.To, plan.Filter.Search);
        var raw = await mediator.Send(toolRequest, cancellationToken)
            ?? throw new AiAssistantUnavailableException("That report returned no data.");

        var rowObjects = tool.ExtractRows(raw);
        var (toolColumns, toolRows) = AiResultFlattener.FlattenAnalyticsRows(rowObjects);
        var extraContext = tool.ExtraContext?.Invoke(raw);
        var toolSummary = await ai.SummarizeAsync(request.Question, tool.Name, toolRows, toolRows.Count, extraContext, cancellationToken);

        return new AiReportAnswer(tool.Name, plan.Reasoning, toolSummary, toolColumns, toolRows, toolRows.Count);
    }

    private static bool IsBrandMasterQuestion(string question)
    {
        var normalized = question.Trim().ToLowerInvariant();
        return normalized.Contains("brand")
            && (normalized.Contains("how many")
                || normalized.Contains("count")
                || normalized.Contains("list")
                || normalized.Contains("configured")
                || normalized.Contains("brand master")
                || normalized is "brands" or "brand");
    }

    private static ReportFilter NormalizeOperationalFilter(ReportKind kind, ReportFilter filter)
    {
        if (kind is not (ReportKind.Capacity or ReportKind.AvailableLocations or ReportKind.RackStock
            or ReportKind.RackLevel or ReportKind.ZoneStock))
        {
            return filter;
        }

        // Capacity/location reports have no transaction date, status, user, reference, SKU or PKM.
        // Small local models occasionally populate those fields despite being told not to, and EF
        // then attempts to filter projected constant/null columns. Retain only meaningful filters.
        return new ReportFilter(
            WarehouseId: filter.WarehouseId,
            Search: filter.Search,
            Location: filter.Location,
            Zone: filter.Zone,
            Rack: filter.Rack,
            Column: filter.Column,
            Level: filter.Level);
    }
}
