using P2PShare.Libs.Models.FileSytem;
using P2PShare.Server.DBAccess;
using P2PShare.Server.Models;
using System.Net;

namespace P2PShare.Server.ConnectionServer
{
    public class ConnectionServer : IDisposable
    {
        private readonly CancellationToken _cancellationToken;
        private readonly ConnectionServerHandler _connectionHandler;
        private readonly AppSettings _appSettings;

        private string? _username = null;

        public ConnectionServer(AppSettings appSettings, CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            _appSettings = appSettings;
            _connectionHandler = new()
            {
                CancellationToken = _cancellationToken,
                IPLocal = IPAddress.Any,
                AppSettings = _appSettings
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
                List<Dir> sharedDirs = [];
                List<Fil> sharedFils = [];

                _username = await _connectionHandler.AuthOnNewPortAsync();

                Array.ForEach(await DBContext.GetSharedFilesAndDirectoriesAsync(_username), x =>
                {
                    if (x["type"] == "File")
                    {
                        FileInfo fi = new(x["path"]!);

                        sharedFils.Add(new()
                        {
                            Name = fi.Name,
                            Size = fi.Length,
                            CanDelete = x["candelete"] == "1",
                            CanRename = x["canrename"] == "1"
                        });
                    }
                    else
                    {
                        sharedDirs.Add(new(x["path"])
                        {
                            CanDelete = x["candelete"] == "1",
                            CanRename = x["canrename"] == "1",
                        });
                    }
                });
                await _connectionHandler.SendUserFilesAsync(new UserFiles()
                {
                    MyDir = new Dir($"{_appSettings.RootFolderPath}\\{_username}"),
                    SharedDirs = 
                });

                while (!_cancellationToken.IsCancellationRequested)
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
                Username = _username ?? "Unknown",
                DateTime = DateTime.Now
            });
        }
    }
}
