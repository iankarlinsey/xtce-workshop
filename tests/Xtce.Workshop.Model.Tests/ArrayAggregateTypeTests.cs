using System.Text;
using Xunit;

namespace Xtce.Workshop.Model.Tests;

public class ArrayAggregateTypeTests
{
    private static SpaceSystem LoadArraysSample()
    {
        using var stream = File.OpenRead(TestPaths.ArraysSample);
        return XtceDocumentReader.Load(stream);
    }

    [Fact]
    public void ArraysSampleFixture_IsItselfSchemaValid()
    {
        Assert.Empty(XsdValidation.Validate(File.ReadAllText(TestPaths.ArraysSample)));
    }

    [Fact]
    public void Load_ParsesArrayTypeWithDimensions()
    {
        var types = LoadArraysSample().TelemetryMetaData!.ParameterTypeSet;

        var matrix = types.Single(t => t.Name == "Matrix_Type");
        Assert.Equal(ParameterTypeKind.Array, matrix.Kind);
        Assert.Equal("Elem_Type", matrix.ArrayTypeRef);
        Assert.Equal(2, matrix.Dimensions!.Count);
        Assert.Equal(0, matrix.Dimensions[0].StartingIndex.FixedValue);
        Assert.Equal(3, matrix.Dimensions[0].EndingIndex.FixedValue);
        Assert.Equal(1, matrix.Dimensions[1].EndingIndex.FixedValue);
    }

    [Fact]
    public void Load_ParsesAggregateMembers()
    {
        var types = LoadArraysSample().TelemetryMetaData!.ParameterTypeSet;

        var aggregate = types.Single(t => t.Name == "Struct_Type");
        Assert.Equal(ParameterTypeKind.Aggregate, aggregate.Kind);
        Assert.Equal(["volt", "badInit", "ghostRef"], aggregate.Members!.Select(m => m.Name).ToList());
        Assert.Equal("Elem_Type", aggregate.Members[0].TypeRef);
        Assert.Equal("42", aggregate.Members[0].InitialValue);
        Assert.Null(aggregate.Members[2].InitialValue);
    }

    [Fact]
    public void RoundTrip_ArraysSample_IsLosslessAndSchemaValid()
    {
        var loaded = LoadArraysSample();

        var xml = XtceDocumentWriter.Write(loaded);
        var reloaded = XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

        Assert.Equal(loaded, reloaded);
        var errors = XsdValidation.Validate(xml);
        Assert.True(errors.Count == 0, "Writer output failed XSD validation:\n" + string.Join("\n", errors));
    }

    [Fact]
    public void RoundTrip_DynamicDimensionIndex_IsPreservedRaw()
    {
        var xml = """
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="S">
              <TelemetryMetaData>
                <ParameterTypeSet>
                  <IntegerParameterType name="N"/>
                  <ArrayParameterType name="Dyn_Type" arrayTypeRef="N">
                    <DimensionList>
                      <Dimension>
                        <StartingIndex><FixedValue>0</FixedValue></StartingIndex>
                        <EndingIndex>
                          <DynamicValue>
                            <ParameterInstanceRef parameterRef="N"/>
                          </DynamicValue>
                        </EndingIndex>
                      </Dimension>
                    </DimensionList>
                  </ArrayParameterType>
                </ParameterTypeSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """;
        var loaded = XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

        var dynType = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "Dyn_Type");
        var ending = dynType.Dimensions![0].EndingIndex;
        Assert.Null(ending.FixedValue);
        Assert.Equal("DynamicValue", ending.Raw!.ElementName);

        var written = XtceDocumentWriter.Write(loaded);
        var reloaded = XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(written)));
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(written));
    }
}
