namespace Books.Domain.Journals;

/// <summary>What a user or AI submits for a journal, before any accounting effect.</summary>
/// <param name="Amount">Signed, in base currency: debit positive, credit negative.</param>
public sealed record DraftLine(
    Guid AccountId, decimal Amount, string? Description = null, Guid? LineId = null, Guid? PartyId = null, Guid? ItemId = null, decimal? Quantity = null);

public sealed record JournalDraft(DateOnly AccountingDate, IReadOnlyList<DraftLine> Lines, string? Reference = null, string? Description = null);

/// <summary>The facts about an account that validation needs.</summary>
public sealed record AccountFacts(Guid Id, bool Active, bool IsControl);

/// <summary>Everything validation reads, gathered by the caller inside its transaction.</summary>
/// <param name="PreviousDate">For an edit, the accounting date of the posted revision being replaced.</param>
/// <param name="Parties">Parties of this ledger named by the draft, with whether each is active.</param>
/// <param name="FromDocument">True when a document's posting adapter produced the lines (control accounts allowed).</param>
public sealed record JournalContext(
    int MinorUnits,
    DateOnly? ClosedThrough,
    IReadOnlyDictionary<Guid, AccountFacts> Accounts,
    DateOnly? PreviousDate = null,
    IReadOnlyDictionary<Guid, bool>? Parties = null,
    bool FromDocument = false,
    IReadOnlyDictionary<Guid, bool>? Items = null);

/// <summary>A definite validation failure. <see cref="Line"/> is 1-based when the issue belongs to one line.</summary>
public sealed record JournalIssue(string Code, string Message, int? Line = null);

/// <summary>One validation rule: a pure function of the draft and its context, with no database access.</summary>
public interface IJournalRule
{
    IEnumerable<JournalIssue> Check(JournalDraft draft, JournalContext context);
}

/// <summary>Runs every rule and returns all issues; an empty list means the journal may be posted.</summary>
public static class JournalValidator
{
    public const int MaxLines = 500;

    /// <summary>Largest amount numeric(20,4) can hold.</summary>
    public const decimal MaxAmount = 9_999_999_999_999_999.9999m;

    private static readonly IJournalRule[] Rules =
    [
        new LineCountRule(),
        new AmountRule(),
        new BalanceRule(),
        new AccountRule(),
        new PartyRule(),
        new ItemRule(),
        new CloseDateRule(),
    ];

    public static IReadOnlyList<JournalIssue> Validate(JournalDraft draft, JournalContext context) =>
        Rules.SelectMany(rule => rule.Check(draft, context)).ToList();
}

/// <summary>A posted journal has at least two lines (rule 22).</summary>
public sealed class LineCountRule : IJournalRule
{
    public IEnumerable<JournalIssue> Check(JournalDraft draft, JournalContext context)
    {
        if (draft.Lines.Count < 2)
        {
            yield return new JournalIssue("TOO_FEW_LINES", "A journal needs at least two lines.");
        }
        if (draft.Lines.Count > JournalValidator.MaxLines)
        {
            yield return new JournalIssue("TOO_MANY_LINES", $"A journal may have at most {JournalValidator.MaxLines} lines.");
        }
    }
}

/// <summary>Amounts are non-zero, within range and use no more decimals than the currency has (rule 11). Nothing is rounded.</summary>
public sealed class AmountRule : IJournalRule
{
    public IEnumerable<JournalIssue> Check(JournalDraft draft, JournalContext context)
    {
        for (var i = 0; i < draft.Lines.Count; i++)
        {
            var amount = draft.Lines[i].Amount;
            if (amount == 0)
            {
                yield return new JournalIssue("ZERO_AMOUNT", "A line needs a debit or a credit.", i + 1);
            }
            else if (Math.Abs(amount) > JournalValidator.MaxAmount)
            {
                yield return new JournalIssue("AMOUNT_TOO_LARGE", "The amount is too large.", i + 1);
            }
            else if (decimal.Round(amount, context.MinorUnits) != amount)
            {
                yield return new JournalIssue("TOO_MANY_DECIMALS", $"The currency allows {context.MinorUnits} decimal places.", i + 1);
            }
        }
    }
}

