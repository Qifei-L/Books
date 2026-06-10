namespace Books.Api.DTOs;

public record TrialBalanceRowDto(string AccountCode, string AccountName, decimal Debit, decimal Credit);
