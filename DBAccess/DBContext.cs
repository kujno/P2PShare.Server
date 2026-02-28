using MySql.Data.MySqlClient;
using P2PShare.Libs.Models.FileSytem;
using System.Data;

namespace P2PShare.Server.DBAccess
{
    public static class DBContext
    {
        private static CancellationToken _cancellationToken;
        private static string? _connectionString;

        public static bool GetBoolFromTinyIntInString(string input) => input == "1";

        public static async Task DeleteSharedFile(string path) => await ExecNonQueryAsync($"DELETE FROM sharedfiles WHERE path = \"{GetSQLPath(path)}\"");

        public static async Task UpdateGroupNameAsync(string oldName, string newName) => await ExecNonQueryAsync($"UPDATE usergroups SET name = \"{newName}\" WHERE name = \"{oldName}\";");

        public static async Task InitAsync(DBCredentials credentials, CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            _connectionString = $"Server={credentials.Server};Database={credentials.Database};User ID={credentials.UserID};Password={credentials.Password};";
        }

        public static async Task AddUserAsync(string username, string hash, string name, string surename)
        {
            int? idUser = null, idGroup = null;
            var tag = "id";

            await ExecNonQueryAsync(new string[]
            {
                $"INSERT INTO users (username, password_hash, name, surename) VALUES (\"{username}\", \"{hash}\", \"{name}\", \"{surename}\");",
                $"INSERT INTO usergroups (name, isuser) VALUES (\"{username}\", 1);"
            });

            idUser = await GetIDFromUsernameAsync(username);
            idGroup = int.Parse((await ExecQueryAsync(tag, "usergroups", $"isuser = 1 && name = \"{username}\""))[0][tag]!);

            await ExecNonQueryAsync($"INSERT INTO usergroups_has_users (usergroups_id, users_id) VALUES ({idGroup}, {idUser});");
        }

        public static async Task<string[]> GetUsernamesAsync()
        {
            List<string> usernames = [];
            var tag = "username";

            Array.ForEach(await ExecQueryAsync(tag, "users"), x => usernames.Add(x[tag]!));

            return usernames.ToArray();
        }

        public static async Task<User[]> GetUsersAsync(string username)
        {
            List<User> users = [];

            Array.ForEach(await ExecQueryAsync(new string[]
            {
                "username",
                "name",
                "surename"
            }, "users", $"username != \"{username}\""), x => users.Add(new()
            {
                Username = x["username"],
                Name = x["name"],
                Surename = x["surename"]
            }));

            return users.ToArray();
        }

        public static async Task<string?> GetPasswordHashAsync(string username)
        {
            var tag = "password_hash";
            var results = await ExecQueryAsync(tag, "users", $"username = \"{username}\"");

            if (results.Length == 1)
                return results[0][tag];

            return null;
        }

        public static async Task ExecNonQueryAsync<T>(T commands)
        {
            var tType = typeof(T);
            var isTString = tType == typeof(string);
            var isTArray = tType == typeof(string[]);
            string[]? commandsArr = null;
            string? commandStr = null;

            if (isTArray) commandsArr = (string[])(object)commands!;
            else if (isTString) commandStr = (string)(object)commands!;
            else throw new NotImplementedException($"Method ExecCommand doesn't have implementation for T of type {tType}");

            using (MySqlConnection connection = new(_connectionString))
            {
                await connection.OpenAsync(_cancellationToken);

                for (var i = 0; i == 0 || (isTArray && i < commandsArr!.Length); i++)
                {
                    using (MySqlCommand command = new(isTString ? commandStr : commandsArr![i], connection))
                    {
                        await command.ExecuteNonQueryAsync(_cancellationToken);
                    }
                }
            }
        }

