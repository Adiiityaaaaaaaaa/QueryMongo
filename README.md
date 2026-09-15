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
| SSH       | SSH.NET 2026.0                         |
| Tests     | xUnit v3 on Microsoft.Testing.Platform |

## Features

### Connect
- Connection string with fail-fast timeouts and wire compression (zstd/snappy/zlib).
- **SSH tunnelling** to reach a deployment behind a bastion, with password or private-key
  authentication. The URI is rewritten to the local end of the tunnel, keeping its
  credentials and options.
- Saved connections, encrypted per Windows account with DPAPI, shown with the password
  redacted so a screen share never leaks it. SSH credentials live inside the same
  protected payload.

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
- A distribution chart per field: category bars for strings and booleans, histograms for
  numbers and dates.
- Flags fields that are missing from some documents, and fields holding more than one
  type — usually a bug.

### Explain
- A visual plan tree: one card per stage, indented by depth, each showing what the stage
  does, documents returned and examined, and time spent.
- Stages that read every document or sort in memory are flagged with specific advice.
- Totals across the plan, plus a warning when the server examined far more documents
  than it returned.
- Raw explain output on a second tab.

### Indexes
- Name, key shape with sort direction, properties, on-disk size, and usage counts from
  `$indexStats`, so indexes nothing queries are visible.
- Create indexes with unique, sparse, background, TTL, partial-filter and collation
  options. Drop any index except `_id`.

### Validation
- Read and edit a collection's JSON-schema rules, with validation level and action.
- Preview which existing documents would pass and which would be rejected — applying
  rules never re-checks existing documents, so this is the only way to see the impact.

### Shell
- A command interpreter over the driver: `show dbs`, `show collections`, `use`, and the
  common `db.<collection>.<method>()` calls with chained `.sort()`, `.limit()`, `.skip()`
  and `.count()`.
- Transcript with up/down history recall, and `help` listing everything supported.
- **Not mongosh.** mongosh is a Node.js runtime; there is no JavaScript engine here, so
  variables, loops and expressions are out of scope. Unsupported methods say so by name.

### Search Indexes
- Lists Atlas Search and Vector Search indexes with type, status and definition.
- Create, edit and drop, with dynamic-mapping and vector templates.
- On a non-Atlas deployment the pane says so plainly instead of surfacing a command error.

### Map
- Plots GeoJSON `Point` fields and legacy `[longitude, latitude]` pairs, auto-detecting
  which fields hold coordinates.
- Points are drawn on an equirectangular graticule with degree labels. There are
  deliberately **no basemap tiles** — fetching them would mean calling a third-party
  service from an app that otherwise only talks to your database.

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
  Connections/          profiles, DPAPI store, query history, SSH tunnel
  Services/             catalog, query, schema, index, search, validation,
                        admin, transfer, geo, export-to-language
  Shell/                command parser and interpreter
  Models/               query spec, explain tree
src/QueryMongo.App/
  Themes/               palette and control styles
  Controls/             JSON highlighter, map, binding converters
  ViewModels/           one per pane, plus the shell and tab containers
  Views/Panes/          one UserControl per pane
  Dialogs/              create/edit/confirm dialogs
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

The UI theme lives entirely in `Themes/Palette.xaml`: every colour resolves through a
theme resource with light and dark variants, so nothing is hard-coded against one
background.

A startup crash is written to `%LOCALAPPDATA%\QueryMongo\crash.log`.

## Not yet built

- Embedded mongosh (a real JavaScript runtime, rather than the command interpreter above)
- Atlas-specific views beyond search indexes: cluster metrics, billing, online archive
- TLS certificate pickers and proxy configuration in the connection form
- Basemap tiles behind the map view

## License

MIT. See [LICENSE](LICENSE).
