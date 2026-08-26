using Microsoft.Extensions.Logging;

namespace Sub2ApiReport.Migrator;

internal static partial class MigrationLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Applying database migrations")]
    public static partial void Applying(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Database migrations completed")]
    public static partial void Completed(ILogger logger);
}
