using System.Text.Json.Nodes;

namespace P2PShare.Server.DatabaseAccess
{
    public class DatabaseCredentials
    {
        public string? Server { get; private set; }
        public string? Database { get; private set; }
        public string? UserID { get; private set; }
        public string? Password { get; private set; }

        private DatabaseCredentials() { }

        public static async Task<DatabaseCredentials> GetAsync(CancellationToken cancellationToken)
        {
            DatabaseCredentials credentials = new();
            JsonNode? databaseCredentials;

            try
            {
                databaseCredentials = JsonNode.Parse(await File.ReadAllTextAsync("AppSettings.json", cancellationToken))?["Database"];
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to convert AppSettings.json.", ex);
            }

            if (databaseCredentials is null) throw new Exception("AppSettings.json is invalid or missing.");

            credentials.Server = databaseCredentials["Server"]?.GetValue<string>();
            credentials.Database = databaseCredentials["Database"]?.GetValue<string>();
            credentials.UserID = databaseCredentials["UserID"]?.GetValue<string>();
            credentials.Password = databaseCredentials["Password"]?.GetValue<string>();

            return credentials;
        }
    }
}
