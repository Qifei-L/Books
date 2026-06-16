namespace Books.Application.DTOs;

public record CreateEntityDto(string Code, string Name, bool IsActive = true);
