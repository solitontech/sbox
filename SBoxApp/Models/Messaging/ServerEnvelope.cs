using System.Text.Json.Serialization;

namespace SBoxApp.Models.Messaging;

public sealed record ServerEnvelope(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("respRequired")] bool ResponseRequired,
    [property: JsonPropertyName("payload")] string? Payload);

public sealed record JoinRoomPayload(
    string RoomName,
    string IP_Addr,
    [property: JsonPropertyName("Port#")] int PortNumber,
    string GameName)
{
    public string EngineAddress => $"{IP_Addr}:{PortNumber}";
}

public sealed record JoinRoomResponsePayload(
    [property: JsonPropertyName("RoomName")] string RoomName,
    [property: JsonPropertyName("Joined")] bool Joined,
    [property: JsonPropertyName("Comment")] string Comment);

public sealed record LeaveRoomResponsePayload(
    [property: JsonPropertyName("RoomName")] string RoomName,
    [property: JsonPropertyName("Left")] bool Left,
    [property: JsonPropertyName("Comment")] string Comment);

public sealed record RegistrationRequest(
    [property: JsonPropertyName("team_id")] string TeamId,
    [property: JsonPropertyName("api_key")] string ApiKey,
    [property: JsonPropertyName("player_email_id")] string PlayerEmail);
