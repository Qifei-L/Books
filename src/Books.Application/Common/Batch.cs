using Books.Domain.Access;

namespace Books.Application.Common;

/// <param name="Document">1-based position of the document in the batch.</param>
public sealed record BatchError(int Document, string Code, string Message);

/// <summary>
/// MCP batch writes succeed or fail as a whole (owner decision): every document is checked, and if any has an
/// input error the batch is rejected with all errors and nothing is saved. Business problems that keep a document
/// a draft are not errors.
/// </summary>
public static class Batch
{
    public const int MaxDocuments = 100;

    public static async Task<List<T>> RunAllAsync<TIn, T>(IReadOnlyList<TIn> items, Func<TIn, Task<T>> each)
    {
        if (items.Count is 0 or > MaxDocuments)
        {
            throw BooksProblem.Invalid($"A batch needs 1–{MaxDocuments} documents; split larger work into several batches.");
        }
        var results = new List<T>();
        var errors = new List<BatchError>();
        for (var i = 0; i < items.Count; i++)
        {
            try
            {
                results.Add(await each(items[i]));
            }
            catch (BooksProblem problem) when (problem.Status == 400)
            {
                errors.Add(new BatchError(i + 1, problem.Code, problem.Message));
            }
            catch (DomainException domain)
            {
                errors.Add(new BatchError(i + 1, "INVALID_ARGUMENT", domain.Message));
            }
        }
        return errors.Count == 0
            ? results
            : throw new BooksProblem(400, "BATCH_REJECTED", $"{errors.Count} of {items.Count} documents have errors; nothing was saved.") { Issues = errors };
    }
}
