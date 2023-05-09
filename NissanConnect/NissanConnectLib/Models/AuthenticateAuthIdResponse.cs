namespace NissanConnectLib.Models
{
    public class AuthenticateAuthIdResponse
    {
        public string? AuthId { get; set; }
        public string? Template { get; set; }
        public string? Stage { get; set; }
        public string? Header { get; set; }
        public List<Callback>? Callbacks { get; set; }
    }
}
