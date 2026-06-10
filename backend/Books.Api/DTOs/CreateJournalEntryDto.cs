namespace Books.Api.DTOs;

public record CreateJournalEntryDto(
    string JournalNo,
    DateTime EntryDate,
    string? Description,
    List<CreateJournalLineDto> Lines);
