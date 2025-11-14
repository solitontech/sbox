namespace SBoxApp.Models;

/// <summary>
/// Represents how far a connection has progressed.
/// </summary>
public enum ConnectionState
{
    Offline = 0,
    Starting = 1,
    Connecting = 2,
    Online = 3,
    Error = 4
}
