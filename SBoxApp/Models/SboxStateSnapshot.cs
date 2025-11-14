namespace SBoxApp.Models;

public record SboxStateSnapshot(
    bool IsRunning,
    ConnectionState BotConnection,
    ConnectionState ServerConnection,
    ConnectionState GameEngineConnection,
    string BotDetail,
    string ServerDetail,
    string GameEngineDetail,
    string TeamName,
    string TeamId,
    GameSessionSnapshot Session,
    IReadOnlyList<LogEntry> Logs)
{
    public static SboxStateSnapshot Empty { get; } = new(
        false,
        ConnectionState.Offline,
        ConnectionState.Offline,
        ConnectionState.Offline,
        "Bot not connected",
        "Server not connected",
        "Not joined",
        string.Empty,
        string.Empty,
        new GameSessionSnapshot("--", "--", "--", null),
        Array.Empty<LogEntry>());
}
