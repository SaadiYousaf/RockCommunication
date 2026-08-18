using System;
using System.Collections.Generic;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Hr;
using CRM.Domain.Entities;
using Xunit;
using DomainRoles = CRM.Domain.Enums.Roles;

namespace CRM.Application.Tests;

/// <summary>
/// Pure (no-DB) unit tests that lock in the MONEY math and the ACCESS gate for HR payroll:
///   • <see cref="PayrollDeductions.Auto"/> — the four attendance-driven deduction amounts.
///   • <see cref="PayrollConfigAccess.EnsureCanConfigure"/> — who may edit a call centre's rules.
/// </summary>
public class PayrollTests
{
    // A minimal in-memory ICurrentUser. IsSuperAdmin is the interface's default member
    // (derived from Roles), so it is deliberately not re-implemented here.
    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid? UserId { get; init; }
        public string? UserName { get; init; }
        public Guid? AgencyId { get; init; }
        public Guid? CallCenterId { get; init; }
        public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
        public bool IsAuthenticated { get; init; } = true;
        public string? IpAddress { get; init; }
    }

    // ── PayrollDeductions.Auto ────────────────────────────────────────────────

    [Fact]
    public void Auto_computes_each_deduction_from_a_worked_example()
    {
        // basic 60000 over 24 working days => a day's pay of 2500.
        var cfg = new CallCenterPayrollConfig
        {
            LateComingFine = 500m,   // flat per occurrence
            HalfDayFactor = 0.5m,
            AbsentDayFactor = 1.0m,
            NcnsFactor = 2.0m
        };

        var (late, half, absent, ncns) = PayrollDeductions.Auto(
            basicSalary: 60000m, workingDays: 24,
            lateComing: 3, halfDays: 1, absentDays: 2, ncns: 1, cfg);

        Assert.Equal(1500m, late);   // 3 late  × 500 flat        = 1500
        Assert.Equal(1250m, half);   // 1 half  × 2500 × 0.5      = 1250
        Assert.Equal(5000m, absent); // 2 absent× 2500 × 1.0      = 5000
        Assert.Equal(5000m, ncns);   // 1 ncns  × 2500 × 2.0      = 5000
    }

    [Fact]
    public void Auto_rounds_half_up_away_from_zero()
    {
        // perDay = 50000 / 21 = 2380.952...; half = 2380.952 × 0.5 = 1190.476 → rounds to 1190.
        // One absent = 2380.952 → rounds to 2381 (away-from-zero at .95).
        var cfg = new CallCenterPayrollConfig
        {
            LateComingFine = 0m,
            HalfDayFactor = 0.5m,
            AbsentDayFactor = 1.0m,
            NcnsFactor = 2.0m
        };

        var (_, half, absent, ncns) = PayrollDeductions.Auto(
            basicSalary: 50000m, workingDays: 21,
            lateComing: 0, halfDays: 1, absentDays: 1, ncns: 1, cfg);

        Assert.Equal(1190m, half);   // 1190.476  → 1190
        Assert.Equal(2381m, absent); // 2380.952  → 2381
        Assert.Equal(4762m, ncns);   // 4761.904  → 4762
    }

    [Fact]
    public void Auto_with_zero_working_days_does_not_divide_by_zero()
    {
        // No working days ⇒ a day's pay is 0, so the day-based deductions collapse to 0.
        // The flat late fine is independent of working days and must still be charged.
        var cfg = new CallCenterPayrollConfig
        {
            LateComingFine = 500m,
            HalfDayFactor = 0.5m,
            AbsentDayFactor = 1.0m,
            NcnsFactor = 2.0m
        };

        var ex = Record.Exception(() => PayrollDeductions.Auto(
            basicSalary: 60000m, workingDays: 0,
            lateComing: 3, halfDays: 2, absentDays: 4, ncns: 1, cfg));
        Assert.Null(ex);

        var (late, half, absent, ncns) = PayrollDeductions.Auto(
            basicSalary: 60000m, workingDays: 0,
            lateComing: 3, halfDays: 2, absentDays: 4, ncns: 1, cfg);

        Assert.Equal(0m, half);
        Assert.Equal(0m, absent);
        Assert.Equal(0m, ncns);
        Assert.Equal(1500m, late); // 3 × 500 flat — unaffected by working days
    }

    // ── PayrollConfigAccess.EnsureCanConfigure ────────────────────────────────

    [Theory]
    [InlineData(DomainRoles.HR)]
    [InlineData(DomainRoles.Admin)]
    [InlineData(DomainRoles.SuperAdmin)]
    public void EnsureCanConfigure_allows_hr_admin_superadmin_on_any_centre(string role)
    {
        var user = new FakeCurrentUser { Roles = new[] { role }, CallCenterId = null };
        var anyCentre = Guid.NewGuid();

        // HR/Admin/SuperAdmin may configure a centre they aren't even pinned to.
        var ex = Record.Exception(() => PayrollConfigAccess.EnsureCanConfigure(user, anyCentre));
        Assert.Null(ex);
    }

    [Fact]
    public void EnsureCanConfigure_allows_callcenteradmin_for_their_own_centre()
    {
        var centre = Guid.NewGuid();
        var user = new FakeCurrentUser
        {
            Roles = new[] { DomainRoles.CallCenterAdmin },
            CallCenterId = centre
        };

        var ex = Record.Exception(() => PayrollConfigAccess.EnsureCanConfigure(user, centre));
        Assert.Null(ex);
    }

    [Fact]
    public void EnsureCanConfigure_forbids_callcenteradmin_for_a_different_centre()
    {
        var user = new FakeCurrentUser
        {
            Roles = new[] { DomainRoles.CallCenterAdmin },
            CallCenterId = Guid.NewGuid()
        };
        var someoneElsesCentre = Guid.NewGuid();

        Assert.Throws<ForbiddenAccessException>(
            () => PayrollConfigAccess.EnsureCanConfigure(user, someoneElsesCentre));
    }

    [Fact]
    public void EnsureCanConfigure_allows_an_agency_level_holder_any_centre()
    {
        // Access is gated by the payroll.config PERMISSION at the API; EnsureCanConfigure enforces
        // SCOPE only. An agency-level holder (no call-centre pin) may configure any centre in their
        // agency (the tenant query filter enforces the agency boundary).
        var user = new FakeCurrentUser { Roles = new[] { DomainRoles.HR }, CallCenterId = null };

        var ex = Record.Exception(() => PayrollConfigAccess.EnsureCanConfigure(user, Guid.NewGuid()));
        Assert.Null(ex);
    }
}
