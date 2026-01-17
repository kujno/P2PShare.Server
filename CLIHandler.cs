namespace P2PShare.Server
{
    public class CLIHandler
    {
        public static async Task<string> GetCommand()
        {
            string? input;

            do
            {
                Console.Write("P2PShare.Server>");

                input = (await Console.In.ReadLineAsync())?.Trim().ToLower();
            }
            while (String.IsNullOrEmpty(input));

            return input;
        }
    }
}
