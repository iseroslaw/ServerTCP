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
    private readonly ConcurrentDictionary<ClientId, SubscribedClient> _subscribedClients = new();
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
        var subscription = SubscribeToClientInput(tcpClient, clientId);
        _ = BroadcastMessageFrom(clientId, "has joined the chat");
        _subscribedClients.TryAdd(clientId, new SubscribedClient(tcpClient, subscription));

        Console.WriteLine($"ClientId: {clientId.Value} Connected. Total: {_subscribedClients.Count}");
    }

    private IDisposable SubscribeToClientInput(TcpClient client, ClientId clientId)
    {
        var reader = ReaderFrom(client);

        return Observable.Using(
                () => reader,
                _ => Observable
                    .FromAsync(reader.ReadLineAsync)
                    .Repeat()
                    .TakeWhile(line => line != null && client.Connected)
            )
            .Subscribe(
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

        await Task.WhenAll(_subscribedClients.MessageReceiversFrom(senderId)
            .Select(subscribedClient => Utils.SendToClient(subscribedClient.Key, subscribedClient.Value.Client, data)));
    }

    private async Task DisconnectClient(ClientId clientId)
    {
        if (!_subscribedClients.TryRemove(clientId, out var subscribedClient)) return;

        DisposeSubscribedClient(subscribedClient);

        Console.WriteLine($"ClientId: {clientId.Value} Disconnected. Remaining: {_subscribedClients.Count}");
        await BroadcastMessageFrom(clientId, "has left the chat");
    }

    private ClientId NextClientId() => new($"Client{++_clientCounter}");

    private static void DisposeSubscribedClient(SubscribedClient subscribedClient)
    {
        try
        {
            subscribedClient.Client.Close();
            subscribedClient.Subscription.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error closing a client during server shutdown: {ex.Message}");
        }
        finally
        {
            subscribedClient.Client.Dispose();
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
            _clientConnectionSubscription?.Dispose();
            _messageStream.Dispose();

            foreach (var subscribedClient in _subscribedClients.Values)
            {
                DisposeSubscribedClient(subscribedClient);
            }

            _subscribedClients.Clear();
        }

        _disposed = true;
    }

    ~TcpChatServer()
    {
        Dispose(false);
    }
}