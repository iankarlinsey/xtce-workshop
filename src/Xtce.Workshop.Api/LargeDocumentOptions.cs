namespace Xtce.Workshop.Api;

/// <summary>
/// Optional input-size short-circuit for large mode (#129). By default it is OFF: every
/// load first answers with the full document, and the browser falls back to a
/// server-held session only when it demonstrably cannot hold the response
/// (result?as=session). Set Xtce:LargeDocumentThresholdBytes to pre-empt the failed
/// attempt for known-huge deployments.
/// </summary>
public static class LargeDocumentOptions
{
    public const long DefaultThresholdBytes = long.MaxValue;

    public static long ThresholdBytes(IConfiguration configuration) =>
        configuration.GetValue<long?>("Xtce:LargeDocumentThresholdBytes") ?? DefaultThresholdBytes;
}
