using QueryMongo.Core.Json;
using Xunit;

namespace QueryMongo.Core.Tests;

public class BsonJsonTests
{
    [Fact]
    public void BlankFilterParsesToEmptyDocument()
    {
        // An empty query box must mean "match everything", not an error.
        var result = BsonJson.ParseDocument("   ");

        Assert.True(result.IsValid);
        Assert.Equal(0, result.Value!.ElementCount);
    }

    [Theory]
    [InlineData("{ name: \"ada\" }")]          // unquoted key, shell style
    [InlineData("{ 'name': 'ada' }")]          // single quotes
    [InlineData("{ \"age\": { \"$gt\": 30 } }")]
    public void ShellStyleInputIsAccepted(string input)
    {
        Assert.True(BsonJson.ParseDocument(input).IsValid);
    }

    [Fact]
    public void MalformedInputReportsErrorInsteadOfThrowing()
    {
        var result = BsonJson.ParseDocument("{ name: ");

        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void ErrorMessageIsSingleLine()
    {
        // The query bar shows this inline, so embedded newlines would break layout.
        var result = BsonJson.ParseDocument("{ broken");

        Assert.DoesNotContain('\n', result.Error!);
        Assert.DoesNotContain('\r', result.Error!);
    }

    [Fact]
    public void PipelineParsesArrayOfStages()
    {
        var result = BsonJson.ParsePipeline("""[ { "$match": { "x": 1 } }, { "$limit": 5 } ]""");

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Value!.Count);
        Assert.True(result.Value[0].Contains("$match"));
    }

    [Fact]
    public void PipelineRejectsNonDocumentStage()
    {
        var result = BsonJson.ParsePipeline("""[ { "$match": {} }, 42 ]""");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void PreviewIsTruncatedAndMarked()
    {
        var doc = new MongoDB.Bson.BsonDocument("text", new string('x', 500));

        var preview = BsonJson.ToPreview(doc, maxLength: 50);

        Assert.Equal(51, preview.Length); // 50 chars plus the ellipsis
        Assert.EndsWith("…", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewShortDocumentIsUntouched()
    {
        var doc = new MongoDB.Bson.BsonDocument("a", 1);

        Assert.DoesNotContain('…', BsonJson.ToPreview(doc));
    }
}
