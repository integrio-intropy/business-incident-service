using Intropy.BusinessIncidentService.Utils;

namespace Intropy.BusinessIncidentService.Test.Utils;

public class SourceParserTests
{
    [Theory]
    [InlineData("urn:cust1:customers-mdc-extractors-fno", "customers-mdc-extractors-fno")]
    [InlineData("urn:a:b", "b")]
    public void Parse_CorrectSource_Succeeds(string source, string expected)
    {
        Assert.Equal(expected, SourceParser.Parse(source));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-urn-customer")]
    [InlineData("urn:no-customer")]
    public void Parse_InvalidSource_Throws(string source)
    {
        Assert.Throws<ArgumentException>(() => SourceParser.Parse(source));
    }

    [Fact]
    public void Normalize_NullSource_Throws()
    {
        Assert.Throws<ArgumentException>(() => SourceParser.Parse((string)null!));
    }
}
