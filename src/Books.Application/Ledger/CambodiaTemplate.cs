using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Ledgers;

/// <summary>
/// The Cambodia starter template (tax pack "cambodia-2026"): standard accounts with their usages and system
/// purposes, and the standard tax codes. Applied by an administrator in one transaction. Applying it again
/// reuses what exists and creates only what is missing. Rates follow 04-cambodia-tax.md; confirm with an adviser.
/// </summary>
public sealed class CambodiaTemplate(IBooksDb db, WriteRunner writes, AccountUsageService usages)
{
    public const string Key = Books.Domain.Documents.TaxPacks.Cambodia2026;

    private sealed record AccountSpec(string Code, string Name, AccountType Type, string Group, Usage[] Usages, SystemPurpose? Purpose = null);

    private sealed record TaxSpec(string Name, TaxKind Kind, decimal Rate, string? AccountCode, string? NonDeductibleCode = null, DateOnly? From = null, string? Category = null);

    private static readonly AccountSpec[] Accounts =
    [
        new("1200", "Accounts receivable", AccountType.Assets, "Current assets", [Usage.Receivable]),
        new("1300", "VAT input (deductible)", AccountType.Assets, "Current assets", [Usage.InputVat]),
        new("2000", "Accounts payable", AccountType.Liabilities, "Current liabilities", [Usage.Payable]),
        new("2100", "VAT output payable", AccountType.Liabilities, "Current liabilities", [Usage.OutputVat]),
        new("2200", "Withholding tax payable", AccountType.Liabilities, "Current liabilities", [Usage.WithholdingPayable]),
        new("2999", "Suspense", AccountType.Liabilities, "Current liabilities", [], SystemPurpose.Suspense),
        new("3100", "Retained earnings", AccountType.Equity, "Equity", [], SystemPurpose.RetainedEarnings),
        new("3900", "Opening balance clearing", AccountType.Equity, "Equity", [], SystemPurpose.OpeningBalanceClearing),
        new("5900", "Non-deductible VAT", AccountType.CostAndExpense, "Operating expenses", [Usage.NonDeductibleVat]),
    ];

    private static readonly TaxSpec[] TaxCodes =
    [
        new("VAT 10% (sales)", TaxKind.VatOutput, 10m, "2100"),
        new("VAT 10% (purchases)", TaxKind.VatInput, 10m, "1300", "5900"),
        new("VAT 0% (zero-rated)", TaxKind.VatZeroRated, 0m, null),
        new("Non-VAT", TaxKind.NonVat, 0m, null),
        new("VAT 4% fuel (sales)", TaxKind.VatOutput, 4m, "2100", From: new DateOnly(2026, 3, 1)),
        new("WHT 15% services, interest, royalties", TaxKind.Withholding, 15m, "2200", Category: "services"),
        new("WHT 10% rent", TaxKind.Withholding, 10m, "2200", Category: "rent"),
        new("WHT 14% non-resident", TaxKind.Withholding, 14m, "2200", Category: "non_resident"),
    ];

    public sealed record Summary(string Template, IReadOnlyList<string> Created, IReadOnlyList<string> Reused);

