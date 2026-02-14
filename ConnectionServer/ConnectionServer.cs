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
        private readonly char _fileSeparator = '\\';

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

        private string[] GetPathParts(string path) => path.Split(_fileSeparator);

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

                await _connectionHandler.SendInfoAsync((await CreateUserFilesAsync()).ToJSON());

                while (!_cancellationToken.IsCancellationRequested)
                {
                    var request = Request.Create(await _connectionHandler.ReceiveRequestAsync());
                    var userFiles = await CreateUserFilesAsync();
                    var userFilesJSON = userFiles.ToJSON();
                    var pathParts = request.FileName is not null ? GetPathParts(request.FileName) : null;

                    switch (request.Tag)
                    {
                        case Tag.Get:
                            await _connectionHandler.SendInfoAsync(userFilesJSON);

                            break;

                        case Tag.Download:
                            var authorized = VerifyUserAccessToFile(userFiles, request, out _);

                            await _connectionHandler.YNSendAsync(true, authorized);

                            if (authorized)
                            {
                                var path = $"{_appSettings.RootFolderPath}{_fileSeparator}";

                                if (request.My)
                                    path += $"{_username}\\";
                                path += request.FileName;

                                // pre priecinok vytvorit zip v tempe
                                if (request.Unit == Unit.Directory)
                                {
                                    var pathTemp = path;

                                    path = $"{_appSettings.RootFolderPath}{_fileSeparator}temp{_fileSeparator}{pathParts!.Last()}.zip";

                                    await ZipFile.CreateFromDirectoryAsync(pathTemp, path, _cancellationToken);
                                }

                                await _connectionHandler.SendFileAsync(new(path), request.Encrypted);
                            }

                            break;

                        case Tag.Upload:
                            Dir dir;

                            pathParts = GetPathParts(request.FileName!);

                            VerifyUserAccessToFile(userFiles, request, out dir, true);

                            await _connectionHandler.YNSendAsync(true, dir.CanAdd);

                            if (dir.CanAdd)
                            {
                                await _connectionHandler.ReceiveFilesAsync(new()
                                {
                                    { pathParts.Last(), request.FileSize }
                                }, $"{_appSettings.RootFolderPath}{dir.Owner}{request.FileName!.Substring(0, request.FileName.LastIndexOf(_fileSeparator))}", request.Encrypted);
                            }

                            break;

                        case Tag.Rename:
                            string unitName = GetPathParts(request.NewFileName!).Last(), userFolder, oldPath, newPath;
                            bool check;

                            VerifyUserAccessToFile(userFiles, request, out dir);

                            userFolder = $"{_appSettings.RootFolderPath}{_fileSeparator}{dir.Owner}{_fileSeparator}";
                            oldPath = userFolder + request.FileName;
                            newPath = userFolder + request.NewFileName;
                            check = dir.CanRename && (request.Unit == Unit.File && dir.Fils is not null && dir.Fils.All(x => !String.Equals(x.Name, unitName)) || (dir.Dirs is not null && dir.Dirs.All(x => !String.Equals(x.Name, unitName))));

                            if (check)
                            {
                                File.Move(oldPath, newPath);

                                await DBContext.ExecNonQueryAsync($"UPDATE sharedfiles SET path = {newPath} WHERE path = {oldPath}");
                            }

                            await _connectionHandler.YNSendAsync(true, check);

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

        private async Task<AllUserInfo> CreateUserFilesAsync()
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
                            Owner = x["username"]
                        });

                        break;
                    case Unit.Directory:
                        sharedDirs.Add(new(x["path"], x["username"], DBContext.GetBoolFromTinyIntInString(x["candelete"]), DBContext.GetBoolFromTinyIntInString(x["canrename"]), DBContext.GetBoolFromTinyIntInString(x["canadd"])));

                        break;
                    default:
                        throw new NotImplementedException("Unknown unit type.");
                }
            });
            return new AllUserInfo()
            {
                MyDir = new Dir($"{_appSettings.RootFolderPath}\\{_username}", _username!, true, true, true),
                Users = await DBContext.GetUsernamesAsync(),
                SharedDirs = sharedDirs.Count > 0 ? sharedDirs.ToArray() : null,
                SharedFils = sharedFils.Count > 0 ? sharedFils.ToArray() : null,
                UserGroups = await DBContext.GetUserGroupsAsync(_username!)
            };
        }

        private bool VerifyUserAccessToFile(AllUserInfo userFiles, Request request, out Dir currentDir, bool oneLevelHigher = false)
        {
            bool check = true;
            var pathParts = GetPathParts(request.FileName!);
            var isDirectory = request.Unit == Unit.Directory;
            var iterationsCount = isDirectory ? pathParts.Length : pathParts.Length - 1;
            var firstCurrentDir = request.My ? userFiles.MyDir.Dirs!.ToArray() : userFiles.SharedDirs!.ToArray();

            if (oneLevelHigher)
            {
                firstCurrentDir = firstCurrentDir[0..(firstCurrentDir.Length - 1)];
                isDirectory = true;
            }
            currentDir = new(String.Empty, _username!, null, firstCurrentDir);

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

            if (currentDir.Fils is not null)
                foreach (var fil in currentDir.Fils)
                    if (fil.Name == pathParts[pathParts.Length - 1])
                        return true;

            return false;
        }
    }
}
