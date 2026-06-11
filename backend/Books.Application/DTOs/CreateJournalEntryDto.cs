namespace Books.Application.DTOs;

public record CreateJournalEntryDto(
    string JournalNo,
    DateTime EntryDate,
    string? Description,
    List<CreateJournalLineDto> Lines);
