using MySql.Data.MySqlClient;
using System.Data;

namespace P2PShare.Server.DBAccess
{
    public static class DBContext
    {
        private static CancellationToken _cancellationToken;
        private static string? _connectionString;

        public static bool GetBoolFromTinyIntInString(string input) => input == "1";

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

            await ExecNonQueryAsync($"INSERT INTO usergroups_has_users (usergroups_id, users_id) VALUES ({idGroup}, {idUser})");
        }

        public static async Task<string[]> GetUsernamesAsync()
        {
            List<string> usernames = [];
            var tag = "username";

            Array.ForEach(await ExecQueryAsync(tag, "users"), x => usernames.Add(x[tag]!));

            return usernames.ToArray();
        }

        public static async Task<string?> GetPasswordHashAsync(string username)
        {
            var tag = "password_hash";
            var results = await ExecQueryAsync(tag, "users", $"username = \"{username}\"");

            if (results.Length == 1)
                return results[0][tag];

            return null;
        }

        private static async Task ExecNonQueryAsync<T>(T commands)
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
                columnsArr = new string[] { (string)(object)columns! };
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
                "owner_id"
            },
            "sharedfiles", $"usergroups_id in {await GetGroupIdsStringFromUsernameAsync(username)}", "JOIN shares ON id = sharedfiles_id");
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
    }
}
