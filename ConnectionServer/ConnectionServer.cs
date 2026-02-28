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

        private async Task<string> GetSharedPath(Request request, string path, bool returnHigher = false)
        {
            var pathDB = DBContext.GetCSPath((await DBContext.ExecQueryAsync("path", "sharedfiles", $"id = {request.ID}")).First()["path"]);

            if (pathDB == path + request.FileName)
                return pathDB;
            else
            {
                var dbParts = GetPathParts(pathDB);
                var requestParts = GetPathParts(request.FileName!);

                for (var i = dbParts.Length - 1; i >= 0; i--)
                {
                    for (var j = requestParts.Length - 1; j >= 0; j--)
                    {
                        var pathNew = string.Join('\\', dbParts[0..i].Concat(requestParts[j..]));
                        var pathNewSub = pathNew.Substring(0, pathNew.LastIndexOf('\\'));

                        if (dbParts[i] == requestParts[j] && Directory.Exists(pathNewSub))
                            return returnHigher ? pathNewSub : pathNew;
                    }
                }
            }

            throw new FileNotFoundException("Shared file or directory was not found.");
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
                                check = VerifyUserAccessToFile(userFiles, request, out _, out _);
                                var path = $"{_appSettings.RootFolderPath}";
                                var isDir = request.Unit == Unit.Directory;

                                if (check)
                                {
                                    if (request.My)
                                    {
                                        path += $"{_fileSeparator}{_username}{_fileSeparator}";
                                        path += request.FileName;
                                    }
                                    else
                                    {
                                        path = await GetSharedPath(request, path + _fileSeparator + pathParts?.First());
                                    }

                                    // pre priecinok vytvorit zip v tempe
                                    if (isDir)
                                    {
                                        var pathTemp = path;

                                        path = $"{_appSettings.RootFolderPath}{_fileSeparator}temp{_fileSeparator}{_username}{_fileSeparator}{pathParts!.Last()}.zip";

                                        try
                                        {
                                            if (File.Exists(path))
                                                File.Delete(path);

                                            await ZipFile.CreateFromDirectoryAsync(pathTemp, path, _cancellationToken);
                                        }
                                        catch
                                        {
                                            check = false;
                                        }
                                    }
                                }

                                // odpoved
                                await _connectionHandler.YNSendAsync(true, check);

                                if (check)
                                {
                                    FileInfo[] fileInfoArr = [new(path)];

                                    await _connectionHandler.SendInviteAsync(fileInfoArr, true);

                                    await _connectionHandler.SendFilesAsync(fileInfoArr, request.Encrypted);

                                    if (isDir)
                                        File.Delete(path);
                                }

                                break;

                            case Tag.Upload:
                                Dir dir;

                                pathParts = GetPathParts(request.FileName!);

                                VerifyUserAccessToFile(userFiles, request, out dir, out _, true);

                                await _connectionHandler.YNSendAsync(true, check = dir.CanAdd);

                                int indexOfSeparator = -1;
                                string owner = request.My ? _username : request.FileName!.Substring(0, indexOfSeparator = request.FileName.IndexOf('\\'));
                                string rootUserPath = $"{_appSettings.RootFolderPath}{_fileSeparator}{owner}";
                                check = check && GetDirectorySize(new DirectoryInfo(rootUserPath)) + request.FileSize <= await DBContext.GetUserSpace(owner);

                                if (check)
                                {
                                    string fileNameTemp = indexOfSeparator == -1 ? request.FileName! : request.FileName!.Substring(indexOfSeparator + 1);
                                    int lastIndexOfSeparator = fileNameTemp.LastIndexOf(_fileSeparator);

                                    if (lastIndexOfSeparator != -1)
                                        fileNameTemp = fileNameTemp.Substring(0, lastIndexOfSeparator);

                                    if (fileNameTemp == pathParts.Last())
                                        fileNameTemp = String.Empty;
                                    else
                                        fileNameTemp = $"{_fileSeparator}{fileNameTemp}";

                                    await _connectionHandler.ReceiveFilesAsync(new()
                                    {
                                        { pathParts.Last(), request.FileSize }
                                    }, request.My ? $"{rootUserPath}{fileNameTemp}" : await GetSharedPath(request, rootUserPath, true), request.Encrypted);
                                }

                                break;

                            case Tag.RenameFile:
                                string unitName = GetPathParts(request.NewFileName!).Last(), userFolder, oldPath, newPath;
                                Fil? fil;
                                check = VerifyUserAccessToFile(userFiles, request, out dir, out fil);
                                var isFile = IsUnitFile(request.Unit);
                                owner = isFile ? fil!.Owner : dir.Owner;

                                userFolder = $"{_appSettings.RootFolderPath}{_fileSeparator}{owner}";
                                if (request.My)
                                {
                                    oldPath = userFolder + _fileSeparator + request.FileName;
                                    newPath = userFolder + _fileSeparator + request.NewFileName;
                                }
                                else
                                {
                                    oldPath = await GetSharedPath(request, userFolder);
                                    newPath = oldPath.Substring(0, oldPath.LastIndexOf(_fileSeparator)) + _fileSeparator + unitName;
                                }

                                check = check
                                    && ((isFile && fil!.CanRename && dir.Fils is not null && dir.Fils.All(x => !String.Equals(x.Name, unitName))) || (!isFile && dir.CanRename));

                                if (!isFile)
                                {
                                    request.My = true;

                                    if (!((isFile && !File.Exists(newPath)) || (!isFile && !Directory.Exists(newPath))))
                                        check = false;
                                }

                                if (check)
                                {
                                    if (isFile)
                                        File.Move(oldPath, newPath);
                                    else
                                        Directory.Move(oldPath, newPath);

                                    await DBContext.ChangePathAsync(oldPath, newPath);
                                }

                                break;

                            case Tag.DeleteFile:
                                check = VerifyUserAccessToFile(userFiles, request, out dir, out fil);

                                if (check)
                                {
                                    isFile = IsUnitFile(request.Unit);
                                    path = $"{_appSettings.RootFolderPath}";

                                    if (isFile)
                                    {
                                        check = fil!.CanDelete;
                                        path += $"{_fileSeparator}{fil.Owner}";
                                    }
                                    else
                                    {
                                        check = dir.CanDelete;
                                        path += $"{_fileSeparator}{dir.Owner}";
                                    }                                    


                                    if (check)
                                    {
                                        if (request.My)
                                            path += $"{_fileSeparator}{request.FileName}";
                                        else
                                            path = await GetSharedPath(request, path);

                                        if (isFile)
                                            await DeleteFile(path);
                                        else
                                            await DeleteDir(path);
                                    }
                                }

                                break;

                            case Tag.CreateGroup:
                                await DBContext.AddUserGroupAsyncAndReturnID(request.Name!, _username);

                                check = true;

                                break;
                            case Tag.DeleteGroup:
                                if (check = await DBContext.GetUserGroupAdminAsync(request.Group!.ID) == _username)
                                {
                                    await DBContext.ExecNonQueryAsync($"DELETE FROM usergroups WHERE id = {request.Group!.ID}");
                                }

                                break;
                            case Tag.EditGroup:
                                if (check = await DBContext.GetUserGroupAdminAsync(request.Group!.ID) == _username)
                                {
                                    await DBContext.EditUserGroupAsync(request.Group);
                                }

                                break;
                            case Tag.AddFolder:
                                VerifyUserAccessToFile(userFiles, request, out dir, out _, true);

                                if (check = dir.CanAdd)
                                {
                                    path = $"{_appSettings.RootFolderPath}{_fileSeparator}{dir.Owner}";

                                    Directory.CreateDirectory(request.My ? $"{_appSettings.RootFolderPath}\\{(request.My ? $"{_username}\\" : String.Empty)}{request.FileName}" : await GetSharedPath(request, path));
                                }

                                break;
                            case Tag.ChangeSharing:
                                path = $"{_appSettings.RootFolderPath}\\{_username}\\{request.FileName}";

                                check = VerifyUserAccessToFile(userFiles, request, out dir, out fil);
                                isFile = request.Unit == Unit.File;
                                check = check && ((isFile && fil!.Owner == _username) || (!isFile && dir.Owner == _username));

                                if (check)
                                    await DBContext.ChangeShares(_username, path, request.Unit, request.Shares!, isFile ? fil?.Shares : dir.Shares);

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
                bool canDelete = DBContext.GetBoolFromTinyIntInString(x["candelete"]), canRename = DBContext.GetBoolFromTinyIntInString(x["canrename"]);

                switch (Enum.Parse<Unit>(x["type"]))
                {
                    case Unit.File:
                        FileInfo fi = new(x["path"]!);
                        var foundFil = sharedFils.FirstOrDefault(y => y.Name == fi.Name);

                        if (foundFil == default(Fil))
                        {
                            sharedFils.Add(new()
                            {
                                Name = fi.Name,
                                Size = fi.Length,
                                CanDelete = canDelete,
                                CanRename = canRename,
                                Owner = x["username"],
                                ID = int.Parse(x["sharedfiles_id"])
                            });
                        }
                        else
                        {
                            foundFil.CanDelete = foundFil.CanDelete || canDelete;
                            foundFil.CanRename = foundFil.CanRename || canRename;
                        }

                        break;
                    case Unit.Directory:
                        var canAdd = DBContext.GetBoolFromTinyIntInString(x["canadd"]);
                        DirectoryInfo di = new(x["path"]);
                        var foundDir = sharedDirs.FirstOrDefault(y => y.Name == di.Name);

                        if (foundDir == default(Dir))
                        {
                            sharedDirs.Add(new(x["path"], x["username"], canDelete, canRename, canAdd, int.Parse(x["sharedfiles_id"])));
                        }
                        else
                        {
                            foundDir.CanDelete = foundDir.CanDelete || canDelete;
                            foundDir.CanRename = foundDir.CanRename || canRename;
                            foundDir.CanAdd = foundDir.CanAdd || canAdd;
                        }

                        break;
                    default:
                        throw new NotImplementedException("Unknown unit type.");
                }
            });
            var allUserInfo = new AllUserInfo()
            {
                User = await DBContext.GetUserInfoAsync(username),
                MyDir = new Dir($"{_appSettings.RootFolderPath}\\{username}", username, true, true, true, null),
                Users = await DBContext.GetUsersAsync(username),
                SharedDirs = sharedDirs.Count > 0 ? sharedDirs.ToArray() : null,
                SharedFils = sharedFils.Count > 0 ? sharedFils.ToArray() : null,
                UserGroups = await DBContext.GetUserGroupsAsync(username)
            };

            var shares = await DBContext.GetMyFileSharesAsync(username);

            foreach (var share in shares)
            {
                var relativePath = share.Key.Substring($"{_appSettings.RootFolderPath}\\{username}\\".Length);
                var pathParts = GetPathParts(relativePath);
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
            string reqOwner = String.Empty;
            if (!request.My)
            {
                reqOwner = pathParts[0];
                pathParts = pathParts[1..];
            }
            var isDirectory = request.Unit == Unit.Directory;
            var iterationsCount = pathParts.Length;

            if ((oneLevelHigher && isDirectory) || !isDirectory)
                iterationsCount--;

            currentDir = request.My ? userFiles.MyDir : new(String.Empty, _username!, false, false, false, userFiles.SharedFils, userFiles.SharedDirs, null, request.ID);

            for (int i = 0; i < iterationsCount && check; i++)
            {
                check = false;

                if (currentDir.Dirs is not null)
                {
                    foreach (var dir in currentDir.Dirs)
                    {
                        if (dir.Name == pathParts[i] && (request.My ? true : dir.Owner == reqOwner))
                        {
                            if (dir.ID == request.ID)
                            {
                                currentDir = dir;

                                check = true;
                            }
                        }
                    }
                }
            }

            currentFil = null;

            if (isDirectory && iterationsCount == 0)
                return false;
            
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
            Dir dir = new(path, String.Empty, true, true, true, null);

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

        private long GetDirectorySize(DirectoryInfo directory)
        {
            long output = 0;

            foreach (var file in directory.GetFiles())
                output += file.Length;

            foreach (var dir in directory.GetDirectories())
                output += GetDirectorySize(dir);

            return output;
        }
    }
}
