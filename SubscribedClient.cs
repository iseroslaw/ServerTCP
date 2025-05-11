using System.Net.Sockets;

namespace ServerTcp;

public class SubscribedClient(TcpClient client, IDisposable subscription)
{
    public TcpClient Client { get; } = client;
    public IDisposable Subscription { get; } = subscription;
}