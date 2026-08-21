using Microsoft.AspNetCore.Mvc;
using TradeMASter.Core.Enums;
using TradeMASter.Core.Interfaces;
using TradeMASter.Infrastructure.Persistence.Repositories;

namespace TradeMASter.Api.Endpoints;

public record CreateOrderDto(
    string Symbol,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal? LimitPrice = null,
    decimal? StopPrice = null);

public static class OrderEndpoints
{
    public static RouteGroupBuilder MapOrderEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/orders").WithTags("Orders & Execution");

        group.MapGet("/", async (
            [FromQuery] OrderStatus? status,
            [FromServices] IPortfolioRepository portfolioRepo,
            [FromServices] IBrokerClient brokerClient) =>
        {
            var portfolio = await portfolioRepo.GetActivePortfolioWithDetailsAsync();
            if (portfolio is null)
            {
                return Results.NotFound(new { error = "No active portfolio found." });
            }

            var ordersResult = await brokerClient.GetOrdersAsync(portfolio.Id, status);
            return ordersResult.IsSuccess
                ? Results.Ok(ordersResult.Value)
                : Results.BadRequest(new { error = ordersResult.Error });
        })
        .WithName("GetOrders")
        .WithSummary("Get recent orders with optional status filter");

        group.MapPost("/", async (
            [FromBody] CreateOrderDto dto,
            [FromServices] IPortfolioRepository portfolioRepo,
            [FromServices] IBrokerClient brokerClient) =>
        {
            var portfolio = await portfolioRepo.GetActivePortfolioWithDetailsAsync();
            if (portfolio is null)
            {
                return Results.NotFound(new { error = "No active portfolio found." });
            }

            var orderRequest = new OrderRequest(
                portfolio.Id,
                dto.Symbol,
                dto.Side,
                dto.Type,
                dto.Quantity,
                dto.LimitPrice,
                dto.StopPrice
            );

            var submitResult = await brokerClient.SubmitOrderAsync(orderRequest);
            return submitResult.IsSuccess
                ? Results.Created($"/api/orders/{submitResult.Value.Id}", submitResult.Value)
                : Results.BadRequest(new { error = submitResult.Error });
        })
        .WithName("SubmitOrder")
        .WithSummary("Submit a new paper-trading order; this endpoint does not place live Robinhood orders");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            [FromServices] IBrokerClient brokerClient) =>
        {
            var cancelResult = await brokerClient.CancelOrderAsync(id);
            return cancelResult.IsSuccess
                ? Results.Ok(cancelResult.Value)
                : Results.BadRequest(new { error = cancelResult.Error });
        })
        .WithName("CancelOrder")
        .WithSummary("Cancel an open or pending order");

        return group;
    }
}
