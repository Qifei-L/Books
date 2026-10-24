using Books.Domain.Access;
using Books.Domain.Journals;

namespace Books.Domain.Documents;

/// <summary>
/// A month's payroll as calculated by an outside payroll system and imported by AI (owner decision: Books receives
/// payroll results, it does not calculate pay). The employee lines stay here; the ledger gets one summary journal,
/// and the tax return reads the lines for form TOS 01.
/// </summary>
public sealed class PayRun
{
    public const string Kind = "pay_run";

    private readonly List<PayRunLine> _lines = [];

    private PayRun() { }

    public PayRun(Guid id, Guid orgId, Guid ledgerId, long payRunNo, Guid createdBy, DateTimeOffset createdAt)
    {
        Id = id;
        OrgId = orgId;
        LedgerId = ledgerId;
        PayRunNo = payRunNo;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public long PayRunNo { get; private set; }

    /// <summary>The first day of the month the pay is for.</summary>
    public DateOnly Period { get; private set; }

    /// <summary>The journal's date.</summary>
    public DateOnly Date { get; private set; }

    public string? Reference { get; private set; }
    public string? Description { get; private set; }
    public PayRunAccounts Accounts { get; private set; } = null!;

    /// <summary>The currency of the employee lines, as the payroll system reports them (usually KHR).</summary>
    public string Currency { get; private set; } = "";

    /// <summary>When the lines are not in the ledger's currency: line currency per one unit of the ledger's currency, for example 4100.</summary>
    public decimal? Rate { get; private set; }
    public DocumentStatus Status { get; private set; } = DocumentStatus.Draft;
    public string? Issues { get; private set; }
    public Guid? JournalId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    public IReadOnlyList<PayRunLine> Lines => _lines;

    public void Set(DateOnly period, DateOnly date, string? reference, string? description, PayRunAccounts accounts, IEnumerable<PayRunLine> lines, bool isNew,
        string currency, decimal? rate, string baseCurrency)
    {
        currency = Ledgers.Currencies.Require(currency).Code;
        if (currency != baseCurrency && rate is not > 0)
        {
            throw new DomainException($"Lines in {currency} need the rate to {baseCurrency}.");
        }
        Currency = currency;
        Rate = currency == baseCurrency ? null : rate;
        var all = lines.ToList();
        if (all.Count is 0 or > 5000)
        {
            throw new DomainException("A pay run needs 1–5000 employee lines.");
        }
        var wrong = all.Select((l, i) => (l, i + 1)).Where(x => x.l.Gross != x.l.Net + x.l.SalaryTax + x.l.NssfEmployee + x.l.OtherDeductions).Select(x => x.Item2).ToList();
        if (wrong.Count > 0)
        {
            throw new DomainException($"Gross must equal net + salary tax + NSSF (employee) + other deductions; lines {string.Join(", ", wrong.Take(20))}{(wrong.Count > 20 ? " …" : "")}.");
        }
        var duplicates = all.GroupBy(l => l.EmployeeCode).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            throw new DomainException($"Employee codes appear more than once: {string.Join(", ", duplicates.Take(20))}.");
        }
        if (all.Sum(l => l.OtherDeductions) != 0 && accounts.OtherDeductions is null)
        {
            throw new DomainException("Other deductions need other_deductions_account_id.");
        }
        if (period.Day != 1)
        {
            throw new DomainException("period must be a month.");
        }
        Period = period;
        Date = date;
        Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Accounts = accounts;
        _lines.Clear();
        _lines.AddRange(all.Select((l, i) => l.Numbered(Id, i + 1)));
        if (!isNew)
        {
            Version++;
        }
    }

    public void PostedAs(Guid journalId)
    {
        JournalId = journalId;
        Status = DocumentStatus.Posted;
        Issues = null;
    }

    public void KeptAsDraft(string? issues) => Issues = issues;

    public void Delete()
    {
        Status = DocumentStatus.Deleted;
        Version++;
    }

