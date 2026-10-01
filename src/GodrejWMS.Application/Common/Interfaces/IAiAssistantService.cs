using GodrejWMS.Application.Features.Reports.Core;

namespace GodrejWMS.Application.Common.Interfaces;

/// <summary>
/// Turns an admin's plain-English question into one of the app's existing, known-safe report
/// queries (never raw SQL) via a locally-hosted open-weight LLM (Ollama), and can summarize the
/// resulting rows back into plain English. The target can be either a unified <see cref="ReportKind"/>
/// (from Operational Reports) or a named <see cref="AnalyticsToolCatalog"/> entry (the specialized
/// reports - Inventory Aging, FIFO-compliance checks, etc.) - <see cref="AiReportPlan.Name"/> is
/// resolved against both by the caller.
/// </summary>
public interface IAiAssistantService
{
    Task<AiReportPlan> PlanReportQueryAsync(string question, DateTimeOffset now, CancellationToken cancellationToken);

    Task<string> SummarizeAsync(string question, string targetName, IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        int totalCount, string? extraContext, CancellationToken cancellationToken);
}

/// <summary>The report/analytics target the model picked to answer a question, plus its own explanation.</summary>
public sealed record AiReportPlan(string Name, ReportFilter Filter, string Reasoning);
