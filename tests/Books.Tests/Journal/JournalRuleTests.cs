using Books.Domain.Journals;

namespace Books.Tests.Journal;

/// <summary>Validation rules are pure functions, tested without a database.</summary>
public sealed class JournalRuleTests
{
    private static readonly Guid Cash = Guid.NewGuid();
    private static readonly Guid Sales = Guid.NewGuid();
    private static readonly Guid Inactive = Guid.NewGuid();
    private static readonly Guid Receivable = Guid.NewGuid();
    private static readonly DateOnly October = new(2026, 10, 15);

    private static JournalContext Usd(DateOnly? closedThrough = null, DateOnly? previousDate = null) => new(
        2,
        closedThrough,
        new Dictionary<Guid, AccountFacts>
        {
            [Cash] = new(Cash, true, false),
            [Sales] = new(Sales, true, false),
            [Inactive] = new(Inactive, false, false),
            [Receivable] = new(Receivable, true, true),
        },
        previousDate);

    private static JournalDraft Draft(params (Guid Account, decimal Amount)[] lines) =>
        new(October, lines.Select(l => new DraftLine(l.Account, l.Amount)).ToList());

    private static string[] Codes(JournalDraft draft, JournalContext context) =>
        JournalValidator.Validate(draft, context).Select(i => i.Code).ToArray();

    [Fact]
    public void A_balanced_journal_on_active_accounts_has_no_issues()
    {
        Assert.Empty(Codes(Draft((Cash, 100.25m), (Sales, -100.25m)), Usd()));
    }

    [Fact]
    public void Debits_must_equal_credits_exactly_with_no_tolerance()
    {
        var issues = JournalValidator.Validate(Draft((Cash, 100m), (Sales, -99.99m)), Usd());

        var issue = Assert.Single(issues);
        Assert.Equal("NOT_BALANCED", issue.Code);
        Assert.Contains("0.01", issue.Message);
    }

    [Fact]
    public void A_journal_needs_two_non_zero_lines()
    {
        Assert.Contains("TOO_FEW_LINES", Codes(Draft((Cash, 0m)), Usd()));
        Assert.Contains("ZERO_AMOUNT", Codes(Draft((Cash, 0m), (Sales, 0m)), Usd()));
    }

    [Theory]
    [InlineData(2, "10.001", true)]
    [InlineData(2, "10.01", false)]
    [InlineData(0, "10.5", true)]
    [InlineData(0, "10", false)]
    public void Amounts_may_not_have_more_decimals_than_the_currency(int minorUnits, string amount, bool rejected)
    {
        var value = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
        var context = Usd() with { MinorUnits = minorUnits };

        var codes = Codes(Draft((Cash, value), (Sales, -value)), context);

        Assert.Equal(rejected, codes.Contains("TOO_MANY_DECIMALS"));
    }

    [Fact]
    public void Amounts_beyond_storage_range_are_rejected_not_clamped()
    {
        var huge = JournalValidator.MaxAmount + 1;

        Assert.Contains("AMOUNT_TOO_LARGE", Codes(Draft((Cash, huge), (Sales, -huge)), Usd()));
    }

    [Fact]
    public void Lines_need_known_active_accounts_and_control_accounts_need_a_party()
    {
        var codes = Codes(Draft((Guid.NewGuid(), 1m), (Inactive, 1m), (Receivable, -2m)), Usd());

        Assert.Equal(["UNKNOWN_ACCOUNT", "INACTIVE_ACCOUNT", "PARTY_REQUIRED"], codes);
    }

    [Fact]
    public void Issues_name_the_line_they_belong_to()
    {
        var issue = Assert.Single(JournalValidator.Validate(Draft((Cash, 1m), (Inactive, -1m)), Usd()));

        Assert.Equal(2, issue.Line);
    }

    [Theory]
    [InlineData("2026-10-14", null, new string[0])]
    [InlineData("2026-10-15", null, new[] { "DATE_CLOSED" })]
    [InlineData("2026-10-14", "2026-10-01", new[] { "ORIGINAL_DATE_CLOSED" })]
    [InlineData("2026-10-31", "2026-10-15", new[] { "DATE_CLOSED", "ORIGINAL_DATE_CLOSED" })]
    public void Both_the_new_and_the_original_date_must_be_after_the_close_date(string closedThrough, string? previous, string[] expected)
    {
        var context = Usd(DateOnly.Parse(closedThrough), previous is null ? null : DateOnly.Parse(previous));

        Assert.Equal(expected, Codes(Draft((Cash, 1m), (Sales, -1m)), context));
    }
}
