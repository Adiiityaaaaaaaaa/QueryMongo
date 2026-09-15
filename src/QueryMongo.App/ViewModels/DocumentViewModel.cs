using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MongoDB.Bson;
using QueryMongo.Core.Json;

namespace QueryMongo.App.ViewModels;

/// <summary>One cell in the table view.</summary>
public sealed record DocumentCell(string Field, string Value);

/// <summary>
/// One document in the results list.
///
/// The pretty-printed JSON is built lazily: a page of 50 rows is cheap, but a single
/// document can be 16 MB, and formatting every one up front would stall the list on
/// collections with large documents.
/// </summary>
public sealed partial class DocumentViewModel : ObservableObject
{
    private string? _json;

    public DocumentViewModel(BsonDocument document, int ordinal)
    {
        Document = document;
        Ordinal = ordinal;
        Preview = BsonJson.ToPreview(document);
        EditorText = "";
    }

    public BsonDocument Document { get; }

    public int Ordinal { get; }

    public string Preview { get; }

    public string Json => _json ??= BsonJson.ToPrettyJson(Document);

    /// <summary>The <c>_id</c> rendered for the row header, or a marker when absent.</summary>
    public string IdDescription =>
        Document.TryGetValue("_id", out var id) ? id.ToString() ?? "" : "(no _id)";

    public BsonValue? Id => Document.TryGetValue("_id", out var id) ? id : null;

    /// <summary>Documents from an aggregation may have no _id, so they cannot be edited in place.</summary>
    public bool CanEdit => Id is not null;

    [ObservableProperty] public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReadOnly))]
    public partial bool IsEditing { get; set; }

    [ObservableProperty] public partial string EditorText { get; set; }

    [ObservableProperty] public partial string? EditError { get; set; }

    public bool IsReadOnly => !IsEditing;

    /// <summary>Loads the document into the editor and switches the row into edit mode.</summary>
    public void BeginEdit()
    {
        EditorText = Json;
        EditError = null;
        IsEditing = true;
        IsExpanded = true;
    }

    public void CancelEdit()
    {
        IsEditing = false;
        EditError = null;
    }

    /// <summary>
    /// Validates the editor contents. Returns null and sets <see cref="EditError"/>
    /// when the text is not a usable document.
    /// </summary>
    public BsonDocument? TryReadEditor()
    {
        var parsed = BsonJson.ParseDocument(EditorText);
        if (!parsed.IsValid)
        {
            EditError = parsed.Error;
            return null;
        }

        if (parsed.Value is not { ElementCount: > 0 } doc)
        {
            EditError = "A document cannot be empty.";
            return null;
        }

        // Changing _id is a delete-and-insert, not an update; the server rejects it,
        // so it is caught here with a clearer message.
        if (Id is not null && doc.TryGetValue("_id", out var newId) && newId != Id)
        {
            EditError = "The _id cannot be changed. Clone the document instead.";
            return null;
        }

        EditError = null;
        return doc;
    }

    /// <summary>Flat field/value pairs for the table view.</summary>
    public ObservableCollection<DocumentCell> Cells { get; } = [];

    /// <summary>
    /// Populates <see cref="Cells"/> for the given column order, so every row in the
    /// table lines up even when documents have different fields.
    /// </summary>
    public void BuildCells(IReadOnlyList<string> columns)
    {
        Cells.Clear();
        foreach (var column in columns)
            Cells.Add(new DocumentCell(column, RenderCell(Document, column)));
    }

    private static string RenderCell(BsonDocument doc, string field)
    {
        if (!doc.TryGetValue(field, out var value)) return "";

        return value switch
        {
            BsonNull => "null",
            BsonString s => s.Value,
            BsonDocument nested => BsonJson.ToPreview(nested, 60),
            BsonArray array => $"[{array.Count}]",
            _ => value.ToString() ?? ""
        };
    }
}
