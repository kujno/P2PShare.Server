using P2PShare.Libs.Models.FileSytem;
using P2PShare.Libs.Models.Requests;
using P2PShare.Server.DBAccess;
using P2PShare.Server.Models;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;

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

                await _connectionHandler.SendInfoAsync((await CreateUserFilesAsync(_username!)).ToJSON());

                while (!_cancellationToken.IsCancellationRequested)
                {
                    var request = Request.Create(await _connectionHandler.ReceiveInfoAsync());
                    var userFiles = await CreateUserFilesAsync(_username!);
                    var userFilesJSON = userFiles.ToJSON();
                    var pathParts = request.FileName is not null ? GetPathParts(request.FileName) : null;
                    bool check = false;
                    Exception? ex = null;

                    try
                    {


                        switch (request.Tag)
                        {
                            case Tag.Get:
                                await _connectionHandler.SendInfoAsync(userFilesJSON);

                                break;

                            case Tag.Download:
                                var authorized = VerifyUserAccessToFile(userFiles, request, out _, out _);
                                string path;

                                await _connectionHandler.YNSendAsync(true, authorized);

                                if (authorized)
                                {
                                    path = $"{_appSettings.RootFolderPath}{_fileSeparator}";

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

                                check = VerifyUserAccessToFile(userFiles, request, out dir, out _, true);

                                await _connectionHandler.YNSendAsync(true, check = dir.CanAdd && check);
                                
                                if (check)
                                {
                                    int lastIndexOfSeparator = request.FileName!.LastIndexOf(_fileSeparator);

                                    await _connectionHandler.ReceiveFilesAsync(new()
                                    {
                                        { pathParts.Last(), request.FileSize }
                                    }, $"{_appSettings.RootFolderPath}{_fileSeparator}{(request.My ? $"{_username}{_fileSeparator}" : String.Empty)}{(lastIndexOfSeparator != -1 ? request.FileName!.Substring(0, lastIndexOfSeparator) : String.Empty)}", request.Encrypted);
                                }

                                break;

                            case Tag.RenameFile:
                                string unitName = GetPathParts(request.NewFileName!).Last(), userFolder, oldPath, newPath;
                                Fil? fil;
                                check = VerifyUserAccessToFile(userFiles, request, out dir, out fil);
                                var isFile = IsUnitFile(request.Unit);
                                var owner = isFile ? fil!.Owner : dir.Owner;

                                userFolder = $"{_appSettings.RootFolderPath}{_fileSeparator}{owner}{_fileSeparator}";
                                oldPath = userFolder + request.FileName;
                                newPath = userFolder + request.NewFileName;
                                check = check
                                    && ((isFile && fil!.CanRename && dir.Fils is not null && dir.Fils.All(x => !String.Equals(x.Name, unitName))) || dir.CanRename);

                                if (!isFile)
                                {
                                    VerifyUserAccessToFile(owner == _username ? userFiles : await CreateUserFilesAsync(owner), request, out dir, out fil, true);

                                    check = check
                                        && dir.Dirs!.All(x => !String.Equals(x.Name, unitName));
                                }

                                if (check)
                                {
                                    File.Move(oldPath, newPath);

                                    await DBContext.ExecNonQueryAsync($"UPDATE sharedfiles SET path = {newPath} WHERE path = {oldPath}");
                                }

                                break;

                            case Tag.DeleteFile:
                                check = VerifyUserAccessToFile(userFiles, request, out dir, out fil);

                                if (check)
                                {
                                    isFile = IsUnitFile(request.Unit);
                                    path = $"{_appSettings.RootFolderPath}{_fileSeparator}";

                                    if (isFile)
                                    {
                                        check = fil!.CanDelete;
                                        path += $"{fil.Owner}{_fileSeparator}{request.FileName}";
                                    }
                                    else
                                    {
                                        check = dir.CanDelete;
                                        path += $"{dir.Owner}{_fileSeparator}{request.FileName}";
                                    }

                                    if (check)
                                    {
                                        if (isFile)
                                            await DeleteFile(path);
                                        else
                                            await DeleteDir(path);
                                    }
                                }

                                break;

                            case Tag.AddGroup:
                                check = _username == request.Group!.Admin.Username
                                    && (await DBContext.GetUserGroupsAsync(_username)).All(x => x.Name != request.Group.Name);

                                if (check)
                                    await DBContext.AddUserGroupAsync(request.Group);

                                break;

                            case Tag.EditGroup:
                                check = _username == await DBContext.GetUserGroupAdminAsync(request.Group!.Name);

                                if (check)
                                {
                                    if (request.Group.Name != request.UpdatedGroup!.Name)
                                        await DBContext.UpdateGroupNameAsync(request.Group.Name, request.UpdatedGroup.Name);

                                    if (!Enumerable.SequenceEqual(request.Group.Users, request.UpdatedGroup.Users))
                                        await DBContext.UpdateUsersInGroupAsync(request.Group, request.UpdatedGroup);
                                }

                                break;

                            case Tag.DeleteGroup:
                                check = _username == await DBContext.GetUserGroupAdminAsync(request.Group!.Name);

                                if (check)
                                    await DBContext.ExecNonQueryAsync($"DELETE FROM usergroups WHERE name = \"{request.Group.Name}\"");

                                break;

                            case Tag.AddShare:
                                path = $"{_appSettings.RootFolderPath}\\{_username}\\{request.FileName}";

                                if (check = (request.Unit == Unit.File && File.Exists(path)) || (request.Unit == Unit.Directory && Directory.Exists(path)))
                                    await DBContext.AddSharesAsync(path, _username!, request.Users ?? [], request.Groups ?? [], request.CanAdd, request.CanRename, request.CanDelete);

                                break;
                            case Tag.RemoveShare:
                                path = $"{_appSettings.RootFolderPath}\\{_username}\\{request.FileName}";

                                if (check = (request.Unit == Unit.File && File.Exists(path)) || (request.Unit == Unit.Directory && Directory.Exists(path)))
                                    await DBContext.RemoveSharesAsync(path, _username!, request.Users ?? [], request.Groups ?? []);

                                break;
                            case Tag.AddFolder:
                                if (check = VerifyUserAccessToFile(userFiles, request, out dir, out _, true) && dir.CanAdd)
                                    Directory.CreateDirectory($"{_appSettings.RootFolderPath}\\{(request.My ? $"{_username}\\" : String.Empty)}{request.FileName}");

                                break;
                        }
                    }
                    catch (Exception exc)
                    {
                        check = false;
                        ex = exc;

                        throw;
                    }
                    finally
                    {
                        if (request.Tag != Tag.Get && request.Tag != Tag.Download && request.Tag != Tag.Upload && ex is not SocketException)
                            await _connectionHandler.YNSendAsync(true, check);
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

                Dispose();
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
            var allUserInfo = new AllUserInfo()
            {
                User = await DBContext.GetUserInfoAsync(_username!),
                MyDir = new Dir($"{_appSettings.RootFolderPath}\\{_username}", _username!, true, true, true),
                Users = await DBContext.GetUsersAsync(_username!),
                SharedDirs = sharedDirs.Count > 0 ? sharedDirs.ToArray() : null,
                SharedFils = sharedFils.Count > 0 ? sharedFils.ToArray() : null,
                UserGroups = await DBContext.GetUserGroupsAsync(_username!)
            };

            var shares = await DBContext.GetMyFileSharesAsync(_username!);

            foreach (var share in shares)
            {
                var pathParts = GetPathParts(share.Key.Substring(share.Key.IndexOf($"{_appSettings.RootFolderPath}\\{_username}\\" + 1)));
                Dir curDir = allUserInfo.MyDir;

                for (var i = 0; i < pathParts.Length - 1; i++)
                    curDir = curDir.Dirs!.First(x => x.Name == pathParts[i]);

                if (share.Value?.First().Type is Unit.File)
                    curDir.Fils?.First(x => x.Name == pathParts.Last()).Shares = share.Value;
                else
                    curDir.Dirs?.First(x => x.Name == pathParts.Last()).Shares = share.Value;
            }

            return allUserInfo;
        }

        private bool VerifyUserAccessToFile(AllUserInfo userFiles, Request request, out Dir currentDir, out Fil? currentFil, bool oneLevelHigher = false)
        {
            bool check = true;
            var pathParts = GetPathParts(request.FileName!);
            var isDirectory = request.Unit == Unit.Directory;
            var iterationsCount = isDirectory ? pathParts.Length : pathParts.Length - 1;

            if (oneLevelHigher && isDirectory)
                iterationsCount--;

            currentDir = request.My ? userFiles.MyDir : new(String.Empty, _username!, false, false, false, null, userFiles.SharedDirs!.ToArray());

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

            if (isDirectory || oneLevelHigher || !check)
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
