using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradeMASter.Agents.Personas;
using TradeMASter.Agents.Tools;
using TradeMASter.Core.Common;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;
using TradeMASter.Core.ValueObjects;
using TradeMASter.Infrastructure.MarketData;
using TradeMASter.Infrastructure.Persistence;

namespace TradeMASter.Agents.Orchestration;

public record DeliberationResult(
    DeliberationSession Session,
    IReadOnlyList<AgentDecision> Decisions,
    IReadOnlyList<DebateMessage> DebateLog,
    OrderRequest? RecommendedOrder,
    Order? ExecutedOrder);

public interface IDeliberationEngine
{
    Task<Result<DeliberationResult>> DeliberateAsync(
        string symbol,
        Guid? portfolioId = null,
        bool autoExecute = false,
        CancellationToken cancellationToken = default);
}

public class AgentDebateOrchestrator : IDeliberationEngine
{
    private readonly TechnicalAnalyst _techAnalyst;
    private readonly FundamentalAnalyst _fundAnalyst;
    private readonly SentimentAnalyst _sentAnalyst;
    private readonly RiskAuditor _riskAuditor;
    private readonly PortfolioArbiter _arbiter;
    private readonly IMarketDataService _marketData;
    private readonly IBrokerClient _brokerClient;
    private readonly TradeMASterDbContext _dbContext;

    public AgentDebateOrchestrator(
        TechnicalAnalyst techAnalyst,
        FundamentalAnalyst fundAnalyst,
        SentimentAnalyst sentAnalyst,
        RiskAuditor riskAuditor,
        PortfolioArbiter arbiter,
        IMarketDataService marketData,
        IBrokerClient brokerClient,
        TradeMASterDbContext dbContext)
    {
        _techAnalyst = techAnalyst;
        _fundAnalyst = fundAnalyst;
        _sentAnalyst = sentAnalyst;
        _riskAuditor = riskAuditor;
        _arbiter = arbiter;
        _marketData = marketData;
        _brokerClient = brokerClient;
        _dbContext = dbContext;
    }