/// <summary>Debits equal credits exactly; no balancing line is invented (rule 22).</summary>
public sealed class BalanceRule : IJournalRule
{
    public IEnumerable<JournalIssue> Check(JournalDraft draft, JournalContext context)
    {
        if (draft.Lines.Any(l => Math.Abs(l.Amount) > JournalValidator.MaxAmount))
        {
            yield break;
        }
        var difference = draft.Lines.Sum(l => l.Amount);
        if (difference != 0)
        {
            yield return new JournalIssue("NOT_BALANCED", $"Debits and credits differ by {difference}.");
        }
    }
}

/// <summary>Lines use active accounts of this ledger; control accounts take document postings only (rule 18, P7).</summary>
public sealed class AccountRule : IJournalRule
{
    public IEnumerable<JournalIssue> Check(JournalDraft draft, JournalContext context)
    {
        for (var i = 0; i < draft.Lines.Count; i++)
        {
            if (!context.Accounts.TryGetValue(draft.Lines[i].AccountId, out var account))
            {
                yield return new JournalIssue("UNKNOWN_ACCOUNT", "The account is not in this ledger.", i + 1);
            }
            else if (!account.Active)
            {
                yield return new JournalIssue("INACTIVE_ACCOUNT", "The account is inactive.", i + 1);
            }
            else if (account.IsControl && !context.FromDocument)
            {
                yield return new JournalIssue("CONTROL_ACCOUNT", "Control accounts are posted only by documents.", i + 1);
            }
            else if (account.IsControl && draft.Lines[i].PartyId is null)
            {
                yield return new JournalIssue("PARTY_REQUIRED", "Lines on control accounts need a party.", i + 1);
            }
        }
    }
}

/// <summary>A named party must be an active party of this ledger (rule 23).</summary>
public sealed class PartyRule : IJournalRule
{
    public IEnumerable<JournalIssue> Check(JournalDraft draft, JournalContext context)
    {
        for (var i = 0; i < draft.Lines.Count; i++)
        {
            if (draft.Lines[i].PartyId is not { } party)
            {
                continue;
            }
            if (context.Parties is null || !context.Parties.TryGetValue(party, out var active))
            {
                yield return new JournalIssue("UNKNOWN_PARTY", "The party is not in this ledger.", i + 1);
            }
            else if (!active)
            {
                yield return new JournalIssue("INACTIVE_PARTY", "The party is inactive.", i + 1);
            }
        }
    }
}

/// <summary>A named item must be an active item of this ledger; a quantity has at most 4 decimals (rule 23).</summary>
public sealed class ItemRule : IJournalRule
{
    public IEnumerable<JournalIssue> Check(JournalDraft draft, JournalContext context)
    {
        for (var i = 0; i < draft.Lines.Count; i++)
        {
            var line = draft.Lines[i];
            if (line.ItemId is { } item)
            {
                if (context.Items is null || !context.Items.TryGetValue(item, out var active))
                {
                    yield return new JournalIssue("UNKNOWN_ITEM", "The item is not in this ledger.", i + 1);
                }
                else if (!active)
                {
                    yield return new JournalIssue("INACTIVE_ITEM", "The item is inactive.", i + 1);
                }
            }
            if (line.Quantity is { } qty && (decimal.Round(qty, 4) != qty || Math.Abs(qty) >= 10_000_000_000_000_000m))
            {
                yield return new JournalIssue("BAD_QUANTITY", "Quantity allows up to 4 decimals.", i + 1);
            }
        }
    }
}

/// <summary>Both the new date and, for an edit, the old date must be after the Close date (rules 13, 27).</summary>
public sealed class CloseDateRule : IJournalRule
{
    public IEnumerable<JournalIssue> Check(JournalDraft draft, JournalContext context)
    {
        if (context.ClosedThrough is not { } closed)
        {
            yield break;
        }
        if (draft.AccountingDate <= closed)
        {
            yield return new JournalIssue("DATE_CLOSED", $"The books are closed through {closed:yyyy-MM-dd}.");
        }
        if (context.PreviousDate is { } previous && previous <= closed)
        {
            yield return new JournalIssue("ORIGINAL_DATE_CLOSED", $"The journal being changed is dated on or before the Close date {closed:yyyy-MM-dd}.");
        }
    }
}
