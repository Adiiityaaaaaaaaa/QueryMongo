using MongoDB.Bson;
using QueryMongo.Core;
using QueryMongo.Core.Json;
using QueryMongo.Core.Models;
using QueryMongo.Core.Services;
using Xunit;

namespace QueryMongo.Core.Tests;

public class BsonValueRenderingTests
{
    [Fact]
    public void ArrayRendersWithoutThrowing()
    {
        // The driver's serializers reject a non-document at the root, which used to
        // crash CSV export on any array field.
        var array = new BsonArray { 1, 2, 3 };

        Assert.Equal("[1,2,3]", BsonJson.ValueToJson(array));
    }

    [Fact]
    public void NestedArrayOfDocumentsRenders()
    {
        var value = new BsonArray { new BsonDocument("a", 1), new BsonDocument("b", 2) };

        var json = BsonJson.ValueToJson(value);

        Assert.StartsWith("[", json, StringComparison.Ordinal);
        Assert.Contains("\"a\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void StringsAreQuotedAndEscaped()
    {
        Assert.Equal("\"say \\\"hi\\\"\"", BsonJson.ValueToJson(new BsonString("say \"hi\"")));
        Assert.Equal("\"back\\\\slash\"", BsonJson.ValueToJson(new BsonString("back\\slash")));
    }

    [Fact]
    public void ScalarsRenderPlainly()
    {
        Assert.Equal("null", BsonJson.ValueToJson(BsonNull.Value));
        Assert.Equal("true", BsonJson.ValueToJson(BsonBoolean.True));
        Assert.Equal("42", BsonJson.ValueToJson(new BsonInt32(42)));
    }
}

public class ByteSizeTests
{
    // Expectations come from Compass's own compactBytes: SI units, two decimals.
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512.00 B")]
    [InlineData(1000, "1.00 kB")]
    [InlineData(1500, "1.50 kB")]
    [InlineData(1_000_000, "1.00 MB")]
    [InlineData(2_500_000_000, "2.50 GB")]
    public void FormatsBytesAsCompassDoes(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSize.Format(bytes));
    }

    [Fact]
    public void KeepsTheSignOnNegativeSizes()
    {
        Assert.Equal("-1.50 kB", ByteSize.Format(-1500));
    }

    [Theory]
    [InlineData(1024, "1.00 KiB")]
    [InlineData(1_048_576, "1.00 MiB")]
    public void FormatsBinaryUnitsWhenAsked(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSize.Format(bytes, si: false));
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1500, "1.5K")]
    [InlineData(2_000_000, "2M")]
    [InlineData(1_234_567_890, "1.2B")]
    public void AbbreviatesCounts(long number, string expected)
    {
        Assert.Equal(expected, ByteSize.CompactNumber(number));
    }
}

public class IndexModelTests
{
    private static IndexInfo Index(string name, string keys, bool unique = false, bool ttl = false) =>
        new(name, BsonDocument.Parse(keys), unique, false, ttl, 4096);

    [Fact]
    public void KeyDescriptionShowsDirection()
    {
        var index = Index("year_rating", "{ year: 1, rating: -1 }");

        Assert.Equal("year ↑, rating ↓", index.KeyDescription);
    }

    [Fact]
    public void SpecialIndexTypesAreShownVerbatim()
    {
        var index = Index("text_idx", "{ body: 'text' }");

        Assert.Contains("body (text)", index.KeyDescription, StringComparison.Ordinal);
    }

    [Fact]
    public void IdIndexCannotBeDropped()
    {
        var detail = new IndexDetail(Index("_id_", "{ _id: 1 }"), 0);

        Assert.False(detail.CanDrop);
    }

    [Fact]
    public void PropertiesListFlags()
    {
        var detail = new IndexDetail(Index("u", "{ a: 1 }", unique: true, ttl: true), 5);

        Assert.Contains("unique", detail.Properties, StringComparison.Ordinal);
        Assert.Contains("TTL", detail.Properties, StringComparison.Ordinal);
    }

    [Fact]
    public void UntrackedUsageIsLabelled()
    {
        var detail = new IndexDetail(Index("a", "{ a: 1 }"), null);

        Assert.Equal("not tracked", detail.UsageDescription);
    }
}

public class SchemaFieldTests
{
    private static SchemaField Field(int present, int sample, params (string Type, int Count)[] types) =>
        new("path", present, sample,
            types.Select(t => new SchemaTypeShare(t.Type, t.Count, (double)t.Count / present)).ToList(),
            [], []);

    [Fact]
    public void FieldInEveryDocumentIsNotSparse()
    {
        var field = Field(100, 100, ("String", 100));

        Assert.False(field.IsSparse);
        Assert.Equal(1.0, field.Presence);
    }

    [Fact]
    public void PartiallyPresentFieldIsSparse()
    {
        var field = Field(40, 100, ("String", 40));

        Assert.True(field.IsSparse);
        Assert.Equal(0.4, field.Presence, precision: 5);
    }

    [Fact]
    public void EmptySampleDoesNotDivideByZero()
    {
        var field = new SchemaField("path", 0, 0, [], [], []);

        Assert.Equal(0, field.Presence);
    }

    [Fact]
    public void TypeDescriptionListsEveryType()
    {
        var field = Field(100, 100, ("String", 70), ("Number", 30));

        Assert.Contains("String", field.TypeDescription, StringComparison.Ordinal);
        Assert.Contains("Number", field.TypeDescription, StringComparison.Ordinal);
    }
}

public class PipelineWriteDetectionTests
{
    private static List<BsonDocument> Pipeline(string json) => BsonJson.ParsePipeline(json).Value!;

    [Fact]
    public void ReadOnlyPipelineIsNotFlagged()
    {
        Assert.False(QueryService.PipelineWrites(Pipeline("""[ { "$match": {} } ]""")));
    }

    [Fact]
    public void OutIsFlaggedAndNamed()
    {
        var pipeline = Pipeline("""[ { "$out": "results" } ]""");

        Assert.True(QueryService.PipelineWrites(pipeline));
        Assert.Equal("results", QueryService.PipelineTarget(pipeline));
    }

    [Fact]
    public void MergeWithDocumentTargetIsNamed()
    {
        var pipeline = Pipeline("""[ { "$merge": { "into": "summary" } } ]""");

        Assert.Equal("summary", QueryService.PipelineTarget(pipeline));
    }

    [Fact]
    public void MergeWithStringTargetIsNamed()
    {
        var pipeline = Pipeline("""[ { "$merge": "summary" } ]""");

        Assert.Equal("summary", QueryService.PipelineTarget(pipeline));
    }

    [Fact]
    public void OutBuriedLaterInThePipelineIsStillFound()
    {
        var pipeline = Pipeline("""[ { "$match": {} }, { "$sort": { "a": 1 } }, { "$out": "final" } ]""");

        Assert.True(QueryService.PipelineWrites(pipeline));
        Assert.Equal("final", QueryService.PipelineTarget(pipeline));
    }
}
