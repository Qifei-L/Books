using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;

namespace Books.Tests.Reporting;

/// <summary>Branding is organization-wide and admin-only; report output defaults are per ledger and editable.</summary>
public sealed class ReportDeliverySettingsTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Branding_is_admin_only_and_ledger_output_defaults_round_trip()
    {
        var books = await JournalBooks.CreateAsync(host);
        var brandingUrl = $"/api/v1/organizations/{books.OrgId}/branding";
        var settingsUrl = $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/report-settings";

        var defaults = await (await books.Admin.GetAsync(settingsUrl, Ct)).DataAsync();
        Assert.Equal("en", Text(defaults, "language"));
        Assert.Equal("A4", Text(defaults, "paper_size"));

        var branding = await (await books.Admin.WriteAsync(HttpMethod.Put, brandingUrl, new
        {
            legal_name = "Acme Co., Ltd.", legal_name_km = "អាកមេ", vat_tin = "K001-1", prepared_by = "Dara",
        })).DataAsync(HttpStatusCode.Created);
        Assert.Equal("Acme Co., Ltd.", Text(branding, "legal_name"));
        var savedBranding = await (await books.Admin.GetAsync(brandingUrl, Ct)).DataAsync();
        Assert.Equal("អាកមេ", Text(savedBranding, "legal_name_km"));

        var settings = await (await books.Admin.WriteAsync(HttpMethod.Put, settingsUrl, new
        {
            language = "km", comparative = "none", show_zero_balances = false, show_account_codes = true,
            paper_size = "Letter", orientation = "landscape", watermark = "draft", pack_sections = "pl,bs,tb", share_days = 30,
        })).DataAsync(HttpStatusCode.Created);
        Assert.Equal("km", Text(settings, "language"));
        Assert.Equal("draft", Text(settings, "watermark"));

        using var reader = await books.AddMemberAsync("read");
        await (await reader.WriteAsync(HttpMethod.Put, brandingUrl, new { legal_name = "Nope" })).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await reader.WriteAsync(HttpMethod.Put, settingsUrl, new { language = "en" })).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    private static string Text(JsonElement element, string property) => element.GetProperty(property).GetString()!;
}
