using Books.Application.Common;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Books.Infrastructure.Persistence;

/// <summary>Stores an enum as snake_case text, for example LockedRecovery as locked_recovery.</summary>
public sealed class LowerCaseEnumConverter<TEnum>() : ValueConverter<TEnum, string>(
    value => Text.Of(value),
    text => Enum.Parse<TEnum>(text.Replace("_", ""), true))
    where TEnum : struct, Enum;
