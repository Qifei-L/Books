using Books.Application.Common;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Documents;

/// <summary>Exchange rates for documents in another currency (M12).</summary>
internal static class Rates
{
    /// <summary>
    /// The rate for a document: the one given, else the latest rate on or before its date from the rate table
    /// (units of the document's currency per one unit of the ledger's); null in the ledger's currency.
    /// </summary>
    public static async Task<decimal?> ForDocumentAsync(IBooksDb db, Ledger ledger, string? currency, decimal? given, DateOnly date, CancellationToken ct)
    {
        if (currency is null || Currencies.Require(currency).Code == ledger.BaseCurrency)
        {
            return null;
        }
        if (given is { } rate)
        {
            return rate > 0 ? rate : throw BooksProblem.Invalid("exchange_rate must be positive.");
        }
        var code = Currencies.Require(currency).Code;
        return await db.ExchangeRates.AsNoTracking()
            .Where(r => r.OrgId == ledger.OrgId && r.FromCurrency == ledger.BaseCurrency && r.ToCurrency == code && r.Date <= date)
            .OrderByDescending(r => r.Date).Select(r => (decimal?)r.Rate).FirstOrDefaultAsync(ct)
            ?? throw BooksProblem.Invalid($"No {ledger.BaseCurrency}→{code} rate on or before {date:yyyy-MM-dd}; give exchange_rate.");
    }
}
