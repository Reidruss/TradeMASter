using System.Text.Json;
using TradeMASter.Agents.LLM;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;

namespace TradeMASter.Agents.Personas;

public class TechnicalAnalyst : IAgentPersona
{
    public AgentRole Role => AgentRole.TechnicalAnalyst;
    public string PersonaName => "Technical Strategist";
    private readonly ILlmClient _llm;

    public TechnicalAnalyst(ILlmClient llm)
    {
        _llm = llm;
    }

    public async Task<AgentDecision> AnalyzeAsync(MarketAnalysisContext context, CancellationToken cancellationToken = default)
    {
        var ind = context.Indicators;
        
        // Quantitative rule-based signals
        var isBullishTrend = ind.LastClose > ind.Ema21 && ind.Ema21 > ind.Ema50;
        var isRsiOversold = ind.Rsi14 < 35;
        var isRsiOverbought = ind.Rsi14 > 70;
        var isMacdBullish = ind.MacdHistogram > 0;

        var signal = (isBullishTrend && isMacdBullish && !isRsiOverbought)
            ? SignalDirection.StrongBuy
            : ((isBullishTrend && !isRsiOverbought) || isRsiOversold)
            ? SignalDirection.Bullish
            : (ind.LastClose < ind.Ema50 && !isMacdBullish)
            ? SignalDirection.Bearish
            : SignalDirection.Neutral;

        var confidence = signal switch
        {
            SignalDirection.StrongBuy => 0.85,
            SignalDirection.Bullish => 0.75,
            SignalDirection.Bearish => 0.70,
            _ => 0.50
        };

        var factors = new List<string>
        {
            $"Trend: {ind.TrendDescription}",
            $"RSI(14): {ind.Rsi14} ({(ind.Rsi14 > 70 ? "Overbought" : ind.Rsi14 < 30 ? "Oversold" : "Neutral")})",
            $"MACD Hist: {ind.MacdHistogram:F2}",
            $"EMA 21/50: ${ind.Ema21} / ${ind.Ema50}",
            $"ATR(14) Volatility: ${ind.Atr14}"
        };

        var prompt = $@"You are the Technical & Quantitative Analyst in a trading committee.
Asset: {context.Symbol}
Current Price: ${context.Quote.Price}
Trend: {ind.TrendDescription}
RSI: {ind.Rsi14}
MACD Histogram: {ind.MacdHistogram}
EMA9: ${ind.Ema9}, EMA21: ${ind.Ema21}, EMA50: ${ind.Ema50}, EMA200: ${ind.Ema200}
Bollinger Bands: [${ind.BollingerLower} - ${ind.BollingerUpper}]

Synthesize your concise technical thesis (2-3 sentences) evaluating support/resistance, momentum, and risk/reward.";

        var llmRes = await _llm.GenerateCompletionAsync(new LlmRequest(new List<ChatMessage>
        {
            new(LlmRole.System, "You are a quantitative technical analyst focusing on price action, moving averages, and momentum indicators."),
            new(LlmRole.User, prompt)
        }), cancellationToken);

        var reasoning = llmRes.IsSuccess && !string.IsNullOrWhiteSpace(llmRes.Value.Content)
            ? llmRes.Value.Content.Trim()
            : $"Price action is {ind.TrendDescription} with RSI at {ind.Rsi14:F1} and MACD histogram at {ind.MacdHistogram:F2}. Key dynamic support is established around EMA 21 (${ind.Ema21}).";

        return new AgentDecision(
            Guid.Empty,
            context.Symbol,
            Role,
            signal,
            confidence,
            reasoning,
            JsonSerializer.Serialize(factors)
        );
    }

    public async Task<string> DefendThesisAsync(string challenge, MarketAnalysisContext context, CancellationToken cancellationToken = default)
    {
        var ind = context.Indicators;
        var prompt = $@"The Portfolio Arbiter asks you this challenge regarding {context.Symbol}:
""{challenge}""

Technical Context:
Price: ${ind.LastClose}, Trend: {ind.TrendDescription}, RSI: {ind.Rsi14}, MACD: {ind.MacdHistogram}, ATR: ${ind.Atr14}.

Respond concisely (1-2 sentences) defending or adapting your technical thesis.";

        var res = await _llm.GenerateCompletionAsync(new LlmRequest(new List<ChatMessage>
        {
            new(LlmRole.System, "You are a sharp, quantitative technical trader."),
            new(LlmRole.User, prompt)
        }), cancellationToken);

        return res.IsSuccess ? res.Value.Content.Trim() : $"While counter-arguments exist, price structure remains defended above EMA 50 (${ind.Ema50}) with positive trend momentum.";
    }
}