    public Task<WriteResult> ApplyAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("ledger.apply_template", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            scope.RequireAdmin();
            var ledger = await db.Ledgers.SingleOrDefaultAsync(l => l.OrgId == orgId && l.Id == ledgerId, ct) ?? throw BooksProblem.NotFound();
            var created = new List<string>();
            var reused = new List<string>();
            var byCode = new Dictionary<string, Account>(StringComparer.OrdinalIgnoreCase);

            foreach (var spec in Accounts)
            {
                var account = await ExistingAsync(ledger.Id, spec, ct);
                if (account is null)
                {
                    account = new Account(Guid.CreateVersion7(), ledger, spec.Code, spec.Name, spec.Type, await GroupAsync(ledger, spec, scope.Now, ct),
                        false, false, null, scope.Now);
                    db.Accounts.Add(account);
                    created.Add($"account {spec.Code} {spec.Name}");
                }
                else
                {
                    reused.Add($"account {account.Code} {account.Name}");
                }
                byCode[spec.Code] = account;
                foreach (var usage in spec.Usages)
                {
                    await usages.EnsureAsync(account, usage, ct);
                }
                if (spec.Purpose is { } purpose && !await db.AccountPurposes.AnyAsync(p => p.LedgerId == ledger.Id && p.Purpose == purpose, ct))
                {
                    db.AccountPurposes.Add(new AccountPurpose(ledger, purpose, account));
                }
            }

            var existingNames = await db.TaxCodes.Where(t => t.LedgerId == ledger.Id).Select(t => t.Name).ToListAsync(ct);
            foreach (var spec in TaxCodes)
            {
                if (existingNames.Contains(spec.Name, StringComparer.OrdinalIgnoreCase))
                {
                    reused.Add($"tax code {spec.Name}");
                    continue;
                }
                var account = spec.AccountCode is null ? null : byCode[spec.AccountCode];
                var nonDeductible = spec.NonDeductibleCode is null ? null : byCode[spec.NonDeductibleCode];
                var code = new TaxCode(Guid.CreateVersion7(), ledger, spec.Name, spec.Kind, spec.Rate, account, nonDeductible, spec.From, null, scope.Now);
                code.SetReturnCategory(spec.Category, [.. Books.Domain.Documents.TaxReturns.Builtin(Key)!.WithholdingCategories.Select(c => c.Key)]);
                db.TaxCodes.Add(code);
                created.Add($"tax code {spec.Name}");
            }

            ledger.UseTaxPack(Key);
            scope.Audit("ledger.template_applied", "ledger", ledger.Id, new { template = Key, created, reused });
            return WriteResult.Of(200, new Summary(Key, created, reused), ledger.Version);
        }, ct);

    /// <summary>
    /// An account already serving the usage or purpose is reused. A matching code alone is a coincidence, not
    /// intent, so a code taken by another account is a conflict the administrator resolves (rename, or give the
    /// intended account the usage first).
    /// </summary>
    private async Task<Account?> ExistingAsync(Guid ledgerId, AccountSpec spec, CancellationToken ct)
    {
        if (spec.Usages.Length > 0)
        {
            var usage = spec.Usages[0];
            var withUsage = await db.AccountUsages.Where(u => u.LedgerId == ledgerId && u.Usage == usage).Select(u => u.AccountId).FirstOrDefaultAsync(ct);
            if (withUsage != Guid.Empty)
            {
                return await db.Accounts.SingleAsync(a => a.Id == withUsage, ct);
            }
        }
        if (spec.Purpose is { } purpose
            && await db.AccountPurposes.Where(p => p.LedgerId == ledgerId && p.Purpose == purpose).Select(p => (Guid?)p.AccountId).SingleOrDefaultAsync(ct) is { } assigned)
        {
            return await db.Accounts.SingleAsync(a => a.Id == assigned, ct);
        }
        var code = spec.Code.ToLowerInvariant();
        return await db.Accounts.AnyAsync(a => a.LedgerId == ledgerId && a.Code.ToLower() == code, ct)
            ? throw BooksProblem.Conflict("TEMPLATE_CONFLICT",
                $"Code {spec.Code} is already used by another account; rename it, or give the intended account its usage, then apply the template.")
            : null;
    }

    private async Task<AccountGroup> GroupAsync(Ledger ledger, AccountSpec spec, DateTimeOffset now, CancellationToken ct)
    {
        var group = await db.AccountGroups.FirstOrDefaultAsync(g => g.LedgerId == ledger.Id && g.Type == spec.Type && g.Name == spec.Group, ct)
            ?? db.AccountGroups.Local.FirstOrDefault(g => g.LedgerId == ledger.Id && g.Type == spec.Type && g.Name == spec.Group);
        if (group is null)
        {
            group = new AccountGroup(Guid.CreateVersion7(), ledger, spec.Group, spec.Type, null, now);
            db.AccountGroups.Add(group);
        }
        return group;
    }
}
