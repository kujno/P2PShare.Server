using P2PShare.Server.Models;

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

                // command output + logic
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

        private static async Task CommandExecAsync(Command? command)
        {
            switch (command)
            {
                case Command.Start:
                    // Start server logic here
                    break;
                case Command.Stop:
                    // Stop server logic here
                    break;
                case Command.Help:
                    // help output
                    break;
                case Command.Exit:
                    // dispose resources here
                    break;

                default:
                    // display unrecognized command
                    break;
            }
        }

        private static void CommandDisplayOutput(string output)
        {
            
        }
    }
}