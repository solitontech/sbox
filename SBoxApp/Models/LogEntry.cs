using Microsoft.Extensions.Logging;

namespace SBoxApp.Models;

public record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message, string Source)
{
    public string FormattedTimestamp => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
}
