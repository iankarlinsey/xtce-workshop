using Xtce.Workshop.Model;

namespace Xtce.Workshop.Api;

/// <summary>Request body for POST /api/xtce/layout.</summary>
public sealed record LayoutRequest(SpaceSystem Document, string ContainerName, int[]? SystemPath = null);

/// <summary>Request body for POST /api/xtce/search.</summary>
public sealed record SearchRequest(SpaceSystem Document, string Query);

/// <summary>Request body for POST /api/xtce/usages. SystemPath is a context path like "Root/Bus".</summary>
public sealed record UsagesRequest(SpaceSystem Document, string SystemPath, string ParameterName);
