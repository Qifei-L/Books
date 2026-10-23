namespace Books.Application.Common;

/// <summary>
/// An expected failure with a stable code. Detail must never contain secrets
/// or another organization's data.
/// </summary>
public sealed class BooksProblem(int status, string code, string detail) : Exception(detail)
{
    public int Status { get; } = status;
    public string Code { get; } = code;

    /// <summary>Optional structured details, for example validation issues of a document that was not saved.</summary>
    public object? Issues { get; init; }

    public static BooksProblem Forbidden(string detail = "You do not have access to this operation.") => new(403, "FORBIDDEN", detail);
    public static BooksProblem EmailNotVerified() => new(403, "EMAIL_NOT_VERIFIED", "A verified email is required.");
    public static BooksProblem NotFound() => new(404, "NOT_FOUND", "The resource was not found.");
    public static BooksProblem Invalid(string detail) => new(400, "INVALID_ARGUMENT", detail);
    public static BooksProblem Conflict(string code, string detail) => new(409, code, detail);
}
