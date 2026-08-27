using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation;

/// <summary>
/// Yields every preserved raw-XML fragment reachable from one SpaceSystem node (not its
/// children — the validator visits each node), paired with the owning construct's location
/// string. This is what lets fragment-inspection rules (R03 checksums, R13 splines) see
/// into content the object model deliberately doesn't represent — including a preserved
/// CommandMetaData's argument encodings.
/// </summary>
public static class FragmentEnumerator
{
    public static IEnumerable<(RawXmlFragment Fragment, string Location)> EnumerateNode(SpaceSystemContext context)
    {
        var path = context.Path;

        foreach (var fragment in context.Node.Preserved ?? [])
        {
            yield return (fragment, $"{path}/{fragment.ElementName}");
        }

        if (context.Node.CommandMetaData is { } commandMetaData)
        {
            var commandPath = $"{path}/CommandMetaData";
            foreach (var fragment in commandMetaData.Preserved ?? [])
            {
                yield return (fragment, $"{commandPath}/{fragment.ElementName}");
            }
            foreach (var fragment in commandMetaData.PreservedArgumentTypes ?? [])
            {
                yield return (fragment, $"{commandPath}/ArgumentTypeSet");
            }
            foreach (var argumentType in commandMetaData.ArgumentTypeSet ?? [])
            {
                foreach (var pair in EnumerateType(argumentType, $"{commandPath}/ArgumentTypeSet/{argumentType.Name}"))
                {
                    yield return pair;
                }
            }
            foreach (var fragment in commandMetaData.PreservedEntries ?? [])
            {
                yield return (fragment, $"{commandPath}/MetaCommandSet");
            }
            foreach (var metaCommand in commandMetaData.MetaCommands)
            {
                var metaCommandPath = $"{commandPath}/MetaCommandSet/{metaCommand.Name}";
                foreach (var fragment in metaCommand.Preserved ?? [])
                {
                    yield return (fragment, metaCommandPath);
                }
                foreach (var argument in metaCommand.Arguments ?? [])
                {
                    foreach (var fragment in argument.Preserved ?? [])
                    {
                        yield return (fragment, metaCommandPath);
                    }
                }
                foreach (var fragment in metaCommand.PreservedArguments ?? [])
                {
                    yield return (fragment, metaCommandPath);
                }
                foreach (var fragment in metaCommand.BaseMetaCommandPreserved ?? [])
                {
                    yield return (fragment, metaCommandPath);
                }
                foreach (var fragment in metaCommand.ExecutionVerifiers ?? [])
                {
                    yield return (fragment, metaCommandPath);
                }
                foreach (var fragment in metaCommand.CompleteVerifiers ?? [])
                {
                    yield return (fragment, metaCommandPath);
                }
                foreach (var fragment in metaCommand.PreservedVerifiers ?? [])
                {
                    yield return (fragment, metaCommandPath);
                }
                if (metaCommand.CommandContainer is { } inlineContainer)
                {
                    foreach (var fragment in inlineContainer.Preserved ?? [])
                    {
                        yield return (fragment, $"{metaCommandPath}/CommandContainer");
                    }
                    foreach (var fragment in inlineContainer.BaseContainerPreserved ?? [])
                    {
                        yield return (fragment, $"{metaCommandPath}/CommandContainer");
                    }
                }
            }
        }

        if (context.Node.TelemetryMetaData is not { } telemetry)
        {
            yield break;
        }

        foreach (var fragment in telemetry.Preserved ?? [])
        {
            yield return (fragment, $"{path}/{fragment.ElementName}");
        }
        foreach (var fragment in telemetry.PreservedParameterTypes ?? [])
        {
            yield return (fragment, $"{path}/ParameterTypeSet");
        }
        foreach (var fragment in telemetry.PreservedParameters ?? [])
        {
            yield return (fragment, $"{path}/ParameterSet");
        }
        foreach (var fragment in telemetry.PreservedContainerEntries ?? [])
        {
            yield return (fragment, $"{path}/ContainerSet");
        }

        foreach (var type in telemetry.ParameterTypeSet)
        {
            foreach (var pair in EnumerateType(type, $"{path}/ParameterTypeSet/{type.Name}"))
            {
                yield return pair;
            }
        }

        foreach (var parameter in telemetry.ParameterSet)
        {
            foreach (var fragment in parameter.Preserved ?? [])
            {
                yield return (fragment, $"{path}/ParameterSet/{parameter.Name}");
            }
        }

        foreach (var container in telemetry.ContainerSet ?? [])
        {
            var containerPath = $"{path}/ContainerSet/{container.Name}";
            foreach (var fragment in container.Preserved ?? [])
            {
                yield return (fragment, containerPath);
            }
            foreach (var entry in container.EntryList)
            {
                if (entry.RawXml is { } rawEntry)
                {
                    yield return (rawEntry, containerPath);
                }
                foreach (var fragment in entry.Preserved ?? [])
                {
                    yield return (fragment, containerPath);
                }
            }
            if (container.BaseContainer?.RestrictionCriteria?.Raw is { } rawCriteria)
            {
                yield return (rawCriteria, containerPath);
            }
        }

        if (telemetry.MessageSet is { } messageSet)
        {
            foreach (var fragment in messageSet.Preserved ?? [])
            {
                yield return (fragment, $"{path}/MessageSet");
            }
            foreach (var message in messageSet.Messages)
            {
                foreach (var fragment in message.Preserved ?? [])
                {
                    yield return (fragment, $"{path}/MessageSet/{message.Name}");
                }
            }
        }
    }

    /// <summary>
    /// One parameter/argument type's reachable fragments: its own preserved children, its
    /// modeled encoding's preserved children (calibrators, ErrorDetectCorrect, size
    /// shapes — what R03/R13 look inside), member fragments, and raw dimension bounds.
    /// </summary>
    private static IEnumerable<(RawXmlFragment Fragment, string Location)> EnumerateType(
        ParameterTypeDefinition type, string typePath)
    {
        foreach (var fragment in type.Preserved ?? [])
        {
            yield return (fragment, typePath);
        }
        foreach (var fragment in type.DataEncoding?.Preserved ?? [])
        {
            yield return (fragment, typePath);
        }
        foreach (var member in type.Members ?? [])
        {
            foreach (var fragment in member.Preserved ?? [])
            {
                yield return (fragment, $"{typePath}/{member.Name}");
            }
        }
        foreach (var dimension in type.Dimensions ?? [])
        {
            if (dimension.StartingIndex.Raw is { } startRaw)
            {
                yield return (startRaw, typePath);
            }
            if (dimension.EndingIndex.Raw is { } endRaw)
            {
                yield return (endRaw, typePath);
            }
        }
    }
}
