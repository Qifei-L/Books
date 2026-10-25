using System.Globalization;
using Books.Api.Auth;
using Books.Application.Common;
using Books.Application.Journals;
using Books.Domain.Journals;
using Books.Domain.Ledgers;

namespace Books.Api.Http;

public static class JournalEndpoints
{
    /// <param name="Debit">Decimal string such as "100.00"; a line has a debit or a credit, not both.</param>
    /// <param name="CurrencyAmount">Lines on a bank account in another currency: the amount in that currency, on the same side.</param>
    public sealed record LineRequest(Guid AccountId, string? Debit, string? Credit, string? Description, Guid? PartyId = null, Guid? ItemId = null, string? Quantity = null,
        string? CurrencyAmount = null, Guid? ClassId = null, Guid? LocationId = null, Guid? TaxCodeId = null, string? TaxDirection = null);

    /// <param name="Post">true = Save (post now); false = Save Draft.</param>
    public sealed record JournalRequest(DateOnly AccountingDate, IReadOnlyList<LineRequest> Lines, bool Post, string? Reference, string? Description);

    public static void MapJournalEndpoints(this RouteGroupBuilder api)
    {
        var journals = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}/journals");

        journals.MapPost("", async (Guid orgId, Guid ledgerId, JournalRequest body, PrincipalAccessor principals, JournalService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.CreateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, ToDraft(body), body.Post, ct)));

        journals.MapPut("/{journalId:guid}", async (Guid orgId, Guid ledgerId, Guid journalId, JournalRequest body, PrincipalAccessor principals, JournalService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.UpdateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, journalId, ToDraft(body), body.Post, ct)));

        journals.MapDelete("/{journalId:guid}", async (Guid orgId, Guid ledgerId, Guid journalId, PrincipalAccessor principals, JournalService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.DeleteAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, ledgerId, journalId, ct)));

        journals.MapGet("", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, JournalService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        journals.MapGet("/{journalId:guid}", async (Guid orgId, Guid ledgerId, Guid journalId, PrincipalAccessor principals, JournalService service, HttpContext http, CancellationToken ct) =>
        {
            var journal = await service.GetAsync(await principals.GetAsync(ct), orgId, ledgerId, journalId, ct);
            return Envelope.Ok(http, journal, journal.Version);
        });
    }

    /// <summary>Turns debit/credit strings into one signed amount per line. Shape errors are 400; accounting checks are the validator's.</summary>
    internal static JournalDraft ToDraft(JournalRequest body)
    {
        var lines = (body.Lines ?? throw BooksProblem.Invalid("lines is required.")).Select((line, i) =>
        {
            var debit = Parse(line.Debit, i, "debit");
            var credit = Parse(line.Credit, i, "credit");
            if (debit != 0 && credit != 0)
            {
                throw BooksProblem.Invalid($"Line {i + 1} has both a debit and a credit.");
            }
            decimal? quantity = string.IsNullOrWhiteSpace(line.Quantity)
                ? null
                : decimal.TryParse(line.Quantity, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var q)
                    ? q
                    : throw BooksProblem.Invalid($"Line {i + 1} quantity must be a decimal string.");
            decimal? currencyAmount = string.IsNullOrWhiteSpace(line.CurrencyAmount) ? null : Math.Sign(debit - credit) * Parse(line.CurrencyAmount, i, "currency_amount");
            var direction = string.IsNullOrWhiteSpace(line.TaxDirection) ? (TaxDirection?)null : line.TaxDirection.Trim().ToLowerInvariant() switch
            {
                "sale" => TaxDirection.Sale,
                "sale_adjustment" => TaxDirection.SaleAdjustment,
                "purchase" => TaxDirection.Purchase,
                "purchase_adjustment" => TaxDirection.PurchaseAdjustment,
                _ => throw BooksProblem.Invalid($"Line {i + 1} tax_direction must be sale, sale_adjustment, purchase or purchase_adjustment."),
            };
            return new DraftLine(line.AccountId, debit - credit, line.Description, PartyId: line.PartyId, ItemId: line.ItemId, Quantity: quantity, CurrencyAmount: currencyAmount,
                ClassId: line.ClassId, LocationId: line.LocationId, TaxCodeId: line.TaxCodeId, TaxDirection: direction);
        }).ToList();
        return new JournalDraft(body.AccountingDate, lines, body.Reference, body.Description);
    }

    private static decimal Parse(string? value, int index, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }
        return decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : throw BooksProblem.Invalid($"Line {index + 1} {field} must be a non-negative decimal string such as \"100.00\".");
    }
}
