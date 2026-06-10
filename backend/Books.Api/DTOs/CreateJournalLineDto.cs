namespace Books.Api.DTOs;

public record CreateJournalLineDto(int AccountId, decimal Debit, decimal Credit, string? Description);
