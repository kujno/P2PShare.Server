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

        private bool IsUnitFile(Unit unit) => unit == Unit.File;

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

                await _connectionHandler.SendInfoAsync((await CreateUserFilesAsync(_usernameProp)).ToJSON());

                while (!_cancellationToken.IsCancellationRequested)
                {
                    var request = Request.Create(await _connectionHandler.ReceiveRequestAsync());
                    var userFiles = await CreateUserFilesAsync(_usernameProp);
                    var userFilesJSON = userFiles.ToJSON();
                    var pathParts = request.FileName is not null ? GetPathParts(request.FileName) : null;

                    // put this in a big try catch that won't catch proly SocketEx... and CanceledEx... and sendYN after the switch block
                    switch (request.Tag)
                    {
                        case Tag.Get:
                            await _connectionHandler.SendInfoAsync(userFilesJSON);

                            break;

                        case Tag.Download:
                            var authorized = VerifyUserAccessToFile(userFiles, request, out _, out _);

                            await _connectionHandler.YNSendAsync(true, authorized);

                            if (authorized)
                            {
                                var path = $"{_appSettings.RootFolderPath}{_fileSeparator}";

                                if (request.My)
                                    path += $"{_usernameProp}\\";
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

                            VerifyUserAccessToFile(userFiles, request, out dir, out _, true);

                            await _connectionHandler.YNSendAsync(true, dir.CanAdd);

                            if (dir.CanAdd)
                            {
                                await _connectionHandler.ReceiveFilesAsync(new()
                                {
                                    { pathParts.Last(), request.FileSize }
                                }, $"{_appSettings.RootFolderPath}{dir.Owner}{request.FileName!.Substring(0, request.FileName.LastIndexOf(_fileSeparator))}", request.Encrypted);
                            }

                            break;

                        case Tag.RenameFile:
                            string unitName = GetPathParts(request.NewFileName!).Last(), userFolder, oldPath, newPath;
                            Fil? fil;
                            bool check = VerifyUserAccessToFile(userFiles, request, out dir, out fil), isFile = IsUnitFile(request.Unit);
                            var owner = isFile ? fil!.Owner : dir.Owner;

                            userFolder = $"{_appSettings.RootFolderPath}{_fileSeparator}{owner}{_fileSeparator}";
                            oldPath = userFolder + request.FileName;
                            newPath = userFolder + request.NewFileName;
                            check = check
                                && ((isFile && fil!.CanRename && dir.Fils is not null && dir.Fils.All(x => !String.Equals(x.Name, unitName))) || dir.CanRename);

                            if (!isFile)
                            {
                                VerifyUserAccessToFile(owner == _usernameProp ? userFiles : await CreateUserFilesAsync(owner), request, out dir, out fil, true);

                                check = check
                                    && dir.Dirs!.All(x => !String.Equals(x.Name, unitName));
                            }

                            if (check)
                            {
                                File.Move(oldPath, newPath);

                                await DBContext.ExecNonQueryAsync($"UPDATE sharedfiles SET path = {newPath} WHERE path = {oldPath}");
                            }

                            await _connectionHandler.YNSendAsync(true, check);

                            break;

                        case Tag.DeleteFile:
                            check = VerifyUserAccessToFile(userFiles, request, out dir, out fil);


                            if (check)
                            {
                                isFile = IsUnitFile(request.Unit);
                                var path = $"{_appSettings.RootFolderPath}{_fileSeparator}";

                                if (isFile)
                                {
                                    check = fil!.CanDelete;
                                    path += $"{fil.Owner}{_fileSeparator}{fil.Name}";
                                }
                                else
                                {
                                    check = dir.CanDelete;
                                    path += $"{dir.Owner}{_fileSeparator}{dir.Name}";
                                }

                                if (check)
                                {
                                    if (isFile)
                                        await DeleteFile(path);
                                    else
                                        await DeleteDir(path);
                                }
                            }

                            await _connectionHandler.YNSendAsync(true, check);

                            break;

                        case Tag.AddGroup:
                            check = _usernameProp == request.Group!.Admin.Username
                                && (await DBContext.GetUserGroupsAsync()).All(x => x.Name != request.Group.Name);

                            if (check)
                                await DBContext.AddUserGroupAsync(request.Group);

                            await _connectionHandler.YNSendAsync(request.Encrypted, check);

                            break;

                        //case Tag.EditGroup: TODO
                        case Tag.DeleteGroup:
                            //check = _usernameProp == (create a method for determining real admin)
                            
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
                Username = _usernameProp ?? "Unknown",
                DateTime = DateTime.Now
            });
        }

        private async Task<AllUserInfo> CreateUserFilesAsync(string username)
        {
            List<Dir> sharedDirs = [];
            List<Fil> sharedFils = [];

            Array.ForEach(await DBContext.GetSharedFilesAndDirectoriesAsync(username), x =>
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
                MyDir = new Dir($"{_appSettings.RootFolderPath}\\{_usernameProp}", _usernameProp, true, true, true),
                Users = await DBContext.GetUsersAsync(),
                SharedDirs = sharedDirs.Count > 0 ? sharedDirs.ToArray() : null,
                SharedFils = sharedFils.Count > 0 ? sharedFils.ToArray() : null,
                UserGroups = await DBContext.GetUserGroupsAsync(_usernameProp)
            };
        }

        private bool VerifyUserAccessToFile(AllUserInfo userFiles, Request request, out Dir currentDir, out Fil? currentFil, bool oneLevelHigher = false)
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
            currentDir = new(String.Empty, _usernameProp, null, firstCurrentDir);

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

            currentFil = null;

            if (isDirectory)
                return check;

            if (currentDir.Fils is not null)
                foreach (var fil in currentDir.Fils)
                    if (fil.Name == pathParts[pathParts.Length - 1])
                    {
                        currentFil = fil;

                        return true;
                    }

            return false;
        }

        private async Task DeleteDir(string path)
        {
            Dir dir = new(path, String.Empty, true, true, true);

            dir.Dirs?.ForEach(async x => await DeleteDir($"{path}{_fileSeparator}{x.Name}"));

            dir.Fils?.ForEach(async x => await DeleteFile($"{path}{_fileSeparator}{x.Name}"));

            Directory.Delete(path);

            await DBContext.DeleteSharedFile(path);
        }

        private async Task DeleteFile(string path)
        {
            File.Delete(path);

            await DBContext.DeleteSharedFile(path);
        }
    }
}
