using Microsoft.AspNetCore.SignalR;
using TradeMASter.Agents.Orchestration;
using TradeMASter.Core.Enums;

namespace TradeMASter.Api.Hubs;

public interface IAgentDebateClient
{
    Task ReceiveDeliberationStatus(string step, string message);
    Task ReceiveAgentThought(string role, string name, string thought, string signal, double confidence, string[] keyFactors);
    Task ReceiveCrossExamMessage(string speakerRole, string speakerName, string content, string timestamp);
    Task ReceiveConsensusVerdict(object verdictPayload);
}

public class AgentDebateHub : Hub<IAgentDebateClient>
{
    private readonly IDeliberationEngine _deliberationEngine;

    public AgentDebateHub(IDeliberationEngine deliberationEngine)
    {
        _deliberationEngine = deliberationEngine;
    }

    public async Task JoinSymbolRoom(string symbol)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, symbol.ToUpperInvariant());
    }

    public async Task LeaveSymbolRoom(string symbol)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, symbol.ToUpperInvariant());
    }

    public async Task StartLiveDeliberation(string symbol)
    {
        var upper = symbol.ToUpperInvariant();
        
        // 1. Notify start
        await Clients.Caller.ReceiveDeliberationStatus("IngestingData", $"Constructing live market, indicator & sentiment context for {upper}...");
        await Task.Delay(350);

        // 2. Stream deliberation via orchestrator
        await Clients.Caller.ReceiveDeliberationStatus("ParallelAnalysis", "Technical, Fundamental & Sentiment agents analyzing in parallel...");
        
        var result = await _deliberationEngine.DeliberateAsync(upper, cancellationToken: Context.ConnectionAborted);
        if (result.IsFailure)
        {
            await Clients.Caller.ReceiveDeliberationStatus("Error", result.Error!);
            return;
        }

        var deliberation = result.Value;

        // 3. Stream each agent's individual decision
        foreach (var decision in deliberation.Decisions)
        {
            var roleName = decision.Role.ToString();
            var factors = System.Text.Json.JsonSerializer.Deserialize<string[]>(decision.KeyFactorsJson) ?? Array.Empty<string>();
            var signal = decision.Direction.ToString();

            await Clients.Caller.ReceiveAgentThought(
                roleName,
                FormatRoleName(decision.Role),
                decision.Reasoning,
                signal,
                decision.ConfidenceScore,
                factors
            );
            await Task.Delay(250);
        }

        // 4. Stream Cross-Examination exchanges
        await Clients.Caller.ReceiveDeliberationStatus("CrossExam", "Arbiter moderating cross-examination and thesis defense...");
        foreach (var msg in deliberation.DebateLog)
        {
            await Clients.Caller.ReceiveCrossExamMessage(
                msg.SpeakerRole,
                msg.SpeakerName,
                msg.Content,
                msg.TimestampUtc.ToString("o")
            );
            await Task.Delay(200);
        }

        // 5. Final consensus synthesis broadcast
        await Clients.Caller.ReceiveDeliberationStatus("Synthesizing", "Arbiter formulating final consensus directive & sizing proposal...");
        await Task.Delay(200);

        var payload = new
        {
            Session = deliberation.Session,
            Verdict = deliberation.Session.FinalVerdict.ToString(),
            deliberation.Session.FinalConsensusSummary,
            deliberation.Session.OverallConfidence,
            deliberation.Session.IsRiskApproved,
            deliberation.Session.RiskNotes,
            deliberation.RecommendedOrder,
            deliberation.ExecutedOrder
        };

        await Clients.Caller.ReceiveConsensusVerdict(payload);
        await Clients.Caller.ReceiveDeliberationStatus("Complete", "Deliberation complete.");
    }

    private static string FormatRoleName(AgentRole role) => role switch
    {
        AgentRole.TechnicalAnalyst => "Technical & Quantitative Analyst",
        AgentRole.FundamentalAnalyst => "Fundamental & Macro Analyst",
        AgentRole.SentimentAnalyst => "Sentiment & News Analyst",
        AgentRole.RiskAuditor => "Risk Guard & Compliance Auditor",
        AgentRole.PortfolioArbiter => "Portfolio Arbiter",
        _ => role.ToString()
    };
}
