namespace Books.Application.DTOs;

public record GeneralLedgerRowDto(
    DateTime EntryDate,
    string JournalNo,
    string Description,
    decimal Debit,
    decimal Credit,
    decimal Balance);
