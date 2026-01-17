namespace P2PShare.Server
{
    class Program
    {
        static async void Main()
        {
            string command;

            do
            {
                command = await CLIHandler.GetCommand();

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
    }
}