using System.Net;
using System.Net.Sockets;

namespace ServerTcp;

internal class TcpChatServer(int port)
{
    private readonly TcpListener _listener = new(IPAddress.Any, port);

    public void Start()
    {
         _listener.Start();
        Console.WriteLine($"[Server] Listening on port {((IPEndPoint)_listener.LocalEndpoint).Port}");
    }
}