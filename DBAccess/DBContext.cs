using MySql.Data.MySqlClient;

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
            await ExecNonQueryAsync(new string[]
            {
                $"INSERT INTO users (username, password_hash, name, surename) VALUES (\"{username}\", \"{hash}\", \"{name}\", \"{surename}\");",
                $"INSERT INTO usergroups (name, isuser) VALUES (\"{username}\", 1)"
            });

            // add to the many to many table
        }

        public static async Task<string[]> GetUsernamesAsync()
        {
            List<string> usernames = new();

            using (MySqlDataReader reader = await ExecQueryAsync("SELECT username FROM users;"))
            {
                while (await reader.ReadAsync(_cancellationToken))
                    usernames.Add(reader.GetString("username"));
            }

            return usernames.ToArray();
        }

        public static async Task<string?> GetPasswordHashAsync(string username)
        {
            using (MySqlDataReader reader = await ExecQueryAsync($"SELECT password_hash FROM users WHERE username = \"{username}\";"))
            {
                if (await reader.ReadAsync(_cancellationToken))
                    return reader.GetString("password_hash");
            }

            return null;
        }

        private static async Task<MySqlDataReader> ExecQueryAsync(string query)
        {

            using (MySqlConnection connection = new(_connectionString))
            {
                connection.Open();

                using (MySqlCommand command = new(query, connection))
                {
                    return (MySqlDataReader)await command.ExecuteReaderAsync();
                }
            }
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
                connection.Open();

                for (var i = 0; i == 0 || (isTArray && i < commandsArr!.Length); i++)
                {
                    using (MySqlCommand command = new(isTString ? commandStr : commandsArr![i], connection))
                    {
                        await command.ExecuteNonQueryAsync(_cancellationToken);
                    }
                }
            }
        }
    }
}
