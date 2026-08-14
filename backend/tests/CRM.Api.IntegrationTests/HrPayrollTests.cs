using System.Net.Http.Json;
using System.Text.Json;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// End-to-end coverage for HR payroll's per-call-centre deduction config and for the
/// appended <c>OfferStatus.Training</c> interview status — both exercised as an Admin
/// (who is treated as HR by <c>HrAccess</c>).
/// </summary>
public class HrPayrollTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public HrPayrollTests(CrmWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Payroll_config_round_trips_and_reports_saved()
    {
        var admin = await _factory.LoginAdminAsync();

        // Provision a call centre (admin holds CallCentersManage + belongs to a real agency).
        var ccName = $"Centre-{Guid.NewGuid():N}".Substring(0, 18);
        var created = await admin.PostJsonAsync("/api/admin/call-centers", new
        {
            name = ccName,
            code = (string?)null,
            adminName = "Centre Admin",
            adminEmail = $"cc-{Guid.NewGuid():N}@crm.local"
        });
        var callCenterId = created.GetProperty("id").GetGuid();

        // Before saving anything, GET returns the defaults and is NOT yet saved.
        var before = await admin.GetJsonAsync($"/api/hr/payroll/config/{callCenterId}");
        Assert.False(before.GetProperty("saved").GetBoolean());

        // PUT the centre's own deduction rules.
        var putResp = await admin.PutAsJsonAsync($"/api/hr/payroll/config/{callCenterId}", new
        {
            lateComingFine = 750m,
            halfDayFactor = 0.5m,
            absentDayFactor = 1.5m,
            ncnsFactor = 3.0m
        });
        putResp.EnsureSuccessStatusCode();
        var saved = JsonDocument.Parse(await putResp.Content.ReadAsStringAsync()).RootElement;

        Assert.True(saved.GetProperty("saved").GetBoolean());
        Assert.Equal(callCenterId, saved.GetProperty("callCenterId").GetGuid());
        Assert.Equal(750m, saved.GetProperty("lateComingFine").GetDecimal());
        Assert.Equal(0.5m, saved.GetProperty("halfDayFactor").GetDecimal());
        Assert.Equal(1.5m, saved.GetProperty("absentDayFactor").GetDecimal());
        Assert.Equal(3.0m, saved.GetProperty("ncnsFactor").GetDecimal());

        // GET now returns the persisted rules and flags them saved.
        var after = await admin.GetJsonAsync($"/api/hr/payroll/config/{callCenterId}");
        Assert.True(after.GetProperty("saved").GetBoolean());
        Assert.Equal(750m, after.GetProperty("lateComingFine").GetDecimal());
        Assert.Equal(0.5m, after.GetProperty("halfDayFactor").GetDecimal());
        Assert.Equal(1.5m, after.GetProperty("absentDayFactor").GetDecimal());
        Assert.Equal(3.0m, after.GetProperty("ncnsFactor").GetDecimal());
    }

    [Fact]
    public async Task Payroll_config_clamps_negative_rates_to_zero()
    {
        var admin = await _factory.LoginAdminAsync();

        var ccName = $"Centre-{Guid.NewGuid():N}".Substring(0, 18);
        var created = await admin.PostJsonAsync("/api/admin/call-centers", new
        {
            name = ccName,
            code = (string?)null,
            adminName = "Centre Admin",
            adminEmail = $"cc-{Guid.NewGuid():N}@crm.local"
        });
        var callCenterId = created.GetProperty("id").GetGuid();

        // A deduction rule can never be below zero — the handler floors negatives at 0.
        var putResp = await admin.PutAsJsonAsync($"/api/hr/payroll/config/{callCenterId}", new
        {
            lateComingFine = -100m,
            halfDayFactor = -1m,
            absentDayFactor = 1m,
            ncnsFactor = 2m
        });
        putResp.EnsureSuccessStatusCode();
        var saved = JsonDocument.Parse(await putResp.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(0m, saved.GetProperty("lateComingFine").GetDecimal());
        Assert.Equal(0m, saved.GetProperty("halfDayFactor").GetDecimal());
        Assert.Equal(1m, saved.GetProperty("absentDayFactor").GetDecimal());
        Assert.Equal(2m, saved.GetProperty("ncnsFactor").GetDecimal());
    }

    [Fact]
    public async Task Absent_deduction_tracks_the_daily_wage_after_a_re_save()
    {
        var admin = await _factory.LoginAdminAsync();

        // Agency-wide employee ⇒ the default deduction rules apply (absentDayFactor = 1.0).
        var emp = await admin.PostJsonAsync("/api/hr/employees", new
        {
            fullName = "Daily Wage Test",
            agentCode = $"DW-{Guid.NewGuid():N}".Substring(0, 12),
            designation = "Fronter",
            gender = "Male",
            maritalStatus = "Single",
            employmentStatus = "Permanent",
        });
        var employeeId = emp.GetProperty("id").GetGuid();

        const int year = 2031, month = 3;

        // Save: basic 30,000 over 30 working days ⇒ a day's pay is 1,000; 3 absents ⇒ 3,000 deducted.
        // The amount we send is deliberately wrong — the server must recompute it from the daily wage.
        await admin.PutAsJsonAsync($"/api/hr/payroll/{employeeId}", new
        {
            year, month,
            input = new { basicSalary = 30000m, workingDays = 30, absentDays = 3, absentDaysAmount = 999999m },
        });
        Assert.Equal(3000m, await AbsentAmount(admin, year, month, employeeId));

        // Double the basic ⇒ a day's pay is 2,000 ⇒ the SAME 3 absents now deduct 6,000, automatically.
        await admin.PutAsJsonAsync($"/api/hr/payroll/{employeeId}", new
        {
            year, month,
            input = new { basicSalary = 60000m, workingDays = 30, absentDays = 3 },
        });
        Assert.Equal(6000m, await AbsentAmount(admin, year, month, employeeId));
    }

    private static async Task<decimal> AbsentAmount(System.Net.Http.HttpClient client, int year, int month, Guid employeeId)
    {
        var list = await client.GetJsonAsync($"/api/hr/payroll?year={year}&month={month}");
        var row = list.EnumerateArray().First(r => r.GetProperty("employeeId").GetGuid() == employeeId);
        return row.GetProperty("absentDaysAmount").GetDecimal();
    }

    [Fact]
    public async Task Interview_created_with_Training_status_persists()
    {
        var admin = await _factory.LoginAdminAsync();

        var created = await admin.PostJsonAsync("/api/hr/interviews", new
        {
            candidateName = $"Trainee-{Guid.NewGuid():N}".Substring(0, 16),
            status = "Training"
        });
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("Training", created.GetProperty("status").GetString());

        // Re-fetch to prove the appended enum value round-tripped through storage.
        var fetched = await admin.GetJsonAsync($"/api/hr/interviews/{id}");
        Assert.Equal("Training", fetched.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Bulk_status_moves_every_selected_candidate()
    {
        var admin = await _factory.LoginAdminAsync();

        // Create three candidates in "Applied".
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var c = await admin.PostJsonAsync("/api/hr/interviews", new
            {
                candidateName = $"Bulk-{Guid.NewGuid():N}".Substring(0, 16),
                status = "Applied"
            });
            ids.Add(c.GetProperty("id").GetGuid());
        }

        // Bulk-move all three to Training in one call.
        var resp = await admin.PatchAsJsonAsync("/api/hr/interviews/bulk-status", new { ids, status = "Training" });
        resp.EnsureSuccessStatusCode();
        var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(3, body.GetProperty("updated").GetInt32());

        foreach (var id in ids)
        {
            var fetched = await admin.GetJsonAsync($"/api/hr/interviews/{id}");
            Assert.Equal("Training", fetched.GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task Interview_can_be_transitioned_to_Training()
    {
        var admin = await _factory.LoginAdminAsync();

        var name = $"Cand-{Guid.NewGuid():N}".Substring(0, 16);
        var created = await admin.PostJsonAsync("/api/hr/interviews", new
        {
            candidateName = name,
            status = "Interviewed"
        });
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("Interviewed", created.GetProperty("status").GetString());

        // Move the candidate into onboarding/training and confirm it sticks.
        var updateResp = await admin.PutAsJsonAsync($"/api/hr/interviews/{id}", new
        {
            candidateName = name,
            status = "Training"
        });
        updateResp.EnsureSuccessStatusCode();

        var fetched = await admin.GetJsonAsync($"/api/hr/interviews/{id}");
        Assert.Equal("Training", fetched.GetProperty("status").GetString());
    }
}