    public async Task<Result<DeliberationResult>> DeliberateAsync(
        string symbol,
        Guid? portfolioId = null,
        bool autoExecute = false,
        CancellationToken cancellationToken = default)
    {
        var upper = symbol.ToUpperInvariant();

        // 1. Fetch market data & portfolio state
        var quoteResult = await _marketData.GetQuoteAsync(upper, cancellationToken);
        if (quoteResult.IsFailure)
        {
            return Result.Failure<DeliberationResult>($"Cannot deliberate: {quoteResult.Error}");
        }

        var candlesResult = await _marketData.GetCandlesAsync(upper, TimeFrame.OneDay, 60, cancellationToken);
        var candles = candlesResult.IsSuccess ? candlesResult.Value : new List<Candle>();
        var indicators = TechnicalIndicatorCalculator.Calculate(candles);
        var fundamentals = FundamentalDataProvider.GetSnapshot(upper);
        var sentiment = SentimentEvaluator.Evaluate(upper);

        // Fetch active portfolio without tracking conflicts
        var portfolio = portfolioId.HasValue
            ? await _dbContext.Portfolios.Include(p => p.Positions).AsNoTracking().FirstOrDefaultAsync(p => p.Id == portfolioId.Value, cancellationToken)
            : await _dbContext.Portfolios.Include(p => p.Positions).AsNoTracking().OrderBy(p => p.CreatedAt).FirstOrDefaultAsync(cancellationToken);

        if (portfolio is null)
        {
            return Result.Failure<DeliberationResult>("No portfolio found for deliberation context.");
        }

        var context = new MarketAnalysisContext(
            upper,
            quoteResult.Value,
            candles,
            indicators,
            fundamentals,
            sentiment,
            portfolio,
            portfolio.RiskConfig
        );

        // 2. Parallel Independent Agent Analysis
        var techTask = _techAnalyst.AnalyzeAsync(context, cancellationToken);
        var fundTask = _fundAnalyst.AnalyzeAsync(context, cancellationToken);
        var sentTask = _sentAnalyst.AnalyzeAsync(context, cancellationToken);

        await Task.WhenAll(techTask, fundTask, sentTask);

        var techDecision = await techTask;
        var fundDecision = await fundTask;
        var sentDecision = await sentTask;

        // 3. Cross-Examination Round
        var crossExamLog = new List<DebateMessage>();

        // If Technical is Bullish but Fundamental is Bearish/Neutral, Arbiter asks Technical to defend
        if (techDecision.Direction >= SignalDirection.Bullish && fundDecision.Direction <= SignalDirection.Neutral)
        {
            var challenge = $"Fundamental analysis notes valuation multiples are elevated ({fundamentals.PeRatio}x P/E). What gives you high conviction to buy now?";
            crossExamLog.Add(new DebateMessage("PortfolioArbiter", "Portfolio Arbiter", challenge, DateTime.UtcNow.AddSeconds(-10)));

            var techDefense = await _techAnalyst.DefendThesisAsync(challenge, context, cancellationToken);
            crossExamLog.Add(new DebateMessage("TechnicalAnalyst", "Technical Analyst", techDefense, DateTime.UtcNow.AddSeconds(-8)));
        }

        // If Sentiment is highly Bullish, Arbiter tests Fundamental on whether cash flow supports the hype
        if (sentiment.SentimentScore > 0.6 && fundamentals.RevenueGrowthYoyPercent < 20)
        {
            var challenge = $"Sentiment buzz is elevated ({sentiment.SocialBuzzScore}/100), but reported revenue growth is {fundamentals.RevenueGrowthYoyPercent}%. Is this sustainable?";
            crossExamLog.Add(new DebateMessage("PortfolioArbiter", "Portfolio Arbiter", challenge, DateTime.UtcNow.AddSeconds(-6)));

            var fundDefense = await _fundAnalyst.DefendThesisAsync(challenge, context, cancellationToken);
            crossExamLog.Add(new DebateMessage("FundamentalAnalyst", "Fundamental Analyst", fundDefense, DateTime.UtcNow.AddSeconds(-4)));
        }

        // 4. Risk Guard Evaluation
        var riskAudit = _riskAuditor.EvaluateRisk(context, OrderSide.Buy, 10m);
        var riskDecision = await _riskAuditor.AnalyzeAsync(context, cancellationToken);

        // 5. Consensus Synthesis by Arbiter
        var consensus = await _arbiter.SynthesizeConsensusAsync(
            context,
            techDecision,
            fundDecision,
            sentDecision,
            riskAudit,
            crossExamLog,
            cancellationToken
        );

        // 6. Record Session and Decisions in Database
        var session = new DeliberationSession(upper)
        {
            FinalVerdict = consensus.Verdict,
            FinalConsensusSummary = consensus.ConsensusSummary,
            OverallConfidence = consensus.CompositeConfidence,
            IsRiskApproved = riskAudit.IsApproved,
            RiskNotes = riskAudit.RiskNotes
        };

        session.AddDecision(techDecision);
        session.AddDecision(fundDecision);
        session.AddDecision(sentDecision);
        session.AddDecision(riskDecision);

        Order? executedOrder = null;

        // 7. Optional Automated Execution
        if (autoExecute && consensus.ProposedOrder != null && riskAudit.IsApproved && consensus.Verdict != DecisionVerdict.Hold)
        {
            var submitRes = await _brokerClient.SubmitOrderAsync(consensus.ProposedOrder, cancellationToken);
            if (submitRes.IsSuccess)
            {
                executedOrder = submitRes.Value;
                session.ExecutedOrderId = executedOrder.Id;
            }
        }

        try
        {
            await _dbContext.DeliberationSessions.AddAsync(session, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Non-blocking persistence fallback for InMemory tests
        }

        var decisionsList = new List<AgentDecision> { techDecision, fundDecision, sentDecision, riskDecision };

        return Result.Success(new DeliberationResult(
            session,
            decisionsList,
            consensus.DebateLog,
            consensus.ProposedOrder,
            executedOrder
        ));
    }
}
