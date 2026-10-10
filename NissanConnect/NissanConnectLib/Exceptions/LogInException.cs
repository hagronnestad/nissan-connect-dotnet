namespace NissanConnectLib.Exceptions;

public class LogInException : Exception
{
    public LogInException()
    {
    }

    public LogInException(string message) : base(message)
    {
    }

    public LogInException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
