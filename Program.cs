using P2PShare.Server.Models;

namespace P2PShare.Server
{
    class Program
    {
        private static bool _running = false;
        private static string _helpSuggestionText = $"Use {Command.Help.ToString().ToLower()} command to display the list of commands", _fullHelpSuggestionText = $"--- {_helpSuggestionText} ---";

        private static void ChangeConsoleColor(ConsoleColor color) => Console.ForegroundColor = color;

        static async Task Main()
        {
            Command? command;

            DisplayHelpSuggestion();

            do
            {
                command = await CommandGet();

                await CommandExecAsync(command);
            }
            while (command is not Command.Exit);
        }

        private static async Task<Command?> CommandGet()
        {
            string? input;
            Command command;

            do
            {
                ChangeConsoleColor(ConsoleColor.White);
                Console.Write("P2PShare.Server>");

                ChangeConsoleColor(ConsoleColor.Yellow);
                input = (await Console.In.ReadLineAsync())?.Trim().ToLower();
            }
            while (String.IsNullOrEmpty(input));

            return Enum.TryParse<Command>(input, out command) ? command : null;
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
                    DisplayCommandOutput($"Unrecognized command. {_helpSuggestionText}!", ConsoleColor.Red);
                    break;
            }
        }

        //private static void DisplayCommandOutput(string output) => DisplayCommandOutput(output, ConsoleColor.White);

        private static void DisplayCommandOutput(string output, ConsoleColor color)
        {
            Console.Clear();

            DisplayHelpSuggestion();

            ChangeConsoleColor(ConsoleColor.White);
            Console.Write("Server: ");
            ChangeConsoleColor(_running ? ConsoleColor.Green : ConsoleColor.Red);
            Console.WriteLine(_running ? "Running" : "Not running");

            ChangeConsoleColor(ConsoleColor.White);
            for (int i = 0; i < _fullHelpSuggestionText.Length; i++) Console.Write('-');
            for (int i = 0; i < 2; i++) Console.WriteLine();

            ChangeConsoleColor(color);
            Console.WriteLine($"{output}\n");
        }

        private static void DisplayHelpSuggestion()
        {
            ChangeConsoleColor(ConsoleColor.Gray);

            Console.WriteLine(_fullHelpSuggestionText);
        }
    }
}