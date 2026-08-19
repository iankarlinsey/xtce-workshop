namespace Xtce.Workshop.Model;

/// <summary>
/// Thrown when an XTCE document is not well-formed XML, or doesn't match the
/// minimal structural expectations this reader enforces (root element name,
/// required attributes present).
/// </summary>
public sealed class XtceParseException : Exception
{
    public XtceParseException(string message) : base(message) { }

    public XtceParseException(string message, Exception innerException)
        : base(message, innerException) { }
}
