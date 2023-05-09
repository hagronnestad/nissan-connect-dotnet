namespace NissanConnectLib.Models
{
    public class Callback
    {
        public string? Type { get; set; }
        public List<Input>? Input { get; set; }
        public List<Output>? Output { get; set; }
    }

    public class Input
    {
        public string? Name { get; set; }
        public string? Value { get; set; }
    }

    public class Output : Input { }
}
