using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Ledgers;

public sealed record PartyDto(
    Guid Id, Guid LedgerId, string Name, string? TaxId, bool IsCustomer, bool IsSupplier,
    string? Email, string? Phone, string? Address, string? ContactName, int? PaymentTermsDays, bool Active, bool Used, string Version,
    string? NameKm = null, string? AddressKm = null);

/// <summary>Fields of a create or partial update. Null leaves a field unchanged; "" clears an optional text.</summary>
public sealed record PartyInput(
    string? Name, string? TaxId, bool? IsCustomer, bool? IsSupplier, string? Email, string? Phone, string? Address,
    string? ContactName, int? PaymentTermsDays, bool? Active, string? NameKm = null, string? AddressKm = null);

/// <summary>Customers and suppliers of a ledger. Reading needs read access; changes need edit access.</summary>
public sealed class PartyService(IBooksDb db, WriteRunner writes, ReadRunner reads)
{
    public Task<IReadOnlyList<PartyDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            return await LoadAsync(ledgerId, null, ct);
        }, ct);

    public Task<WriteResult> CreateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, PartyInput input, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("party.create", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var party = new Party(Guid.CreateVersion7(), ledger, input.Name ?? "", scope.Now);
            party.Update(null, input.TaxId, input.IsCustomer, input.IsSupplier, input.Email, input.Phone, input.Address,
                input.ContactName, input.PaymentTermsDays, input.Active, isNew: true);
            db.Parties.Add(party);
            await SetLocalizationAsync(party.Id, input.NameKm, input.AddressKm, scope.Now, ct);
            scope.Audit("party.created", "party", party.Id, new { ledger_id = ledgerId, name = party.Name, tax_id = party.TaxId, name_km = input.NameKm });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(201, (await LoadAsync(ledgerId, party.Id, ct)).Single(), party.Version);
        }, ct);

    public Task<WriteResult> UpdateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid partyId, PartyInput input, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("party.update", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var party = await db.Parties.SingleOrDefaultAsync(p => p.LedgerId == ledgerId && p.Id == partyId, ct) ?? throw BooksProblem.NotFound();
            scope.CheckVersion(party.Version);
            var before = (await LoadAsync(ledgerId, party.Id, ct)).Single();
            party.Update(input.Name, input.TaxId, input.IsCustomer, input.IsSupplier, input.Email, input.Phone, input.Address,
                input.ContactName, input.PaymentTermsDays, input.Active);
            await SetLocalizationAsync(party.Id, input.NameKm, input.AddressKm, scope.Now, ct);
            await db.SaveChangesAsync(ct);
            var after = (await LoadAsync(ledgerId, party.Id, ct)).Single();
            scope.Audit("party.updated", "party", party.Id, new { before, after });
            return WriteResult.Of(200, after, party.Version);
        }, ct);

    /// <summary>A party named on any journal line, including drafts, is kept; deactivate it instead.</summary>
    public Task<WriteResult> DeleteAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid partyId, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("party.delete", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var party = await db.Parties.SingleOrDefaultAsync(p => p.LedgerId == ledgerId && p.Id == partyId, ct) ?? throw BooksProblem.NotFound();
            scope.CheckVersion(party.Version);
            if (await db.JournalLines.AnyAsync(l => l.PartyId == party.Id, ct))
            {
                throw BooksProblem.Conflict("PARTY_IN_USE", "Journals name this party; deactivate it instead.");
            }
            var before = (await LoadAsync(ledgerId, party.Id, ct)).Single();
            db.Parties.Remove(party);
            scope.Audit("party.deleted", "party", party.Id, new { before });
            return WriteResult.Of(200, new { id = party.Id, deleted = true });
        }, ct);

    private async Task<IReadOnlyList<PartyDto>> LoadAsync(Guid ledgerId, Guid? partyId, CancellationToken ct)
    {
        var rows = await db.Parties.AsNoTracking()
            .Where(p => p.LedgerId == ledgerId && (partyId == null || p.Id == partyId))
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Select(p => new { p, Used = db.JournalLines.Any(l => l.PartyId == p.Id) })
            .ToListAsync(ct);
        var ids = rows.Select(x => x.p.Id).ToList();
        var localizations = await db.PartyLocalizations.AsNoTracking()
            .Where(l => ids.Contains(l.PartyId) && l.Locale == PartyLocalization.Khmer)
            .ToDictionaryAsync(l => l.PartyId, ct);
        return rows.Select(x =>
        {
            var km = localizations.GetValueOrDefault(x.p.Id);
            return new PartyDto(x.p.Id, x.p.LedgerId, x.p.Name, x.p.TaxId, x.p.IsCustomer, x.p.IsSupplier,
                x.p.Email, x.p.Phone, x.p.Address, x.p.ContactName, x.p.PaymentTermsDays, x.p.Active, x.Used, x.p.Version.ToString(),
                km?.Name, km?.Address);
        }).ToList();
    }

    /// <summary>Upserts the Khmer details; null leaves them, empty text removes the localization.</summary>
    private async Task SetLocalizationAsync(Guid partyId, string? nameKm, string? addressKm, DateTimeOffset now, CancellationToken ct)
    {
        if (nameKm is null && addressKm is null)
        {
            return;
        }
        var row = await db.PartyLocalizations.SingleOrDefaultAsync(l => l.PartyId == partyId, ct);
        if (string.IsNullOrWhiteSpace(nameKm))
        {
            if (!string.IsNullOrWhiteSpace(addressKm))
            {
                throw BooksProblem.Invalid("name_km is required when address_km is given.");
            }
            if (row is not null)
            {
                db.PartyLocalizations.Remove(row);
            }
            return;
        }
        try
        {
            if (row is null)
            {
                db.PartyLocalizations.Add(new PartyLocalization(partyId, PartyLocalization.Khmer, nameKm, addressKm, now));
            }
            else
            {
                row.Set(nameKm, addressKm, now);
            }
        }
        catch (DomainException e)
        {
            throw BooksProblem.Invalid(e.Message);
        }
    }
}
