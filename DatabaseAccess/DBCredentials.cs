using Newtonsoft.Json;
using System.Text;

namespace P2PShare.Server.DatabaseAccess
{
    public class DBCredentials
    {
        public static string DBCredentialsFileName { get; } = "DBCredentials.json";

        public string? Server { get; init; }
        public string? Database { get; init; }
        public string? UserID { get; init; }
        public string? Password { get; init; }

        public static async Task<DBCredentials> GetAsync(CancellationToken cancellationToken)
        {
            DBCredentials credentials;

            try
            {
                var json = await File.ReadAllTextAsync(DBCredentialsFileName, cancellationToken);
                credentials = JsonConvert.DeserializeObject<DBCredentials>(json)!;
            }
            catch (Exception ex)
            {
                throw ex is OperationCanceledException ?
                    ex :
                    new FormatException($"Failed to convert {DBCredentialsFileName}.", ex);
            }

            return credentials;
        }

        public static async Task SaveToFileAsync(string server, string database, string userID, string password)
        {
            using (var stream = new FileStream(DBCredentialsFileName, FileMode.Create))
                await stream.WriteAsync(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new DBCredentials()
                {
                    Server = server,
                    Database = database,
                    UserID = userID,
                    Password = password
                })));
        }
    }
}
