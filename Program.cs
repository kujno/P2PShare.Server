namespace P2PShare.Server
{
    class Program
    {
        static async Task Main()
        {
            string command;

            do
            {
                command = await GetCommand();

                switch (command)
                {
                    case "start":
                        // Start server logic here
                        break;
                    case "stop":
                        // Stop server logic here
                        break;
                    case "exit":
                        // dispose resources here
                        break;
                    
                    default:
                        // display help probably
                        break;
                }
            }
            while (command != "exit");
        }

        private static async Task<string> GetCommand()
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