using P2PShare.Server.Models;

namespace P2PShare.Server.ConnectionServer
{
    public class ConnectionServer(CancellationToken cancellationToken) : IDisposable
    {
        private ConnectionServerHandler _connectionHandler = new(cancellationToken);
        private Task? _communication;

        public static event EventHandler<ConnectionErrorEventArgs>? ConnectionError;

        public bool IsDone { get; private set; } = false;

        public void Dispose() => _connectionHandler.Dispose();

        private void OnConnectionError(ConnectionErrorEventArgs e) => ConnectionError?.Invoke(this, e);

        public void Serve() => _communication = ServeLoopAsync();

        public async Task InitAsync() => await _connectionHandler.WaitForConnectionAsync();

        private async Task ServeLoopAsync()
        {
            // this will handle the client requests
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {

                }
            }
            catch (Exception ex)
            {
                IsDone = true;
                if (ex is OperationCanceledException) return;

                OnConnectionError(new() 
                { ErrorMessage = ex.Message,
                    RemoteIP = _connectionHandler.RemoteIP, 
                    DateTime = DateTime.Now
                });
            }
        }
    }
}