        private static async Task<Dictionary<string, string>[]> ExecQueryAsync<T>(T columns, string table, string? condition = null, string joinString = "")
        {
            var tType = columns?.GetType();
            string[] columnsArr;

            if (tType == typeof(string[]))
                columnsArr = (string[])(object)columns!;
            else if (tType == typeof(string))
                columnsArr = [(string)(object)columns!];
            else
                throw new NotImplementedException();

            List<Dictionary<string, string>> values = [];
            string columnsStr = String.Empty;

            for (var i = 0; i < columnsArr.Length; i++)
            {
                columnsStr += $"{columnsArr[i]}";
                columnsArr[i] = columnsArr[i].Replace('.', '_');
                columnsStr += $" AS {columnsArr[i]}";
                if (i < columnsArr.Length - 1)
                    columnsStr += ", ";
            }

            using (MySqlConnection connection = new(_connectionString))
            {
                await connection.OpenAsync(_cancellationToken);

                if (joinString != String.Empty) joinString = $" {joinString} ";

                using (MySqlCommand command = new($"SELECT DISTINCT {columnsStr} FROM {table}{joinString}{(condition is not null ? $" WHERE {condition}" : String.Empty)};", connection))
                {
                    using (var reader = (MySqlDataReader)await command.ExecuteReaderAsync(_cancellationToken))
                    {
                        while (!_cancellationToken.IsCancellationRequested && await reader.ReadAsync(_cancellationToken))
                        {
                            Dictionary<string, string> row = [];

                            foreach (var column in columnsArr)
                                row.Add(column, reader.GetValue(column).ToString()!);

                            values.Add(row);
                        }
                    }
                }
            }

            return values.ToArray();
        }

        public static async Task<Dictionary<string, string>[]> GetSharedFilesAndDirectoriesAsync(string username)
        {
            return await ExecQueryAsync(new string[]
            {
                "path",
                "type",
                "candelete",
                "canrename",
                "canadd",
                "username"
            },
            "users", $"usergroups_id in {await GetGroupIdsStringFromUsernameAsync(username)} && owner_id != {await GetIDFromUsernameAsync(username)}", "JOIN sharedfiles ON users.id = owner_id JOIN shares ON sharedfiles.id = sharedfiles_id");
        }

        private static async Task<string> GetGroupIdsStringFromUsernameAsync(string username)
        {
            var tag = "id";
            var results = await ExecQueryAsync(tag, "usergroups", $"users_id = {await GetIDFromUsernameAsync(username)}", "JOIN usergroups_has_users ON id = usergroups_id");
            string output = "(";

            for (var i = 0; i < results.Length; i++)
            {
                output += results[i][tag];
                if (i < results.Length - 1)
                    output += ", ";
            }
            return $"{output})";
        }

        public static async Task<int> GetIDFromUsernameAsync(string username)
        {
            var tag = "id";

            return int.Parse((await ExecQueryAsync(tag, "users", $"username = \"{username}\"")).First()[tag]);
        }

        public static async Task<IEnumerable<Group>> GetNewUserGroupsAsync(string name) => (await GetUserGroupsAsync()).Where(x => x.Name == name);

        public static async Task<Group[]> GetUserGroupsAsync(string username) => (await GetUserGroupsAsync()).Where(x => x.Users.Any(y => y.Username == username) || x.Admin?.Username == username).ToArray();

        public static async Task<Group[]> GetUserGroupsAsync()
        {
            List<string> names = [];
            List<int> ids = [];
            List<User?> admins = [];
            List<User[]> users = [];
            Group[] groups;
            var results = await ExecQueryAsync(new string[]
            {
                "name",
                "id"
            }, "usergroups", "isuser = 0");

            foreach (var group in results)
            {
                var id = int.Parse(group["id"]);
                User? admin = null;
                var groupResult = await ExecQueryAsync(new string[]
                {
                    "name",
                    "surename",
                    "username",
                    "isadmin"
                }, "usergroups_has_users", $"usergroups_id = {id}", "JOIN users ON users_id = id");
                List<User> groupUsers = [];

                names.Add(group["name"]);
                ids.Add(int.Parse(group["id"]));
                foreach (var user in groupResult)
                {
                    User newUser = new()
                    {
                        Username = user["username"],
                        Name = user["name"],
                        Surename = user["surename"]
                    };

                    if (GetBoolFromTinyIntInString(user["isadmin"]))
                        admin = newUser;
                    else
                        groupUsers.Add(newUser);
                }

                users.Add(groupUsers.ToArray());
                admins.Add(admin);
            }

            groups = new Group[names.Count];
            for (var i = 0; i < names.Count; i++)
            {
                groups[i] = new Group()
                {
                    Name = names[i],
                    ID = ids[i],
                    Admin = admins[i],
                    Users = users[i]
                };
            }

            return groups;
        }

