using QueryMongo.Core.Shell;
using Xunit;

namespace QueryMongo.Core.Tests;

public class ShellParserTests
{
    [Fact]
    public void EmptyInputIsEmpty()
    {
        Assert.Equal(ShellCommandKind.Empty, ShellParser.Parse("   ").Kind);
    }

    [Theory]
    [InlineData("show dbs", ShellCommandKind.ShowDatabases)]
    [InlineData("show databases", ShellCommandKind.ShowDatabases)]
    [InlineData("show collections", ShellCommandKind.ShowCollections)]
    [InlineData("show tables", ShellCommandKind.ShowCollections)]
    [InlineData("help", ShellCommandKind.Help)]
    public void RecognisesBareCommands(string input, ShellCommandKind expected)
    {
        Assert.Equal(expected, ShellParser.Parse(input).Kind);
    }

    [Fact]
    public void UseCapturesTheDatabase()
    {
        var command = ShellParser.Parse("use myDatabase");

        Assert.Equal(ShellCommandKind.Use, command.Kind);
        Assert.Equal("myDatabase", command.Target);
    }

    [Fact]
    public void TrailingSemicolonIsIgnored()
    {
        Assert.Equal(ShellCommandKind.ShowDatabases, ShellParser.Parse("show dbs;").Kind);
    }

    [Fact]
    public void CollectionCallIsParsed()
    {
        var command = ShellParser.Parse("db.movies.find({ a: 1 })");

        Assert.Equal(ShellCommandKind.Collection, command.Kind);
        Assert.Equal("movies", command.Collection);
        Assert.Equal("find", Assert.Single(command.Calls).Method);
    }

    [Fact]
    public void ChainedCallsAreKeptInOrder()
    {
        var command = ShellParser.Parse("db.movies.find({}).sort({ year: -1 }).limit(5)");

        Assert.Equal(["find", "sort", "limit"], command.Calls.Select(c => c.Method));
        Assert.Equal("5", command.Calls[2].Arguments[0]);
    }

    [Fact]
    public void DatabaseCallIsDistinguishedFromCollection()
    {
        var command = ShellParser.Parse("db.runCommand({ ping: 1 })");

        Assert.Equal(ShellCommandKind.Database, command.Kind);
        Assert.Null(command.Collection);
    }

    [Fact]
    public void BareCollectionDefaultsToFind()
    {
        var command = ShellParser.Parse("db.movies");

        Assert.Equal("movies", command.Collection);
        Assert.Equal("find", Assert.Single(command.Calls).Method);
    }

    [Fact]
    public void TopLevelCommasSplitArguments()
    {
        var command = ShellParser.Parse("db.c.find({ a: 1 }, { b: 1 })");

        Assert.Equal(2, command.Calls[0].Arguments.Count);
    }

    [Fact]
    public void CommasInsideDocumentsDoNotSplit()
    {
        var command = ShellParser.Parse("db.c.find({ a: 1, b: 2 })");

        Assert.Equal("{ a: 1, b: 2 }", Assert.Single(command.Calls[0].Arguments));
    }

    [Fact]
    public void CommasInsideArraysDoNotSplit()
    {
        var command = ShellParser.Parse("db.c.aggregate([ { $match: {} }, { $limit: 2 } ])");

        Assert.Single(command.Calls[0].Arguments);
    }

    [Fact]
    public void DotInsideAStringDoesNotSplitTheChain()
    {
        // A dotted field name must not be read as another chain segment.
        var command = ShellParser.Parse("""db.movies.find({ "a.b": 1 })""");

        Assert.Equal("movies", command.Collection);
        Assert.Equal("find", Assert.Single(command.Calls).Method);
    }

    [Fact]
    public void CommaInsideAStringDoesNotSplitArguments()
    {
        var command = ShellParser.Parse("""db.c.find({ "name": "Smith, John" })""");

        Assert.Single(command.Calls[0].Arguments);
    }

    [Fact]
    public void EscapedQuoteIsHandled()
    {
        var command = ShellParser.Parse("""db.c.find({ "q": "say \" hi" })""");

        Assert.Equal("c", command.Collection);
        Assert.Single(command.Calls[0].Arguments);
    }

    [Fact]
    public void NonDbInputIsUnknown()
    {
        Assert.Equal(ShellCommandKind.Unknown, ShellParser.Parse("select * from movies").Kind);
    }

    [Fact]
    public void EmptyArgumentListYieldsNoArguments()
    {
        var command = ShellParser.Parse("db.c.getIndexes()");

        Assert.Empty(Assert.Single(command.Calls).Arguments);
    }

    [Fact]
    public void UnterminatedCallDoesNotHang()
    {
        // A half-typed command must return, not loop looking for the closing bracket.
        var command = ShellParser.Parse("db.c.find({ a: 1");

        Assert.Equal(ShellCommandKind.Collection, command.Kind);
    }
}
