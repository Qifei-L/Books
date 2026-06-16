namespace Books.Application.DTOs;

public record ReportSummaryDto(
    string Code,
    string Name,
    string Description,
    string Path);
