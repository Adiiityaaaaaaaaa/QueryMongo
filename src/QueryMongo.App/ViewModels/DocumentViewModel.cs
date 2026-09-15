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

        // Compass shows a document's fields immediately rather than as a one-line
        // preview that has to be opened.
        IsExpanded = true;
    }

    public BsonDocument Document { get; }

    public int Ordinal { get; }

    public string Preview { get; }

    public string Json => _json ??= BsonJson.ToPrettyJson(Document);

    /// <summary>
    /// Field rows for the expanded card, built on first expand. A flat list is rebuilt
    /// whenever a branch opens, so one ItemsControl can render the whole tree.
    /// </summary>
    public ObservableCollection<DocumentFieldViewModel> Fields { get; } = [];

    private List<DocumentFieldViewModel>? _roots;

    private void EnsureFields()
    {
        if (_roots is not null) return;

        _roots = DocumentFieldViewModel.ForDocument(Document).ToList();

        // RebuildFields hooks every visible row, roots included, so no separate
        // subscription is needed here.
        RebuildFields();
    }

    /// <summary>
    /// How many top-level fields a document shows before the "show more" toggle. Compass
    /// uses 25, so a wide document does not bury the ones after it.
    /// </summary>
    private const int DefaultVisibleFields = 25;

    private int _visibleFieldLimit = DefaultVisibleFields;

    /// <summary>Top-level fields in the document, however many are on screen.</summary>
    public int TotalFieldCount => _roots?.Count ?? 0;

    public bool HasHiddenFields => TotalFieldCount > _visibleFieldLimit;

    public bool CanHideFields => _visibleFieldLimit > DefaultVisibleFields;

    public string ShowMoreLabel =>
        $"Show {Math.Min(1000, TotalFieldCount - _visibleFieldLimit)} more fields";

    /// <summary>Reveals the next block of fields, as Compass's toggle does.</summary>
    public void ShowMoreFields()
    {
        _visibleFieldLimit += 1000;
        RebuildFields();
    }

    public void ShowFewerFields()
    {
        _visibleFieldLimit = DefaultVisibleFields;
        RebuildFields();
    }

    private void RebuildFields()
    {
        if (_roots is null) return;

        Fields.Clear();

        var line = 1;

        foreach (var row in _roots.Take(_visibleFieldLimit).SelectMany(r => r.Visible()))
        {
            // A newly created child needs the same hook, or expanding it does nothing.
            row.PropertyChanged -= OnFieldChanged;
            row.PropertyChanged += OnFieldChanged;

            // Line numbers follow the flattened view, so they stay contiguous as
            // branches open and close.
            row.LineNumber = line++;

            Fields.Add(row);
        }

        OnPropertyChanged(nameof(TotalFieldCount));
        OnPropertyChanged(nameof(HasHiddenFields));
        OnPropertyChanged(nameof(CanHideFields));
        OnPropertyChanged(nameof(ShowMoreLabel));
    }

    private void OnFieldChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DocumentFieldViewModel.IsExpanded)) RebuildFields();
    }

    /// <summary>The <c>_id</c> rendered for the row header, or a marker when absent.</summary>
    public string IdDescription =>
        Document.TryGetValue("_id", out var id) ? id.ToString() ?? "" : "(no _id)";

    public BsonValue? Id => Document.TryGetValue("_id", out var id) ? id : null;

    /// <summary>Documents from an aggregation may have no _id, so they cannot be edited in place.</summary>
    public bool CanEdit => Id is not null;

    /// <summary>
    /// Compass shows a document's fields straight away rather than collapsing it to a
    /// one-line preview, so this starts open. Set in the constructor because a partial
    /// property cannot carry an initializer.
    /// </summary>
    [ObservableProperty] public partial bool IsExpanded { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) EnsureFields();
    }

    /// <summary>Switches the expanded card between field rows and raw JSON.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFields))]
    public partial bool ShowRawJson { get; set; }

    public bool ShowFields => !ShowRawJson;

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
