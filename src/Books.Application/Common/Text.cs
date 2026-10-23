using System.Text.Json;

namespace Books.Application.Common;

public static class Text
{
    /// <summary>The API and database spelling of an enum value, for example LockedRecovery as locked_recovery.</summary>
    public static string Of<TEnum>(TEnum value) where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
}
