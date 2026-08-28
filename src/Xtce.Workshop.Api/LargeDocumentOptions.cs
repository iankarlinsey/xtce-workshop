namespace Xtce.Workshop.Api;

/// <summary>
/// The input-size threshold above which loads answer in large mode (#129): the document
/// stays server-held and the response carries a documentSessionId instead of document
/// JSON. Configurable via Xtce:LargeDocumentThresholdBytes; default 25 MB.
/// </summary>
public static class LargeDocumentOptions
{
    public const long DefaultThresholdBytes = 25_000_000;

    public static long ThresholdBytes(IConfiguration configuration) =>
        configuration.GetValue<long?>("Xtce:LargeDocumentThresholdBytes") ?? DefaultThresholdBytes;
}
