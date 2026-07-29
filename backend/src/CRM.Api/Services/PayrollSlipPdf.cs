using System.Globalization;
using CRM.Application.Hr;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CRM.Api.Services;

/// <summary>Renders a one-page PDF salary slip from a computed <see cref="PayrollRowDto"/>.</summary>
public static class PayrollSlipPdf
{
    private static string Money(decimal v) => v.ToString("C0", CultureInfo.GetCultureInfo("en-US"));

    public static byte[] Build(PayrollRowDto p, string companyName)
    {
        var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(p.Month);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(36);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(t => t.FontSize(10).FontColor(Colors.Grey.Darken3));

                page.Header().Column(col =>
                {
                    col.Item().Text(companyName).FontSize(16).SemiBold().FontColor(Colors.Green.Darken2);
                    col.Item().Text($"Salary Slip — {monthName} {p.Year}").FontSize(11).FontColor(Colors.Grey.Medium);
                    col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().PaddingVertical(12).Column(col =>
                {
                    col.Spacing(12);

                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text(t => { t.Span("Employee: ").SemiBold(); t.Span(p.FullName); });
                        r.RelativeItem().AlignRight().Text(t => { t.Span("Agent ID: ").SemiBold(); t.Span(p.AgentCode); });
                    });

                    // Attendance summary (the "number" against each line)
                    col.Item().Background(Colors.Grey.Lighten4).Padding(8).Column(a =>
                    {
                        a.Item().Text("Attendance").SemiBold();
                        a.Item().PaddingTop(2).Text(
                            $"Working {p.WorkingDays}   ·   Present {p.PresentDays}   ·   Late {p.LateComing}   ·   " +
                            $"Half {p.HalfDays}   ·   Leave {p.LeavesApproved}   ·   Absent {p.AbsentDays}   ·   NCNS {p.Ncns}")
                            .FontColor(Colors.Grey.Darken1);
                    });

                    col.Item().Row(r =>
                    {
                        r.RelativeItem().PaddingRight(8).Element(c => AmountTable(c, "Earnings", new (string, decimal)[]
                        {
                            ("Basic salary", p.BasicSalary),
                            ("Punctuality", p.Punctuality),
                            ("Daily bonus", p.DailyBonus),
                            ("Monthly commissions", p.MonthlyCommissions),
                            ("Transport allowance", p.TransportAllowance),
                            ("Special allowance", p.SpecialAllowance),
                        }, p.GrossEarnings, Colors.Green.Darken2));

                        r.RelativeItem().PaddingLeft(8).Element(c => AmountTable(c, "Deductions", new (string, decimal)[]
                        {
                            ("Advance salary", p.AdvanceSalary),
                            ("Docks", p.Docks),
                        }, p.Deductions, Colors.Red.Darken1));
                    });

                    col.Item().PaddingTop(6).Background(Colors.Green.Lighten5).Padding(10).Row(r =>
                    {
                        r.RelativeItem().Text("NET PAY").FontSize(12).SemiBold();
                        r.RelativeItem().AlignRight().Text(Money(p.NetPay)).FontSize(14).Bold().FontColor(Colors.Green.Darken2);
                    });

                    if (!string.IsNullOrWhiteSpace(p.Notes))
                        col.Item().PaddingTop(4).Text(t => { t.Span("Notes: ").SemiBold(); t.Span(p.Notes); });
                });

                page.Footer().AlignRight().Text($"Generated {DateTime.UtcNow:yyyy-MM-dd}")
                    .FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });

        return document.GeneratePdf();
    }

    private static void AmountTable(IContainer container, string title, (string Label, decimal Amount)[] rows,
        decimal total, string totalColor)
    {
        container.Border(1).BorderColor(Colors.Grey.Lighten2).Column(col =>
        {
            col.Item().Background(Colors.Grey.Lighten4).Padding(6).Text(title).SemiBold();
            foreach (var (label, amount) in rows)
            {
                col.Item().Padding(6).Row(r =>
                {
                    r.RelativeItem().Text(label).FontColor(Colors.Grey.Darken2);
                    r.ConstantItem(90).AlignRight().Text(Money(amount));
                });
            }
            col.Item().BorderTop(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Row(r =>
            {
                r.RelativeItem().Text("Total").SemiBold();
                r.ConstantItem(90).AlignRight().Text(Money(total)).SemiBold().FontColor(totalColor);
            });
        });
    }
}
