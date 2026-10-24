using System.Globalization;
using Books.Application.Common;
using Books.Domain.Documents;
using Books.Domain.Journals;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Documents;

/// <summary>A rate entered on the document instead of the rate table's; the reason is required.</summary>
public sealed record TaxCurrencyInput(decimal? Rate, string? Reason);

public sealed record TaxCurrencyDto(string Currency, string Rate, DateOnly RateDate, string RateSource, string? Reason, string GrossTotal, string TaxTotal);

/// <summary>
/// Fills the tax currency slot of a document being saved, inside the document's transaction. The rate is the
/// latest one on or before the slot's rate date (no NBC rate is published on weekends and holidays).
/// </summary>
public sealed class TaxCurrencyService(IBooksDb db)
{
    /// <returns>An issue that keeps the document a draft when no rate is available, otherwise null.</returns>
    public async Task<JournalIssue?> ApplyAsync(
        Ledger ledger, string documentKind, Guid documentId, DateOnly date, decimal gross, decimal tax, TaxCurrencyInput? input, CancellationToken ct,
        string? documentCurrency = null, decimal? documentRate = null)
    {
        var slot = TaxPacks.TaxCurrency(ledger.TaxPack);
        var existing = await db.DocumentTaxCurrencies.SingleOrDefaultAsync(t => t.DocumentId == documentId, ct);
        if (slot is null || !slot.AppliesTo(documentKind, ledger.BaseCurrency))
        {
            if (input?.Rate is not null)
            {
                throw BooksProblem.Invalid("This ledger's tax pack shows no tax currency; omit tax_currency_rate.");
            }
            Remove(existing);
            return null;
        }

        var rateDate = slot.RateDate(date);
        decimal rate;
        string source;
        if (documentCurrency == slot.Currency && documentRate is { } own)
        {
            // Already in the tax currency: its own amounts are the tax amounts, at the document's rate.
            existing ??= Add(documentId, ledger.Id, documentKind);
            existing.Set(slot.Currency, own, date, "Document", null, gross / own, tax / own);
            return null;
        }
        if (input?.Rate is { } entered)
        {
            if (string.IsNullOrWhiteSpace(input.Reason))
            {
                throw BooksProblem.Invalid("tax_currency_reason is required when tax_currency_rate is entered.");
            }
            (rate, source) = (entered, "Override");
        }
        else
        {
            var table = await db.ExchangeRates.AsNoTracking()
                .Where(r => r.OrgId == ledger.OrgId && r.FromCurrency == ledger.BaseCurrency && r.ToCurrency == slot.Currency && r.Date <= rateDate)
                .OrderByDescending(r => r.Date).FirstOrDefaultAsync(ct);
            if (table is null)
            {
                Remove(existing);
                return new JournalIssue("TAX_RATE_MISSING",
                    $"No {ledger.BaseCurrency}→{slot.Currency} rate on or before {rateDate:yyyy-MM-dd}; add the rate, or enter tax_currency_rate with a reason.");
            }
            (rate, source, rateDate) = (table.Rate, table.Source, table.Date);
        }

        if (existing is null)
        {
            existing = new DocumentTaxCurrency(documentId, ledger.Id, documentKind);
            db.DocumentTaxCurrencies.Add(existing);
        }
        existing.Set(slot.Currency, rate, rateDate, source, input?.Rate is null ? null : input.Reason, gross, tax);
        return null;
    }

    public async Task<Dictionary<Guid, TaxCurrencyDto>> LoadAsync(Guid ledgerId, IReadOnlyCollection<Guid> documentIds, CancellationToken ct) =>
        (await db.DocumentTaxCurrencies.AsNoTracking().Where(t => t.LedgerId == ledgerId && documentIds.Contains(t.DocumentId)).ToListAsync(ct))
        .ToDictionary(t => t.DocumentId, ToDto);

    public static TaxCurrencyDto ToDto(DocumentTaxCurrency t)
    {
        var minor = Currencies.Require(t.Currency).MinorUnits;
        return new TaxCurrencyDto(t.Currency, t.Rate.ToString("0.##########", CultureInfo.InvariantCulture), t.RateDate, t.RateSource, t.Reason,
            t.GrossTotal.ToString("F" + minor, CultureInfo.InvariantCulture), t.TaxTotal.ToString("F" + minor, CultureInfo.InvariantCulture));
    }

    private DocumentTaxCurrency Add(Guid documentId, Guid ledgerId, string documentKind)
    {
        var created = new DocumentTaxCurrency(documentId, ledgerId, documentKind);
        db.DocumentTaxCurrencies.Add(created);
        return created;
    }

    private void Remove(DocumentTaxCurrency? existing)
    {
        if (existing is not null)
        {
            db.DocumentTaxCurrencies.Remove(existing);
        }
    }
}
