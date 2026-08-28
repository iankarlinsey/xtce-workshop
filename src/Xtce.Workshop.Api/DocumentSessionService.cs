using System.Collections.Concurrent;
using Xtce.Workshop.Model;

namespace Xtce.Workshop.Api;

/// <summary>
/// Server-held documents for large files (#129): above the size threshold the browser
/// never receives the full document JSON — it browses and edits the model held here,
/// item by item, and Save streams XML from this copy. Sessions are swept after a
/// sliding idle window; every touch renews it.
/// </summary>
public sealed class DocumentSessionService
{
    public sealed class DocumentSession
    {
        public required string Name { get; init; }
        public required SpaceSystem Document { get; set; }
        public DateTime LastTouched { get; set; } = DateTime.UtcNow;
        public readonly object Gate = new();
    }

    private static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, DocumentSession> _sessions = new();

    public string Store(SpaceSystem document)
    {
        Sweep();
        var id = Guid.NewGuid().ToString("n");
        _sessions[id] = new DocumentSession { Name = document.Name, Document = document };
        return id;
    }

    public DocumentSession? Get(string id)
    {
        Sweep();
        if (!_sessions.TryGetValue(id, out var session))
        {
            return null;
        }
        session.LastTouched = DateTime.UtcNow;
        return session;
    }

    public bool Drop(string id) => _sessions.TryRemove(id, out _);

    private void Sweep()
    {
        var cutoff = DateTime.UtcNow - IdleLifetime;
        foreach (var (id, session) in _sessions)
        {
            if (session.LastTouched < cutoff)
            {
                _sessions.TryRemove(id, out _);
            }
        }
    }
}

/// <summary>
/// The item-kind plumbing shared by the session endpoints: the same kind strings the
/// frontend's document-tree helpers use, mapped to their sets on the model. Kept in one
/// switch per operation so a new modeled kind fails loudly here when forgotten.
/// </summary>
public static class DocumentItems
{
    public static readonly string[] Kinds =
    [
        "parameterType", "parameter", "container", "message", "algorithm", "stream", "service",
        "metaCommand", "blockMetaCommand", "argumentType", "commandParameterType",
        "commandParameter", "commandAlgorithm", "commandContainer",
    ];

    /// <summary>The CLR record type an item of this kind deserializes into.</summary>
    public static Type? ClrType(string kind) => kind switch
    {
        "parameterType" or "argumentType" or "commandParameterType" => typeof(ParameterTypeDefinition),
        "parameter" or "commandParameter" => typeof(Parameter),
        "container" => typeof(SequenceContainer),
        "message" => typeof(Message),
        "algorithm" or "commandAlgorithm" => typeof(Algorithm),
        "stream" => typeof(StreamDefinition),
        "service" => typeof(Service),
        "metaCommand" => typeof(MetaCommand),
        "blockMetaCommand" => typeof(BlockMetaCommand),
        "commandContainer" => typeof(CommandContainer),
        _ => null,
    };

    public static IReadOnlyList<object>? ItemsOf(SpaceSystem system, string kind) => kind switch
    {
        "parameterType" => system.TelemetryMetaData?.ParameterTypeSet,
        "parameter" => system.TelemetryMetaData?.ParameterSet,
        "container" => system.TelemetryMetaData?.ContainerSet,
        "message" => system.TelemetryMetaData?.MessageSet?.Messages,
        "algorithm" => system.TelemetryMetaData?.AlgorithmSet,
        "stream" => system.TelemetryMetaData?.StreamSet,
        "service" => system.ServiceSet,
        "metaCommand" => system.CommandMetaData?.MetaCommands,
        "blockMetaCommand" => system.CommandMetaData?.BlockMetaCommands,
        "argumentType" => system.CommandMetaData?.ArgumentTypeSet,
        "commandParameterType" => system.CommandMetaData?.ParameterTypeSet,
        "commandParameter" => system.CommandMetaData?.ParameterSet,
        "commandAlgorithm" => system.CommandMetaData?.AlgorithmSet,
        "commandContainer" => system.CommandMetaData?.CommandContainerSet,
        _ => null,
    };

