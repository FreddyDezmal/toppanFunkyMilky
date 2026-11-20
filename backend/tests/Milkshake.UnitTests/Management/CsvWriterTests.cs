using System.Text;
using Milkshake.Application.Management;

namespace Milkshake.UnitTests.Management;

/// <summary>M12 exports: RFC 4180 quoting and protection against CSV (formula) injection (decision R5).</summary>
public sealed class CsvWriterTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"https://evil.test\")", "\"'=HYPERLINK(\"\"https://evil.test\"\")\"")]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+27821234567", "'+27821234567")]
    [InlineData("-2", "'-2")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tcmd", "'\tcmd")]
    [InlineData("\rcmd", "\"'\rcmd\"")]
    public void CellsThatASpreadsheetWouldRun_StartWithAnApostrophe(string value, string expected)
    {
        Assert.Equal(expected, CsvWriter.Cell(value));
    }

    [Theory]
    [InlineData("Chocolate", "Chocolate")]
    [InlineData("Oreo, crushed", "\"Oreo, crushed\"")]
    [InlineData("The \"best\" one", "\"The \"\"best\"\" one\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData(" padded ", "\" padded \"")]
    [InlineData("1 Main Road", "1 Main Road")]
    [InlineData("a=b", "a=b")]
    [InlineData("Crème brûlée", "Crème brûlée")]
    public void OrdinaryCells_AreQuotedOnlyWhenNeeded(string value, string expected)
    {
        Assert.Equal(expected, CsvWriter.Cell(value));
    }

    [Fact]
    public void EmptyAndMissingValues_AreEmptyCells()
    {
        Assert.Equal((string.Empty, string.Empty), (CsvWriter.Cell(null), CsvWriter.Cell(string.Empty)));
    }

    [Fact]
    public void AFile_HasAByteOrderMark_AHeader_AndCrlfLines()
    {
        byte[] file = CsvWriter.Build(["Flavour", "Orders"], [new[] { "Crème", "3" }, new string?[] { "=cmd", null }]);

        Assert.Equal(Encoding.UTF8.GetPreamble(), file[..3]);
        Assert.Equal("Flavour,Orders\r\nCrème,3\r\n'=cmd,\r\n", Encoding.UTF8.GetString(file[3..]));
    }

    [Fact]
    public void TheExportLimit_IsTenThousandRecords()
    {
        Assert.Equal(10_000, CsvWriter.MaxRecords);
    }
}
