using Newtonsoft.Json;
using P2PShare.Server.DBAccess;

namespace P2PShare.Server
{
    public class AppSettings
    {
        public static string AppSettingsFileName { get; } = "appsettings.json";

        public required string RootFolderPath { get; init; }
        public required DBCredentials DBCredentials { get; init; }

        public async Task SaveToFileAsync() => await File.WriteAllTextAsync(AppSettingsFileName, JsonConvert.SerializeObject(this, Formatting.Indented));

        public static async Task<AppSettings> GetAsync(CancellationToken cancellationToken)
        {
            AppSettings appSettings;

            try
            {
                var json = await File.ReadAllTextAsync(AppSettingsFileName, cancellationToken);
                appSettings = JsonConvert.DeserializeObject<AppSettings>(json)!;
            }
            catch (Exception ex)
            {
                throw ex is OperationCanceledException ?
                    ex :
                    new FormatException($"Failed to convert {AppSettingsFileName}.", ex);
            }

            return appSettings;
        }
    }
}
