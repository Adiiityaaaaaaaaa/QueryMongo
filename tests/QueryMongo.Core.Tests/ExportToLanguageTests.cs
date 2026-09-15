using MongoDB.Bson;
using QueryMongo.Core.Models;
using QueryMongo.Core.Services;
using Xunit;

namespace QueryMongo.Core.Tests;

public class ExportToLanguageTests
{
    private static readonly QuerySpec Spec = new()
    {
        Filter = "{ genre: 'Sci-Fi' }",
        Sort = "{ year: -1 }",
        Limit = 10
    };

    [Fact]
    public void EveryLanguageProducesCode()
    {
        foreach (var language in ExportToLanguage.All)
        {
            var code = ExportToLanguage.Query(language, "db", "movies", Spec);

            Assert.False(string.IsNullOrWhiteSpace(code));
            Assert.Contains("movies", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ShellIncludesSortAndLimit()
    {
        var code = ExportToLanguage.Query(TargetLanguage.MongoShell, "db", "movies", Spec);

        Assert.Contains(".sort(", code, StringComparison.Ordinal);
        Assert.Contains(".limit(10)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyClausesAreOmitted()
    {
        var bare = new QuerySpec { Filter = "{}", Limit = 0 };

        var code = ExportToLanguage.Query(TargetLanguage.MongoShell, "db", "movies", bare);

        // An empty sort or limit must not appear as an empty call.
        Assert.DoesNotContain(".sort(", code, StringComparison.Ordinal);
        Assert.DoesNotContain(".limit(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyFilterStillRendersAsBraces()
    {
        var code = ExportToLanguage.Query(
            TargetLanguage.MongoShell, "db", "movies", new QuerySpec { Filter = "", Limit = 0 });

        Assert.Contains("find({})", code, StringComparison.Ordinal);
    }

    [Fact]
    public void PythonUsesPythonLiterals()
    {
        var spec = new QuerySpec { Filter = "{ active: true, deleted: false }", Limit = 0 };

        var code = ExportToLanguage.Query(TargetLanguage.Python, "db", "movies", spec);

        Assert.Contains("True", code, StringComparison.Ordinal);
        Assert.Contains("False", code, StringComparison.Ordinal);
        Assert.DoesNotContain(": true", code, StringComparison.Ordinal);
    }

    [Fact]
    public void PipelineRendersEveryStage()
    {
        var stages = new List<BsonDocument>
        {
            BsonDocument.Parse("{ $match: { x: 1 } }"),
            BsonDocument.Parse("{ $limit: 5 }")
        };

        foreach (var language in ExportToLanguage.All)
        {
            var code = ExportToLanguage.Pipeline(language, "db", "movies", stages);

            Assert.Contains("$match", code, StringComparison.Ordinal);
            Assert.Contains("$limit", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DisplayNamesAreFriendly()
    {
        Assert.Equal("C#", ExportToLanguage.DisplayName(TargetLanguage.CSharp));
        Assert.Equal("Node.js", ExportToLanguage.DisplayName(TargetLanguage.JavaScript));
        Assert.Equal("Mongo Shell", ExportToLanguage.DisplayName(TargetLanguage.MongoShell));
    }
}
