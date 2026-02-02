namespace P2PShare.Server
{
    class Program
    {
        private static Task? _server;
        private static CancellationTokenSource? _cancellationTokenSource;

        private static readonly string _helpSuggestionText = $"Use \"{Command.Help.ToString().ToLower()}\" command to display the list of all of the commands", _fullHelpSuggestionText = $"--- {_helpSuggestionText} ---";
        private static readonly Dictionary<Command, string> _commandDescriptions = new()
        {
            { Command.Start, "Starts the server." },
            { Command.Stop, "Stops the server." },
            { Command.Help, "Displays all of the commands." },
            { Command.Exit, "Exits the application." }
        };

        private static void ChangeConsoleColor(ConsoleColor color) => Console.ForegroundColor = color;

        static async Task Main()
        {
            Command? command;

            DisplayHeader();

            do
            {
                command = await CommandGet();

                CommandExec(command);
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
                input = (await Console.In.ReadLineAsync())?.Trim();
            }
            while (String.IsNullOrEmpty(input));

            return input.Length > 1 && Enum.TryParse<Command>($"{input.Substring(0, 1).ToUpper()}{input.Substring(1).ToLower()}", out command) ? command : null;
        }

        private static void CommandExec(Command? command)
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
                    string output = String.Empty;

                    foreach (var commandAndDesc in _commandDescriptions) output += $"{commandAndDesc.Key.ToString().ToLower()} - {commandAndDesc.Value}\n";

                    DisplayCommandOutput(output.Trim());
                    break;
                case Command.Exit:
                    // nothing to do here
                    break;

                default:
                    DisplayCommandOutput($"Unrecognized command. {_helpSuggestionText}!", ConsoleColor.Red);
                    break;
            }
        }

        private static void DisplayCommandOutput(string output) => DisplayCommandOutput(output, ConsoleColor.White);

        private static void DisplayCommandOutput(string output, ConsoleColor color)
        {
            Console.Clear();

            DisplayHeader();

            ChangeConsoleColor(color);
            Console.WriteLine($"{output}\n");
        }

        private static void DisplayHeader()
        {
            var running = _server is not null && _server.Status == TaskStatus.Running;

            ChangeConsoleColor(ConsoleColor.Gray);
            Console.WriteLine(_fullHelpSuggestionText);

            // status servera
            ChangeConsoleColor(ConsoleColor.White);
            Console.Write("Server: ");
            ChangeConsoleColor(running ? ConsoleColor.Green : ConsoleColor.Red);
            Console.WriteLine(running ? "Running" : "Not running");

            // koniec headeru
            ChangeConsoleColor(ConsoleColor.White);
            for (int i = 0; i < _fullHelpSuggestionText.Length; i++) Console.Write('-');
            for (int i = 0; i < 2; i++) Console.WriteLine();
        }

        private static async Task StartServerAsync()
        {
            // this will handle the creation and management of the ConnectionServer instances
        }
    }
}