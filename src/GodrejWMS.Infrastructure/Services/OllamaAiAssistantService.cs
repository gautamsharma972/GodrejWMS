using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Reports.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GodrejWMS.Infrastructure.Services;

/// <summary>Bound from configuration section "Ai" ("Ai:OllamaBaseUrl", "Ai:Model").</summary>
public sealed class AiOptions
{
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3.2:3b";
}

/// <summary>
/// Talks to a locally-hosted Ollama instance (free, open-weight models - no API key, no per-token
/// cost, data never leaves the server) to turn an admin's plain-English question into one of the
/// app's existing report queries - a <see cref="ReportKind"/> or an <see cref="AnalyticsToolCatalog"/>
/// entry. The model is constrained to a strict JSON schema so it can only ever pick a known target +
/// filter fields - it never writes SQL and never touches the database directly. Accuracy comes from
/// in-context learning (a domain glossary + worked examples baked into the system prompt below),
/// not weight fine-tuning: that's the free/local-friendly way to ground a small open model in a
/// specific app's terminology, and it's fully editable without any GPU training step.
/// </summary>
public sealed class OllamaAiAssistantService(HttpClient httpClient, IOptions<AiOptions> options, ILogger<OllamaAiAssistantService> logger)
    : IAiAssistantService
{
    private readonly AiOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly (string Name, string Description)[] ReportCatalog =
    [
        (nameof(ReportKind.CurrentStock), "All current stock across all locations, unfiltered."),
        (nameof(ReportKind.SkuStock), "Stock totals grouped by SKU/material (one row per SKU)."),
        (nameof(ReportKind.LocationStock), "Stock grouped by storage location (one row per location)."),
        (nameof(ReportKind.PkmStock), "Stock grouped by SKU and PKM (manufacturing month) together."),
        (nameof(ReportKind.InwardTransactions), "Inward (GRN/goods receipt) transaction headers."),
        (nameof(ReportKind.InwardDetails), "Inward transaction line-level detail (one row per material line on a GRN)."),
        (nameof(ReportKind.InwardPutaway), "Inward requested quantity vs. quantity actually put away."),
        (nameof(ReportKind.PutawayDetails), "Put-away allocation detail: which rack location each inward line's stock went to."),
        (nameof(ReportKind.InwardExceptions), "Inward lines with a discrepancy: partially or fully failed allocation (short receipt)."),
        (nameof(ReportKind.OutwardTransactions), "Outward/pullout transaction headers."),
        (nameof(ReportKind.PickingDetails), "Picking line-level detail for pullouts (which locations stock was picked from)."),
        (nameof(ReportKind.PendingPicking), "Pullout lines still waiting to be picked."),
        (nameof(ReportKind.OutwardShortages), "Pullout lines with a shortage: partially or fully failed picking."),
        (nameof(ReportKind.MovementRegister), "The internal stock-movement register: every location-to-location / SKU move."),
        (nameof(ReportKind.LocationHistory), "Movement history filtered to one specific storage location."),
        (nameof(ReportKind.SkuHistory), "Movement history filtered to one specific SKU."),
        (nameof(ReportKind.Ledger), "The full inventory transaction audit ledger (every inward/outward/movement, combined)."),
        (nameof(ReportKind.RackStock), "Stock grouped by rack."),
        (nameof(ReportKind.ZoneStock), "Stock grouped by zone."),
        (nameof(ReportKind.Capacity), "Detailed warehouse capacity utilization, broken down by warehouse/zone/rack/level."),
        (nameof(ReportKind.AvailableLocations), "Empty / available storage locations with free capacity."),
        (nameof(ReportKind.RackLevel), "Rack and level occupancy detail.")
    ];

    private static readonly (string Name, string Description)[] AnalyticsCatalog =
        [.. AnalyticsToolCatalog.Tools.Select(t => (t.Name, t.Description))];

    private const string DomainGlossary =
        """
        Domain glossary (Godrej WMS warehouse management system):
        - SKU / Material: a sellable item, identified by "Material Number". Has a Design Type (mould/pattern
          code) and belongs to a Season (Rainy, Summer, or Winter).
        - PKM: the manufacturing month of a stock batch, formatted like "MAR|2026". Lower/older PKM = older stock.
        - GRN / Inward: a goods-receipt transaction (incoming stock from a supplier), which is automatically
          put away (allocated) into rack locations.
        - Put-away: the automatic allocation of freshly received inward stock into specific pallet positions.
        - Pullout / Outward: an outgoing pick transaction (stock leaving the warehouse). Picking is FIFO by
          PKM - the oldest manufacturing month is picked first.
        - Location code format: "{Rack}-{Column:00}-{Level:00}", e.g. "A-01-01". Level 1 is ground level.
        - Zone type: Fast (fast-moving/easy-access), Reserve (bulk storage), Seasonal, or DispatchNear
          (near the dispatch dock).
        - Location subtype / stock condition: Good, Damage, Expire, or Hold.
        - Movement type (SKU velocity classification): FastMoving or SlowMoving.
        - "Exception" / "shortage" / "discrepancy" all mean the same thing: an inward or pullout line whose
          allocation/pick was Partial or Failed (i.e. didn't fully succeed).
        - Internal Movement: relocating already-stored stock between locations, for a reason such as Rack
          Reorganization, Space Optimization, Stock Consolidation, Operational Requirement, or Supervisor
          Correction.
        - "Aging" or "slow-moving" stock: use InventoryAging, not CurrentStock.
        - "Value" or "valuation" of stock: use InventoryValuation, not CurrentStock.
        - A simple "how full is the warehouse by zone" question: use WarehouseUtilizationByZone. A detailed
          rack/level breakdown: use the Capacity report.
        - "FIFO compliance" or "how old was stock when picked": use PulloutStockAgeAtPick.
        - "How fast do we put stock away" / GRN turnaround time: use InwardPutawayTurnaround.
        """;

    private const string FewShotExamples =
        """
        Worked examples (question -> target + filters):
        - "Show inward discrepancies from the last 7 days" -> target: InwardExceptions, fromDate/toDate
          covering the last 7 days.
        - "Which SKUs are slow-moving and sitting too long?" -> target: InventoryAging, no filters.
        - "What's our total inventory value?" -> target: InventoryValuation, no filters.
        - "How full is warehouse capacity by zone?" -> target: WarehouseUtilizationByZone, no filters.
        - "Give me a detailed capacity breakdown by rack and level" -> target: Capacity, no filters.
        - "Which locations are empty and available?" -> target: AvailableLocations, no filters.
        - "Show pullout shortages this month" -> target: OutwardShortages, fromDate = the 1st of the
          current month, toDate = today.
        - "Was FIFO followed on recent pullouts?" -> target: PulloutStockAgeAtPick, no filters unless a
          date range is mentioned.
        - "Who moved the most stock last week?" -> target: MovementActivityByUser, fromDate/toDate covering
          the last 7 days.
        - "Why are GRNs getting rejected?" -> target: InwardRejectionReasons, no filters unless a date
          range is mentioned.
        - "Show me everything for SKU 40058047" -> target: SkuHistory, sku = 40058047.
        """;

    public async Task<AiReportPlan> PlanReportQueryAsync(string question, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new AiAssistantUnavailableException("Please type a question first.");
        }

        var catalogText = string.Join('\n', ReportCatalog.Concat(AnalyticsCatalog).Select(r => $"- {r.Name}: {r.Description}"));

        var systemPrompt =
            $"""
             You are a routing assistant for the reporting module of Godrej WMS, a warehouse management
             system. Today's date/time (UTC) is {now:yyyy-MM-dd'T'HH:mm:ss'Z'}.

             {DomainGlossary}

             Given an admin's question, choose exactly ONE target from this list that best answers it:
             {catalogText}

             {FewShotExamples}

             Then fill in only the filter fields that are clearly implied by the question; leave everything
             else null. Dates must be ISO 8601 (yyyy-MM-dd), resolved relative to today's date above.
             Respond with JSON only, matching the schema.
             """;

        var requestBody = new JsonObject
        {
            ["model"] = _options.Model,
            ["stream"] = false,
            ["options"] = new JsonObject { ["temperature"] = 0 },
            ["format"] = BuildPlanSchema(),
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = question }
            }
        };

        var content = await SendAsync(requestBody, cancellationToken);

        PlanResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<PlanResponse>(content, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "AI assistant returned unparseable JSON: {Content}", content);
            throw new AiAssistantUnavailableException("The AI assistant couldn't understand that question. Try rephrasing it.", ex);
        }

        if (parsed is null || string.IsNullOrWhiteSpace(parsed.Kind))
        {
            throw new AiAssistantUnavailableException("The AI assistant couldn't match that question to a known report.");
        }

        var isKnownTarget = ReportCatalog.Any(r => r.Name.Equals(parsed.Kind, StringComparison.OrdinalIgnoreCase))
            || AnalyticsCatalog.Any(r => r.Name.Equals(parsed.Kind, StringComparison.OrdinalIgnoreCase));
        if (!isKnownTarget)
        {
            throw new AiAssistantUnavailableException($"The AI assistant picked an unrecognized report (\"{parsed.Kind}\"). Try rephrasing the question.");
        }

        var filter = new ReportFilter(
            WarehouseId: parsed.WarehouseId,
            Search: NullIfBlank(parsed.Search),
            Location: NullIfBlank(parsed.Location),
            Zone: NullIfBlank(parsed.Zone),
            Rack: NullIfBlank(parsed.Rack),
            Sku: parsed.Sku,
            Design: NullIfBlank(parsed.Design),
            Pkm: parsed.Pkm,
            From: ParseDate(parsed.FromDate),
            To: ParseDate(parsed.ToDate)?.AddDays(1),
            Status: NullIfBlank(parsed.Status),
            Reference: NullIfBlank(parsed.Reference));

        return new AiReportPlan(parsed.Kind, filter, parsed.Reasoning ?? "");
    }

    public async Task<string> SummarizeAsync(string question, string targetName, IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        int totalCount, string? extraContext, CancellationToken cancellationToken)
    {
        var sample = rows.Take(15).ToList();

        var systemPrompt =
            """
            You are a warehouse operations assistant. You are given the results of a report query that
            already ran (you did not run it and cannot change it). Summarize them for a warehouse admin
            in 2-3 short sentences: mention the total row count and anything operationally notable (large
            quantities, exceptions/shortages, a concentration in one warehouse/zone/user, etc). Do not
            invent numbers beyond what's given. Plain text only.
            """;

        var userPrompt =
            $"""
             Question: {question}
             Report used: {targetName}
             Total matching rows: {totalCount}
             {(extraContext is null ? "" : $"Summary stats: {extraContext}")}
             First {sample.Count} rows (JSON): {JsonSerializer.Serialize(sample, JsonOptions)}
             """;

        var requestBody = new JsonObject
        {
            ["model"] = _options.Model,
            ["stream"] = false,
            ["options"] = new JsonObject { ["temperature"] = 0.3 },
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userPrompt }
            }
        };

        try
        {
            return (await SendAsync(requestBody, cancellationToken)).Trim();
        }
        catch (AiAssistantUnavailableException)
        {
            return totalCount == 0
                ? "No matching rows were found."
                : $"Found {totalCount} matching row(s).";
        }
    }

    private async Task<string> SendAsync(JsonObject requestBody, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync("api/chat",
                new StringContent(requestBody.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
                cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AiAssistantUnavailableException(
                "Couldn't reach the local AI assistant (Ollama). Make sure it's running.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiAssistantUnavailableException("The AI assistant took too long to respond.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Ollama returned {StatusCode}: {Body}", response.StatusCode, body);
            throw new AiAssistantUnavailableException("The AI assistant returned an error.");
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        var text = payload?["message"]?["content"]?.GetValue<string>();

        return string.IsNullOrWhiteSpace(text)
            ? throw new AiAssistantUnavailableException("The AI assistant returned an empty response.")
            : text;
    }

    private static JsonObject BuildPlanSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["kind"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray(ReportCatalog.Concat(AnalyticsCatalog).Select(r => (JsonNode)JsonValue.Create(r.Name)).ToArray())
            },
            ["warehouseId"] = new JsonObject { ["type"] = new JsonArray("integer", "null") },
            ["search"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["location"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["zone"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["rack"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["sku"] = new JsonObject { ["type"] = new JsonArray("integer", "null") },
            ["design"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["pkm"] = new JsonObject { ["type"] = new JsonArray("integer", "null") },
            ["fromDate"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["toDate"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["status"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["reference"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["reasoning"] = new JsonObject { ["type"] = "string" }
        },
        ["required"] = new JsonArray("kind", "reasoning")
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTimeOffset? ParseDate(string? value) =>
        !string.IsNullOrWhiteSpace(value) && DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private sealed class PlanResponse
    {
        public string? Kind { get; set; }
        public int? WarehouseId { get; set; }
        public string? Search { get; set; }
        public string? Location { get; set; }
        public string? Zone { get; set; }
        public string? Rack { get; set; }
        public long? Sku { get; set; }
        public string? Design { get; set; }
        public int? Pkm { get; set; }
        public string? FromDate { get; set; }
        public string? ToDate { get; set; }
        public string? Status { get; set; }
        public string? Reference { get; set; }
        public string? Reasoning { get; set; }
    }
}
