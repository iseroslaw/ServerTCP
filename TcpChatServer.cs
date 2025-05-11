using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ServerTcp;

internal sealed class TcpChatServer(int port) : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Any, port);
    private readonly ConcurrentDictionary<string, TcpClient> _clients = new();
    private int _clientCounter = 1;
    private bool _disposed;

    public async Task StartAsync()
    {
        _listener.Start();
        Console.WriteLine($"Listening on port {((IPEndPoint)_listener.LocalEndpoint).Port}");

        while (true)
        {
            try
            {
                var tcpClient = await _listener.AcceptTcpClientAsync();
                var clientId = $"Client{_clientCounter++}";

                _clients.TryAdd(clientId, tcpClient);
                Console.WriteLine($"ClientId: {clientId} Connected. Total: {_clients.Count}");

                await BroadcastMessage(clientId, "has joined the chat");
                _ = HandleClientAsync(clientId, tcpClient);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"StartAsync Error: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(string clientId, TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var buffer = new byte[4096];

            try
            {
                while (client.Connected)
                {
                    var bytesRead = await stream.ReadAsync(buffer);
                    if (bytesRead == 0) break; // Client disconnected

                    var message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                    if (!string.IsNullOrEmpty(message))
                        await BroadcastMessage(clientId, message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ClientId {clientId} Error: {ex.Message}");
            }
            finally
            {
                DisconnectClient(clientId);
            }
        }
    }

    private void DisconnectClient(string clientId)
    {
        if (!_clients.TryRemove(clientId, out var client)) return;

        client.Dispose();
        Console.WriteLine($"ClientId: {clientId} Disconnected. Remaining: {_clients.Count}");
        _ = BroadcastMessage(clientId, "has left the chat");
    }

    private async Task BroadcastMessage(string senderId, string message)
    {
        var formatted = CompleteMessageFrom(senderId, message);
        var data = Encoding.UTF8.GetBytes(formatted);

        foreach (var (clientId, client) in MessageReceiversFrom(senderId))
        {
            await SendToClient(clientId, client, data);
        }
    }

    private IEnumerable<KeyValuePair<string, TcpClient>> MessageReceiversFrom(string senderId) =>
        _clients.Where((client) => client.Key != senderId);

    private static string CompleteMessageFrom(string senderId, string message) => $"{senderId}: {message}\r\n";

    private static async Task SendToClient(string clientId, TcpClient client, byte[] data)
    {
        if (!client.Connected)
        {
            Console.WriteLine($"ClientId {clientId} is not connected.");
            return;
        }

        try
        {
            var stream = client.GetStream();

            if (!stream.CanWrite)
            {
                Console.WriteLine($"Stream for ClientId {clientId} is not writable.");
                return;
            }

            await stream.WriteAsync(data);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SendToClient Failed for ClientId {clientId}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _listener.Stop();
            foreach (var client in _clients.Values)
            {
                client.Dispose();
            }
        }

        _disposed = true;
    }

    ~TcpChatServer()
    {
        Dispose(false);
    }
}