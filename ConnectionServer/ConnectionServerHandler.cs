using P2PShare.Libs;
using P2PShare.Libs.Models;

namespace P2PShare.Server.ConnectionServer
{
    public class ConnectionServerHandler : ConnectionHandler
    {
        private int _port;

        public string IPRemote { get => _ipRemote?.ToString() ?? "No remote IP"; }

        public async Task WaitForConnectionAsync()
        {
            using (Client = await ReceiveTcpClientAsync(_initialServerPort))
            {
                await ReceiveEncryptionKeyAsync();
                _port = await SendPortAsync(true);
            }
        }

        public async Task<bool> AuthOnNewPortAsync()
        {
            var request = new string[3];
            string requestString;

            Client = await ReceiveTcpClientAsync(_port);

            do
            {
                requestString = await ReceiveRequestAsync(true);
                for (int i = 0; i < request.Length - 1; i++)
                {
                    var index = requestString.IndexOf(InviteSeparator);
                    request[i] = requestString.Substring(0, index);
                    requestString = requestString.Substring(index + 1);
                }
                request[2] = requestString;

                if (Enum.Parse<Tag>(request[0]) is Tag.Register)
                {
                    // find if the username doesn't exist already
                    // if not register
                }
                else
                {
                    // login logic
                }
            }
            while ();
        }
    }
}
