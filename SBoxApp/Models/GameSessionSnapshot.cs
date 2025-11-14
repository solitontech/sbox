namespace SBoxApp.Models;

public record GameSessionSnapshot(
    string RoomName,
    string GameName,
    string EngineAddress,
    DateTimeOffset? JoinedAt);
