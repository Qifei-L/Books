using Books.Domain.Enums;

namespace Books.Application.DTOs;

public record CreateAccountDto(string Code, string Name, AccountType Type, string? Description, bool IsActive);
