using System.Net;
using Books.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Books.Tests.Access;

/// <summary>Rules 6 and 7: explicit acceptance by the verified recipient; rejoining never revives old grants.</summary>
public sealed class InvitationAcceptTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_recipient_accepts_and_gets_exactly_the_offered_access()
    {
        var (admin, orgId) = await OrgAsync();
        var ledger = await admin.CreateLedgerAsync(orgId);
        var subject = Ids.Subject("newcomer");
        using var newcomer = host.Client(subject);
        var invitation = await InvitationTests.InviteAsync(admin, orgId, Email(subject), "member", [new { ledger_id = ledger, access = "read" }]);

        var accepted = await (await newcomer.PostAsync(AcceptUrl(invitation.Id()), new { }, ifMatch: 1)).DataAsync();

        Assert.Equal("member", accepted.GetProperty("membership").GetProperty("role").GetString());
        var ledgers = await (await newcomer.GetAsync($"/api/v1/organizations/{orgId}/ledgers", Ct)).DataAsync();
        Assert.Equal("read", Assert.Single(ledgers.EnumerateArray()).GetProperty("effective_access").GetString());
        var listed = await (await admin.GetAsync($"/api/v1/organizations/{orgId}/invitations", Ct)).DataAsync();
        Assert.Equal("accepted", Assert.Single(listed.EnumerateArray()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Only_the_verified_recipient_can_accept()
    {
        var (admin, orgId) = await OrgAsync();
        var subject = Ids.Subject();
        var invitation = await InvitationTests.InviteAsync(admin, orgId, Email(subject), "member");
        using var stranger = host.Client(Ids.Subject("stranger"));
        using var unverified = host.Client(Ids.Subject(), Email(subject), emailVerified: false);

        await (await stranger.PostAsync(AcceptUrl(invitation.Id()), new { }, ifMatch: 1)).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        await (await unverified.PostAsync(AcceptUrl(invitation.Id()), new { }, ifMatch: 1)).AssertProblemAsync(HttpStatusCode.Forbidden, "EMAIL_NOT_VERIFIED");
        await (await stranger.GetAsync($"/api/v1/organizations/{orgId}", Ct)).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Expired_and_revoked_invitations_cannot_be_accepted()
    {
        var (admin, orgId) = await OrgAsync();
        var late = Ids.Subject("late");
        var revoked = Ids.Subject("revoked");
        using var lateClient = host.Client(late);
        using var revokedClient = host.Client(revoked);
        var expiring = await InvitationTests.InviteAsync(admin, orgId, Email(late), "member");
        var revoking = await InvitationTests.InviteAsync(admin, orgId, Email(revoked), "member");
        await new InvitationTests(host).ExpireAsync(expiring.Id());
        await (await admin.PostAsync($"/api/v1/organizations/{orgId}/invitations/{revoking.Id()}/revoke", new { }, ifMatch: 1)).DataAsync();

        await (await lateClient.PostAsync(AcceptUrl(expiring.Id()), new { }, ifMatch: 1)).AssertProblemAsync(HttpStatusCode.Gone, "INVITE_UNAVAILABLE");
        await (await revokedClient.PostAsync(AcceptUrl(revoking.Id()), new { }, ifMatch: 2)).AssertProblemAsync(HttpStatusCode.Gone, "INVITE_UNAVAILABLE");
    }

    [Fact]
    public async Task An_invitation_dies_when_its_inviter_stops_being_an_administrator_even_after_re_promotion()
    {
        var (owner, orgId) = await OrgAsync();
        using var inviter = host.Client(Ids.Subject("inviter"));
        var inviterMembership = await host.AddMemberAsync(orgId, inviter, role: "admin");
        var subject = Ids.Subject("target");
        using var target = host.Client(subject);
        var invitation = await InvitationTests.InviteAsync(inviter, orgId, Email(subject), "member");
        var access = $"/api/v1/organizations/{orgId}/memberships/{inviterMembership}/access";

        await (await owner.WriteAsync(HttpMethod.Put, access, new { role = "member", grants = Array.Empty<object>() }, ifMatch: 1)).DataAsync();
        await (await owner.WriteAsync(HttpMethod.Put, access, new { role = "admin", grants = Array.Empty<object>() }, ifMatch: 2)).DataAsync();

        await (await target.PostAsync(AcceptUrl(invitation.Id()), new { }, ifMatch: 2)).AssertProblemAsync(HttpStatusCode.Gone, "INVITE_UNAVAILABLE");
        Assert.Equal(0, (await InvitationTests.InboxAsync(target)).GetArrayLength());
    }

    [Fact]
    public async Task A_member_who_rejoins_gets_a_new_generation_and_only_the_new_grants()
    {
        var (admin, orgId) = await OrgAsync();
        var oldLedger = await admin.CreateLedgerAsync(orgId, "Old");
        var newLedger = await admin.CreateLedgerAsync(orgId, "New");
        var subject = Ids.Subject("returning");
        using var member = host.Client(subject);
        var first = await InvitationTests.InviteAsync(admin, orgId, Email(subject), "member", [new { ledger_id = oldLedger, access = "edit" }]);
        var membershipId = (await (await member.PostAsync(AcceptUrl(first.Id()), new { }, ifMatch: 1)).DataAsync()).Id("membership");
        await (await admin.PostAsync($"/api/v1/organizations/{orgId}/memberships/{membershipId}/remove", new { }, ifMatch: 1)).DataAsync();

        var second = await InvitationTests.InviteAsync(admin, orgId, Email(subject), "member", [new { ledger_id = newLedger, access = "read" }]);
        var rejoined = await (await member.PostAsync(AcceptUrl(second.Id()), new { }, ifMatch: 1)).DataAsync();

        Assert.Equal(membershipId, rejoined.Id("membership"));
        Assert.Equal(2, rejoined.GetProperty("membership").GetProperty("generation").GetInt32());
        var ledgers = await (await member.GetAsync($"/api/v1/organizations/{orgId}/ledgers", Ct)).DataAsync();
        Assert.Equal(newLedger, Assert.Single(ledgers.EnumerateArray()).Id());
    }

    [Fact]
    public async Task Retrying_an_acceptance_with_the_same_key_joins_once()
    {
        var (admin, orgId) = await OrgAsync();
        var subject = Ids.Subject();
        using var member = host.Client(subject);
        var invitation = await InvitationTests.InviteAsync(admin, orgId, Email(subject), "member");
        var key = Guid.NewGuid();

        var first = await (await member.PostAsync(AcceptUrl(invitation.Id()), new { }, key, ifMatch: 1)).DataAsync();
        var retry = await (await member.PostAsync(AcceptUrl(invitation.Id()), new { }, key, ifMatch: 1)).DataAsync();

        Assert.Equal(first.Id("membership"), retry.Id("membership"));
        await using var db = host.CreateDbContext();
        Assert.Equal(2, await db.Memberships.CountAsync(m => m.OrgId == orgId, Ct));
    }

    [Fact]
    public async Task Concurrent_accept_and_revoke_have_one_outcome()
    {
        var (admin, orgId) = await OrgAsync();
        var subject = Ids.Subject();
        using var member = host.Client(subject);
        var invitation = await InvitationTests.InviteAsync(admin, orgId, Email(subject), "member");

        var (accept, revoke) = (member.PostAsync(AcceptUrl(invitation.Id()), new { }, ifMatch: 1),
            admin.PostAsync($"/api/v1/organizations/{orgId}/invitations/{invitation.Id()}/revoke", new { }, ifMatch: 1));
        await Task.WhenAll(accept, revoke);

        // Whichever commits first wins; the other sees a newer version or a consumed invitation.
        Assert.True(accept.Result.IsSuccessStatusCode ^ revoke.Result.IsSuccessStatusCode);
        await using var db = host.CreateDbContext();
        var joined = await db.Memberships.CountAsync(m => m.OrgId == orgId, Ct) == 2;
        Assert.Equal(accept.Result.IsSuccessStatusCode, joined);
    }

    [Fact]
    public async Task Emails_match_ignoring_case_and_administrators_see_member_emails()
    {
        var (admin, orgId) = await OrgAsync();
        var subject = Ids.Subject("case");
        using var member = host.Client(subject, $"{subject}@example.test");
        var invitation = await InvitationTests.InviteAsync(admin, orgId, $"{subject.ToUpperInvariant()}@EXAMPLE.TEST", "member");

        await (await member.PostAsync(AcceptUrl(invitation.Id()), new { }, ifMatch: 1)).DataAsync();

        var members = await (await admin.GetAsync($"/api/v1/organizations/{orgId}/memberships", Ct)).DataAsync();
        Assert.Contains(members.EnumerateArray(), m => m.GetProperty("email").GetString() == $"{subject}@example.test");
    }

    private async Task<(HttpClient Admin, Guid OrgId)> OrgAsync()
    {
        var admin = host.Client(Ids.Subject("admin"));
        return (admin, await admin.CreateOrganizationAsync());
    }

    private static string Email(string subject) => $"{subject}@example.test";

    private static string AcceptUrl(Guid invitationId) => $"/api/v1/me/invitations/{invitationId}/accept";
}
