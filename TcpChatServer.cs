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
    private IDisposable? _clientConnectionSubscription;
    private int _clientCounter;
    private bool _disposed;

    public async Task StartAsync()
    {
        StartListener();
        SubscribeToClientConnections();
        SubscribeToMessageStream();

        await Task.Delay(Timeout.Infinite);
    }
    
    private void StartListener()
    {
        _listener.Start();
        Console.WriteLine($"Listening on port {((IPEndPoint)_listener.LocalEndpoint).Port}");
    }

    private void SubscribeToClientConnections()
    {
        _clientConnectionSubscription = Observable
            .FromAsync(_listener.AcceptTcpClientAsync)
            .Repeat()
            .Subscribe(OnClientConnected);
    }

    private void OnClientConnected(TcpClient tcpClient)
    {
        var clientId = NextClientId();
        _clients.TryAdd(clientId, tcpClient);
        Console.WriteLine($"ClientId: {clientId.Value} Connected. Total: {_clients.Count}");
        _ = BroadcastMessageFrom(clientId, "has joined the chat");
        SubscribeToClientInput(tcpClient, clientId);
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
    
    private void SubscribeToMessageStream()
    {
        _messageStream.Subscribe(OnMessageReceived);
    }
    
    private void OnMessageReceived((ClientId SenderId, string Message) tuple)
    {
        _ = BroadcastMessageFrom(tuple.SenderId, tuple.Message);
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
            _clientConnectionSubscription?.Dispose();
            _messageStream.Dispose();
            
            foreach (var client in _clients.Values)
            {
                try
                {
                    client.Close();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error closing a client during server shutdown: {ex.Message}");
                }
                finally
                {
                    client.Dispose();
                }
            }
            _clients.Clear();
        }

        _disposed = true;
    }

    ~TcpChatServer()
    {
        Dispose(false);
    }
}