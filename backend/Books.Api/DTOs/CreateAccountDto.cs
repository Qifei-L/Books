using Books.Api.Models;

namespace Books.Api.DTOs;

public record CreateAccountDto(string Code, string Name, AccountType Type, string? Description, bool IsActive);
