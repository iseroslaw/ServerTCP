using ServerTcp;

const int port = 9000; 
var server = new TcpChatServer(port);
server.Start();