using ServerTcp;

const int port = 9000; 
var observableServer = new TcpChatServer(port);
await observableServer.StartAsync();