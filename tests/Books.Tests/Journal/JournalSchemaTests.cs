using Books.Tests.Infrastructure;
using Books.Tests.Ledger;
using Npgsql;

namespace Books.Tests.Journal;

/// <summary>Journal invariants the database enforces even when the service is bypassed (rules 22, 24, 28, 37).</summary>
public sealed class JournalSchemaTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_posted_revision_that_does_not_balance_cannot_commit()
    {
        var setup = await SetUpAsync();

        var error = await Assert.ThrowsAsync<PostgresException>(() => setup.InsertJournalAsync(1, "posted", 100m, -99.99m));

        Assert.Equal("23514", error.SqlState);
        Assert.Equal("posted_revision_balances", error.ConstraintName);
    }

    [Fact]
    public async Task A_posted_revision_needs_two_lines_but_a_draft_may_be_unbalanced()
    {
        var setup = await SetUpAsync();

        var single = await Assert.ThrowsAsync<PostgresException>(() => setup.InsertJournalAsync(1, "posted", 0.01m));
        await setup.InsertJournalAsync(2, "draft", 100m, -1m);

        Assert.Equal("23514", single.SqlState);
    }

    [Fact]
    public async Task Lines_and_revisions_cannot_be_changed_or_deleted_but_revisions_can_be_superseded()
    {
        var setup = await SetUpAsync();
        var (_, revision) = await setup.InsertJournalAsync(1, "posted", 50m, -50m);

        var changeLine = await Assert.ThrowsAsync<PostgresException>(() => setup.ExecAsync("UPDATE journal_lines SET amount = 60 WHERE revision_id = @r", revision));
        var deleteLine = await Assert.ThrowsAsync<PostgresException>(() => setup.ExecAsync("DELETE FROM journal_lines WHERE revision_id = @r", revision));
        var changeDate = await Assert.ThrowsAsync<PostgresException>(() => setup.ExecAsync("UPDATE journal_revisions SET accounting_date = '2026-01-01' WHERE id = @r", revision));
        var deleteRevision = await Assert.ThrowsAsync<PostgresException>(() => setup.ExecAsync("DELETE FROM journal_revisions WHERE id = @r", revision));
        await setup.ExecAsync("UPDATE journals SET effective_revision_id = NULL WHERE effective_revision_id = @r; UPDATE journal_revisions SET status = 'superseded', superseded_at = now() WHERE id = @r", revision);

        Assert.All([changeLine, deleteLine, changeDate, deleteRevision], e => Assert.Equal("42501", e.SqlState));
    }

    [Fact]
    public async Task The_effective_revision_must_be_posted()
    {
        var setup = await SetUpAsync();
        var (journal, _) = await setup.InsertJournalAsync(1, "draft", 10m, -10m);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            setup.ExecAsync("UPDATE journals SET effective_revision_id = current_revision_id WHERE id = @r", journal));

        Assert.Equal("effective_revision_posted", error.ConstraintName);
    }

    [Fact]
    public async Task Journal_numbers_are_unique_per_ledger_and_lines_stay_in_their_ledger()
    {
        var setup = await SetUpAsync();
        var other = await SetUpAsync(setup.Admin, setup.OrgId);
        await setup.InsertJournalAsync(7, "posted", 1m, -1m);

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => setup.InsertJournalAsync(7, "posted", 1m, -1m));
        await other.InsertJournalAsync(7, "posted", 1m, -1m);
        var foreignAccount = await Assert.ThrowsAsync<PostgresException>(() =>
            setup.InsertJournalAsync(8, "posted", [(setup.Cash, 1m), (other.Cash, -1m)]));

        Assert.Equal("23505", duplicate.SqlState);
        Assert.Equal("23503", foreignAccount.SqlState);
    }

    private async Task<Setup> SetUpAsync(HttpClient? admin = null, Guid? orgId = null)
    {
        admin ??= host.Client(Ids.Subject("admin"));
        var org = orgId ?? await admin.CreateOrganizationAsync();
        var ledger = await admin.CreateLedgerAsync(org, $"Company {Guid.NewGuid():N}");
        var cash = await AccountTests.CreateAsync(admin, org, ledger, "1000", "Cash", "assets");
        var sales = await AccountTests.CreateAsync(admin, org, ledger, "4000", "Sales", "income");
        return new Setup(host, admin, org, ledger, cash.Id(), sales.Id(), await admin.UserIdAsync());
    }

    private sealed record Setup(TestHost Host, HttpClient Admin, Guid OrgId, Guid LedgerId, Guid Cash, Guid Sales, Guid UserId)
    {
        public Task<(Guid Journal, Guid Revision)> InsertJournalAsync(long journalNo, string status, params decimal[] amounts) =>
            InsertJournalAsync(journalNo, status, amounts.Select((a, i) => (i % 2 == 0 ? Cash : Sales, a)).ToArray());

        /// <summary>Inserts a journal, one revision and its lines in a single transaction, the way the service will.</summary>
        public async Task<(Guid Journal, Guid Revision)> InsertJournalAsync(long journalNo, string status, (Guid Account, decimal Amount)[] lines)
        {
            var journal = Guid.NewGuid();
            var revision = Guid.NewGuid();
            await using var connection = new NpgsqlConnection(Host.ConnectionString);
            await connection.OpenAsync(Ct);
            await using var tx = await connection.BeginTransactionAsync(Ct);
            var sql = """
                INSERT INTO journals (id, org_id, ledger_id, journal_no, revision_count, created_by, created_at, version)
                VALUES (@j, @o, @l, @no, 1, @u, now(), 1);
                INSERT INTO journal_revisions (id, org_id, ledger_id, journal_id, revision_no, kind, status, accounting_date, created_by, created_at)
                VALUES (@r, @o, @l, @j, 1, 'manual', @s, '2026-10-01', @u, now());
                UPDATE journals SET current_revision_id = @r,
                    effective_revision_id = CASE WHEN @s = 'posted' THEN @r END WHERE id = @j;
                """;
            await using (var command = new NpgsqlCommand(sql, connection, tx))
            {
                command.Parameters.AddWithValue("j", journal);
                command.Parameters.AddWithValue("r", revision);
                command.Parameters.AddWithValue("o", OrgId);
                command.Parameters.AddWithValue("l", LedgerId);
                command.Parameters.AddWithValue("no", journalNo);
                command.Parameters.AddWithValue("u", UserId);
                command.Parameters.AddWithValue("s", status);
                await command.ExecuteNonQueryAsync(Ct);
            }
            for (var i = 0; i < lines.Length; i++)
            {
                await using var line = new NpgsqlCommand(
                    "INSERT INTO journal_lines (revision_id, line_no, line_id, org_id, ledger_id, account_id, amount) VALUES (@r, @n, @id, @o, @l, @a, @amt)",
                    connection, tx);
                line.Parameters.AddWithValue("r", revision);
                line.Parameters.AddWithValue("n", i + 1);
                line.Parameters.AddWithValue("id", Guid.NewGuid());
                line.Parameters.AddWithValue("o", OrgId);
                line.Parameters.AddWithValue("l", LedgerId);
                line.Parameters.AddWithValue("a", lines[i].Account);
                line.Parameters.AddWithValue("amt", lines[i].Amount);
                await line.ExecuteNonQueryAsync(Ct);
            }
            await tx.CommitAsync(Ct);
            return (journal, revision);
        }

        public async Task ExecAsync(string sql, Guid id)
        {
            await using var connection = new NpgsqlConnection(Host.ConnectionString);
            await connection.OpenAsync(Ct);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("r", id);
            await command.ExecuteNonQueryAsync(Ct);
        }
    }
}
