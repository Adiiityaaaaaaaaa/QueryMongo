# QueryMongo

A native Windows MongoDB client. MongoDB Compass's workflow — browse, query, aggregate,
analyse, administer — as a single `QueryMongo.exe`, with no Electron runtime underneath.

## Why

Compass is an Electron app: it ships a browser to render a data grid. QueryMongo targets
the same daily workflow on the native Windows stack, so memory sits around 150 MB instead
of the high hundreds, and startup does not pay for a Chromium boot.

## Stack

| Layer     | Choice                                 |
|-----------|----------------------------------------|
| Runtime   | .NET 10                                |
| UI        | WinUI 3 / Windows App SDK 2.4          |
| Data      | MongoDB .NET Driver 3.11               |
| MVVM      | CommunityToolkit.Mvvm 8.4              |
| Tests     | xUnit v3 on Microsoft.Testing.Platform |

## Features

### Connect
- Connection string with fail-fast timeouts and wire compression (zstd/snappy/zlib).
- Saved connections, encrypted per Windows account with DPAPI, shown with the password
  redacted so a screen share never leaks it.

### Browse
- Sidebar of databases and collections, filterable by name. Collections load on expand,
  so a deployment with hundreds of databases still opens instantly.
- Views and time-series collections are marked and treated as read-only where relevant.
- Create and drop databases and collections, rename a collection, or empty one while
  keeping its indexes. Capped and time-series options are available at creation.
- Multiple collections open as tabs; reopening a collection focuses its existing tab.

### Documents
- Query bar: filter, project, sort, collation, limit and skip, accepting the shell syntax
  you'd type in `mongosh` (unquoted keys, single quotes).
- Three view modes — **List** (expandable JSON), **Table** (aligned columns across
  heterogeneous documents) and **JSON** (the page as one array).
- Edit, clone, copy and delete any document in place. `_id` edits are rejected with an
  explanation rather than a server error.
- Insert a document, update every document matching the filter, or delete them all.
- Query history and named favourites, per collection, deduplicated and capped.
- Export the current query as driver code in 9 languages.

### Aggregations
- Stage-by-stage pipeline builder: add, reorder, duplicate, disable or remove stages,
  with all 28 of Compass's stage operators and starter templates.
- Per-stage previews showing what the pipeline looks like at each step.
- Text mode for editing the whole pipeline as JSON, round-tripping to the stage list.
- `$out` and `$merge` are detected, named, and never run by accident — they require an
  explicit confirmation naming the target collection.
- Aggregation explain, and export to driver code in 9 languages.

### Schema
- Samples documents server-side with `$sample` and reports every field, its type mix,
  how often it appears and example values.
- Flags fields that are missing from some documents, and fields holding more than one
  type — usually a bug.

### Explain
- Execution plan for the current query: stage, index used, documents and keys examined,
  and execution time, with the raw plan below.
- Warns when the server fell back to a collection scan on a large collection.

### Indexes
- Name, key shape with sort direction, properties, on-disk size, and usage counts from
  `$indexStats`, so indexes nothing queries are visible.
- Create indexes with unique, sparse, background, TTL, partial-filter and collation
  options. Drop any index except `_id`.

### Validation
- Read and edit a collection's JSON-schema rules, with validation level and action.
- Preview which existing documents would pass and which would be rejected — applying
  rules never re-checks existing documents, so this is the only way to see the impact.

### Performance
- Live server metrics sampled every 2 seconds: insert/query/update/delete/command rates,
  connections, memory, network throughput and uptime.
- Current operations list, with the ability to kill a long-running operation.

### Import / export
- Export query results to JSON or CSV, streamed so a collection larger than memory works.
- Import from a JSON array, newline-delimited JSON, or CSV with type inference. A
  malformed line is collected and reported rather than aborting the run.

## Build and run

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
Visual Studio is not needed.

```powershell
dotnet build QueryMongo.slnx
dotnet test tests/QueryMongo.Core.Tests/QueryMongo.Core.Tests.csproj
dotnet run --project src/QueryMongo.App -r win-x64
```

### Producing the exe

```powershell
dotnet publish src/QueryMongo.App -c Release -r win-x64 -o dist
```

`dist/QueryMongo.exe` runs on a clean machine: the .NET runtime and the Windows App SDK
are both bundled. That costs about 285 MB on disk for roughly 150 MB of working set.

If your users already have both runtimes, a framework-dependent build is far smaller:

```powershell
dotnet publish src/QueryMongo.App -c Release -r win-x64 `
  -p:SelfContained=false -p:WindowsAppSDKSelfContained=false -o dist-fd
```

Swap `win-x64` for `win-arm64` to target ARM devices.

## Layout

```
src/QueryMongo.Core/    driver access, storage, BSON/JSON — no UI types
  Connections/          connection profiles, DPAPI store, query history
  Services/             catalog, query, schema, index, validation, admin, transfer
src/QueryMongo.App/     WinUI 3 views, view models and dialogs
tests/                  Core tests; no running MongoDB required
```

`Core` references nothing from WinUI, so query, parsing and storage logic is testable
without a UI thread.

## Notes for contributors

Three settings are load-bearing and easy to break:

- **`EnableMsixTooling` must stay `true`** even though the app is unpackaged. It emits
  `QueryMongo.pri`, the resource index holding the compiled XAML. Turn it off and the
  published exe builds cleanly, then dies at startup with a bare `XamlParseException`.
- **`PublishTrimmed` must stay off.** The MongoDB driver resolves BSON serializers
  reflectively, and trimming removes them silently.
- **Declare `xmlns` before `mc:Ignorable`** in XAML. The Release XBF compiler rejects the
  reverse order that Debug tolerates.

View models use `[ObservableProperty]` on **partial properties**, not fields — the field
form generates WinRT marshalling the toolkit rejects for WinUI targets. Partial properties
cannot carry initializers, so defaults go in the constructor.

A startup crash is written to `%LOCALAPPDATA%\QueryMongo\crash.log`.

## Not yet built

Compass features this does not cover:

- Embedded `mongosh` shell
- Atlas Search index management and Atlas-specific views
- SSH tunnelling and advanced connection forms (TLS certificate pickers, proxies)
- Geospatial map view for coordinate data
- Charts and visual schema histograms (schema data is reported as text)
- Visual explain-plan tree (the plan is shown as structured text plus raw JSON)

## License

MIT. See [LICENSE](LICENSE).
