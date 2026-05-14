namespace OpenVulScan;

public class MissingSdkException : ProjectLoadException
{
    public MissingSdkException()
    {
    }

    public MissingSdkException(string message)
        : base(message)
    {
    }

    public MissingSdkException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
