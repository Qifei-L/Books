using System.Net;
using System.Net.Http.Headers;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;

namespace Books.Tests.Documents;

/// <summary>M17: files on journals and documents are uploaded, listed, downloaded and deleted under ledger access.</summary>
public sealed class AttachmentTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_journal_attachment_round_trips_and_read_access_may_download_but_not_upload()
    {
        var books = await JournalBooks.CreateAsync(host);
        var journal = (await (await books.SaveAsync(true, ("10.00", null), (null, "10.00"))).DataAsync(HttpStatusCode.Created)).Id();
        var bytes = "%PDF-1.4 attachment test"u8.ToArray();

        var uploaded = await (await UploadAsync(books.Admin, books, "journal", journal, "../../invoice.pdf", bytes)).DataAsync(HttpStatusCode.Created);
        var id = uploaded.Id();
        Assert.Equal("invoice.pdf", uploaded.GetProperty("file_name").GetString());
        Assert.Equal(64, uploaded.GetProperty("hash").GetString()!.Length);

        var listed = (await (await books.Admin.GetAsync(AttachmentsUrl(books, "journal", journal), Ct)).DataAsync()).EnumerateArray().ToList();
        Assert.Single(listed);
        Assert.Equal(id, listed[0].Id());

        var download = await books.Admin.GetAsync($"{AttachmentsUrl(books)}/{id}/download", Ct);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync(Ct));

        using var reader = await books.AddMemberAsync("read");
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"{AttachmentsUrl(books)}/{id}/download", Ct)).StatusCode);
        await (await UploadAsync(reader, books, "journal", journal, "again.pdf", bytes)).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await reader.WriteAsync(HttpMethod.Delete, $"{AttachmentsUrl(books)}/{id}", null)).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Deleting_an_attachment_removes_the_file_and_the_row()
    {
        var books = await JournalBooks.CreateAsync(host);
        var journal = (await (await books.SaveAsync(true, ("10.00", null), (null, "10.00"))).DataAsync(HttpStatusCode.Created)).Id();
        var uploaded = await (await UploadAsync(books.Admin, books, "journal", journal, "note.txt", "hello"u8.ToArray())).DataAsync(HttpStatusCode.Created);

        await (await books.Admin.WriteAsync(HttpMethod.Delete, $"{AttachmentsUrl(books)}/{uploaded.Id()}", null)).DataAsync();

        Assert.Empty((await (await books.Admin.GetAsync(AttachmentsUrl(books, "journal", journal), Ct)).DataAsync()).EnumerateArray());
        await (await books.Admin.GetAsync($"{AttachmentsUrl(books)}/{uploaded.Id()}/download", Ct))
            .AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Dangerous_file_types_and_empty_files_are_rejected()
    {
        var books = await JournalBooks.CreateAsync(host);
        var journal = (await (await books.SaveAsync(true, ("10.00", null), (null, "10.00"))).DataAsync(HttpStatusCode.Created)).Id();

        await (await UploadAsync(books.Admin, books, "journal", journal, "script.html", "<script>"u8.ToArray()))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await (await UploadAsync(books.Admin, books, "journal", journal, "empty.pdf", []))
            .AssertProblemAsync(HttpStatusCode.RequestEntityTooLarge, "FILE_TOO_LARGE");
    }

    [Fact]
    public async Task A_record_from_another_ledger_cannot_take_the_file()
    {
        var books = await JournalBooks.CreateAsync(host);
        var other = await JournalBooks.CreateAsync(host);
        var journal = (await (await books.SaveAsync(true, ("10.00", null), (null, "10.00"))).DataAsync(HttpStatusCode.Created)).Id();

        await (await UploadAsync(other.Admin, other, "journal", journal, "invoice.pdf", "%PDF-1.4"u8.ToArray()))
            .AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    private static string AttachmentsUrl(JournalBooks books) => $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/attachments";

    private static string AttachmentsUrl(JournalBooks books, string kind, Guid id) => $"{AttachmentsUrl(books)}?recordKind={kind}&recordId={id}";

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, JournalBooks books, string kind, Guid recordId, string name, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(kind), "record_kind");
        form.Add(new StringContent(recordId.ToString()), "record_id");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", name);
        using var request = new HttpRequestMessage(HttpMethod.Post, AttachmentsUrl(books)) { Content = form };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await client.SendAsync(request, Ct);
    }
}
