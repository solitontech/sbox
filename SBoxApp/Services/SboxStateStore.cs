using Microsoft.Extensions.Logging;
using SBoxApp.Models;

namespace SBoxApp.Services;

/// <summary>
/// Thread-safe state container shared with Blazor components.
/// </summary>
public class SboxStateStore
{
    private readonly object _gate = new();

    private bool _isRunning;
    private ConnectionState _botConnection = ConnectionState.Offline;
    private ConnectionState _serverConnection = ConnectionState.Offline;
    private ConnectionState _engineConnection = ConnectionState.Offline;
    private string _botDetail = "Bot not connected";
    private string _serverDetail = "Server not connected";
    private string _engineDetail = "Waiting for instructions";
    private string _teamName = string.Empty;
    private string _teamId = string.Empty;
    private GameSessionSnapshot _session = new("--", "--", "--", null);
    private readonly LinkedList<LogEntry> _logs = new();
    private const int MaxLogEntries = 500;

    public event Action? StateChanged;

    public SboxStateSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new SboxStateSnapshot(
                    _isRunning,
                    _botConnection,
                    _serverConnection,
                    _engineConnection,
                    _botDetail,
                    _serverDetail,
                    _engineDetail,
                    _teamName,
                    _teamId,
                    _session,
                    _logs.ToList());
            }
        }
    }

    public void SetIsRunning(bool isRunning)
    {
        lock (_gate)
        {
            _isRunning = isRunning;
        }
        Notify();
    }

    public void UpdateBotState(ConnectionState state, string detail)
    {
        lock (_gate)
        {
            _botConnection = state;
            _botDetail = detail;
        }
        Notify();
    }

    public void UpdateServerState(ConnectionState state, string detail)
    {
        lock (_gate)
        {
            _serverConnection = state;
            _serverDetail = detail;
        }
        Notify();
    }

    public void UpdateEngineState(ConnectionState state, string detail)
    {
        lock (_gate)
        {
            _engineConnection = state;
            _engineDetail = detail;
        }
        Notify();
    }

    public void UpdateTeamProfile(string teamId, string teamName)
    {
        lock (_gate)
        {
            _teamId = teamId;
            _teamName = teamName;
        }
        Notify();
    }

    public void UpdateSession(GameSessionSnapshot snapshot)
    {
        lock (_gate)
        {
            _session = snapshot;
        }
        Notify();
    }

    public void AddLog(LogLevel level, string source, string message)
    {
        lock (_gate)
        {
            _logs.AddLast(new LogEntry(DateTimeOffset.UtcNow, level, message, source));
            while (_logs.Count > MaxLogEntries)
            {
                _logs.RemoveFirst();
            }
        }
        Notify();
    }

    public void ClearLogs()
    {
        lock (_gate)
        {
            _logs.Clear();
        }
        Notify();
    }

    private void Notify() => StateChanged?.Invoke();
}
