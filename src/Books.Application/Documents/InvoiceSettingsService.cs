using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Documents;

public sealed record InvoiceSettingsInput(
    string? NameKhmer, string? NameEnglish, string? TaxId, string? AddressKhmer, string? AddressEnglish, string? Phone, string? Email,
    string? InvoicePrefix, string? CreditNotePrefix);

public sealed record InvoiceSettingsDto(
    string? NameKhmer, string? NameEnglish, string? TaxId, string? AddressKhmer, string? AddressEnglish, string? Phone, string? Email,
    string InvoicePrefix, string CreditNotePrefix, string Version);

/// <summary>A ledger's invoice settings; anyone with access reads them, administrators change them.</summary>
public sealed class InvoiceSettingsService(IBooksDb db, WriteRunner writes, ReadRunner reads)
{
    public Task<InvoiceSettingsDto> GetAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            return ToDto(await LoadAsync(db, ledgerId, orgId, ct));
        }, ct);

    public Task<WriteResult> SetAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, InvoiceSettingsInput input, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("invoice_settings.set", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            scope.RequireAdmin();
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var settings = await db.InvoiceSettings.SingleOrDefaultAsync(s => s.LedgerId == ledgerId, ct);
            var isNew = settings is null;
            if (settings is null)
            {
                settings = new InvoiceSettings(ledgerId, orgId);
                db.InvoiceSettings.Add(settings);
            }
            else
            {
                scope.CheckVersion(settings.Version);
            }
            try
            {
                settings.Set(input.NameKhmer, input.NameEnglish, input.TaxId, input.AddressKhmer, input.AddressEnglish, input.Phone, input.Email,
                    input.InvoicePrefix, input.CreditNotePrefix, isNew);
            }
            catch (DomainException e)
            {
                throw BooksProblem.Invalid(e.Message);
            }
            scope.Audit("invoice_settings.set", "ledger", ledgerId, ToDto(settings));
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(200, ToDto(settings), settings.Version);
        }, ct);

    /// <summary>The ledger's settings, or the defaults (INV- and CN- prefixes) before any are saved.</summary>
    internal static async Task<InvoiceSettings> LoadAsync(IBooksDb db, Guid ledgerId, Guid orgId, CancellationToken ct) =>
        await db.InvoiceSettings.AsNoTracking().SingleOrDefaultAsync(s => s.LedgerId == ledgerId, ct) ?? new InvoiceSettings(ledgerId, orgId);

    public static InvoiceSettingsDto ToDto(InvoiceSettings s) => new(
        s.NameKhmer, s.NameEnglish, s.TaxId, s.AddressKhmer, s.AddressEnglish, s.Phone, s.Email, s.InvoicePrefix, s.CreditNotePrefix, s.Version.ToString());
}
