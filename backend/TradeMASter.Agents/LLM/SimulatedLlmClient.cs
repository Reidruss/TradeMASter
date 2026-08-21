using System.Text.Json;
using System.Text.RegularExpressions;
using TradeMASter.Core.Common;

namespace TradeMASter.Agents.LLM;

public class SimulatedLlmClient : ILlmClient
{
    public string ProviderName => "QuantitativeAgenticEngine";

    public Task<Result<LlmResponse>> GenerateCompletionAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var lastUserMsg = request.Messages.LastOrDefault(m => m.Role == LlmRole.User)?.Content ?? string.Empty;
        var systemMsg = request.Messages.FirstOrDefault(m => m.Role == LlmRole.System)?.Content ?? string.Empty;

        // Parse context from prompt text
        var symbolMatch = Regex.Match(lastUserMsg, @"Asset:\s*([A-Za-z0-9\-]+)");
        var symbol = symbolMatch.Success ? symbolMatch.Groups[1].Value : "ASSET";

        var priceMatch = Regex.Match(lastUserMsg, @"(?:Current Price|Asset:[^\r\n]+ at):\s*\$?([0-9\.,]+)");
        var priceStr = priceMatch.Success ? priceMatch.Groups[1].Value : "scenario price unavailable";

        var rsiMatch = Regex.Match(lastUserMsg, @"RSI:\s*([0-9\.]+)");
        var rsiVal = rsiMatch.Success && double.TryParse(rsiMatch.Groups[1].Value, out var rsi) ? rsi : 55.0;

        var trendMatch = Regex.Match(lastUserMsg, @"Trend:\s*([A-Za-z\s]+)");
        var trendStr = trendMatch.Success ? trendMatch.Groups[1].Value.Trim() : "Bullish";

        var peMatch = Regex.Match(lastUserMsg, @"(?:P/E Ratio|P/E)\s*:?\s*([0-9\.]+)");
        var peVal = peMatch.Success && double.TryParse(peMatch.Groups[1].Value, out var pe) ? pe : 28.5;

        var revMatch = Regex.Match(lastUserMsg, @"(?:YoY Revenue Growth|YoY growth)\s*:?\s*([0-9\.\+\-%]+)", RegexOptions.IgnoreCase);
        var revStr = revMatch.Success ? revMatch.Groups[1].Value.Trim() : "+22.5%";

        var sentimentMatch = Regex.Match(lastUserMsg, @"(?:News Sentiment Score|Cached sentiment baseline[^:]*):\s*([0-9\.\+\-]+)", RegexOptions.IgnoreCase);
        var sentScore = sentimentMatch.Success && double.TryParse(sentimentMatch.Groups[1].Value, out var sc) ? sc : 0.65;

        // Check if JSON format requested (e.g. structured decision)
        if (request.JsonMode)
        {
            var isBullish = rsiVal < 70 && sentScore > 0;
            var direction = isBullish ? (rsiVal < 35 ? "StrongBuy" : "Bullish") : (rsiVal > 75 ? "Bearish" : "Neutral");
            var confidence = Math.Round(0.72 + (Math.Abs(sentScore) * 0.15), 2);

            var keyFactors = new List<string>
            {
                $"Trend: {trendStr}",
                $"RSI: {rsiVal:F1} ({(rsiVal > 70 ? "Overbought" : rsiVal < 35 ? "Oversold" : "Neutral Zone")})",
                $"P/E: {peVal:F1}x vs Sector",
                $"Sentiment Index: {sentScore:+0.00;-0.00;0.00}"
            };

            var fallbackJson = JsonSerializer.Serialize(new
            {
                direction,
                confidence,
                reasoning = $"Synthetic mock scenario for {symbol}: the supplied baseline uses P/E {peVal:F1}x, revenue growth {revStr}, and sentiment {sentScore:+0.00;-0.00;0.00}. These are test inputs, not observed live facts.",
                keyFactors
            });

            return Task.FromResult(Result.Success(new LlmResponse(fallbackJson, 140, 65, "stop")));
        }

        // Generate tailored persona responses
        if (systemMsg.Contains("technical", StringComparison.OrdinalIgnoreCase))
        {
            var techResponse = $"{symbol} at ${priceStr} exhibits {trendStr.ToLower()} momentum. 14-period RSI is calibrated at {rsiVal:F1}, holding healthy clearance below overbought thresholds while exponential moving averages provide ascending dynamic support.";
            return Task.FromResult(Result.Success(new LlmResponse(techResponse, 110, 48, "stop")));
        }

        if (systemMsg.Contains("fundamental", StringComparison.OrdinalIgnoreCase))
        {
            var fundResponse = $"{symbol} demonstrates durable fundamental quality with trailing revenue expanding {revStr} YoY against a P/E multiple of {peVal:F1}x. Cash flow margins and balance sheet strength justify maintaining core allocation.";
            return Task.FromResult(Result.Success(new LlmResponse(fundResponse, 115, 52, "stop")));
        }

        if (systemMsg.Contains("sentiment", StringComparison.OrdinalIgnoreCase))
        {
            var sentResponse = $"Natural language tone for {symbol} registers a positive conviction score of {sentScore:+0.00;-0.00;0.00}. News coverage highlights strong institutional inflows and positive forward product catalysts with minimal tail-risk exposure.";
            return Task.FromResult(Result.Success(new LlmResponse(sentResponse, 105, 45, "stop")));
        }

        if (systemMsg.Contains("risk", StringComparison.OrdinalIgnoreCase))
        {
            var riskResponse = $"Risk audit cleared for {symbol}. Current sizing satisfies portfolio concentration guardrails (<25%), downside volatility is bounded by a 1.5x ATR trailing buffer, and drawdown caps remain unbreached.";
            return Task.FromResult(Result.Success(new LlmResponse(riskResponse, 100, 42, "stop")));
        }

        if (systemMsg.Contains("arbiter", StringComparison.OrdinalIgnoreCase) || lastUserMsg.Contains("Arbiter", StringComparison.OrdinalIgnoreCase))
        {
            var arbiterResponse = $"Committee consensus reached on {symbol}: Technical trend alignment ({trendStr}) and solid fundamentals ({revStr} YoY growth) outweigh near-term volatility. Approved for risk-adjusted rebalancing.";
            return Task.FromResult(Result.Success(new LlmResponse(arbiterResponse, 120, 50, "stop")));
        }

        // Generic defense / cross-examination response
        var generalResponse = $"Regarding {symbol}: Data indicates robust risk-adjusted expected value supported by trend structure ({trendStr}) and positive sentiment ({sentScore:+0.00;-0.00;0.00}).";
        return Task.FromResult(Result.Success(new LlmResponse(generalResponse, 95, 40, "stop")));
    }
}
