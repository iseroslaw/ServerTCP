using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;

namespace ServerTcp;

public class TcpChatServer(int port) : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Any, port);
    private readonly ConcurrentDictionary<ClientId, TcpClient> _clients = new();
    private readonly Subject<(ClientId SenderId, string Message)> _messageStream = new();
    private int _clientCounter;
    private bool _disposed;

    public async Task StartAsync()
    {
        _listener.Start();
        Console.WriteLine($"Listening on port {((IPEndPoint)_listener.LocalEndpoint).Port}");

        try
        {
            Observable
                .FromAsync(_listener.AcceptTcpClientAsync)
                .Repeat()
                .Subscribe( tcpClient =>
                {
                    var clientId = NextClientId();
                    _clients.TryAdd(clientId, tcpClient);
                    Console.WriteLine($"ClientId: {clientId.Value} Connected. Total: {_clients.Count}");
                     _ = BroadcastMessageFrom(clientId, "has joined the chat");
                    SubscribeToClientInput(tcpClient, clientId);
                });

            _messageStream.Subscribe( tuple =>
            {
                 _ = BroadcastMessageFrom(tuple.SenderId, tuple.Message);
            });

            await Task.Delay(Timeout.Infinite);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"StartAsync Error: {ex.Message}");
        }
    }

    private void SubscribeToClientInput(TcpClient client, ClientId clientId)
    {
        var reader = ReaderFrom(client);

        var linesObservable = Observable.Using(
            () => reader,
            _ => Observable
                .FromAsync(reader.ReadLineAsync)
                .Repeat()
                .TakeWhile(line => line != null && client.Connected)
        );

        linesObservable.Subscribe(
            line => _messageStream.OnNext((clientId, line)!),
            ex => { Console.WriteLine($"ClientId {clientId.Value} Error: {ex.Message}"); },
            () => { _ = DisconnectClient(clientId); });
    }

    private static StreamReader ReaderFrom(TcpClient client) => new(client.GetStream(), Encoding.UTF8);

    private async Task BroadcastMessageFrom(ClientId senderId, string message)
    {
        var formatted = Utils.CompleteMessageFrom(senderId, message);
        var data = Encoding.UTF8.GetBytes(formatted);

        await Task.WhenAll(_clients.MessageReceiversFrom(senderId)
            .Select(client => Utils.SendToClient(client.Key, client.Value, data)));
    }

    private async Task DisconnectClient(ClientId clientId)
    {
        if (!_clients.TryRemove(clientId, out var client)) return;

        client.Dispose();
        Console.WriteLine($"ClientId: {clientId.Value} Disconnected. Remaining: {_clients.Count}");
        await BroadcastMessageFrom(clientId, "has left the chat");
    }

    private ClientId NextClientId() => new($"Client{++_clientCounter}");

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