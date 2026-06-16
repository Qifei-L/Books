namespace Books.Application.DTOs;

public record ReportExportResult(
    byte[] Content,
    string FileName,
    string ContentType);
