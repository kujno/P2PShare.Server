using MySql.Data.MySqlClient;

namespace P2PShare.Server.DatabaseAccess
{
    public static class DatabaseContext
    {
        private static CancellationToken _cancellationToken;
        private static string? _connectionString;

        public static async Task InitAsync(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            await DatabaseCredentials.InitAsync(cancellationToken);
            _connectionString = $"Server={DatabaseCredentials.Server};Database={DatabaseCredentials.Database};User ID={DatabaseCredentials.UserID};Password={DatabaseCredentials.Password};";
        }

        public static async Task AddUserAsync(string username, string hash, string name, string surename)
        {
            using (MySqlConnection connection = new(_connectionString))
            {
                connection.Open();

                using (MySqlCommand command = new($"insert into users (username, password_hash, name, surename) values (\"{username}\", \"{hash}\", \"{name}\", \"{surename}\");", connection))
                {
                    await command.ExecuteNonQueryAsync(_cancellationToken);
                }
            }
        }
    }
}
