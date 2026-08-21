using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradeMASter.Agents.Backtesting;
using TradeMASter.Agents.LLM;
using TradeMASter.Agents.Optimization;
using TradeMASter.Agents.Orchestration;
using TradeMASter.Agents.Personas;
using TradeMASter.Agents.Research;
using TradeMASter.Core.Backtesting;
using TradeMASter.Core.Interfaces;

namespace TradeMASter.Agents;

public static class DependencyInjection
{
    public static IServiceCollection AddAgentCommittee(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 1. LLM Client configuration
        var openAiKey = configuration["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var anthropicKey = configuration["Anthropic:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

        services.AddHttpClient("OpenAI");
        services.AddHttpClient("Anthropic");
        services.AddHttpClient<SecFundamentalResearchService>(client =>
            client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<SimulatedLlmClient>();

        if (!string.IsNullOrWhiteSpace(openAiKey))
        {
            var model = configuration["OpenAI:Model"] ?? "gpt-5.6-terra";
            var enableWebSearch = configuration.GetValue("OpenAI:EnableWebSearch", true);
            services.AddSingleton<ILlmClient>(serviceProvider => new OpenAiLlmClient(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("OpenAI"),
                openAiKey,
                model,
                enableWebSearch));
        }
        else if (!string.IsNullOrWhiteSpace(anthropicKey))
        {
            services.AddSingleton<ILlmClient>(serviceProvider => new AnthropicLlmClient(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("Anthropic"),
                anthropicKey));
        }
        else
        {
            // Simulated quantitative fallback engine for zero-dependency offline runs
            services.AddSingleton<ILlmClient>(serviceProvider =>
                serviceProvider.GetRequiredService<SimulatedLlmClient>());
        }

        // 2. Register Individual Personas
        services.AddScoped<TechnicalAnalyst>();
        services.AddScoped<FundamentalAnalyst>();
        services.AddScoped<SentimentAnalyst>();
        services.AddScoped<RiskAuditor>();
        services.AddScoped<PortfolioArbiter>();
        services.AddScoped<MacroRegimeObserver>();
        services.AddScoped<AssetSelectionCandidateScreener>();
        services.AddScoped<CandidateApprovalGate>();
        services.AddScoped<QuantitativeAllocator>();
        services.AddScoped<RiskComplianceAuditor>();
        services.AddScoped<ExecutionRebalancingManager>();
        services.AddScoped<PostMortemReflectionAgent>();

        // 3. Register Orchestrator & Deliberation Engine
        services.AddScoped<IDeliberationEngine, AgentDebateOrchestrator>();
        services.AddScoped<IMarketIntelligenceService, MarketIntelligenceOrchestrator>();

        // 4. Register Backtesting Engine
        services.AddScoped<IBacktestEngine, BacktestEngine>();

        // 5. Register Bi-Weekly Portfolio Optimizer
        services.AddScoped<IPortfolioOptimizerService, PortfolioOptimizerService>();

        return services;
    }
}
