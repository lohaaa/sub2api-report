using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Sub2ApiReport.Infrastructure.Persistence;

internal sealed class UnixMillisecondsDateTimeOffsetConverter()
    : ValueConverter<DateTimeOffset, long>(
        value => value.ToUnixTimeMilliseconds(),
        value => DateTimeOffset.FromUnixTimeMilliseconds(value));

internal sealed class NullableUnixMillisecondsDateTimeOffsetConverter()
    : ValueConverter<DateTimeOffset?, long?>(
        value => value.HasValue ? value.Value.ToUnixTimeMilliseconds() : null,
        value => value.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value) : null);
