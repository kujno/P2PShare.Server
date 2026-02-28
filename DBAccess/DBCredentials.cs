namespace P2PShare.Server.DBAccess
{
    public class DBCredentials
    {
        public required string Server { get; init; }
        public required string Database { get; init; }
        public required string UserID { get; init; }
        public required string Password { get; init; }
    }
}
