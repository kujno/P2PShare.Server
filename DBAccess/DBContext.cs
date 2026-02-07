using MySql.Data.MySqlClient;

namespace P2PShare.Server.DBAccess
{
    public static class DBContext
    {
        private static CancellationToken _cancellationToken;
        private static string? _connectionString;

        public static async Task InitAsync(CancellationToken cancellationToken)
        {
            DBCredentials credentials;

            _cancellationToken = cancellationToken;
            credentials = await DBCredentials.GetAsync(cancellationToken);
            _connectionString = $"Server={credentials.Server};Database={credentials.Database};User ID={credentials.UserID};Password={credentials.Password};";
        }

        public static async Task AddUserAsync(string username, string hash, string name, string surename) => await ExecCommand($"insert into users (username, password_hash, name, surename) values (\"{username}\", \"{hash}\", \"{name}\", \"{surename}\");", false);

        public static async Task<string[]> GetUsernamesAsync()
        {
            List<string> usernames = new();

            using (MySqlDataReader reader = (await ExecCommand("select username from users;", true))!)
            {
                while (await reader.ReadAsync(_cancellationToken))
                    usernames.Add(reader.GetString("username"));
            }

            return usernames.ToArray();
        }

        public static async Task<string?> GetPasswordHashAsync(string username)
        {
            using (MySqlDataReader reader = (await ExecCommand($"select password_hash from users where username = \"{username}\";", true))!)
            {
                if (await reader.ReadAsync(_cancellationToken))
                    return reader.GetString("password_hash");
            }

            return null;
        }

        private static async Task<MySqlDataReader?> ExecCommand(string commandString, bool query)
        {
            using (MySqlConnection connection = new(_connectionString))
            {
                connection.Open();

                using (MySqlCommand command = new(commandString, connection))
                {
                    if (query)
                        return (MySqlDataReader)await command.ExecuteReaderAsync();

                    await command.ExecuteNonQueryAsync(_cancellationToken);
                    return null;
                }
            }
        }
    }
}
