using CRM.Application.CommissionDesk;
using CRM.Application.Retention;
using CRM.Domain.Enums;
using FluentValidation.TestHelper;
using Xunit;

namespace CRM.Application.Tests;

/// <summary>
/// Guards the Commission Desk's business rules: which statuses the desk may set, which of those hand
/// the policy to Retention, and the validator's note requirement on negative outcomes.
/// </summary>
public class CommissionDeskTests
{
    [Theory]
    [InlineData(ValidatorStatus.Approved, true)]
    [InlineData(ValidatorStatus.ActivePaid, true)]
    [InlineData(ValidatorStatus.ChargedBack, true)]
    [InlineData(ValidatorStatus.Nsf, true)]
    [InlineData(ValidatorStatus.BadBank, true)]
    [InlineData(ValidatorStatus.Decline, true)]
    [InlineData(ValidatorStatus.ClientCancelled, true)]
    // Not the commission desk's to set — these belong to submission/closing.
    [InlineData(ValidatorStatus.Completed, false)]
    [InlineData(ValidatorStatus.ErrorInApplicationInformation, false)]
    [InlineData(ValidatorStatus.NoUpdateInCommission, false)]
    public void Settable_covers_exactly_the_desk_statuses(ValidatorStatus status, bool expected)
        => Assert.Equal(expected, CommissionDeskStatuses.Settable.Contains(status));

    [Theory]
    [InlineData(ValidatorStatus.Nsf, true)]
    [InlineData(ValidatorStatus.BadBank, true)]
    [InlineData(ValidatorStatus.ClientCancelled, true)]
    [InlineData(ValidatorStatus.Decline, true)]
    // A healthy or charged-back policy is NOT a retention case.
    [InlineData(ValidatorStatus.Approved, false)]
    [InlineData(ValidatorStatus.ActivePaid, false)]
    [InlineData(ValidatorStatus.ChargedBack, false)]
    public void GoesToRetention_matches_the_negative_outcomes(ValidatorStatus status, bool expected)
        => Assert.Equal(expected, CommissionDeskStatuses.GoesToRetention(status));

    /// <summary>
    /// The transfer to Retention IS the status change — Retention lists sales by exactly these
    /// statuses. If the two sets ever drift, a sale the desk "sent to retention" would vanish.
    /// </summary>
    [Fact]
    public void Every_retention_outcome_is_listed_by_the_retention_desk()
    {
        foreach (var status in CommissionDeskStatuses.MovesToRetention)
            Assert.Contains(status, RetentionStatuses.All);
    }

    [Theory]
    [InlineData(ValidatorStatus.Nsf)]
    [InlineData(ValidatorStatus.BadBank)]
    [InlineData(ValidatorStatus.ClientCancelled)]
    [InlineData(ValidatorStatus.Decline)]
    public void A_retention_outcome_requires_a_note(ValidatorStatus status)
    {
        var result = new SetCommissionStatusValidator()
            .TestValidate(new SetCommissionStatusCommand(Guid.NewGuid(), status, Note: null));
        result.ShouldHaveValidationErrorFor(x => x.Note);
    }

    [Fact]
    public void A_positive_outcome_does_not_require_a_note()
    {
        var result = new SetCommissionStatusValidator()
            .TestValidate(new SetCommissionStatusCommand(Guid.NewGuid(), ValidatorStatus.ActivePaid, Note: null));
        result.ShouldNotHaveValidationErrorFor(x => x.Note);
    }

    [Fact]
    public void A_status_outside_the_desk_is_rejected()
    {
        var result = new SetCommissionStatusValidator()
            .TestValidate(new SetCommissionStatusCommand(Guid.NewGuid(), ValidatorStatus.Completed, "note"));
        result.ShouldHaveValidationErrorFor(x => x.Status);
    }

    [Theory]
    [InlineData(-1, 6)]      // negative rate
    [InlineData(250, 6)]     // > 200%
    public void Carrier_rule_rejects_an_out_of_range_rate(decimal rate, int months)
    {
        var result = new UpsertCarrierRuleValidator()
            .TestValidate(new UpsertCarrierRuleCommand(null, "ABC Life", rate, months, null));
        result.ShouldHaveValidationErrorFor(x => x.CommissionRate);
    }

    [Fact]
    public void Carrier_rule_requires_a_carrier_name()
    {
        var result = new UpsertCarrierRuleValidator()
            .TestValidate(new UpsertCarrierRuleCommand(null, "", 80m, 6, null));
        result.ShouldHaveValidationErrorFor(x => x.Carrier);
    }

    [Fact]
    public void Carrier_rule_accepts_a_typical_configuration()
    {
        // The example from the spec: ABC Life, 80%, 6 advanced months.
        var result = new UpsertCarrierRuleValidator()
            .TestValidate(new UpsertCarrierRuleCommand(null, "ABC Life", 80m, 6, "Standard terms"));
        result.ShouldNotHaveAnyValidationErrors();
    }
}
