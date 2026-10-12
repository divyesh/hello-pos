using HelloPos.Api.Features.Catalog.Import;

namespace HelloPos.Api.Tests.Features.Catalog;

/// <summary>Research R8: the CSV reader handles quoting, line ends, BOM and the formula guard.</summary>
public sealed class CsvReaderTests
{
    [Fact]
    public void QuotedFields_CommasDoubledQuotesAndLineBreaks()
    {
        var (rows, error) = CsvReader.Parse("﻿name,price\r\n\"Cola, 6 x 355 ml\",1.99\r\n\"The \"\"best\"\" tea\nloose\",4.50\n\n");

        Assert.Null(error);
        Assert.Equal(3, rows.Count);
        Assert.Equal(["name", "price"], rows[0].Fields);
        Assert.Equal(["Cola, 6 x 355 ml", "1.99"], rows[1].Fields);
        Assert.Equal("The \"best\" tea\nloose", rows[2].Fields[0]);
        Assert.Equal([1, 2, 3], rows.Select(r => r.Line));
    }

    [Fact]
    public void EmptyFields_AndAMissingFinalLineEnd()
    {
        var (rows, _) = CsvReader.Parse("a,,c\n,,");

        Assert.Equal(["a", "", "c"], rows[0].Fields);
        Assert.Equal(["", "", ""], rows[1].Fields);
    }

    [Fact]
    public void AnUnterminatedQuote_ReportsItsLine()
    {
        var (_, error) = CsvReader.Parse("name\nok\n\"broken,1.00\n");

        Assert.Equal(3, error);
    }

    [Theory]
    [InlineData("Cola", "Cola")]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("-50% sale", "'-50% sale")]
    [InlineData("@home", "'@home")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    public void Cells_AreQuotedAndFormulaGuarded_AndComeBackUnchanged(string value, string cell)
    {
        Assert.Equal(cell, CsvReader.Cell(value));
        var (rows, _) = CsvReader.Parse(cell);
        Assert.Equal(value, CsvReader.Uncell(rows[0].Fields[0]));
    }
}
