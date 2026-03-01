using Org.BouncyCastle.Crypto;
using P2PShare.Libs;
using P2PShare.Server.DBAccess;
using P2PShare.Server.Models;
using System.Text;

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
            { Command.Clear, "Clear the the command prompt and display server status."},
            { Command.Newadmin, "Create new admin credentials and delete the old."},
            { Command.Exit, "Exits the application." }
        };

        private static void ChangeConsoleColor(ConsoleColor color) => Console.ForegroundColor = color;

        static async Task Main()
        {
            Command? command;
            AppSettings appSettings;

            ConnectionServer.ConnectionServer.ConnectionError += OnConnectionError;

            try
            {
                while (InterfaceHandling.GetUpInterfaces().Length == 0)
                {
                    DisplayHeader();

                    ChangeConsoleColor(ConsoleColor.Red);

                    Console.WriteLine("Server not connected to the network. Connect and try again by pressing any key!");

                    Console.ReadKey();
                }

                DisplayHeader();

                if (!File.Exists(AppSettings.AppSettingsFileName))
                {
                    appSettings = new AppSettings()
                    {
                        RootFolderPath = GetString("Enter root folder path"),
                        DBCredentials = new DBCredentials()
                        {
                            Server = GetString("Enter database server"),
                            Database = GetString("Enter database name"),
                            UserID = GetString("Enter database user ID"),
                            Password = GetString("Enter database password")
                        }
                    };

                    await appSettings.SaveToFileAsync();
                }
                else appSettings = await AppSettings.GetAsync(CancellationToken.None);

                Directory.CreateDirectory($"{appSettings.RootFolderPath}\\temp");

                do
                {
                    command = await CommandGet();

                    await CommandExecAsync(command);
                }
                while (command is not Command.Exit);
            }
            catch
            {
                DisplayCommandOutput("Server failed. Press any key to exit!");

                Console.ReadKey();
            }
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
                    string message;
                    ConsoleColor color = ConsoleColor.White;

                    if (IsServerRunning())
                    {
                        message = "Server is already running. If restart's been intended, stop and start the server again!";

                        color = ConsoleColor.Red;
                    }
                    else
                    {
                        _server = StartServerAsync();

                        message = "Server started.";
                    }

                    DisplayCommandOutput(message, color);

                    break;
                case Command.Stop:
                    _cancellationTokenSource?.Cancel();

                    if (IsServerRunning())
                    {
                        // Čakanie, kým server skončí.
                        await _server!;

                        message = "Server stopped.";
                    }
                    else message = "Server is not running.";

                    DisplayCommandOutput(message);

                    break;
                case Command.Help:
                    string output = String.Empty;

                    foreach (var commandAndDesc in _commandDescriptions)
                        output += $"{commandAndDesc.Key.ToString().ToLower()} - {commandAndDesc.Value}\n";

                    DisplayCommandOutput(output.Trim());
                    break;
                case Command.Clear:
                    DisplayHeader();
                    break;
                case Command.Exit:
                    // Tu netreba nič.
                    break;
                case Command.Newadmin:
                    AppSettings appSettings = await AppSettings.GetAsync(CancellationToken.None);

                    await DBContext.InitAsync(appSettings.DBCredentials, CancellationToken.None);

                    await DBContext.ExecNonQueryAsync("DELETE FROM users WHERE username = \"admin\";");

                    await DBContext.AddUserAsync("admin", Hasher.Hash(GetString("Username is admin.\nPassword")), String.Empty, String.Empty);

                    DisplayCommandOutput("New admin credentials created.");

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
            var running = IsServerRunning();

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
            List<ConnectionServer.ConnectionServer> connections = [];

            _cancellationTokenSource = new();
            try
            {
                var appSettings = await AppSettings.GetAsync(_cancellationTokenSource.Token);

                await DBContext.InitAsync(appSettings.DBCredentials, _cancellationTokenSource.Token);

                while (!_cancellationTokenSource!.IsCancellationRequested)
                {
                    ConnectionServer.ConnectionServer connection = new(appSettings, _cancellationTokenSource.Token);
                    ConnectionServer.ConnectionServer[] doneConnections;

                    await connection.InitAsync();
                    if (!connection.IsDone) connection.Serve();

                    connections.Add(connection);

                    doneConnections = connections.Where(x => x.IsDone).ToArray();
                    foreach (var doneConnection in doneConnections)
                    {
                        connections.Remove(doneConnection);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch
            {
                _ = Task.Run(DisplayError);
            }
            finally
            {
                connections.ForEach(x => x.Dispose());
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private static async Task DisplayError()
        {
            try
            {
                await _server!;
            }
            catch { }

            DisplayCommandOutput("Server failed.", ConsoleColor.Red);
        }

        private static bool IsServerRunning()
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

            return running;
        }

        private static async void OnConnectionError(object? sender, ConnectionErrorEventArgs e)
        {
            string log = $"{e.DateTime} - User: {e.Username} [{e.RemoteIP}] - {e.ErrorMessage}\n";

            ChangeConsoleColor(ConsoleColor.Red);

            Console.WriteLine(log);
            using (FileStream stream = new("log.txt", FileMode.Append))
                await stream.WriteAsync(Encoding.UTF8.GetBytes(log));
        }
    }
}
