using System.Globalization;
using Books.Api.Auth;
using Books.Application.Documents;
using Books.Application.Common;
using Books.Application.Ledgers;

namespace Books.Api.Http;

public static class BankEndpoints
{
    public sealed record ClearRequest(DateOnly ClearedOn, IReadOnlyList<Guid>? JournalIds);

    public sealed record UnclearRequest(IReadOnlyList<Guid>? JournalIds);

    public sealed record StatementLineRequest(DateOnly Date, string? Description, string? Reference, string Amount, string? ExternalId);

    public sealed record StatementRequest(string OpeningBalance, string ClosingBalance, IReadOnlyList<StatementLineRequest> Lines);

    internal static (decimal Opening, decimal Closing, List<StatementLineInput> Lines) Statement(StatementRequest body) =>
        (PurchaseEndpoints.Number(body.OpeningBalance, 0, "opening_balance", signed: true) ?? throw BooksProblem.Invalid("opening_balance is required."),
         PurchaseEndpoints.Number(body.ClosingBalance, 0, "closing_balance", signed: true) ?? throw BooksProblem.Invalid("closing_balance is required."),
         (body.Lines ?? throw BooksProblem.Invalid("lines is required.")).Select((l, i) => new StatementLineInput(l.Date, l.Description, l.Reference,
             PurchaseEndpoints.Number(l.Amount, i, "amount", signed: true) ?? throw BooksProblem.Invalid($"Line {i + 1}: amount is required."), l.ExternalId)).ToList());

    public sealed record BankAccountRequest(Guid AccountId, string BankName, string? AccountNumber);

    public sealed record BankAccountUpdate(string BankName, string? AccountNumber);

    /// <param name="Rate">Units of to_currency per one from_currency, as a decimal string, for example "4100".</param>
    public sealed record RateRequest(DateOnly Date, string FromCurrency, string ToCurrency, string Rate, string? Source);

    public static void MapBankEndpoints(this RouteGroupBuilder api)
    {
        var reconciliation = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}/bank-accounts/{accountId:guid}");

        // Manager.io style: the statement balance against the cleared balance, with the postings still pending.
        reconciliation.MapGet("/reconciliation", async (Guid orgId, Guid ledgerId, Guid accountId, DateOnly asOf, string? statementBalance,
            PrincipalAccessor principals, BankReconciliationService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ReconciliationAsync(await principals.GetAsync(ct), orgId, ledgerId, accountId, asOf,
                PurchaseEndpoints.Number(statementBalance, 0, "statementBalance", signed: true), ct)));

        reconciliation.MapPost("/clearings", async (Guid orgId, Guid ledgerId, Guid accountId, ClearRequest body, PrincipalAccessor principals, BankReconciliationService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.ClearAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, accountId, body.ClearedOn, body.JournalIds ?? [], ct)));

        reconciliation.MapGet("/statements", async (Guid orgId, Guid ledgerId, Guid accountId, PrincipalAccessor principals, BankStatementService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, accountId, ct)));

        reconciliation.MapPost("/statements", async (Guid orgId, Guid ledgerId, Guid accountId, StatementRequest body, PrincipalAccessor principals, BankStatementService service, HttpContext http, CancellationToken ct) =>
        {
            var (opening, closing, lines) = Statement(body);
            return WriteHttp.Result(http, await service.ImportAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, accountId, opening, closing, lines, ct));
        });

        api.MapPost("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}/bank-statements/{statementId:guid}/post-drafts",
            async (Guid orgId, Guid ledgerId, Guid statementId, PrincipalAccessor principals, BankStatementService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.PostDraftsAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, ledgerId, statementId, ct)));

        reconciliation.MapPost("/clearings/remove", async (Guid orgId, Guid ledgerId, Guid accountId, UnclearRequest body, PrincipalAccessor principals, BankReconciliationService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.UnclearAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, accountId, body.JournalIds ?? [], ct)));

        var banks = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}/bank-accounts");

        banks.MapGet("", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, BankService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        banks.MapPost("", async (Guid orgId, Guid ledgerId, BankAccountRequest body, PrincipalAccessor principals, BankService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.CreateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, body.AccountId, body.BankName, body.AccountNumber, ct)));

        banks.MapPut("/{accountId:guid}", async (Guid orgId, Guid ledgerId, Guid accountId, BankAccountUpdate body, PrincipalAccessor principals, BankService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.UpdateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, accountId, body.BankName, body.AccountNumber, ct)));

        var rates = api.MapGroup("/organizations/{orgId:guid}/exchange-rates");

        rates.MapGet("", async (Guid orgId, DateOnly from, DateOnly to, PrincipalAccessor principals, BankService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.RatesAsync(await principals.GetAsync(ct), orgId, from, to, ct)));

        rates.MapPut("", async (Guid orgId, RateRequest body, PrincipalAccessor principals, BankService service, HttpContext http, CancellationToken ct) =>
        {
            var rate = decimal.TryParse(body.Rate, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var r)
                ? r
                : throw BooksProblem.Invalid("rate must be a positive decimal string such as \"4100\".");
            return WriteHttp.Result(http, await service.SetRateAsync(
                await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, body.Date, body.FromCurrency, body.ToCurrency, rate, body.Source ?? "Manual", ct));
        });
    }
}
