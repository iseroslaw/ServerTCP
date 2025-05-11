using System.Collections.Concurrent;
using System.Net.Sockets;

namespace ServerTcp;

public readonly record struct ClientId(string Value);
public static class Utils
{
    public static IEnumerable<KeyValuePair<ClientId, SubscribedClient>> MessageReceiversFrom(this ConcurrentDictionary<ClientId, SubscribedClient> clients, ClientId senderId) =>
        clients.Where((subscribedClient) => subscribedClient.Key != senderId);
    
    public static async Task SendToClient(ClientId clientId, TcpClient client, byte[] data)
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
    
    public static string CompleteMessageFrom(ClientId senderId, string message) => $"{senderId.Value}: {message}\r\n";
}