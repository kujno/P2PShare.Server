using P2PShare.Server.Models;
using System.Net;

namespace P2PShare.Server.ConnectionServer
{
    public class ConnectionServer : IDisposable
    {
        public required CancellationToken CancellationToken { get; init; }

        private ConnectionServerHandler _connectionHandler;

        public ConnectionServer()
        {
            _connectionHandler = new()
            {
                CancellationToken = CancellationToken,
                IPLocal = IPAddress.Any
            };
        }

        private Task? _communication;

        public static event EventHandler<ConnectionErrorEventArgs>? ConnectionError;

        public bool IsDone { get; private set; } = false;

        public void Dispose() => _connectionHandler.Dispose();

        public void Serve() => _communication = ServeLoopAsync();

        public async Task InitAsync()
        {
            try
            {
                await _connectionHandler.WaitForConnectionAsync();
            }
            catch (OperationCanceledException)
            {
                IsDone = true;
            }
            catch (Exception ex)
            {
                IsDone = true;

                OnConnectionError(ex);
            }
        }

        private async Task ServeLoopAsync()
        {
            try
            {
                await _connectionHandler.AuthOnNewPortAsync();
                // send authorized info

                while (!CancellationToken.IsCancellationRequested)
                {
                    // handle requests
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                OnConnectionError(ex);
            }
            finally
            {
                IsDone = true;
            }
        }

        private void OnConnectionError(Exception ex)
        {
            ConnectionError?.Invoke(this, new()
            {
                ErrorMessage = ex.Message,
                RemoteIP = _connectionHandler.IPRemote,
                DateTime = DateTime.Now
            });
        }
    }
}
