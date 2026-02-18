using P2PShare.Libs;
using P2PShare.Libs.Models.Requests;
using P2PShare.Server.DBAccess;
using System.Text;

namespace P2PShare.Server.ConnectionServer
{
    public class ConnectionServerHandler : ConnectionHandler
    {
        private int _port;

        public string IPRemote { get => _ipRemote?.ToString() ?? "Unknown"; }

        public required AppSettings AppSettings { get; init; }

        public async Task<string> ReceiveRequestAsync() => await ReceiveRequestAsync(true);

        public async Task WaitForConnectionAsync()
        {
            using (Client = await ReceiveTcpClientAsync(_initialServerPort))
            {
                await ReceiveEncryptionKeyAsync();
                _port = await SendPortAsync(true);
            }
        }

        public async Task<string> AuthOnNewPortAsync()
        {
            Request request;
            bool auth = false;

            Client = await ReceiveTcpClientAsync(_port);

            do
            {
                request = Request.Create(await ReceiveRequestAsync());
                bool response = false;

                switch (request.Tag)
                {
                    case Tag.Register:
                        if ((await DBContext.GetUsernamesAsync()).Where(x => x == request.Username).ToArray().Length == 0)
                        {
                            await DBContext.AddUserAsync(request.Username!, Hasher.Hash(request.Password!), request.Name!, request.Surename!);

                            Directory.CreateDirectory($"{AppSettings.RootFolderPath}\\{request.Username}");

                            response = true;
                        }

                        break;

                    case Tag.Login:
                        string? dbHash;

                        response = await DBContext.IsUserVerifiedAsync(request.Username!);

                        if (response)
                        {
                            dbHash = await DBContext.GetPasswordHashAsync(request.Username!);

                            if (dbHash is not null)
                                response = Hasher.Verify(request.Password!, dbHash);
                        }

                        auth = response;

                        break;
                    default:
                        throw new Exception("Invalid tag received during authentication.");
                }

                await YNSendAsync(true, response);
            }
            while (!auth);

            return request.Username!;
        }

        public async Task SendFileAsync(FileInfo file, bool encrypted)
        {
            var fileArr = new FileInfo[] { file };

            await SendInviteAsync(fileArr, encrypted);
            await YNReceiveAsync(encrypted);

            await SendFilesAsync(fileArr, encrypted);
        }

        public async Task SendInfoAsync(string info)
        {
            var infoBytes = _encryptionSymmetrical!.Encrypt(Encoding.UTF8.GetBytes(info));

            await SendInfoLengthAsync(infoBytes.Length, true);

            await _netStream!.WriteAsync(infoBytes, CancellationToken);
        }
    }
}
