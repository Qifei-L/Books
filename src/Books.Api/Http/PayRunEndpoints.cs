using System.Globalization;
using Books.Api.Auth;
using Books.Application.Common;
using Books.Application.Documents;
using Books.Domain.Documents;

namespace Books.Api.Http;

public static class PayRunEndpoints
{
    /// <summary>One employee's results as decimal strings; gross must equal net + salary tax + NSSF (employee) + other deductions.</summary>
    public sealed record LineRequest(
        string EmployeeCode, string Name, bool Resident, string Gross, string? FringeBenefits, string? SalaryTax, string? FringeBenefitTax,
        string? NssfEmployee, string? NssfEmployer, string? OtherDeductions, string Net);

    public sealed record AccountsRequest(Guid SalaryExpense, Guid NssfExpense, Guid SalariesPayable, Guid SalaryTaxPayable, Guid NssfPayable, Guid? OtherDeductions);

    /// <param name="Period">The month the pay is for, yyyy-MM.</param>
    public sealed record PayRunRequest(string Period, DateOnly Date, string? Reference, string? Description, AccountsRequest? Accounts, IReadOnlyList<LineRequest> Lines, bool Post = true);

    public static void MapPayRunEndpoints(this RouteGroupBuilder api)
    {
        var runs = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}/pay-runs");

        runs.MapGet("", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, PayRunService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        runs.MapPost("", async (Guid orgId, Guid ledgerId, PayRunRequest body, PrincipalAccessor principals, PayRunService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.CreateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, Input(body), body.Post, ct)));

        runs.MapPut("/{payRunId:guid}", async (Guid orgId, Guid ledgerId, Guid payRunId, PayRunRequest body, PrincipalAccessor principals, PayRunService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.UpdateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, payRunId, Input(body), body.Post, ct)));

        runs.MapDelete("/{payRunId:guid}", async (Guid orgId, Guid ledgerId, Guid payRunId, PrincipalAccessor principals, PayRunService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.DeleteAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, ledgerId, payRunId, ct)));
    }

    internal static PayRunInput Input(PayRunRequest body)
    {
        if (!DateOnly.TryParseExact(body.Period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var period))
        {
            throw BooksProblem.Invalid("period must be a month, yyyy-MM.");
        }
        decimal N(string? value, int i, string field) => PurchaseEndpoints.Number(value, i, field, signed: true) ?? 0;
        return new PayRunInput(period, body.Date, body.Reference, body.Description,
            body.Accounts is { } a ? new PayRunAccounts(a.SalaryExpense, a.NssfExpense, a.SalariesPayable, a.SalaryTaxPayable, a.NssfPayable, a.OtherDeductions) : null,
            (body.Lines ?? throw BooksProblem.Invalid("lines is required.")).Select((l, i) => new PayRunLineInput(l.EmployeeCode, l.Name, l.Resident,
                N(l.Gross, i, "gross"), N(l.FringeBenefits, i, "fringe_benefits"), N(l.SalaryTax, i, "salary_tax"), N(l.FringeBenefitTax, i, "fringe_benefit_tax"),
                N(l.NssfEmployee, i, "nssf_employee"), N(l.NssfEmployer, i, "nssf_employer"), N(l.OtherDeductions, i, "other_deductions"), N(l.Net, i, "net"))).ToList());
    }
}
