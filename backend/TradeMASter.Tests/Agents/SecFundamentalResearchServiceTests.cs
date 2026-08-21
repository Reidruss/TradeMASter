using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TradeMASter.Agents.Research;
using Xunit;

namespace TradeMASter.Tests.Agents;

public sealed class SecFundamentalResearchServiceTests
{
    [Fact]
    public async Task GetAsync_UsesVerifiedCompanyFactsAndPreservesProvenance()
    {
        var handler = new SecFixtureHandler();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sec:UserAgent"] = "TradeMASter.Tests test@example.com"
        }).Build();
        var service = new SecFundamentalResearchService(new HttpClient(handler), configuration);

        var result = await service.GetAsync("ZZZT", 2_000_000_000m, false, CancellationToken.None);

        result.IsSynthetic.Should().BeFalse();
        result.CompanyName.Should().Be("Zeta Test Corporation");
        result.HealthScore.Should().BeGreaterThan(70m);
        result.RevenueGrowthYoyPercent.Should().Be(25m);
        result.ProfitMarginPercent.Should().Be(10m);
        result.FreeCashFlowYieldPercent.Should().Be(5m);
        result.Sources.Should().Contain(source => source.Contains("companyfacts", StringComparison.Ordinal));
        result.Sources.Should().Contain(source => source.Contains("0001234525000001", StringComparison.Ordinal));
        handler.UserAgents.Should().OnlyContain(value => value == "TradeMASter.Tests test@example.com");
    }

    private sealed class SecFixtureHandler : HttpMessageHandler
    {
        public List<string> UserAgents { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            UserAgents.Add(request.Headers.UserAgent.ToString());
            var json = request.RequestUri!.AbsolutePath.Contains("company_tickers_exchange", StringComparison.Ordinal)
                ? """
                  {"fields":["cik","name","ticker","exchange"],"data":[[12345,"Zeta Test Corporation","ZZZT","NYSE"]]}
                  """
                : CompanyFacts;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }

        private const string CompanyFacts = """
            {
              "entityName":"Zeta Test Corporation",
              "facts":{"us-gaap":{
                "RevenueFromContractWithCustomerExcludingAssessedTax":{"units":{"USD":[
                  {"start":"2024-01-01","end":"2024-12-31","val":1000000000,"form":"10-K","fp":"FY","fy":2024,"filed":"2025-02-15","accn":"00012345-25-000001"},
                  {"start":"2023-01-01","end":"2023-12-31","val":800000000,"form":"10-K","fp":"FY","fy":2023,"filed":"2024-02-15","accn":"00012345-24-000001"}
                ]}},
                "NetIncomeLoss":{"units":{"USD":[
                  {"start":"2024-01-01","end":"2024-12-31","val":100000000,"form":"10-K","fp":"FY","fy":2024,"filed":"2025-02-15","accn":"00012345-25-000001"}
                ]}},
                "NetCashProvidedByUsedInOperatingActivities":{"units":{"USD":[
                  {"start":"2024-01-01","end":"2024-12-31","val":150000000,"form":"10-K","fp":"FY","fy":2024,"filed":"2025-02-15","accn":"00012345-25-000001"}
                ]}},
                "PaymentsToAcquirePropertyPlantAndEquipment":{"units":{"USD":[
                  {"start":"2024-01-01","end":"2024-12-31","val":50000000,"form":"10-K","fp":"FY","fy":2024,"filed":"2025-02-15","accn":"00012345-25-000001"}
                ]}},
                "StockholdersEquity":{"units":{"USD":[
                  {"end":"2024-12-31","val":500000000,"form":"10-K","fp":"FY","fy":2024,"filed":"2025-02-15","accn":"00012345-25-000001"}
                ]}},
                "LongTermDebt":{"units":{"USD":[
                  {"end":"2024-12-31","val":250000000,"form":"10-K","fp":"FY","fy":2024,"filed":"2025-02-15","accn":"00012345-25-000001"}
                ]}}
              }}
            }
            """;
    }
}
