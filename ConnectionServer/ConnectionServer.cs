namespace P2PShare.Server.ConnectionServer
{
    public class ConnectionServer(CancellationToken cancellationToken) : IDisposable
    {
        private ConnectionServerHandler _connectionHandler = new(cancellationToken);
        private Task? _communication;

        public void Dispose() => _connectionHandler.Dispose();

        public void Serve() => _communication = ServeLoopAsync();

        public async void InitAsync() => await _connectionHandler.WaitForConnectionAsync();

        private async Task ServeLoopAsync()
        {
            // this will handle the client requests
        }
    }
}
