using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ServerTcp;

internal sealed class TcpChatServer(int port) : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Any, port);
    private readonly ConcurrentDictionary<ClientId, TcpClient> _clients = new();
    private int _clientCounter = 0;
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
                var clientId = NextClientId();

                _clients.TryAdd(clientId, tcpClient);
                Console.WriteLine($"ClientId: {clientId.Value} Connected. Total: {_clients.Count}");

                await BroadcastMessageFrom(clientId, "has joined the chat");
                _ = HandleClientAsync(clientId, tcpClient);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"StartAsync Error: {ex.Message}");
            }
        }
    }

    private ClientId NextClientId() => new($"Client{++_clientCounter}");

    private async Task HandleClientAsync(ClientId clientId, TcpClient client)
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
                        await BroadcastMessageFrom(clientId, message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ClientId {clientId.Value} Error: {ex.Message}");
            }
            finally
            {
                DisconnectClient(clientId);
            }
        }
    }

    private void DisconnectClient(ClientId clientId)
    {
        if (!_clients.TryRemove(clientId, out var client)) return;

        client.Dispose();
        Console.WriteLine($"ClientId: {clientId.Value} Disconnected. Remaining: {_clients.Count}");
        _ = BroadcastMessageFrom(clientId, "has left the chat");
    }

    private async Task BroadcastMessageFrom(ClientId senderId, string message)
    {
        var formatted = Utils.CompleteMessageFrom(senderId, message);
        var data = Encoding.UTF8.GetBytes(formatted);

        await Task.WhenAll(_clients.MessageReceiversFrom(senderId)
            .Select(client => SendToClient(client.Key, client.Value, data)));
    }

    private static async Task SendToClient(ClientId clientId, TcpClient client, byte[] data)
    {
        if (!client.Connected)
        {
            Console.WriteLine($"ClientId {clientId.Value} is not connected.");
            return;
        }

        try
        {
            var stream = client.GetStream();

            if (!stream.CanWrite)
            {
                Console.WriteLine($"Stream for ClientId {clientId.Value} is not writable.");
                return;
            }

            await stream.WriteAsync(data);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SendToClient Failed for ClientId {clientId.Value}: {ex.Message}");
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