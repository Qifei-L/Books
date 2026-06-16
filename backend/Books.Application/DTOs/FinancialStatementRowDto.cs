using Books.Domain.Enums;

namespace Books.Application.DTOs;

public record FinancialStatementRowDto(
    string AccountCode,
    string AccountName,
    AccountType AccountType,
    decimal Debit,
    decimal Credit,
    decimal Balance);
