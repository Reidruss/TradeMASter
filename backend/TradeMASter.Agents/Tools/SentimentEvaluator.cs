namespace TradeMASter.Agents.Tools;

public record SentimentSnapshot(
    string Symbol,
    double SentimentScore, // -1.0 (Extreme Bearish) to +1.0 (Extreme Bullish)
    string SentimentLabel, // Bullish, Bearish, Neutral
    double SocialBuzzScore, // 0.0 to 100.0
    IReadOnlyList<string> RecentHeadlines,
    IReadOnlyList<string> KeyThemes);

public static class SentimentEvaluator
{
    private static readonly Dictionary<string, (double Score, string Label, double Buzz, string[] Headlines, string[] Themes)> Data = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NVDA"] = (
            0.82, "Bullish", 94.5,
            new[] { "NVIDIA Next-Gen AI Architecture Surpasses Wall St Projections", "Hyperscalers Accelerate Data Center Capex Allocations", "Chip Demand Backlog Remains Elevated" },
            new[] { "Data Center Capex", "AI Infrastructure", "Enterprise Adoption" }
        ),
        ["AAPL"] = (
            0.45, "Neutral / Mildly Bullish", 82.0,
            new[] { "Apple Intelligence Rollout Drives Smartphone Upgrade Cycle", "Services Revenue Reaches Record Quarterly Run-Rate", "China Hardware Sales Show Signs of Stabilization" },
            new[] { "Device Upgrade Cycle", "Services Growth", "China Market" }
        ),
        ["MSFT"] = (
            0.74, "Bullish", 88.0,
            new[] { "Azure Cloud Growth Accelerates Powered by Generative AI", "Enterprise Copilot Seat Monetization Expands", "Cybersecurity & Gaming Segments Outperform" },
            new[] { "Azure Momentum", "Copilot Enterprise", "Cloud Margins" }
        ),
        ["TSLA"] = (
            -0.15, "Cautious / Mixed", 91.0,
            new[] { "Global EV Price Wars Put Pressure on Automotive Margins", "Robotaxi Regulatory Pathway Remains Under Scrutiny", "Energy Storage Megapack Deployments Surge 120% YoY" },
            new[] { "Gross Margin Pressure", "Robotaxi Timeline", "Energy Storage Alpha" }
        ),
        ["BTC-USD"] = (
            0.78, "Bullish", 96.0,
            new[] { "Spot Bitcoin ETFs Record Sustained Net Inflows", "On-Chain Accumulation by Long-Term Holders at Multi-Year High", "Global Central Banks Transition Toward Monetary Easing" },
            new[] { "Institutional ETF Flows", "Long-Term HODLing", "Macro Liquidity" }
        ),
        ["ETH-USD"] = (
            0.65, "Bullish", 84.0,
            new[] { "Ethereum Layer 2 Gas Consumption Hits New Peak", "Total Value Locked Across DeFi Protocols Expands", "Staking Supply Ratio Steady Above 28%" },
            new[] { "L2 Scaling", "DeFi TVL Rebound", "Staking Yield" }
        ),
        ["SPY"] = (
            0.60, "Bullish", 75.0,
            new[] { "Corporate Earnings Resilient Across S&P 500 Constituents", "Inflation Metrics Moderate in Line with Target Bands", "Market Breadth Broadening Beyond Mega-Cap Tech" },
            new[] { "Earnings Resilience", "Disinflation", "Broad Breadth" }
        ),
        ["QQQ"] = (
            0.70, "Bullish", 89.0,
            new[] { "Nasdaq Surges on Tech Sector Productivity Gains", "Semiconductor Index Outperforms", "Venture & Software Valuations Consolidate" },
            new[] { "Tech Productivity", "Semis Leadership", "Multiple Expansion" }
        )
    };

    public static SentimentSnapshot Evaluate(string symbol)
    {
        var upper = symbol.ToUpperInvariant();
        if (Data.TryGetValue(upper, out var info))
        {
            return new SentimentSnapshot(upper, info.Score, info.Label, info.Buzz, info.Headlines, info.Themes);
        }

        return new SentimentSnapshot(
            Symbol: upper,
            SentimentScore: 0.15,
            SentimentLabel: "Neutral",
            SocialBuzzScore: 50.0,
            RecentHeadlines: new[] { $"{upper} trading in line with broader sector movements", "Market participants await upcoming catalyst update" },
            KeyThemes: new[] { "Sector Correlation", "Baseline Volume" }
        );
    }
}
