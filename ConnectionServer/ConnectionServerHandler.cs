using P2PShare.Libs;
using System.Net;

namespace P2PShare.Server.ConnectionServer
{
    public class ConnectionServerHandler(CancellationToken cancellationToken) : ConnectionHandler(IPAddress.Any, cancellationToken)
    {
        public string RemoteIP { get => _ipRemote?.ToString() ?? "No remote IP"; }
        
        public async Task WaitForConnectionAsync()
        {
            // just initialize connection
            // when finished, it will return to the ConnectionServer class
            // which will return to Server class
            // that will call the ServerConnection.Run() method and crete another ConnectionServer object and call this method again
        }
    }
}