        public static async Task AddUserGroupAsyncAndReturnID(string groupName, string username)
        {
            var oldGroups = await GetUserGroupsAsync(username);
            int id;
            IEnumerable<Group> newGroups;

            await ExecNonQueryAsync($"INSERT INTO usergroups (name, isuser) VALUES (\"{groupName}\", 0);");

            newGroups = await GetNewUserGroupsAsync(groupName);
            id = newGroups.First(x => oldGroups.FirstOrDefault(y => x.ID == y.ID) == default(Group) && x.Name == groupName).ID;

            await ExecNonQueryAsync($"INSERT INTO usergroups_has_users VALUES ({id}, {await GetIDFromUsernameAsync(username)}, 1);");
        }

        public static async Task EditUserGroupAsync(Group group)
        {
            await ExecNonQueryAsync($"DELETE FROM usergroups_has_users WHERE usergroups_id = {group.ID} && isadmin = 0");

            foreach (var user in group.Users)
            {
                await ExecNonQueryAsync($"INSERT INTO usergroups_has_users VALUES ({group.ID}, {await GetIDFromUsernameAsync(user.Username)}, 0);");
            }

            await ExecNonQueryAsync($"UPDATE usergroups SET name = \"{group.Name}\" WHERE id = {group.ID};");
        }

        public static async Task<bool> IsUserVerifiedAsync(string username)
        {
            var tag = "verified";

            return (await ExecQueryAsync(tag, "users", $"username = \"{username}\""))
                .First()[tag] == "1"
                ? true
                : false;
        }

        public static async Task<string> GetUserGroupAdminAsync(int groupID)
        {
            var tag = "username";

            return (await ExecQueryAsync(tag, "users", $"usergroups_id = {groupID} && isadmin = 1", "JOIN usergroups_has_users ON id = users_id")).First()[tag];
        }

        public static async Task<User> GetUserInfoAsync(string username)
        {
            var result = (await ExecQueryAsync(new string[]
                {
                    "username",
                    "name",
                    "surename"
                }, "users", $"username = \"{username}\""))
                .First();

            return new()
            {
                Username = result["username"],
                Name = result["name"],
                Surename = result["surename"]
            };
        }

        public static async Task<bool> DoesUserExistAsync(string username) => (await ExecQueryAsync("username", "users", $"username = \"{username}\"")).Count() == 1;

        public async static Task<Share[]?> GetSharesAsync(string path, string username)
        {
            var groups = await GetUserGroupsAsync(username);
            var users = await GetUsersAsync(username);
            Dictionary<int, User> usersGroupIDs = [];
            bool isuser;

            foreach (var user in users)
            {
                usersGroupIDs.Add(int
                    .Parse((await ExecQueryAsync("id", "usergroups", $"name = \"{user.Username}\" && isuser = 1"))
                    .First()["id"]), user);
            }

            return (await ExecQueryAsync(new string[]
            {
                "usergroups_id",
                "canadd",
                "canrename",
                "candelete",
                "isuser",
                "type"
            }, "usergroups", $"path = \"{GetSQLPath(path)}\"", "JOIN shares ON usergroups.id = usergroups_id JOIN sharedfiles ON sharedfiles_id = sharedfiles.id"))
            .Select(x => new Share()
            {
                CanAdd = GetBoolFromTinyIntInString(x["canadd"]),
                CanRename = GetBoolFromTinyIntInString(x["canrename"]),
                CanDelete = GetBoolFromTinyIntInString(x["candelete"]),
                User = (isuser = GetBoolFromTinyIntInString(x["isuser"])) ? usersGroupIDs[int.Parse(x["usergroups_id"])] : null,
                Group = !isuser ? groups.First(y => y.ID == int.Parse(x["usergroups_id"])) : null,
                Type = Enum.Parse<Unit>(x["type"])
            })
            .ToArray();
        }

