using MySql.Data.MySqlClient;
using P2PShare.Libs;

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

        public static async Task<string> GetUsernamesAsync()
        {
            using (MySqlConnection connection = new(_connectionString))
            {
                connection.Open();

                using (MySqlCommand command = new("select username from users;", connection))
                {
                    using (var reader = await command.ExecuteReaderAsync(_cancellationToken))
                    {
                        using (var textReader = reader.GetTextReader(0))
                        {
                            var buffer = new char[ConnectionHandler.BufferSize];
                            var read = await textReader.ReadAsync(buffer, _cancellationToken);

                            return new string(buffer, 0, read);
                        }
                    }
                }
            }
        }
    }
}
