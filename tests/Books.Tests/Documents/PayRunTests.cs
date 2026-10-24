using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Ledger;

namespace Books.Tests.Documents;

/// <summary>Payroll results (owner decision): one summary journal per pay run; employee lines feed form TOS 01.</summary>
public sealed class PayRunTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_pay_run_posts_one_summary_journal_and_fills_tos_01()
    {
        var (s, accounts) = await SetUpAsync();

        var run = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/pay-runs", Body(accounts))).DataAsync(HttpStatusCode.Created);

        Assert.Equal(("posted", 3, "3500.00", "2920.00"), (Get(run, "status"), run.GetProperty("totals").GetProperty("employees").GetInt32(),
            Get(run.GetProperty("totals"), "gross"), Get(run.GetProperty("totals"), "net")));
        var journal = await (await s.Books.Admin.GetAsync($"{s.Books.JournalsUrl}/{run.GetProperty("journal_id").GetGuid()}", Ct)).DataAsync();
        var lines = journal.GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("account_id").GetGuid(), l => (Get(l, "debit"), Get(l, "credit")));
        Assert.Equal(6, lines.Count);
        Assert.Equal(("3560.00", "39.00"), (lines[accounts["salary_expense"]].Item1, lines[accounts["nssf_expense"]].Item1));
        Assert.Equal(("2920.00", "510.00", "69.00", "100.00"),
            (lines[accounts["salaries_payable"]].Item2, lines[accounts["salary_tax_payable"]].Item2, lines[accounts["nssf_payable"]].Item2, lines[accounts["other_deductions"]].Item2));

        var october = await TaxReturnTests.GetAsync(s, "tax-returns/2026-10");
        var boxes = TaxReturnTests.Boxes(october);
        Assert.Equal(("1500.00", "50.00", "2000.00", "400.00", "300.00", "60.00"),
            (boxes["tos_resident_gross"].Usd, boxes["tos_resident_tax"].Usd, boxes["tos_nonresident_gross"].Usd, boxes["tos_nonresident_tax"].Usd,
                boxes["tos_fringe_benefits"].Usd, boxes["tos_fringe_benefit_tax"].Usd));
        Assert.Equal(("510.00", "2091000"), boxes["tos_total"]);                       // at the rate of the 15th (4100)
    }

    [Fact]
    public async Task Results_that_do_not_add_up_are_refused_and_the_next_run_reuses_the_accounts()
    {
        var (s, accounts) = await SetUpAsync();
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/pay-runs", Body(accounts))).DataAsync(HttpStatusCode.Created);

        var wrong = await s.Books.Admin.PostAsync($"{s.LedgerUrl}/pay-runs", new
        {
            period = "2026-11", date = "2026-11-30",
            lines = new[] { new { employee_code = "E1", name = "Sok Dara", resident = true, gross = "1000.00", salary_tax = "50.00", net = "900.00" } },
        });
        var november = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/pay-runs", new
        {
            period = "2026-11", date = "2026-11-30", post = false,
            lines = new[] { new { employee_code = "E1", name = "Sok Dara", resident = true, gross = "1000.00", salary_tax = "50.00", net = "950.00" } },
        })).DataAsync(HttpStatusCode.Created);

        await wrong.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        Assert.Equal(("draft", accounts["salaries_payable"]), (Get(november, "status"), november.GetProperty("accounts").GetProperty("salaries_payable").GetGuid()));
    }

    [Fact]
    public async Task Khr_results_keep_their_amounts_on_tos_01_and_post_converted_totals_to_template_accounts()
    {
        var (s, accounts) = await SetUpAsync();

        var run = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/pay-runs", new
        {
            period = "2026-10", date = "2026-10-31", currency = "KHR", rate = "4000",
            lines = new[]
            {
                new
                {
                    employee_code = "E1", name = "Sok Dara", resident = true, gross = "4000000", salary_tax = "100000",
                    nssf_employee = "80000", nssf_employer = "104000", net = "3820000",
                },
            },
        })).DataAsync(HttpStatusCode.Created);

        var journal = await (await s.Books.Admin.GetAsync($"{s.Books.JournalsUrl}/{run.GetProperty("journal_id").GetGuid()}", Ct)).DataAsync();
        var lines = journal.GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("account_id").GetGuid(), l => (Get(l, "debit"), Get(l, "credit")));
        Assert.Equal(("KHR", "4000", "4000000"), (Get(run, "currency"), Get(run, "rate"), Get(run.GetProperty("totals"), "gross")));
        Assert.Equal(("1000.00", "26.00"), (lines[accounts["salary_expense"]].Item1, lines[accounts["nssf_expense"]].Item1));
        Assert.Equal(("955.00", "25.00", "46.00"), (lines[accounts["salaries_payable"]].Item2, lines[accounts["salary_tax_payable"]].Item2, lines[accounts["nssf_payable"]].Item2));
        var boxes = TaxReturnTests.Boxes(await TaxReturnTests.GetAsync(s, "tax-returns/2026-10"));
        Assert.Equal(("25.00", "100000"), boxes["tos_resident_tax"]);                  // exactly what the payroll system reported in KHR
    }

    private async Task<(SalesInvoiceTests.Setup, Dictionary<string, Guid>)> SetUpAsync()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        async Task<Guid> Account(string code, string name, string type) => (await AccountTests.CreateAsync(s.Books.Admin, s.Books.OrgId, s.Books.LedgerId, code, name, type)).Id();
        // The Cambodia template sets up the payroll accounts; a staff-loan account takes other deductions.
        var accounts = new Dictionary<string, Guid>
        {
            ["salary_expense"] = s.Account("6100"),
            ["nssf_expense"] = s.Account("6110"),
            ["salaries_payable"] = s.Account("2300"),
            ["salary_tax_payable"] = s.Account("2310"),
            ["nssf_payable"] = s.Account("2320"),
            ["other_deductions"] = await Account("2330", "Staff loans", "liabilities"),
        };
        return (s, accounts);
    }

    /// <summary>Two residents and one non-resident with a fringe benefit and a loan repayment.</summary>
    private static object Body(Dictionary<string, Guid> a) => new
    {
        period = "2026-10", date = "2026-10-31", reference = "PAY-2026-10",
        accounts = new
        {
            salary_expense = a["salary_expense"], nssf_expense = a["nssf_expense"], salaries_payable = a["salaries_payable"],
            salary_tax_payable = a["salary_tax_payable"], nssf_payable = a["nssf_payable"], other_deductions = a["other_deductions"],
        },
        lines = new object[]
        {
            new { employee_code = "E1", name = "Sok Dara", resident = true, gross = "1000.00", salary_tax = "50.00", nssf_employee = "20.00", nssf_employer = "26.00", net = "930.00" },
            new { employee_code = "E2", name = "Chan Sophea", resident = true, gross = "500.00", nssf_employee = "10.00", nssf_employer = "13.00", net = "490.00" },
            new
            {
                employee_code = "E3", name = "Expat Manager", resident = false, gross = "2000.00", fringe_benefits = "300.00", salary_tax = "400.00",
                fringe_benefit_tax = "60.00", other_deductions = "100.00", net = "1500.00",
            },
        },
    };

    private static string Get(JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
