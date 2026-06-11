namespace Books.Application.DTOs;

public record CreateJournalLineDto(int AccountId, decimal Debit, decimal Credit, string? Description);
