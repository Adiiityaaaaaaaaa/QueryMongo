using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MongoDB.Bson;
using QueryMongo.Core.Json;

namespace QueryMongo.App.ViewModels;

/// <summary>
/// One field inside a document, as Compass's document view shows it: name, a type
/// badge, the value, and children for documents and arrays.
/// </summary>
public sealed partial class DocumentFieldViewModel : ObservableObject
{
    /// <summary>Deeper than this and the indent stops being readable; the raw JSON covers it.</summary>
    private const int MaxDepth = 6;

    public DocumentFieldViewModel(string name, BsonValue value, int depth)
    {
        Name = name;
        Value = value;
        Depth = depth;

        TypeName = DescribeType(value);
        RenderedValue = RenderValue(value);
        HasChildren = value is BsonDocument { ElementCount: > 0 } or BsonArray { Count: > 0 };

        // Top levels open by default; deeper ones stay closed so a big document is
        // still scannable when it first appears.
        IsExpanded = HasChildren && depth < 1;

        if (HasChildren && IsExpanded) BuildChildren();
    }

    public string Name { get; }
    public BsonValue Value { get; }
    public int Depth { get; }

    public string TypeName { get; }
    public string RenderedValue { get; }
    public bool HasChildren { get; }

    /// <summary>
    /// Indent per level. Compass uses one expand-icon width (LeafyGreen spacing[400],
    /// 16px) per level, so nested fields line up under their parent's caret.
    /// </summary>
    public double Indent => Depth * 16;

    /// <summary>Line number in the flattened view, assigned when the rows are laid out.</summary>
    public int LineNumber { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CaretGlyph))]
    public partial bool IsExpanded { get; set; }

    /// <summary>The row caret, pointing down when the branch is open.</summary>
    public string CaretGlyph => IsExpanded ? "CaretDown" : "CaretRight";

    public ObservableCollection<DocumentFieldViewModel> Children { get; } = [];

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && Children.Count == 0) BuildChildren();
    }

    private void BuildChildren()
    {
        if (Depth >= MaxDepth) return;

        switch (Value)
        {
            case BsonDocument document:
                foreach (var element in document.Elements)
                    Children.Add(new DocumentFieldViewModel(element.Name, element.Value, Depth + 1));
                break;

            case BsonArray array:
                // Array entries are named by index, which is how Compass labels them.
                for (var i = 0; i < array.Count; i++)
                    Children.Add(new DocumentFieldViewModel($"{i}", array[i], Depth + 1));
                break;
        }
    }

    /// <summary>The short type label on each row's badge.</summary>
    public static string DescribeType(BsonValue value) => value.BsonType switch
    {
        BsonType.Double => "Double",
        BsonType.Int32 => "Int32",
        BsonType.Int64 => "Int64",
        BsonType.Decimal128 => "Decimal128",
        BsonType.String => "String",
        BsonType.Document => "Object",
        BsonType.Array => "Array",
        BsonType.Boolean => "Boolean",
        BsonType.DateTime => "Date",
        BsonType.ObjectId => "ObjectId",
        BsonType.Null => "Null",
        BsonType.Binary => "Binary",
        BsonType.RegularExpression => "Regex",
        BsonType.Timestamp => "Timestamp",
        BsonType.JavaScript => "Code",
        _ => value.BsonType.ToString()
    };

    private static string RenderValue(BsonValue value) => value switch
    {
        // Containers show their size rather than their contents; the children carry those.
        BsonDocument document => document.ElementCount == 1 ? "{ 1 field }" : $"{{ {document.ElementCount} fields }}",
        BsonArray array => array.Count == 1 ? "[ 1 item ]" : $"[ {array.Count} items ]",
        BsonNull => "null",
        // Strings show quoted, matching how Compass renders a string value.
        BsonString s => $"\"{s.Value}\"",
        BsonBoolean b => b.Value ? "true" : "false",
        BsonDateTime date => date.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"),
        BsonBinaryData binary => $"Binary({binary.Bytes.Length} bytes)",
        _ => value.ToString() ?? ""
    };

    /// <summary>Builds the field rows for a whole document.</summary>
    public static IEnumerable<DocumentFieldViewModel> ForDocument(BsonDocument document) =>
        document.Elements.Select(e => new DocumentFieldViewModel(e.Name, e.Value, 0));

    /// <summary>
    /// Flattens the visible rows in order, so the UI can bind one flat list and still
    /// show a tree. Collapsed branches contribute nothing.
    /// </summary>
    public IEnumerable<DocumentFieldViewModel> Visible()
    {
        yield return this;

        if (!IsExpanded) yield break;

        foreach (var child in Children)
            foreach (var descendant in child.Visible())
                yield return descendant;
    }
}
