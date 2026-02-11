using MySql.Data.MySqlClient;
using System.Data;

namespace P2PShare.Server.DBAccess
{
    public static class DBContext
    {
        private static CancellationToken _cancellationToken;
        private static string? _connectionString;

        public static async Task InitAsync(DBCredentials credentials, CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            _connectionString = $"Server={credentials.Server};Database={credentials.Database};User ID={credentials.UserID};Password={credentials.Password};";
        }

        public static async Task AddUserAsync(string username, string hash, string name, string surename)
        {
            var tag = "id";
            int? idUser = null, idGroup = null;

            await ExecNonQueryAsync(new string[]
            {
                $"INSERT INTO users (username, password_hash, name, surename) VALUES (\"{username}\", \"{hash}\", \"{name}\", \"{surename}\");",
                $"INSERT INTO usergroups (name, isuser) VALUES (\"{username}\", 1);"
            });

            idUser = int.Parse((await ExecQueryAsync(tag, "users", $"username = \"{username}\""))[0][tag]!);
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

        private static async Task<Dictionary<string, string?>[]> ExecQueryAsync<T>(T columns, string table, string? condition = null)
        {
            var tType = columns?.GetType();
            string[] columnsArr;

            if (tType == typeof(string[]))
                columnsArr = (string[])(object)columns!;
            else if (tType == typeof(string))
                columnsArr = new string[] { (string)(object)columns! };
            else
                throw new NotImplementedException();

            List<Dictionary<string, string?>> values = [];
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

                using (MySqlCommand command = new($"SELECT DISTINCT {columnsStr} FROM {table}{(condition is not null ? $" WHERE {condition}" : String.Empty)};", connection))
                {
                    using (var reader = (MySqlDataReader)await command.ExecuteReaderAsync(_cancellationToken))
                    {
                        while (!_cancellationToken.IsCancellationRequested && await reader.ReadAsync(_cancellationToken))
                        {
                            Dictionary<string, string?> row = [];

                            foreach (var column in columnsArr)
                                row.Add(column, reader.GetValue(column).ToString());

                            values.Add(row);
                        }
                    }
                }
            }

            return values.ToArray();
        }
    }
}
