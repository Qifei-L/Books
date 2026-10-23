using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Books.Application.Common;

namespace Books.Api.Http;

/// <summary>Turns HTTP write headers into <see cref="WriteMeta"/> and a committed result back into a response.</summary>
public static class WriteHttp
{
    /// <summary>
    /// Reads Idempotency-Key (required UUID) and If-Match ("v3" or "3"). The fingerprint covers the
    /// route, the parsed body and If-Match, so the same key with different content is detected.
    /// </summary>
    public static WriteMeta Meta(HttpContext http, object? body)
    {
        if (!Guid.TryParse(http.Request.Headers["Idempotency-Key"].ToString(), out var key))
        {
            throw BooksProblem.Invalid("The Idempotency-Key header must be a UUID.");
        }

        long? expected = null;
        var ifMatch = http.Request.Headers.IfMatch.ToString().Trim().Trim('"');
        if (ifMatch.Length > 0)
        {
            expected = long.TryParse(ifMatch.TrimStart('v'), out var v) && v > 0
                ? v
                : throw BooksProblem.Invalid("If-Match must be a version such as \"v3\".");
        }

        var fingerprint = JsonSerializer.Serialize(new { method = http.Request.Method, path = http.Request.Path.Value, body, expected }, BooksJson.Options);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
        return new WriteMeta(key, hash, expected, http.TraceIdentifier);
    }

    public static IResult Result(HttpContext http, WriteResult result)
    {
        if (result.Version is { } version)
        {
            http.Response.Headers.ETag = $"\"v{version}\"";
        }
        var json = $"{{\"data\":{result.Json},\"request_id\":{JsonSerializer.Serialize(http.TraceIdentifier)}}}";
        return Results.Content(json, "application/json", Encoding.UTF8, result.Status);
    }
}
