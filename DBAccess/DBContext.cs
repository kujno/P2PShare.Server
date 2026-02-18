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

        public static async Task DeleteSharedFile(string path) => await ExecNonQueryAsync($"DELETE FROM sharedfiles WHERE path = \"{path}\"");

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

        public static async Task<User[]> GetUsersAsync()
        {
            List<User> users = [];

            Array.ForEach(await ExecQueryAsync(new string[]
            {
                "username",
                "name",
                "surename"
            }, "users"), x => users.Add(new()
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
                columnsStr += columnsArr[i];
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
            "users", $"usergroups_id in {await GetGroupIdsStringFromUsernameAsync(username)}", "JOIN sharedfiles ON users.id = owner_id JOIN shares ON sharedfiles.id = sharedfiles_id");
        }

        private static async Task<string> GetGroupIdsStringFromUsernameAsync(string username)
        {
            var tag = "id";
            var results = await ExecQueryAsync(tag, "usergroups", $"users_id = {await GetIDFromUsernameAsync(username)}", "JOIN usergroups_hash_users ON id = usergroups_id");
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

        public static async Task<Group[]> GetUserGroupsAsync(string username) => (await GetUserGroupsAsync()).Where(x => x.Users.Any(y => y.Username == username) || x.Admin.Username == username).ToArray();

        public static async Task<Group[]> GetUserGroupsAsync()
        {
            List<int> groupIDs = [];
            List<string> groupNames = [];
            List<User> admins = [];
            List<Group> output = [];

            Array.ForEach(await ExecQueryAsync(new string[]
            {
                "usergroups.name",
                "usergroups.id",
                "users.username",
                "users.name",
                "users.surename"
            }, "usergroups", null, "JOIN usergroups_has_users ON usergroups.id = usergroups_id JOIN users ON users_id = users.id"), x =>
            {
                groupNames.Add(x["name"]);
                groupIDs.Add(int.Parse(x["id"]));
                admins.Add(new()
                {
                    Username = x["users.username"],
                    Name = x["users.name"],
                    Surename = x["users.surename"]
                });
            });

            for (var i = 0; i < groupNames.Count; i++)
            {
                List<User> users = [];

                Array.ForEach(await ExecQueryAsync(new string[]
                {
                    "username",
                    "name",
                    "surename"
                }, "users", $"usergroups_id = {groupIDs[i]} && isadmin = 0", "JOIN usergroups_has_users ON id = users_id"), x =>
                {
                    users.Add(new()
                    {
                        Username = x["username"],
                        Name = x["name"],
                        Surename = x["surename"]
                    });
                });

                output.Add(new()
                {
                    Name = groupNames[i],
                    Admin = admins[i],
                    Users = users.ToArray(),
                });
            }

            return output.ToArray();
        }

        public static async Task AddUserGroupAsync(Group group)
        {
            int groupID, adminID = await GetIDFromUsernameAsync(group.Admin.Username);
            List<int> userIDs = [];

            foreach (var user in group.Users)
                userIDs.Add(await GetIDFromUsernameAsync(user.Username));

            await ExecNonQueryAsync($"INSERT INTO usergroups (name) VALUES (\"{group.Name}\");");

            groupID = int.Parse((await ExecQueryAsync("id", "usergroups", $"name = {group.Name}")).First()["id"]);

            await ExecNonQueryAsync($"INSERT INTO usergroups_has_users VALUES ({groupID}, {adminID}, 1);");

            userIDs.ForEach(async x => await ExecNonQueryAsync($"INSERT INTO usergroups_has_users (usergroups_id, users_id) VALUES ({groupID}, {x});"));
        }

        public static async Task<bool> IsUserVerifiedAsync(string username)
        {
            var tag = "verified";

            return (await ExecQueryAsync(tag, "users", $"username = \"{username}\""))
                .First()[tag] == "1"
                ? true
                : false;
        }

        public static async Task<string> GetUserGroupAdminAsync(string groupName)
        {
            var tag = "username";

            return (await ExecQueryAsync(tag, "users", $"usergroups.name = \"{groupName}\" && isadmin = 1", "JOIN usergroups_has_users ON users.id = users_id JOIN usergroups ON usergroups_id = usergroups.id")).First()[tag];
        }

        public static async Task UpdateUsersInGroupAsync(Group oldGroup, Group newGroup)
        {
            string[] oldUsers = oldGroup.GetUsersUsernames(), newUsers = newGroup.GetUsersUsernames();
            var tag = "id";
            var groupID = (await ExecQueryAsync(tag, "groups", $"name = \"{newGroup.Name}\"")).First()[tag];


            Array.ForEach(oldUsers, async x =>
            {
                var found = false;

                for (var i = 0; i < newUsers.Length && !found; i++)
                {
                    if (x == newUsers[i])
                        found = true;
                }

                if (!found)
                    await ExecNonQueryAsync($"DELETE FROM usergroups_has_users WHERE username = \"{x}\" && isadmin = 0 JOIN users ON users_id = id;");
            });

            Array.ForEach(newUsers, async x =>
            {
                var found = false;

                for (var i = 0; i < oldUsers.Length && !found; i++)
                {
                    if (x == newUsers[i])
                        found = true;
                }

                if (!found)
                    await ExecNonQueryAsync($"INSERT INTO usergroups_has_users (usergroups_id, users_id) VALUES ({groupID}, {await GetIDFromUsernameAsync(x)};");
            });
        }
    }
}
