using System.Text;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Modeled data encodings (#96): the four encoding elements' attributes are first-class;
/// their children (calibrators, size shapes, ErrorDetectCorrect, transforms) ride as
/// preserved fragments in original order.
/// </summary>
public class DataEncodingTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem Load(string xml) =>
        XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static string Sample => $"""
        <SpaceSystem xmlns="{Ns}" name="S">
          <TelemetryMetaData>
            <ParameterTypeSet>
              <IntegerParameterType name="I" signed="false" sizeInBits="16">
                <UnitSet/>
                <IntegerDataEncoding encoding="twosComplement" sizeInBits="12" changeThreshold="2" bitOrder="leastSignificantBitFirst">
                  <DefaultCalibrator>
                    <PolynomialCalibrator><Term coefficient="1" exponent="1"/></PolynomialCalibrator>
                  </DefaultCalibrator>
                </IntegerDataEncoding>
              </IntegerParameterType>
              <StringParameterType name="Str">
                <StringDataEncoding encoding="UTF-8">
                  <SizeInBits><Fixed><FixedValue>64</FixedValue></Fixed><TerminationChar>00</TerminationChar></SizeInBits>
                </StringDataEncoding>
              </StringParameterType>
              <StringParameterType name="VarStr">
                <StringDataEncoding>
                  <Variable maxSizeInBits="256">
                    <DynamicValue><ParameterInstanceRef parameterRef="Len"/></DynamicValue>
                  </Variable>
                </StringDataEncoding>
              </StringParameterType>
              <BinaryParameterType name="Blob">
                <BinaryDataEncoding>
                  <SizeInBits><FixedValue>32</FixedValue></SizeInBits>
                </BinaryDataEncoding>
              </BinaryParameterType>
            </ParameterTypeSet>
            <ParameterSet>
              <Parameter name="Len" parameterTypeRef="I"/>
            </ParameterSet>
          </TelemetryMetaData>
        </SpaceSystem>
        """;

    [Test]
    public void Load_ModelsEncodingAttributes_AndPreservesChildren()
    {
        var types = Load(Sample).TelemetryMetaData!.ParameterTypeSet;

        var integer = types.Single(t => t.Name == "I").DataEncoding!;
        Assert.Equal(DataEncodingKind.Integer, integer.Kind);
        Assert.Equal(("twosComplement", 12L, "2", "leastSignificantBitFirst", null),
            (integer.Encoding, integer.SizeInBits, integer.ChangeThreshold, integer.BitOrder, integer.ByteOrder));
        Assert.Equal(["DefaultCalibrator"], integer.Preserved!.Select(f => f.ElementName).ToList());

        // The encoding element itself no longer rides in the type's preserved list.
        Assert.Equal(["UnitSet"], types.Single(t => t.Name == "I").Preserved!.Select(f => f.ElementName).ToList());

        var variable = types.Single(t => t.Name == "VarStr").DataEncoding!;
        Assert.Equal(DataEncodingKind.String, variable.Kind);
        Assert.Null(variable.Encoding);
        Assert.Equal(["Variable"], variable.Preserved!.Select(f => f.ElementName).ToList());
    }

    [Test]
    public void RoundTrip_IsLosslessAndSchemaValid()
    {
        var loaded = Load(Sample);

        var xml = XtceDocumentWriter.Write(loaded);
        var reloaded = Load(xml);

        Assert.Equal(loaded, reloaded);
        var errors = XsdValidation.Validate(xml);
        Assert.True(errors.Count == 0, "Writer output failed XSD validation:\n" + string.Join("\n", errors));
        // UnitSet must still precede the encoding per BaseDataType's sequence.
        Assert.True(xml.IndexOf("<UnitSet", StringComparison.Ordinal)
                    < xml.IndexOf("<IntegerDataEncoding", StringComparison.Ordinal));
        Assert.Contains("changeThreshold=\"2\"", xml);
    }

    [Test]
    public void Load_ModelsEncodingsOnArgumentTypes_Too()
    {
        var document = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <CommandMetaData>
                <ArgumentTypeSet>
                  <IntegerArgumentType name="U8"><IntegerDataEncoding sizeInBits="8"/></IntegerArgumentType>
                </ArgumentTypeSet>
              </CommandMetaData>
            </SpaceSystem>
            """);

        var encoding = document.CommandMetaData!.ArgumentTypeSet!.Single().DataEncoding!;
        Assert.Equal((DataEncodingKind.Integer, 8L), (encoding.Kind, encoding.SizeInBits));
    }

    [Test]
    public void Load_SchemaInvalidSecondEncoding_StaysPreservedForLosslessness()
    {
        var xml = $"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <TelemetryMetaData>
                <ParameterTypeSet>
                  <IntegerParameterType name="I">
                    <IntegerDataEncoding sizeInBits="8"/>
                    <IntegerDataEncoding sizeInBits="16"/>
                  </IntegerParameterType>
                </ParameterTypeSet>
                <ParameterSet/>
              </TelemetryMetaData>
            </SpaceSystem>
            """;
        var loaded = Load(xml);

        var type = loaded.TelemetryMetaData!.ParameterTypeSet.Single();
        Assert.Equal(8, type.DataEncoding!.SizeInBits);
        Assert.Equal(["IntegerDataEncoding"], type.Preserved!.Select(f => f.ElementName).ToList());

        var written = XtceDocumentWriter.Write(loaded);
        Assert.Equal(loaded, Load(written));
    }
}
