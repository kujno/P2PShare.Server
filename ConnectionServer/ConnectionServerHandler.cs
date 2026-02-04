using P2PShare.Libs;

namespace P2PShare.Server.ConnectionServer
{
    public class ConnectionServerHandler : ConnectionHandler
    {
        private int _port;

        public string IPRemote { get => _ipRemote?.ToString() ?? "No remote IP"; }

        public async Task WaitForConnectionAsync()
        {
            using (Client = await ReceiveTcpClientAsync(_initialServerPort))
            {
                await ReceiveEncryptionKeyAsync();
                _port = await SendPortAsync(true);
            }
        }

        public async Task<bool> AuthOnNewPortAsync()
        {
            byte[] buffer = new byte[_modulusLength * 3];
            
            Client = await ReceiveTcpClientAsync(_port);

            await _netStream!.ReadExactlyAsync(buffer, CancellationToken);

            // hashing and db querying here
        }
    }
}
