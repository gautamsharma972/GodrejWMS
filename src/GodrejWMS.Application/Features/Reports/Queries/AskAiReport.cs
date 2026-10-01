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
        var plan = await ai.PlanReportQueryAsync(request.Question, clock.UtcNow, cancellationToken);

        if (Enum.TryParse<ReportKind>(plan.Name, ignoreCase: true, out var kind) && Enum.IsDefined(kind))
        {
            var page = await mediator.Send(
                new GetOperationalReportQuery(kind, plan.Filter, 1, 50, "reference", false), cancellationToken);
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
}
