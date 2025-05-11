using ServerTcp;

const int port = 9000; 
// var server = new TcpChatServer(port);
// await server.StartAsync();

var observableServer = new TcpChatServerWithObservable(port);
await observableServer.StartAsync();