using System.Xml;

namespace Xtce.Workshop.Validation;

/// <summary>
/// Lightweight peeks into preserved raw-XML fragments — several rules need one attribute
/// off a fragment's root element (a preserved type's name, a time Encoding's units) without
/// modeling the construct. A malformed fragment yields null rather than an exception:
/// preserved content must never take validation down.
/// </summary>
public static class XmlFragmentInspector
{
    public static string? RootAttribute(string outerXml, string attributeName)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            return reader.MoveToContent() == XmlNodeType.Element ? reader.GetAttribute(attributeName) : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>The text content of a fragment's root element (e.g. a MetaCommandRef's reference).</summary>
    public static string? RootText(string outerXml)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            if (reader.MoveToContent() != XmlNodeType.Element)
            {
                return null;
            }
            var text = reader.ReadElementContentAsString().Trim();
            return text.Length == 0 ? null : text;
        }
        catch (XmlException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null; // element has child elements, not simple text content
        }
    }

    /// <summary>
    /// One StringDataEncoding found in a fragment: which length form it uses and which
    /// optional length markers are present inside that form (rule R17).
    /// </summary>
    public sealed record StringEncodingInfo(bool IsVariable, bool HasTerminationChar, bool HasLeadingSize);

    public static IReadOnlyList<StringEncodingInfo> FindStringEncodings(string outerXml) =>
        ScanElements(outerXml, "StringDataEncoding", (reader, depth) =>
        {
            var isVariable = false;
            var hasTermination = false;
            var hasLeadingSize = false;
            if (!reader.IsEmptyElement)
            {
                while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
                {
                    if (reader.NodeType != XmlNodeType.Element)
                    {
                        continue;
                    }
                    switch (reader.LocalName)
                    {
                        case "Variable":
                            isVariable = true;
                            break;
                        case "TerminationChar":
                            hasTermination = true;
                            break;
                        case "LeadingSize":
                            hasLeadingSize = true;
                            break;
                    }
                }
            }
            return new StringEncodingInfo(isVariable, hasTermination, hasLeadingSize);
        });

    /// <summary>
    /// Change-alarm attributes found on any element in a fragment (rule R19): only elements
    /// that EXPLICITLY carry changeType or spanOfInterestInSeconds are reported — which
    /// elements would receive the schema defaults can't be known from the fragment alone.
    /// </summary>
    public sealed record ChangeAlarmInfo(string? ChangeType, double? SpanOfInterestInSeconds);

    public static IReadOnlyList<ChangeAlarmInfo> FindChangeAlarmAttributes(string outerXml)
    {
        var results = new List<ChangeAlarmInfo>();
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }
                var changeType = reader.GetAttribute("changeType");
                var spanText = reader.GetAttribute("spanOfInterestInSeconds");
                if (changeType is null && spanText is null)
                {
                    continue;
                }
                double? span = double.TryParse(spanText, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
                results.Add(new ChangeAlarmInfo(changeType, span));
            }
        }
        catch (XmlException)
        {
        }

        return results;
    }

    /// <summary>One SplineCalibrator found in a fragment: order attribute + point count.</summary>
    public sealed record SplineInfo(long Order, int PointCount);

    /// <summary>
    /// Finds every SplineCalibrator element anywhere inside a fragment, with its order
    /// (XSD default 1 applied here — the rule needs the effective value) and its
    /// SplinePoint count.
    /// </summary>
    public static IReadOnlyList<SplineInfo> FindSplineCalibrators(string outerXml) =>
        ScanElements(outerXml, "SplineCalibrator", (reader, depth) =>
        {
            var order = long.TryParse(reader.GetAttribute("order"), out var parsed) ? parsed : 1;
            var points = 0;
            if (!reader.IsEmptyElement)
            {
                while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "SplinePoint")
                    {
                        points++;
                    }
                }
            }
            return new SplineInfo(order, points);
        });

    /// <summary>One Checksum found in a fragment: name attribute + InputAlgorithm presence.</summary>
    public sealed record ChecksumInfo(string? Name, bool HasInputAlgorithm);

    public static IReadOnlyList<ChecksumInfo> FindChecksums(string outerXml) =>
        ScanElements(outerXml, "Checksum", (reader, depth) =>
        {
            var name = reader.GetAttribute("name");
            var hasInputAlgorithm = false;
            if (!reader.IsEmptyElement)
            {
                while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "InputAlgorithm")
                    {
                        hasInputAlgorithm = true;
                    }
                }
            }
            return new ChecksumInfo(name, hasInputAlgorithm);
        });

    private static IReadOnlyList<T> ScanElements<T>(string outerXml, string elementName, Func<XmlReader, int, T> capture)
    {
        var results = new List<T>();
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == elementName)
                {
                    results.Add(capture(reader, reader.Depth));
                }
            }
        }
        catch (XmlException)
        {
            // Malformed preserved content contributes nothing rather than failing validation.
        }

        return results;
    }

    /// <summary>One Dimension found in a fragment's DimensionList: fixed bounds or nulls.</summary>
    public sealed record DimensionInfo(long? StartingFixed, long? EndingFixed);

    /// <summary>
    /// Reads the Dimension entries of a fragment's DimensionList (e.g. on a raw
    /// ArrayParameterRefEntry). Only FixedValue bounds are captured — dynamic values
    /// come back null and rules skip what they can't statically see.
    /// </summary>
    public static IReadOnlyList<DimensionInfo> FindDimensions(string outerXml)
    {
        var dimensions = new List<DimensionInfo>();
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

            long? starting = null, ending = null;
            var insideDimension = false;
            string? currentIndex = null;

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (reader.LocalName == "Dimension")
                    {
                        insideDimension = true;
                        starting = ending = null;
                        if (reader.IsEmptyElement)
                        {
                            dimensions.Add(new DimensionInfo(null, null));
                            insideDimension = false;
                        }
                    }
                    else if (insideDimension && reader.LocalName is "StartingIndex" or "EndingIndex")
                    {
                        currentIndex = reader.LocalName;
                    }
                    else if (insideDimension && currentIndex is not null && reader.LocalName == "FixedValue")
                    {
                        if (long.TryParse(reader.ReadElementContentAsString(), out var parsed))
                        {
                            if (currentIndex == "StartingIndex")
                            {
                                starting = parsed;
                            }
                            else
                            {
                                ending = parsed;
                            }
                        }
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (reader.LocalName == "Dimension" && insideDimension)
                    {
                        dimensions.Add(new DimensionInfo(starting, ending));
                        insideDimension = false;
                    }
                    else if (reader.LocalName is "StartingIndex" or "EndingIndex")
                    {
                        currentIndex = null;
                    }
                }
            }
        }
        catch (XmlException)
        {
            // Malformed preserved content contributes nothing rather than failing validation.
        }

        return dimensions;
    }

    /// <summary>
    /// One LocationInContainerInBits found inside a fragment: its referenceLocation
    /// attribute (null = absent, XSD default previousEntry) and its FixedValue child's
    /// integer content (null when absent, dynamic, or unparseable).
    /// </summary>
    public sealed record LocationInfo(string? ReferenceLocation, long? FixedValue);

    /// <summary>
    /// Finds every LocationInContainerInBits element anywhere inside a fragment — they
    /// appear as direct children of modeled ref entries' preserved fragments AND nested
    /// inside raw (unmodeled) entry fragments, so a descendant scan covers both.
    /// </summary>
    public static IReadOnlyList<LocationInfo> FindLocations(string outerXml)
    {
        var locations = new List<LocationInfo>();
        try
        {
            using var reader = XmlReader.Create(new StringReader(outerXml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "LocationInContainerInBits")
                {
                    continue;
                }

                var referenceLocation = reader.GetAttribute("referenceLocation");
                long? fixedValue = null;

                if (!reader.IsEmptyElement)
                {
                    var depth = reader.Depth;
                    while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
                    {
                        if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "FixedValue")
                        {
                            var text = reader.ReadElementContentAsString();
                            if (long.TryParse(text, out var parsed))
                            {
                                fixedValue = parsed;
                            }
                            // ReadElementContentAsString consumed through FixedValue's end
                            // tag; the loop's Read() continues from the following node.
                            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                            {
                                break;
                            }
                        }
                    }
                }

                locations.Add(new LocationInfo(referenceLocation, fixedValue));
            }
        }
        catch (XmlException)
        {
            // Malformed preserved content contributes nothing rather than failing validation.
        }

        return locations;
    }
}
