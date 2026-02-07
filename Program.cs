using P2PShare.Server.DBAccess;
using P2PShare.Server.Models;

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

            if (!File.Exists(DBCredentials.DBCredentialsFileName)) await DBCredentials.SaveToFileAsync(GetString("Enter database server"), GetString("Enter database name"), GetString("Enter database user ID"), GetString("Enter database password"));

            do
            {
                command = await CommandGet();

                await CommandExecAsync(command);
            }
            while (command is not Command.Exit);
        }

        private static string GetString(string message)
        {
            string? input = null;

            for (var i = false; String.IsNullOrEmpty(input); i = true)
            {
                if (i) DisplayCommandOutput("Input can't be empty!", ConsoleColor.Red);

                ChangeConsoleColor(ConsoleColor.White);
                Console.Write($"{message}: ");
                ChangeConsoleColor(ConsoleColor.Yellow);
                input = Console.ReadLine()?.Trim() ?? String.Empty;
            }

            DisplayHeader();

            return input;
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

        private static async Task CommandExecAsync(Command? command)
        {
            switch (command)
            {
                case Command.Start:
                    _server = StartServerAsync();

                    DisplayCommandOutput("Server started.");

                    break;
                case Command.Stop:
                    _cancellationTokenSource?.Cancel();

                    // Čakanie, kým server skončí.
                    await _server!;

                    DisplayCommandOutput("Server stopped.");

                    break;
                case Command.Help:
                    string output = String.Empty;

                    foreach (var commandAndDesc in _commandDescriptions) output += $"{commandAndDesc.Key.ToString().ToLower()} - {commandAndDesc.Value}\n";

                    DisplayCommandOutput(output.Trim());
                    break;
                case Command.Exit:
                    // Tu netreba nič.
                    break;

                default:
                    DisplayCommandOutput($"Unrecognized command. {_helpSuggestionText}!", ConsoleColor.Red);
                    break;
            }
        }

        private static void DisplayCommandOutput(string output) => DisplayCommandOutput(output, ConsoleColor.White);

        private static void DisplayCommandOutput(string output, ConsoleColor color)
        {
            DisplayHeader();

            ChangeConsoleColor(color);
            Console.WriteLine($"{output}\n");
        }

        private static void DisplayHeader()
        {
            bool running = _server is not null;

            if (running)
            {
                Array.ForEach(new TaskStatus[]
                {
                    TaskStatus.Faulted,
                    TaskStatus.Canceled,
                    TaskStatus.RanToCompletion
                }, x =>
                {
                    if (_server?.Status == x) running = false;
                });
            }

            Console.Clear();

            ChangeConsoleColor(ConsoleColor.Gray);
            Console.WriteLine(_fullHelpSuggestionText);

            // Status servera.
            ChangeConsoleColor(ConsoleColor.White);
            Console.Write("Server: ");
            ChangeConsoleColor(running ? ConsoleColor.Green : ConsoleColor.Red);
            Console.WriteLine(running ? "Running" : "Not running");

            // Koniec hlavičky.
            ChangeConsoleColor(ConsoleColor.White);
            for (int i = 0; i < _fullHelpSuggestionText.Length; i++) Console.Write('-');
            for (int i = 0; i < 2; i++) Console.WriteLine();
        }

        private static async Task StartServerAsync()
        {
            List<ConnectionServer.ConnectionServer> connections = new();

            _cancellationTokenSource = new();
            await DBContext.InitAsync(_cancellationTokenSource.Token);

            while (!_cancellationTokenSource!.IsCancellationRequested)
            {
                ConnectionServer.ConnectionServer connection = new()
                {
                    CancellationToken = _cancellationTokenSource.Token
                };
                ConnectionServer.ConnectionServer[] doneConnections;

                await connection.InitAsync();
                connection.Serve();

                connections.Add(connection);

                doneConnections = connections.Where(x => x.IsDone).ToArray();
                foreach (var doneConnection in doneConnections)
                {
                    try
                    {
                        doneConnection.Dispose();
                    }
                    catch
                    {
                    }

                    connections.Remove(doneConnection);
                }
            }
        }
    }
}