        public static async Task<Dictionary<string, Share[]?>> GetMyFileSharesAsync(string username)
        {
            Dictionary<string, Share[]?> output = [];
            var paths = (await ExecQueryAsync("path", "sharedfiles", $"owner_id = {await GetIDFromUsernameAsync(username)}")).Select(x => GetCSPath(x["path"]));

            foreach (var path in paths)
            {
                output.Add(path, await GetSharesAsync(path, username));
            }

            return output;
        }

        public static async Task<long> GetUserSpace(string username)
        {
            string tag = "space";

            return long
                .Parse((await ExecQueryAsync(tag, "users", $"username = \"{username}\""))
                .First()[tag]);
        }

        public static async Task ChangeShares(string username, string path, Unit unit, Share[] newShares, Share[]? oldShares)
        {
            var userID = await GetIDFromUsernameAsync(username);
            var sqlPath = GetSQLPath(path);

            if (newShares.Length > 0)
            {
                if ((await ExecQueryAsync("id", "sharedfiles", $"path = \"{sqlPath}\"")).Count() == 0)
                {
                    await ExecNonQueryAsync($"INSERT INTO sharedfiles (path, type, owner_id) VALUES (\"{sqlPath}\", \"{Enum.GetName(unit)}\", {userID})");
                }

                var fileID = int.Parse((await ExecQueryAsync("id", "sharedfiles", $"path = \"{sqlPath}\"")).First()["id"]);

                Array.ForEach(oldShares ?? [], async x =>
                {
                    if (!newShares.Contains(x))
                    {
                        await ExecNonQueryAsync($"DELETE FROM shares WHERE usergroups_id = {(x.Group is not null ? x.Group.ID : (await ExecQueryAsync("id", "usergroups", $"isuser = 1 && users_id = (SELECT id FROM users WHERE username = \"{x.User?.Username}\")", "JOIN usergroups_has_users ON id = usergroups_id")).First()["id"])} && sharedfiles_id = {fileID}");
                    }
                });

                Array.ForEach(newShares ?? [], async x =>
                {
                    if (oldShares.Contains(x))
                    {
                        await ExecNonQueryAsync($"DELETE FROM shares WHERE usergroups_id = {(x.Group is not null ? x.Group.ID : (await ExecQueryAsync("id", "usergroups", $"isuser = 1 && users_id = (SELECT id FROM users WHERE username = \"{x.User?.Username}\")", "JOIN usergroups_has_users ON id = usergroups_id")).First()["id"])} && sharedfiles_id = {fileID}");
                        await ExecNonQueryAsync($"INSERT INTO shares (sharedfiles_id, usergroups_id, candelete, canrename, canadd) VALUES ({fileID}, {(x.Group is not null ? x.Group.ID : (await ExecQueryAsync("id", "usergroups", $"isuser = 1 && users_id = (SELECT id FROM users WHERE username = \"{x.User?.Username}\")", "JOIN usergroups_has_users ON id = usergroups_id")).First()["id"])}, {(x.CanDelete ? 1 : 0)}, {(x.CanRename ? 1 : 0)}, {(x.CanAdd ? 1 : 0)})");
                    }
                    else
                    {
                        await ExecNonQueryAsync($"INSERT INTO shares (sharedfiles_id, usergroups_id, candelete, canrename, canadd) VALUES ({fileID}, {(x.Group is not null ? x.Group.ID : (await ExecQueryAsync("id", "usergroups", $"isuser = 1 && users_id = (SELECT id FROM users WHERE username = \"{x.User?.Username}\")", "JOIN usergroups_has_users ON id = usergroups_id")).First()["id"])}, {(x.CanDelete ? 1 : 0)}, {(x.CanRename ? 1 : 0)}, {(x.CanAdd ? 1 : 0)})");
                    }
                });
            }
            else
                await ExecNonQueryAsync($"DELETE FROM sharedfiles WHERE path = \"{sqlPath}\"");
        }

        private static string GetSQLPath(string path) => path.Replace("\\", "\\\\\\\\");
        private static string GetCSPath(string path) => path.Replace("\\\\", "\\");
    }
}
