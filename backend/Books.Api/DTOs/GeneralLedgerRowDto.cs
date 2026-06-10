namespace Books.Api.DTOs;

public record GeneralLedgerRowDto(
    DateTime EntryDate,
    string JournalNo,
    string Description,
    decimal Debit,
    decimal Credit,
    decimal Balance);
