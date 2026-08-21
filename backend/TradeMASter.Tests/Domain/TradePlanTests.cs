using FluentAssertions;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using Xunit;

namespace TradeMASter.Tests.Domain;

public sealed class TradePlanTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Approval_RequiresExactHashAndPrimaryPhrase()
    {
        var plan = Plan();

        plan.Approve(new string('b', 64), TradePlan.PrimaryApprovalConfirmation, null, DateTime.UtcNow).IsFailure.Should().BeTrue();
        plan.Approve(Hash, "approve", null, DateTime.UtcNow).IsFailure.Should().BeTrue();
        plan.Approve(Hash, TradePlan.PrimaryApprovalConfirmation, null, DateTime.UtcNow).IsSuccess.Should().BeTrue();

        plan.Status.Should().Be(TradePlanStatus.Approved);
        plan.ApprovedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void MaterialPlan_RequiresExactSecondPhrase()
    {
        var plan = Plan(requiresSecondary: true);

        plan.Approve(Hash, TradePlan.PrimaryApprovalConfirmation, null, DateTime.UtcNow).IsFailure.Should().BeTrue();
        plan.Approve(Hash, TradePlan.PrimaryApprovalConfirmation, "confirm", DateTime.UtcNow).IsFailure.Should().BeTrue();
        plan.Approve(Hash, TradePlan.PrimaryApprovalConfirmation, TradePlan.SecondaryApprovalConfirmation, DateTime.UtcNow)
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Approval_IsIdempotentOnlyForTheSameHash()
    {
        var plan = Plan();
        plan.Approve(Hash, TradePlan.PrimaryApprovalConfirmation, null, DateTime.UtcNow).IsSuccess.Should().BeTrue();
        var approvedAt = plan.ApprovedAtUtc;

        plan.Approve(Hash, string.Empty, null, DateTime.UtcNow.AddSeconds(1)).IsSuccess.Should().BeTrue();
        plan.Approve(new string('b', 64), string.Empty, null, DateTime.UtcNow.AddSeconds(2)).IsFailure.Should().BeTrue();

        plan.ApprovedAtUtc.Should().Be(approvedAt);
    }

    [Fact]
    public void ExpiredPlan_FailsClosed()
    {
        var plan = Plan();

        plan.RefreshExpiry(DateTime.UtcNow.AddMinutes(10)).Should().BeTrue();
        plan.Approve(Hash, TradePlan.PrimaryApprovalConfirmation, null, DateTime.UtcNow.AddMinutes(10)).IsFailure.Should().BeTrue();

        plan.Status.Should().Be(TradePlanStatus.Expired);
    }

    [Fact]
    public void Rejection_RequiresExactHashAndMeaningfulReason()
    {
        var plan = Plan();

        plan.Reject(new string('b', 64), "The allocation is too concentrated.", DateTime.UtcNow).IsFailure.Should().BeTrue();
        plan.Reject(Hash, "no", DateTime.UtcNow).IsFailure.Should().BeTrue();
        plan.Reject(Hash, "The allocation is too concentrated.", DateTime.UtcNow).IsSuccess.Should().BeTrue();

        plan.Status.Should().Be(TradePlanStatus.Rejected);
        plan.DecisionReason.Should().Be("The allocation is too concentrated.");
    }

    private static TradePlan Plan(bool requiresSecondary = false) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Hash,
        "{}",
        DateTime.UtcNow.AddMinutes(5),
        1,
        requiresSecondary,
        requiresSecondary ? ["Material notional"] : []);
}