    /// <summary>The display name of one item (every modeled kind is named).</summary>
    public static string NameOf(object item) => item switch
    {
        ParameterTypeDefinition t => t.Name,
        Parameter p => p.Name,
        SequenceContainer c => c.Name,
        Message m => m.Name,
        Algorithm a => a.Name,
        StreamDefinition s => s.Name,
        Service s => s.Name,
        MetaCommand m => m.Name,
        BlockMetaCommand b => b.Name,
        CommandContainer c => c.Name,
        _ => "?",
    };

    /// <summary>Returns a new system with the kind's list replaced (structure untouched).</summary>
    public static SpaceSystem WithList(SpaceSystem system, string kind, IReadOnlyList<object> items)
    {
        var telemetry = system.TelemetryMetaData ?? new TelemetryMetaData([], []);
        var command = system.CommandMetaData ?? new CommandMetaData([]);
        return kind switch
        {
            "parameterType" => system with { TelemetryMetaData = telemetry with { ParameterTypeSet = Cast<ParameterTypeDefinition>(items) } },
            "parameter" => system with { TelemetryMetaData = telemetry with { ParameterSet = Cast<Parameter>(items) } },
            "container" => system with { TelemetryMetaData = telemetry with { ContainerSet = Cast<SequenceContainer>(items) } },
            "message" => system with
            {
                TelemetryMetaData = telemetry with
                {
                    MessageSet = (telemetry.MessageSet ?? new MessageSet([])) with { Messages = Cast<Message>(items) },
                },
            },
            "algorithm" => system with { TelemetryMetaData = telemetry with { AlgorithmSet = Cast<Algorithm>(items) } },
            "stream" => system with { TelemetryMetaData = telemetry with { StreamSet = Cast<StreamDefinition>(items) } },
            "service" => system with { ServiceSet = Cast<Service>(items) },
            "metaCommand" => system with { CommandMetaData = command with { MetaCommands = Cast<MetaCommand>(items) } },
            "blockMetaCommand" => system with { CommandMetaData = command with { BlockMetaCommands = Cast<BlockMetaCommand>(items) } },
            "argumentType" => system with { CommandMetaData = command with { ArgumentTypeSet = Cast<ParameterTypeDefinition>(items) } },
            "commandParameterType" => system with { CommandMetaData = command with { ParameterTypeSet = Cast<ParameterTypeDefinition>(items) } },
            "commandParameter" => system with { CommandMetaData = command with { ParameterSet = Cast<Parameter>(items) } },
            "commandAlgorithm" => system with { CommandMetaData = command with { AlgorithmSet = Cast<Algorithm>(items) } },
            "commandContainer" => system with { CommandMetaData = command with { CommandContainerSet = Cast<CommandContainer>(items) } },
            _ => system,
        };
    }

    /// <summary>Follows a slash-separated child-index path ("", "0", "0/2") to a system.</summary>
    public static SpaceSystem? Resolve(SpaceSystem root, string? path)
    {
        var node = root;
        if (string.IsNullOrEmpty(path))
        {
            return node;
        }
        foreach (var segment in path.Split('/'))
        {
            if (!int.TryParse(segment, out var index) || index < 0 || index >= node.Children.Count)
            {
                return null;
            }
            node = node.Children[index];
        }
        return node;
    }

    /// <summary>Returns a new root with the system at the index path replaced by update().</summary>
    public static SpaceSystem UpdateAt(SpaceSystem root, string? path, Func<SpaceSystem, SpaceSystem> update)
    {
        if (string.IsNullOrEmpty(path))
        {
            return update(root);
        }
        var cut = path.IndexOf('/');
        var head = int.Parse(cut < 0 ? path : path[..cut]);
        var rest = cut < 0 ? "" : path[(cut + 1)..];
        var children = root.Children.ToList();
        children[head] = UpdateAt(children[head], rest, update);
        return root with { Children = children };
    }

    private static List<T> Cast<T>(IReadOnlyList<object> items) => items.Cast<T>().ToList();
}
