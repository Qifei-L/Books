using System.Data.Common;
using Books.Application.Common;
using Books.Domain.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Books.Api;

/// <summary>Writes application/problem+json with a stable <c>code</c>. Never includes SQL, stacks or tokens.</summary>
public static class Problems
{
    public static Task WriteAsync(HttpContext context, int status, string code, string detail, object? issues = null)
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
        if (issues is not null)
        {
            problem.Extensions["issues"] = issues;
        }
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
                await Problems.WriteAsync(context, problem.Status, problem.Code, problem.Message, problem.Issues);
                return true;
            case DomainException domain:
                await Problems.WriteAsync(context, 400, "INVALID_ARGUMENT", domain.Message);
                return true;
            case DbException { SqlState: "23505" }:
                await Problems.WriteAsync(context, 409, "CONFLICT", "The change conflicts with existing data.");
                return true;
            case DbUpdateException { InnerException: DbException { SqlState: "23505" } }:
                await Problems.WriteAsync(context, 409, "CONFLICT", "The change conflicts with existing data.");
                return true;
            case BadHttpRequestException:
                await Problems.WriteAsync(context, 400, "INVALID_ARGUMENT", "The request body or parameters are invalid.");
                return true;
            default:
                return false;
        }
    }
}
