using Books.Domain.Enums;

namespace Books.Application.DTOs;

public record CreateLedgerDto(
    int EntityId,
    string Code,
    string Name,
    LedgerType LedgerType,
    bool IsActive = true,
    bool AllowDeletePostedJournal = true);
