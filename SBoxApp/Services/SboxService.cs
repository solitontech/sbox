using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Fleck;
using Microsoft.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using SBoxApp.Models;
using SBoxApp.Models.Messaging;

namespace SBoxApp.Services;

/// <summary>
/// Hosts the bot gateway and coordinates connections with the SPL server and game engines.
/// </summary>
public sealed class SboxService : IAsyncDisposable
{
    private readonly SboxConfigurationStore _configurationStore;
    private readonly SboxStateStore _stateStore;
    private readonly ILogger<SboxService> _logger;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    private CancellationTokenSource? _runCts;
    private WebSocketServer? _botGateway;
    private IWebSocketConnection? _botConnection;
    private ClientWebSocket? _serverSocket;
    private ClientWebSocket? _gameEngineSocket;
    private Task? _serverListenerTask;
    private Task? _engineListenerTask;
    private SboxConfiguration? _activeConfiguration;

    public SboxService(SboxConfigurationStore configurationStore, SboxStateStore stateStore, ILogger<SboxService> logger)
    {
        _configurationStore = configurationStore;
        _stateStore = stateStore;
        _logger = logger;
    }

    public bool IsRunning => _runCts != null;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (_runCts != null)
            {
                _logger.LogInformation("SBOX runtime already active.");
                return;
            }

            _activeConfiguration = _configurationStore.GetSnapshot();
            _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _stateStore.SetIsRunning(true);
            _stateStore.UpdateTeamProfile(_activeConfiguration.TeamId, _activeConfiguration.PlayerEmail);
            _stateStore.UpdateBotState(ConnectionState.Starting, $"Listening on {_activeConfiguration.BotHost}:{_activeConfiguration.BotPort}");
            _stateStore.UpdateServerState(ConnectionState.Offline, "Waiting for bot before registering");
            _stateStore.UpdateEngineState(ConnectionState.Offline, "Not connected");
            _stateStore.AddLog(LogLevel.Information, "Runtime", "SBOX starting");

            await StartBotGatewayAsync(_activeConfiguration, _runCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SBOX failed to start");
            _stateStore.AddLog(LogLevel.Error, "Runtime", $"Failed to start: {ex.Message}");
            await StopAsync();
            await ResetStateAsync();
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private Task ResetStateAsync()
    {
        _stateStore.SetIsRunning(false);
        _stateStore.UpdateTeamProfile(string.Empty, string.Empty);
        _stateStore.UpdateBotState(ConnectionState.Offline, "Idle");
        _stateStore.UpdateServerState(ConnectionState.Offline, "Idle");
        _stateStore.UpdateEngineState(ConnectionState.Offline, "Idle");
        _stateStore.UpdateSession(new GameSessionSnapshot("--", "--", "--", null));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (_runCts == null)
            {
                return;
            }

            _runCts.Cancel();
            await StopGameEngineAsync("Runtime stopped", cancellationToken);
            await StopServerAsync(cancellationToken);
            await StopBotConnectionAsync();
            await StopBotGatewayAsync();
            _runCts.Dispose();
            _runCts = null;
            _activeConfiguration = null;
            _stateStore.SetIsRunning(false);
            _stateStore.UpdateTeamProfile(string.Empty, string.Empty);
            _stateStore.UpdateBotState(ConnectionState.Offline, "Stopped");
            _stateStore.UpdateServerState(ConnectionState.Offline, "Stopped");
            _stateStore.UpdateEngineState(ConnectionState.Offline, "Stopped");
            _stateStore.UpdateSession(new GameSessionSnapshot("--", "--", "--", null));
            _stateStore.AddLog(LogLevel.Information, "Runtime", "SBOX stopped");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken);
        await StartAsync(cancellationToken);
    }

