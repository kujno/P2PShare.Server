namespace P2PShare.Server.Models
{
    public class ConnectionErrorEventArgs : EventArgs
    {
        public required string ErrorMessage { get; init; }
        public required string RemoteIP { get; init; }
        public required string Username { get; init; }
        public required DateTime DateTime { get; init; }
    }
}
