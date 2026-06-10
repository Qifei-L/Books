namespace Books.Api.DTOs;

public record CreateLedgerDto(string Name, bool IsActive = true);