    private Task StartBotGatewayAsync(SboxConfiguration configuration, CancellationToken token)
    {
        EnsurePortAvailable(configuration.BotHost, configuration.BotPort);

        var endpoint = $"ws://{configuration.BotHost}:{configuration.BotPort}";
        var server = new WebSocketServer(endpoint)
        {
            RestartAfterListenError = true
        };

        server.Start(connection =>
        {
            connection.OnOpen = () => OnBotConnected(connection);
            connection.OnClose = () => _ = HandleBotDisconnectedAsync();
            connection.OnError = ex => _stateStore.AddLog(LogLevel.Error, "Bot", $"Gateway error: {ex.Message}");
            connection.OnMessage = message =>
            {
                var payload = Encoding.UTF8.GetBytes(message);
                _ = HandleBotMessageAsync(payload, WebSocketMessageType.Text);
            };
            connection.OnBinary = payload =>
            {
                var clone = new byte[payload.Length];
                Buffer.BlockCopy(payload, 0, clone, 0, payload.Length);
                _ = HandleBotMessageAsync(clone, WebSocketMessageType.Binary);
            };
        });

        _botGateway = server;
        _stateStore.AddLog(LogLevel.Information, "Runtime", $"Bot gateway listening on {endpoint}");
        return Task.CompletedTask;
    }

    private void EnsurePortAvailable(string host, int port)
    {
        TcpListener? listener = null;
        try
        {
            var address = string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                ? IPAddress.Loopback
                : IPAddress.Parse(host);

            listener = new TcpListener(address, port);
            listener.Start();
        }
        catch (Exception ex)
        {
            var message = $"Unable to bind to {host}:{port} - {ex.Message}";
            _logger.LogError(ex, "Bot gateway port unavailable: {Host}:{Port}", host, port);
            _stateStore.AddLog(LogLevel.Error, "Runtime", message);
            throw new InvalidOperationException(message, ex);
        }
        finally
        {
            listener?.Stop();
        }
    }

    private void OnBotConnected(IWebSocketConnection connection)
    {
        if (_botConnection != null)
        {
            try
            {
                _botConnection.Close();
            }
            catch
            {
                // ignored
            }
        }

        _botConnection = connection;
        _stateStore.UpdateBotState(ConnectionState.Online, "Bot connected");
        _stateStore.AddLog(LogLevel.Information, "Bot", "Bot connected");

        var token = _runCts?.Token ?? CancellationToken.None;
        _ = EnsureServerConnectionAsync(token);
    }

    private Task HandleBotMessageAsync(byte[] payload, WebSocketMessageType messageType)
    {
        var token = _runCts?.Token ?? CancellationToken.None;
        return ForwardToEngineAsync(payload, payload.Length, true, messageType, token);
    }

    private async Task HandleBotDisconnectedAsync()
    {
        _botConnection = null;
        _stateStore.UpdateBotState(ConnectionState.Offline, "Bot disconnected");
        _stateStore.AddLog(LogLevel.Warning, "Bot", "Bot disconnected");
        await StopGameEngineAsync("Bot offline", CancellationToken.None, notifyServer: false);
        await StopServerAsync(CancellationToken.None);
    }

