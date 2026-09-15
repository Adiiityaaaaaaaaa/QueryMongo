# QueryMongo

A native Windows MongoDB client. Compass-style workflow — browse, query, aggregate,
inspect indexes — as a single `QueryMongo.exe` with no Electron runtime underneath.

> Status: early. The query, aggregation, index and explain paths work end to end;
> document editing and several Compass features are not built yet. See
> [Roadmap](#roadmap).

## Why

MongoDB Compass is an Electron app: it ships a browser to render a data grid. QueryMongo
targets the same daily workflow on the native Windows stack, so memory sits in the
low hundreds of MB rather than the high ones, and startup does not pay for a Chromium
boot.

## Stack

| Layer     | Choice                          |
|-----------|---------------------------------|
| Runtime   | .NET 10                         |
| UI        | WinUI 3 / Windows App SDK 2.4   |
| Data      | MongoDB .NET Driver 3.11        |
| MVVM      | CommunityToolkit.Mvvm 8.4       |
| Tests     | xUnit v3 on Microsoft.Testing.Platform |

## What works today

- **Connect** — connection string with fail-fast timeouts; saved connections encrypted
  per Windows account with DPAPI, displayed with the password redacted.
- **Browse** — databases and collections in a sidebar, collections loaded on expand so
  a deployment with hundreds of databases still opens instantly. Views and time-series
  collections are marked.
- **Query** — filter / project / sort / limit / skip, accepting the shell syntax you'd
  type in `mongosh` (unquoted keys, single quotes). Paged, with an in-flight query
  cancelled when you run a new one.
- **Explain** — every query also reports its plan, and warns when the server fell back
  to a collection scan.
- **Aggregate** — run a pipeline, capped at 50 preview documents so a pipeline without
  a `$limit` can't pull a whole collection into the UI.
- **Indexes** — name, key shape, and on-disk size per index.

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
are both bundled. That costs about 285 MB on disk for roughly 140 MB of working set at
runtime.

If your users already have both runtimes installed, a framework-dependent build is a
fraction of the size:

```powershell
dotnet publish src/QueryMongo.App -c Release -r win-x64 `
  -p:SelfContained=false -p:WindowsAppSDKSelfContained=false -o dist-fd
```

Swap `win-x64` for `win-arm64` to target ARM devices.

## Layout

```
src/QueryMongo.Core/    driver access, connection storage, BSON/JSON — no UI types
src/QueryMongo.App/     WinUI 3 views and view models
tests/                  Core tests; no running MongoDB required
```

`Core` deliberately references nothing from WinUI, so query, parsing and storage logic
is testable without a UI thread.

## Notes for contributors

Two build settings are load-bearing and easy to break:

- `EnableMsixTooling` must stay `true` even though the app is unpackaged. It is what
  emits `QueryMongo.pri`, the resource index holding the compiled XAML. Turn it off and
  the published exe builds cleanly, then dies at startup with a bare
  `XamlParseException`.
- `PublishTrimmed` must stay off. The MongoDB driver resolves BSON serializers
  reflectively, and trimming removes them silently.

View models use `[ObservableProperty]` on **partial properties**, not fields — the
field form generates WinRT marshalling that the toolkit rejects for WinUI targets.

A startup crash is written to `%LOCALAPPDATA%\QueryMongo\crash.log`.

## Roadmap

- [ ] Editing, inserting and deleting documents from the results list
- [ ] Schema analysis (field frequency and type distribution)
- [ ] Index creation and drop
- [ ] Table and tree views of results, alongside the current JSON view
- [ ] Query history and saved queries
- [ ] Export results to JSON and CSV
- [ ] Multiple open collection tabs

## License

MIT. See [LICENSE](LICENSE).
