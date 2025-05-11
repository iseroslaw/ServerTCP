using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace ServerTcp;

public class TcpChatServerWithObservable(int port) : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Any, port);
    private readonly ConcurrentDictionary<ClientId, TcpClient> _clients = new();
    private  Subject<(string SenderId, string Message)> _messageStream = new();
    private int _clientCounter = 0;
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
                .Subscribe(tcpClient =>
                {
                    var clientId = NextClientId();
                    _clients.TryAdd(clientId, tcpClient);
                    Console.WriteLine($"ClientId: {clientId.Value} Connected. Total: {_clients.Count}");
                });
            
            await Task.Delay(-1);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"StartAsync Error: {ex.Message}");
        }
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

    ~TcpChatServerWithObservable()
    {
        Dispose(false);
    }
}