    private Task StopBotGatewayAsync()
    {
        if (_botGateway == null)
        {
            return Task.CompletedTask;
        }

        try
        {
            (_botGateway as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bot gateway stop failed");
        }
        finally
        {
            _botGateway = null;
        }

        _stateStore.AddLog(LogLevel.Information, "Runtime", "Bot gateway stopped");
        return Task.CompletedTask;
    }

    private async Task EnsureServerConnectionAsync(CancellationToken token)
    {
        if (_activeConfiguration == null)
        {
            return;
        }

        if (_serverSocket is { State: WebSocketState.Open })
        {
            return;
        }

        _serverSocket?.Dispose();
        var client = new ClientWebSocket();
        var uri = BuildUri(_activeConfiguration.ServerHost, _activeConfiguration.ServerPort, secure: false);
        _stateStore.UpdateServerState(ConnectionState.Connecting, $"Connecting to {uri}");

        try
        {
            await client.ConnectAsync(uri, token);
            _stateStore.UpdateServerState(ConnectionState.Online, $"Connected to {uri.Host}:{uri.Port}");
            _stateStore.AddLog(LogLevel.Information, "Server", $"Connected to {uri}");
            _serverSocket = client;
            await SendRegistrationAsync(client, token);
            _serverListenerTask = Task.Run(() => ListenToServerAsync(client, token), token);
        }
        catch (Exception ex)
        {
            _stateStore.UpdateServerState(ConnectionState.Error, $"Failed to connect: {ex.Message}");
            _stateStore.AddLog(LogLevel.Error, "Server", $"Failed to connect: {ex.Message}");
            client.Dispose();
        }
    }

    private async Task SendRegistrationAsync(ClientWebSocket socket, CancellationToken token)
    {
        if (_activeConfiguration == null)
        {
            return;
        }

        var registration = new RegistrationRequest(
            _activeConfiguration.TeamId,
            _activeConfiguration.ApiKey,
            _activeConfiguration.PlayerEmail);

        await SendJsonAsync(socket, registration, token);
        _stateStore.AddLog(LogLevel.Information, "Server", "Registration request sent");
    }

    private async Task ListenToServerAsync(ClientWebSocket socket, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var payload = await ReceiveStringAsync(socket, token);
                if (payload == null)
                {
                    break;
                }

                await HandleServerMessageAsync(payload, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _stateStore.AddLog(LogLevel.Error, "Server", $"Server listener faulted: {ex.Message}");
            _logger.LogError(ex, "Server listener faulted");
        }
        finally
        {
            _stateStore.UpdateServerState(ConnectionState.Offline, "Disconnected");
            _serverSocket?.Dispose();
            _serverSocket = null;
        }
    }

    private async Task HandleServerMessageAsync(string payload, CancellationToken token)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (root.TryGetProperty("request", out var requestElement))
        {
            var value = requestElement.GetString();
            var success = string.Equals(value, "success", StringComparison.OrdinalIgnoreCase);
            _stateStore.AddLog(LogLevel.Information, "Server", $"Registration {(success ? "accepted" : "rejected")}");
            _stateStore.UpdateServerState(success ? ConnectionState.Online : ConnectionState.Error, success ? "Registered" : "Registration denied");
            return;
        }

        var envelope = root.Deserialize<ServerEnvelope>(_serializerOptions);
        if (envelope?.Type is null)
        {
            _stateStore.AddLog(LogLevel.Warning, "Server", $"Unknown server message: {payload}");
            return;
        }

        switch (envelope.Type)
        {
            case "join-room":
                await HandleJoinRoomAsync(envelope, token);
                break;
            case "leave-room":
                await StopGameEngineAsync("Server requested leave", token);
                break;
            default:
                _stateStore.AddLog(LogLevel.Warning, "Server", $"Unsupported message type: {envelope.Type}");
                break;
        }
    }

    private async Task HandleJoinRoomAsync(ServerEnvelope envelope, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(envelope.Payload))
        {
            _stateStore.AddLog(LogLevel.Error, "Server", "Join payload missing.");
            return;
        }

        JoinRoomPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<JoinRoomPayload>(envelope.Payload, _serializerOptions);
        }
        catch (Exception ex)
        {
            _stateStore.AddLog(LogLevel.Error, "Server", $"Failed to parse join payload: {ex.Message}");
            return;
        }

        if (payload == null)
        {
            _stateStore.AddLog(LogLevel.Error, "Server", "Join payload empty.");
            return;
        }

        var success = await StartGameEngineAsync(payload, token);
        if (envelope.ResponseRequired)
        {
            await SendJoinRoomResponseAsync(payload, success, success ? string.Empty : "Connection refused", token);
        }
    }

    private async Task<bool> StartGameEngineAsync(JoinRoomPayload payload, CancellationToken token)
    {
        if (_activeConfiguration == null)
        {
            return false;
        }

        await StopGameEngineAsync("Switching engine", token, notifyServer: false);

        var client = new ClientWebSocket();
        var uri = BuildUri(payload.IP_Addr, payload.PortNumber, secure: false);
        _stateStore.UpdateEngineState(ConnectionState.Connecting, $"Connecting to {payload.EngineAddress}");
        _stateStore.UpdateSession(new GameSessionSnapshot(payload.RoomName, payload.GameName, payload.EngineAddress, null));

        try
        {
            await client.ConnectAsync(uri, token);
        }
        catch (Exception ex)
        {
            _stateStore.UpdateEngineState(ConnectionState.Error, $"Failed to connect: {ex.Message}");
            _stateStore.AddLog(LogLevel.Error, "Engine", $"Failed to connect: {ex.Message}");
            client.Dispose();
            return false;
        }

        var registration = new RegistrationRequest(
            _activeConfiguration.TeamId,
            _activeConfiguration.ApiKey,
            _activeConfiguration.PlayerEmail);

        await SendJsonAsync(client, registration, token);
        var response = await ReceiveStringAsync(client, token);
        var registered = response != null && IsSuccessResponse(response);

        if (!registered)
        {
            _stateStore.UpdateEngineState(ConnectionState.Error, "Registration failed");
            _stateStore.AddLog(LogLevel.Error, "Engine", "Registration failed");
            await CloseSocketQuietly(client);
            return false;
        }

        _stateStore.UpdateEngineState(ConnectionState.Online, $"Joined {payload.RoomName}");
        _stateStore.UpdateSession(new GameSessionSnapshot(payload.RoomName, payload.GameName, payload.EngineAddress, DateTimeOffset.UtcNow));
        _stateStore.AddLog(LogLevel.Information, "Engine", $"Joined {payload.RoomName}");

        _gameEngineSocket = client;
        _engineListenerTask = Task.Run(() => ListenToGameEngineAsync(client, token), token);
        return true;
    }

    private async Task ListenToGameEngineAsync(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = new byte[8192];
        try
        {
            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                await ForwardToBotAsync(buffer, result.Count, result.EndOfMessage, result.MessageType);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _stateStore.AddLog(LogLevel.Error, "Engine", $"Listener error: {ex.Message}");
            _logger.LogError(ex, "Engine listener faulted");
        }
        finally
        {
            _stateStore.UpdateEngineState(ConnectionState.Offline, "Disconnected");
            await StopGameEngineAsync("Engine disconnected", CancellationToken.None, notifyServer: false);
        }
    }

    private Task ForwardToBotAsync(byte[] buffer, int count, bool endOfMessage, WebSocketMessageType messageType)
    {
        var connection = _botConnection;
        if (connection is null)
        {
            _stateStore.AddLog(LogLevel.Warning, "Engine", "Bot offline, dropping engine packet");
            return Task.CompletedTask;
        }

        try
        {
            if (messageType == WebSocketMessageType.Binary)
            {
                var payload = new byte[count];
                Buffer.BlockCopy(buffer, 0, payload, 0, count);
                connection.Send(payload);
            }
            else
            {
                var text = Encoding.UTF8.GetString(buffer, 0, count);
                connection.Send(text);
            }
        }
        catch (Exception ex)
        {
            _stateStore.AddLog(LogLevel.Error, "Bot", $"Failed to send to bot: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private async Task ForwardToEngineAsync(byte[] buffer, int count, bool endOfMessage, WebSocketMessageType messageType, CancellationToken token)
    {
        var socket = _gameEngineSocket;
        if (socket is null || socket.State != WebSocketState.Open)
        {
            _stateStore.AddLog(LogLevel.Warning, "Bot", "Engine offline, dropping bot packet");
            return;
        }

        await socket.SendAsync(new ArraySegment<byte>(buffer, 0, count), messageType, endOfMessage, token);
    }

    private Task StopBotConnectionAsync()
    {
        var connection = _botConnection;
        if (connection == null)
        {
            return Task.CompletedTask;
        }

        try
        {
            connection.Close();
        }
        catch
        {
        }
        finally
        {
            _botConnection = null;
        }

        return Task.CompletedTask;
    }

    private async Task StopServerAsync(CancellationToken cancellationToken)
    {
        if (_serverSocket == null)
        {
            return;
        }

        try
        {
            if (_serverSocket.State == WebSocketState.Open)
            {
                await _serverSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutdown", cancellationToken);
            }
        }
        catch
        {
        }
        finally
        {
            _serverSocket.Dispose();
            _serverSocket = null;
            _stateStore.UpdateServerState(ConnectionState.Offline, "Disconnected");
            _stateStore.AddLog(LogLevel.Information, "Server", "Server connection closed");
        }
    }

    private async Task StopGameEngineAsync(string reason, CancellationToken cancellationToken, bool notifyServer = true)
    {
        if (_gameEngineSocket == null)
        {
            _stateStore.UpdateEngineState(ConnectionState.Offline, reason);
            _stateStore.UpdateSession(new GameSessionSnapshot("--", "--", "--", null));
            if (!string.IsNullOrWhiteSpace(reason))
            {
                _stateStore.AddLog(LogLevel.Information, "Engine", reason);
            }
            if (notifyServer)
            {
                _ = SendLeaveAckAsync(reason);
            }
            return;
        }

        try
        {
            if (_gameEngineSocket.State == WebSocketState.Open)
            {
                await _gameEngineSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, cancellationToken);
            }
        }
        catch
        {
        }
        finally
        {
            _gameEngineSocket.Dispose();
            _gameEngineSocket = null;
            _stateStore.UpdateEngineState(ConnectionState.Offline, reason);
            _stateStore.UpdateSession(new GameSessionSnapshot("--", "--", "--", null));
            if (!string.IsNullOrWhiteSpace(reason))
            {
                _stateStore.AddLog(LogLevel.Information, "Engine", reason);
            }
            if (notifyServer)
            {
                _ = SendLeaveAckAsync(reason);
            }
        }
    }

    private async Task SendJoinRoomResponseAsync(JoinRoomPayload payload, bool success, string comment, CancellationToken token)
    {
        if (_serverSocket == null)
        {
            return;
        }

        var envelope = new ServerEnvelope(
            "join-room-resp",
            false,
            JsonSerializer.Serialize(new JoinRoomResponsePayload(payload.RoomName, success, comment), _serializerOptions));

        await SendJsonAsync(_serverSocket, envelope, token);
        _stateStore.AddLog(LogLevel.Information, "Server", $"Join response sent ({(success ? "success" : "failure")})");
    }

    private Task SendLeaveAckAsync(string reason)
    {
        var socket = _serverSocket;
        if (socket == null)
        {
            return Task.CompletedTask;
        }

        var envelope = new ServerEnvelope(
            "leave-room-resp",
            false,
            JsonSerializer.Serialize(new { Comment = reason }, _serializerOptions));

        return SendJsonAsync(socket, envelope, CancellationToken.None);
    }

    private async Task SendJsonAsync(WebSocket socket, object payload, CancellationToken token)
    {
        var json = JsonSerializer.Serialize(payload, _serializerOptions);
        var buffer = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(buffer, WebSocketMessageType.Text, true, token);
    }

    private static async Task<string?> ReceiveStringAsync(WebSocket socket, CancellationToken token)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();

        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            stream.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool IsSuccessResponse(string payload)
    {
        try
        {
            using var json = JsonDocument.Parse(payload);
            if (json.RootElement.TryGetProperty("request", out var request))
            {
                return string.Equals(request.GetString(), "success", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
        }

        return false;
    }

    private static Uri BuildUri(string host, int port, bool secure)
    {
        var builder = new UriBuilder
        {
            Scheme = secure ? "wss" : "ws",
            Host = host,
            Port = port
        };
        return builder.Uri;
    }

    private static async Task CloseSocketQuietly(WebSocket socket)
    {
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(WebSocketCloseStatus.InternalServerError, "Dispose", CancellationToken.None);
            }
        }
        catch
        {
        }
        finally
        {
            socket.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopBotConnectionAsync();
        await StopAsync();
        _lifecycleGate.Dispose();
    }
}
