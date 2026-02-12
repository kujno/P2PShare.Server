using P2PShare.Libs.Models.FileSytem;
using P2PShare.Libs.Models.Requests;
using P2PShare.Server.DBAccess;
using P2PShare.Server.Models;
using System.IO.Compression;
using System.Net;

namespace P2PShare.Server.ConnectionServer
{
    public class ConnectionServer : IDisposable
    {
        private readonly CancellationToken _cancellationToken;
        private readonly ConnectionServerHandler _connectionHandler;
        private readonly AppSettings _appSettings;

        private string? _username;
        private Task? _communication;

        private string _usernameProp
        {
            get => _username
                ?? throw new ArgumentNullException($"{nameof(_usernameProp)} is null.");
        }

        public static event EventHandler<ConnectionErrorEventArgs>? ConnectionError;

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

        public bool IsDone { get; private set; } = false;

        public void Dispose() => _connectionHandler.Dispose();

        public void Serve() => _communication = ServeLoopAsync();

        private async Task SendUserFilesAsync() => await _connectionHandler.SendUserFilesAsync((await CreateUserFilesAsync()).ToJSON());

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
                _username = await _connectionHandler.AuthOnNewPortAsync();

                await SendUserFilesAsync();

                while (!_cancellationToken.IsCancellationRequested)
                {
                    var request = Request.Create(await _connectionHandler.ReceiveRequestAsync());


                    switch (request.Tag)
                    {
                        case Tag.Get:
                            await SendUserFilesAsync();

                            break;

                        case Tag.Download:
                            var userFiles = await CreateUserFilesAsync();
                            var pathParts = request.FileName!.Split('\\');
                            var authorized = VerifyUserRightsToFile(userFiles, request, pathParts);

                            await _connectionHandler.YNSendAsync(true, authorized);

                            if (authorized)
                            {
                                var path = $"{_appSettings.RootFolderPath}\\";

                                if (request.My)
                                    path += $"{_username}\\";
                                path += request.FileName;

                                // pre priecinok vytvorit zip v tempe
                                if (request.Unit == Unit.Directory)
                                {
                                    var pathTemp = path;

                                    path = $"{_appSettings.RootFolderPath}\\temp\\{pathParts.Last()}.zip";

                                    await ZipFile.CreateFromDirectoryAsync(pathTemp, path, _cancellationToken);
                                }

                                await _connectionHandler.YNSendAsync(true, authorized);

                                await _connectionHandler.SendFilesAsync(new FileInfo[]
                                {
                                    new(path)
                                }, request.Encrypted);
                            }
                            else await _connectionHandler.YNSendAsync(true, authorized);

                            break;
                    }
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

        private async Task<UserFiles> CreateUserFilesAsync()
        {
            List<Dir> sharedDirs = [];
            List<Fil> sharedFils = [];

            Array.ForEach(await DBContext.GetSharedFilesAndDirectoriesAsync(_usernameProp), x =>
            {
                switch (Enum.Parse<Unit>(x["type"]))
                {
                    case Unit.File:
                        FileInfo fi = new(x["path"]!);

                        sharedFils.Add(new()
                        {
                            Name = fi.Name,
                            Size = fi.Length,
                            CanDelete = DBContext.GetBoolFromTinyIntInString(x["candelete"]),
                            CanRename = DBContext.GetBoolFromTinyIntInString(x["canrename"]),
                            Owner = x["owner_id"]
                        });

                        break;
                    case Unit.Directory:
                        sharedDirs.Add(new(x["path"], x["owner_id"])
                        {
                            CanDelete = DBContext.GetBoolFromTinyIntInString(x["candelete"]),
                            CanRename = DBContext.GetBoolFromTinyIntInString(x["canrename"]),
                            CanAdd = DBContext.GetBoolFromTinyIntInString(x["canadd"])
                        });

                        break;
                    default:
                        throw new NotImplementedException("Unknown unit type.");
                }
            });
            return new UserFiles()
            {
                MyDir = new Dir($"{_appSettings.RootFolderPath}\\{_username}", _username!),
                SharedDirs = sharedDirs.Count > 0 ? sharedDirs.ToArray() : null,
                SharedFils = sharedFils.Count > 0 ? sharedFils.ToArray() : null
            };
        }

        private bool VerifyUserRightsToFile(UserFiles userFiles, Request request, string[] pathParts)
        {
            Dir? currentDir = new(String.Empty, null, request.My ? userFiles.MyDir.Dirs?.ToArray() : userFiles.SharedDirs?.ToArray());
            bool check = true;
            var isDirectory = request.Unit == Unit.Directory;
            var iterationsCount = isDirectory ? pathParts.Length : pathParts.Length - 1;

            for (int i = 0; i < iterationsCount && check; i++)
            {
                check = false;

                if (currentDir.Dirs is not null)
                {
                    foreach (var dir in currentDir.Dirs)
                    {
                        if (dir.Name == pathParts[i])
                        {
                            currentDir = dir;

                            check = true;
                        }
                    }
                }
            }

            if (isDirectory)
                return check;

            check = false;

            if (currentDir.Fils is not null)
                foreach (var fil in currentDir.Fils)
                    if (fil.Name == pathParts[pathParts.Length - 1])
                        check = true;

            return check;
        }
    }
}
