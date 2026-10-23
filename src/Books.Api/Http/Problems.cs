using Books.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Books.Api;

/// <summary>Writes application/problem+json with a stable <c>code</c>. Never includes SQL, stacks or tokens.</summary>
public static class Problems
{
    public static Task WriteAsync(HttpContext context, int status, string code, string detail)
    {
        var problem = new ProblemDetails
        {
            Type = $"https://books.test/problems/{code.ToLowerInvariant().Replace('_', '-')}",
            Title = code,
            Status = status,
            Detail = detail,
        };
        problem.Extensions["code"] = code;
        problem.Extensions["request_id"] = context.TraceIdentifier;
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
    }
}

public sealed class ProblemExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        switch (exception)
        {
            case BooksProblem problem:
                await Problems.WriteAsync(context, problem.Status, problem.Code, problem.Message);
                return true;
            case BadHttpRequestException:
                await Problems.WriteAsync(context, 400, "INVALID_ARGUMENT", "The request body or parameters are invalid.");
                return true;
            default:
                return false;
        }
    }
}
