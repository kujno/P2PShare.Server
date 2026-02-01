using System.Text.Json.Nodes;

namespace P2PShare.Server.DatabaseAccess
{
    public static class DatabaseCredentials
    {
        public static string? Server { get; private set; }
        public static string? Database { get; private set; }
        public static string? UserID { get; private set; }
        public static string? Password { get; private set; }

        public static async Task InitAsync(CancellationToken cancellationToken)
        {
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

            Server = databaseCredentials[nameof(Server)]?.GetValue<string>();
            Database = databaseCredentials[nameof(Database)]?.GetValue<string>();
            UserID = databaseCredentials[nameof(UserID)]?.GetValue<string>();
            Password = databaseCredentials[nameof(Password)]?.GetValue<string>();
        }
    }
}
