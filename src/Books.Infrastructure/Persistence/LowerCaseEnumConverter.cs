using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Books.Infrastructure.Persistence;

/// <summary>Stores an enum as snake_case text, for example LockedRecovery as locked_recovery.</summary>
public sealed partial class LowerCaseEnumConverter<TEnum>() : ValueConverter<TEnum, string>(
    value => ToText(value),
    text => Enum.Parse<TEnum>(text.Replace("_", ""), true))
    where TEnum : struct, Enum
{
    public static string ToText(TEnum value) => Words().Replace(value.ToString(), "$1_$2").ToLowerInvariant();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex Words();
}
