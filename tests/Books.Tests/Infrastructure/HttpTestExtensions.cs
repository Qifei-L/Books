using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Books.Tests.Infrastructure;

public static class HttpTestExtensions
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Sends a write with an Idempotency-Key (a new one unless given) and optional If-Match version.</summary>
    public static Task<HttpResponseMessage> WriteAsync(
        this HttpClient client, HttpMethod method, string url, object? body, Guid? key = null, long? ifMatch = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body ?? new { }) };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        if (ifMatch is { } version)
        {
            request.Headers.TryAddWithoutValidation("If-Match", $"\"v{version}\"");
        }
        return client.SendAsync(request, Ct);
    }

    public static Task<HttpResponseMessage> PostAsync(this HttpClient client, string url, object? body, Guid? key = null, long? ifMatch = null) =>
        client.WriteAsync(HttpMethod.Post, url, body, key, ifMatch);

    public static async Task<JsonElement> DataAsync(this HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("data").Clone();
    }

    public static async Task<string?> CodeAsync(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public static async Task AssertProblemAsync(this HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == status, $"Expected {status}, got {(int)response.StatusCode}: {body}");
        Assert.Equal(code, JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
    }

    public static Guid Id(this JsonElement element, string? property = null) =>
        (property is null ? element : element.GetProperty(property)).GetProperty("id").GetGuid();

    /// <summary>Creates an organization as the client's user and returns its id.</summary>
    public static async Task<Guid> CreateOrganizationAsync(this HttpClient client, string name = "Test Org")
    {
        var data = await (await client.PostAsync("/api/v1/organizations", new { name })).DataAsync(HttpStatusCode.Created);
        return data.Id("organization");
    }
}
