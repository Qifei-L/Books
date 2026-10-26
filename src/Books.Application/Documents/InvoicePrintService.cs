using System.Globalization;
using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Documents;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Documents;

public sealed record PrintParty(string Name, string? TaxId, string? Address, string? Phone, string? Email, string? NameKm = null, string? AddressKm = null);

/// <param name="Tax">The line's VAT code name, for example "VAT 10% (sales)"; null when the line has none.</param>
public sealed record PrintLine(int No, string? Description, string? Quantity, string? UnitPrice, string Amount, string? Tax);

public sealed record PrintAmount(string Label, string Amount);

/// <summary>The invoice total in the tax currency (KHR) with the rate it was converted at.</summary>
public sealed record PrintTaxCurrency(string Currency, string Rate, DateOnly RateDate, string Gross, string Tax);

/// <param name="Style">tax, zero_non_vat or commercial (owner decision M9: three kinds of invoice).</param>
/// <param name="StateCharge">Label the VAT as state charge, for suppliers paying VAT on behalf of the state.</param>
public sealed record InvoicePrint(
    string Style, bool StateCharge, bool CreditNote, bool Draft, string Number, string? OriginalNumber, DateOnly Date, DateOnly DueDate,
    string? Reference, string? Description, string Currency, InvoiceSettingsDto SellerSettings, string SellerName, PrintParty Buyer,
    IReadOnlyList<PrintLine> Lines, string NetTotal, IReadOnlyList<PrintAmount> OtherTaxes, IReadOnlyList<PrintAmount> StateCharges,
    string VatTotal, string GrossTotal, PrintTaxCurrency? TaxCurrency);

/// <summary>
/// Gathers what a printed sales invoice or credit note shows (M9). The PDF itself is drawn by the API from this.
/// </summary>
public sealed class InvoicePrintService(IBooksDb db, ReadRunner reads, TaxCurrencyService taxCurrency)
{
    public static readonly string[] Styles = ["tax", "zero_non_vat", "commercial"];

    public Task<InvoicePrint> GetAsync(Principal principal, Guid orgId, Guid ledgerId, Guid invoiceId, bool creditNote, string? style, bool stateCharge,
        CancellationToken ct)
    {
        if (style is not null && !Styles.Contains(style))
        {
            throw BooksProblem.Invalid("style must be tax, zero_non_vat or commercial.");
        }
        return reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var invoice = await db.SalesInvoices.AsNoTracking().Include(i => i.Lines)
                .SingleOrDefaultAsync(i => i.LedgerId == ledgerId && i.Id == invoiceId && i.IsCreditNote == creditNote, ct)
                ?? throw BooksProblem.NotFound();
            if (invoice.Status == DocumentStatus.Deleted)
            {
                throw BooksProblem.Conflict("DOCUMENT_DELETED", "This invoice was deleted.");
            }
            var ledger = await db.Ledgers.AsNoTracking().SingleAsync(l => l.Id == ledgerId, ct);
            var companyName = await db.Companies.Where(c => c.Id == ledger.CompanyId).Select(c => c.Name).SingleAsync(ct);
            var settings = await InvoiceSettingsService.LoadAsync(db, ledgerId, orgId, ct);
            var customer = await db.Parties.AsNoTracking().SingleAsync(p => p.Id == invoice.CustomerId, ct);
            var customerKm = await db.PartyLocalizations.AsNoTracking()
                .SingleOrDefaultAsync(l => l.PartyId == customer.Id && l.Locale == PartyLocalization.Khmer, ct);
            var codeIds = invoice.Lines.SelectMany(l => new[] { l.TaxCodeId, l.OtherTaxCodeId, l.StateChargeCodeId }).OfType<Guid>().Distinct().ToList();
            var codes = await db.TaxCodes.AsNoTracking().Where(t => codeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
            string? original = null;
            if (invoice.OriginalInvoiceId is { } originalId)
            {
                var no = await db.SalesInvoices.Where(i => i.Id == originalId).Select(i => (long?)i.InvoiceNo).SingleOrDefaultAsync(ct);
                original = no is { } n ? settings.Number(n, creditNote: false) : null;
            }

            var currency = invoice.Currency == "" ? ledger.BaseCurrency : invoice.Currency;
            var minor = Currencies.Require(currency).MinorUnits;
            string F(decimal v) => v.ToString("N" + minor, CultureInfo.InvariantCulture);
            static string? Q(decimal? v) => v?.ToString("#,0.####", CultureInfo.InvariantCulture);
            string? P(decimal? v) => v?.ToString("#,0.00##", CultureInfo.InvariantCulture);
            var lines = invoice.Lines.OrderBy(l => l.LineNo).Select((l, i) => new PrintLine(i + 1, l.Description, l.Unit is null ? Q(l.Quantity) : $"{Q(l.Quantity)} {l.Unit}", P(l.UnitPrice), F(l.NetAmount),
                l.TaxCodeId is { } t && codes.TryGetValue(t, out var code) ? code.Name : null)).ToList();
            var otherTaxes = invoice.Lines.Where(l => l.OtherTaxCodeId is not null && l.OtherTaxAmount != 0)
                .GroupBy(l => codes.TryGetValue(l.OtherTaxCodeId!.Value, out var c) ? c.Name : "Other tax")
                .Select(g => new PrintAmount(g.Key, F(g.Sum(l => l.OtherTaxAmount)))).ToList();
            var stateCharges = invoice.Lines.Where(l => l.StateChargeCodeId is not null && l.StateChargeAmount != 0)
                .GroupBy(l => codes.TryGetValue(l.StateChargeCodeId!.Value, out var c) ? c.Name : "State charge")
                .Select(g => new PrintAmount(g.Key, F(g.Sum(l => l.StateChargeAmount)))).ToList();

            var kinds = invoice.Lines.Select(l => l.TaxCodeId is { } t && codes.TryGetValue(t, out var c) ? c : null).OfType<TaxCode>().ToList();
            var chosen = style
                ?? (kinds.Any(c => c.Kind == TaxKind.VatOutput && c.Rate > 0) ? "tax"
                    : kinds.Any(c => c.Kind is TaxKind.VatZeroRated or TaxKind.NonVat or TaxKind.VatOutput) ? "zero_non_vat"
                    : "commercial");

            var khr = (await taxCurrency.LoadAsync(ledgerId, [invoice.Id], ct)).GetValueOrDefault(invoice.Id);
            return new InvoicePrint(
                chosen, stateCharge, creditNote, invoice.Status != DocumentStatus.Posted, settings.Number(invoice.InvoiceNo, creditNote), original,
                invoice.Date, invoice.DueDate, invoice.Reference, invoice.Description, currency, InvoiceSettingsService.ToDto(settings),
                settings.NameEnglish ?? companyName,
                new PrintParty(customer.Name, customer.TaxId, customer.Address, customer.Phone, customer.Email, customerKm?.Name, customerKm?.Address),
                lines, F(invoice.NetTotal), otherTaxes, stateCharges, F(invoice.TaxTotal), F(invoice.GrossTotal),
                khr is null ? null : new PrintTaxCurrency(khr.Currency, khr.Rate, khr.RateDate, Group(khr.GrossTotal), Group(khr.TaxTotal)));
        }, ct);
    }

    private static string Group(string amount)
    {
        var value = decimal.Parse(amount, CultureInfo.InvariantCulture);
        var decimals = amount.Contains('.') ? amount.Length - amount.IndexOf('.') - 1 : 0;
        return value.ToString("N" + decimals, CultureInfo.InvariantCulture);
    }
}
