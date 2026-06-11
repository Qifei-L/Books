namespace Books.Application.DTOs;

public record CreateLedgerDto(string Name, bool IsActive = true);