    /// <summary>
    /// One summary journal: Dr salary expense (gross pay and fringe benefit tax), Dr NSSF expense (employer part);
    /// Cr salaries payable (net), Cr salary tax payable (salary and fringe benefit tax), Cr NSSF payable (both parts),
    /// Cr the other deductions account.
    /// </summary>
    /// <remarks>
    /// Lines in another currency are converted at the pay run's rate, each total rounded to the ledger's currency;
    /// the salary expense takes the rounding so the journal balances.
    /// </remarks>
    public JournalDraft ToJournal(int minorUnits)
    {
        decimal C(decimal amount) => Rate is { } rate ? Math.Round(amount / rate, minorUnits, MidpointRounding.AwayFromZero) : amount;
        var text = Description ?? $"Payroll {Period:yyyy-MM}";
        var a = Accounts;
        var credits = new List<DraftLine>
        {
            new(a.SalariesPayable, -C(_lines.Sum(l => l.Net)), $"{text} (net pay)"),
            new(a.SalaryTaxPayable, -C(_lines.Sum(l => l.SalaryTax + l.FringeBenefitTax)), $"{text} (tax on salary)"),
            new(a.NssfPayable, -C(_lines.Sum(l => l.NssfEmployee + l.NssfEmployer)), $"{text} (NSSF)"),
        };
        if (a.OtherDeductions is { } other)
        {
            credits.Add(new DraftLine(other, -C(_lines.Sum(l => l.OtherDeductions)), $"{text} (other deductions)"));
        }
        var nssf = C(_lines.Sum(l => l.NssfEmployer));
        var lines = new List<DraftLine>
        {
            new(a.SalaryExpense, -credits.Sum(l => l.Amount) - nssf, text),
            new(a.NssfExpense, nssf, $"{text} (NSSF employer)"),
        };
        lines.AddRange(credits);
        return new JournalDraft(Date, lines.Where(l => l.Amount != 0).ToList(), Reference, text);
    }
}

/// <summary>Where a pay run posts; taken from the previous pay run when omitted.</summary>
public sealed record PayRunAccounts(Guid SalaryExpense, Guid NssfExpense, Guid SalariesPayable, Guid SalaryTaxPayable, Guid NssfPayable, Guid? OtherDeductions);

/// <summary>One employee's results for the month, in the ledger's currency, as the payroll system calculated them.</summary>
public sealed class PayRunLine
{
    private PayRunLine() { }

    public PayRunLine(string employeeCode, string name, bool resident, decimal gross, decimal fringeBenefits, decimal salaryTax,
        decimal fringeBenefitTax, decimal nssfEmployee, decimal nssfEmployer, decimal otherDeductions, decimal net)
    {
        EmployeeCode = Names.Require(employeeCode, "employee_code");
        Name = Names.Require(name, "name");
        Resident = resident;
        Gross = gross;
        FringeBenefits = fringeBenefits;
        SalaryTax = salaryTax;
        FringeBenefitTax = fringeBenefitTax;
        NssfEmployee = nssfEmployee;
        NssfEmployer = nssfEmployer;
        OtherDeductions = otherDeductions;
        Net = net;
    }

    public Guid PayRunId { get; private set; }
    public int LineNo { get; private set; }
    public string EmployeeCode { get; private set; } = "";
    public string Name { get; private set; } = "";
    public bool Resident { get; private set; }
    public decimal Gross { get; private set; }
    public decimal FringeBenefits { get; private set; }
    public decimal SalaryTax { get; private set; }
    public decimal FringeBenefitTax { get; private set; }
    public decimal NssfEmployee { get; private set; }
    public decimal NssfEmployer { get; private set; }
    public decimal OtherDeductions { get; private set; }
    public decimal Net { get; private set; }

    internal PayRunLine Numbered(Guid payRunId, int lineNo)
    {
        PayRunId = payRunId;
        LineNo = lineNo;
        return this;
    }
}
