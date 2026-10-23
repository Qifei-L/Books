using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Books.Tests.Access;

/// <summary>Rule 6: invitations target one verified identity, last 7 days and can be revoked.</summary>
public sealed class InvitationTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_invitation_lasts_exactly_seven_days_and_appears_only_in_the_recipients_inbox()
    {
        var (admin, orgId) = await OrgAsync();
        var recipient = Ids.Subject("recipient");
        using var invited = host.Client(recipient);
        using var stranger = host.Client(Ids.Subject("stranger"));
        using var unverified = host.Client(Ids.Subject(), $"{recipient}@example.test", emailVerified: false);

        var created = await InviteAsync(admin, orgId, $"{recipient}@EXAMPLE.test", "member");

        Assert.Equal("pending", created.GetProperty("status").GetString());
        Assert.Equal(TimeSpan.FromDays(7), created.GetProperty("expires_at").GetDateTimeOffset() - created.GetProperty("created_at").GetDateTimeOffset());
        var inbox = await InboxAsync(invited);
        Assert.Equal(created.Id(), Assert.Single(inbox.EnumerateArray()).Id());
        Assert.Equal(0, (await InboxAsync(stranger)).GetArrayLength());
        Assert.Equal(0, (await InboxAsync(unverified)).GetArrayLength());
        await using var db = host.CreateDbContext();
        Assert.Equal(1, await db.AuditEvents.CountAsync(a => a.TargetId == created.Id() && a.Action == "invitation.created", Ct));
    }

    [Fact]
    public async Task Repeating_an_invitation_returns_it_unchanged_and_a_different_one_conflicts()
    {
        var (admin, orgId) = await OrgAsync();
        var email = $"{Ids.Subject()}@example.test";
        var first = await InviteAsync(admin, orgId, email, "member");

        var again = await (await admin.PostAsync($"/api/v1/organizations/{orgId}/invitations", new { email, role = "member", grants = Array.Empty<object>() })).DataAsync();
        var different = await admin.PostAsync($"/api/v1/organizations/{orgId}/invitations", new { email, role = "admin", grants = Array.Empty<object>() });

        Assert.Equal(first.Id(), again.Id());
        Assert.Equal(first.GetProperty("expires_at").GetDateTimeOffset(), again.GetProperty("expires_at").GetDateTimeOffset());
        await different.AssertProblemAsync(HttpStatusCode.Conflict, "INVITE_CONFLICT");
    }

    [Fact]
    public async Task A_revoked_invitation_leaves_the_inbox_and_cannot_be_revoked_again()
    {
        var (admin, orgId) = await OrgAsync();
        var recipient = Ids.Subject();
        using var invited = host.Client(recipient);
        var invitation = await InviteAsync(admin, orgId, $"{recipient}@example.test", "member");
        var url = $"/api/v1/organizations/{orgId}/invitations/{invitation.Id()}/revoke";

        await (await admin.PostAsync(url, new { })).AssertProblemAsync(HttpStatusCode.PreconditionRequired, "PRECONDITION_REQUIRED");
        var revoked = await (await admin.PostAsync(url, null, ifMatch: 1)).DataAsync();
        var again = await admin.PostAsync(url, null, ifMatch: 2);

        Assert.Equal("revoked", revoked.GetProperty("status").GetString());
        Assert.Equal(0, (await InboxAsync(invited)).GetArrayLength());
        await again.AssertProblemAsync(HttpStatusCode.Gone, "INVITE_UNAVAILABLE");
        await InviteAsync(admin, orgId, $"{recipient}@example.test", "admin");
    }

    [Fact]
    public async Task An_expired_invitation_is_shown_as_expired_and_can_be_replaced()
    {
        var (admin, orgId) = await OrgAsync();
        var recipient = Ids.Subject();
        using var invited = host.Client(recipient);
        var old = await InviteAsync(admin, orgId, $"{recipient}@example.test", "member");
        await ExpireAsync(old.Id());

        var listed = await (await admin.GetAsync($"/api/v1/organizations/{orgId}/invitations", Ct)).DataAsync();
        var inboxWhileExpired = await InboxAsync(invited);
        var replacement = await InviteAsync(admin, orgId, $"{recipient}@example.test", "member");

        Assert.Equal("expired", Assert.Single(listed.EnumerateArray()).GetProperty("status").GetString());
        Assert.Equal(0, inboxWhileExpired.GetArrayLength());
        Assert.NotEqual(old.Id(), replacement.Id());
    }

    [Fact]
    public async Task Invitations_reject_members_foreign_ledgers_and_non_administrators()
    {
        var (admin, orgId) = await OrgAsync();
        using var member = host.Client(Ids.Subject("member"));
        await host.AddMemberAsync(orgId, member);
        using var other = host.Client(Ids.Subject("other"));
        var otherOrg = await other.CreateOrganizationAsync();
        var foreignLedger = await other.CreateLedgerAsync(otherOrg);
        var adminEmail = (await (await admin.GetAsync("/api/v1/me", Ct)).DataAsync()).GetProperty("email").GetString();
        var url = $"/api/v1/organizations/{orgId}/invitations";
        var newcomer = $"{Ids.Subject()}@example.test";

        await (await admin.PostAsync(url, new { email = adminEmail, role = "member", grants = Array.Empty<object>() }))
            .AssertProblemAsync(HttpStatusCode.Conflict, "ALREADY_MEMBER");
        await (await admin.PostAsync(url, new { email = newcomer, role = "member", grants = new[] { new { ledger_id = foreignLedger, access = "read" } } }))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await (await member.PostAsync(url, new { email = newcomer, role = "member", grants = Array.Empty<object>() }))
            .AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await other.PostAsync(url, new { email = newcomer, role = "member", grants = Array.Empty<object>() }))
            .AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    private async Task<(HttpClient Admin, Guid OrgId)> OrgAsync()
    {
        var admin = host.Client(Ids.Subject("admin"));
        return (admin, await admin.CreateOrganizationAsync());
    }

    internal static async Task<JsonElement> InviteAsync(HttpClient admin, Guid orgId, string email, string role, object[]? grants = null) =>
        await (await admin.PostAsync($"/api/v1/organizations/{orgId}/invitations", new { email, role, grants = grants ?? [] })).DataAsync(HttpStatusCode.Created);

    internal static async Task<JsonElement> InboxAsync(HttpClient client) =>
        await (await client.GetAsync("/api/v1/me/invitations", Ct)).DataAsync();

    /// <summary>Moves an invitation's expiry into the past, as if seven days had gone by.</summary>
    internal async Task ExpireAsync(Guid invitationId)
    {
        await using var db = host.CreateDbContext();
        await db.Database.ExecuteSqlAsync($"UPDATE invitations SET expires_at = now() - interval '1 second' WHERE id = {invitationId}", Ct);
    }
}
