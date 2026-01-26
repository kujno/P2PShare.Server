using MySql.Data.MySqlClient;

namespace P2PShare.Server.DatabaseAccess
{
    public class DatabaseContext
    {
        private DatabaseCredentials? _credentials;
        private CancellationToken _cancellationToken;
        private string? _connectionString;

        private DatabaseContext(CancellationToken cancellationToken) => _cancellationToken = cancellationToken;

        public static async Task<DatabaseContext> CreateAsync(CancellationToken cancellationToken)
        {
            DatabaseContext context = new(cancellationToken);
            
            context._credentials = await DatabaseCredentials.GetAsync(cancellationToken);
            context._connectionString = $"Server={context._credentials.Server};Database={context._credentials.Database};User ID={context._credentials.UserID};Password={context._credentials.Password};";
            
            return context;
        }

        public async Task AddUserAsync(string username, string hash)
        {    
            using (MySqlConnection connection = new(_connectionString))
            {
                connection.Open();

                using (MySqlCommand command = new($"INSERT INTO users VALUES (\"{username}\", \"{hash}\");", connection))
                {
                    await command.ExecuteNonQueryAsync();
                }
            }
        }
    }
}
