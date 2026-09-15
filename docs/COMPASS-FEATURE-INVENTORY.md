# MongoDB Compass — complete feature inventory

The full surface of MongoDB Compass, taken from its source (the clone at `C:\CodeBase\compass`),
to be used as the checklist for porting it to QueryMongo.

Status marks what QueryMongo has today:

- **[x]** built and reasonably faithful
- **[~]** partially built — the item names what is missing
- **[ ]** not built

Compass package names are given so each item can be traced back to its source.

---

## 1. Application shell

### 1.1 Window chrome

| Item | Status |
|---|---|
| Frameless window with custom title bar | [x] |
| Minimise / maximise / close | [x] |
| Window title tracks connection + namespace (`updateTitle`) | [ ] |
| Multiple windows (`New Window`, Ctrl+N) | [ ] |
| Quit confirmation dialog, with "don't ask again" | [ ] |
| Auto-update ("Check for updates…", "Installing updates…", "Restart to Update") | [ ] |

### 1.2 Application menu bar (`packages/compass/src/main/menu.ts`)

Windows layout: **Connections · Edit · View · [Collection] · Help**. None of it is built.

**Connections** — [ ]
- Import Saved Connections
- Export Saved Connections
- —
- Exit

**Edit** — [ ]
- Undo (Ctrl+Z) · Redo (Ctrl+Shift+Z)
- —
- Cut (Ctrl+X) · Copy (Ctrl+C) · Paste (Ctrl+V) · Select All (Ctrl+A)
- —
- Find (Ctrl+F)
- —
- Settings (Ctrl+,)

**View** — [ ]
- Reload (Ctrl+Shift+R) · Reload Data (Ctrl+R)
- —
- Actual Size (Ctrl+0) · Zoom In (Ctrl+=) · Zoom Out (Ctrl+-)
- —
- Toggle DevTools (Ctrl+Alt+I) — only when DevTools are enabled

**Collection** — contributed by the open collection tab — [ ]
- Share Schema as JSON (Legacy)
- Import Data
- Export Collection

**Help** — [ ]
- Online Compass Help (F1)
- License
- View Source Code on GitHub
- Suggest a Feature
- Report a Bug
- Open Log File
- —
- About Compass
- Check for updates…

### 1.3 Workspace tab strip (`compass-workspaces`, `compass-components/workspace-tabs`)

| Item | Status |
|---|---|
| Tab types: Welcome, My Queries, Databases, Collections, Collection, Shell, Performance, Data Modeling | [~] Data Modeling missing |
| Replace-active vs open-in-new-tab rules | [x] |
| Per-connection colour bar along the tab's top edge | [x] |
| Icon + title + close-on-hover, 96–192px wide | [x] |
| Tooltip listing connection / database / collection | [x] |
| Drag to reorder (dnd-kit) | [ ] |
| Context menu: Close all other tabs, Duplicate | [~] Duplicate missing |
| Middle-click to close | [x] |
| New-tab (+) button | [x] |
| Ctrl+Tab / Ctrl+Shift+Tab — next / previous tab | [ ] |
| Ctrl+Shift+] / Ctrl+Shift+[ — next / previous tab | [ ] |
| Ctrl+W — close tab · Ctrl+T — new tab | [ ] |
| Arrow keys / Home / End navigate the strip | [ ] |
| Restore previous session's tabs on startup | [ ] |
| Empty state: faint MongoDB logo mark | [~] wrong glyph (graduation cap) |

### 1.4 Sidebar (`compass-sidebar`, `compass-connections-navigation`)

| Item | Status |
|---|---|
| Resizable, 300px default, 210 min, 600 max | [x] |
| Header: app name + settings gear | [~] gear opens nothing |
| Global nav: My Queries, Data Modeling | [~] Data Modeling missing |
| `CONNECTIONS (n)` header with count | [x] |
| Collapse-all-connections button | [x] |
| Add-new-connection button | [x] |
| Import / Export connections buttons | [ ] |
| Search box filtering connections, databases and collections by regex | [~] plain substring, not regex |
| Filter popover: "exclude inactive connections" | [ ] |
| Tree: connection → database → collection, 28px rows | [x] |
| Status marker on the connection icon (connected / connecting / failed) | [x] |
| Icon by kind: favourite star, laptop for localhost, server, database, folder, view, time series | [x] |
| Per-connection colour tint on its rows | [x] |
| Virtualised list for large trees | [ ] |
| Disconnected / inferred-from-privileges rows greyed | [~] no privilege inference |
| Atlas cluster-state badges (PAUSED, CREATING, TERMINATING, TERMINATED) | [ ] |
| Non-genuine-MongoDB marker | [ ] |
| In-Use Encryption (CSFLE) marker | [ ] |

**Connection row actions** (`item-actions.ts`) — [~] built except where noted
- Refresh databases · Create database · Open MongoDB shell · View performance metrics
- Show connection info · Disconnect
- Edit connection — [ ] (rename only)
- Copy connection string · Favorite / Unfavorite connection
- Duplicate connection — [ ]
- Remove connection
- Connect in new window — [ ]
- Atlas-only: Connect via…, View cluster overview, View monitoring, View query insights — [ ]

**Database row actions** — Create collection, Drop database — [x]

**Collection row actions** — Open in new tab, Rename collection, Drop collection / Drop view, Duplicate view, Modify view — [~] Duplicate/Modify view missing

### 1.5 Theme

| Item | Status |
|---|---|
| Light / Dark / OS themes, switchable in Settings | [~] follows OS only, no switch |
| LeafyGreen palette throughout | [x] |
| LeafyGreen icon set | [x] |

---

## 2. Connections

### 2.1 Connection form (`connection-form`) — six tabs

**General** — [~] only URI and name
- Connection string input with "Edit connection string" toggle and parse errors
- Scheme: `mongodb://` vs `mongodb+srv://`
- Host list (add/remove rows), Direct Connection toggle

**Authentication** — [ ]
- None / Username & Password (SCRAM) / X.509 / Kerberos (GSSAPI) / LDAP (PLAIN) / AWS IAM / OIDC
- SCRAM: Username, Password, Authentication Database, Auth mechanism
- Kerberos: Principal, Service Name, Service Realm, "Provide password directly", canonicalise host name
- LDAP: Username, Password
- AWS IAM: Access Key ID, Secret Access Key, Session Token
- OIDC: Client ID, Auth Code Flow Redirect URI, Identity Platform Endpoint, Tenant ID, Client Secret, Service Account E-Mail, "Stay logged in"

**TLS/SSL** — [ ]
- Default / On / Off
- Certificate Authority (.pem), Client Certificate and Key (.pem), Client Key Password
- tlsInsecure, tlsAllowInvalidHostnames, tlsAllowInvalidCertificates

**Proxy/SSH** — [~] SSH password and key only
- None / SSH with Password / SSH with Identity File / SOCKS5
- SSH: Hostname, Port, Username, Password, Identity File, Passphrase
- SOCKS5: Proxy Hostname, Proxy Tunnel Port, Proxy Username, Proxy Password

**In-Use Encryption (CSFLE)** — [ ]
- Key Vault Namespace
- KMS providers: AWS, GCP, Azure, KMIP, Local — add / rename / remove, per-provider credentials
- EncryptedFieldsMap, SchemaMap
- "Fully configured" / "Incomplete configuration" status

**Advanced** — [ ]
- Read Preference, Replica Set Name, Default Authentication Database, Read Concern, Write Concern, URL options table (key/value)

**Form chrome** — [~]
- Favourite toggle and connection **Colour** picker — [x]
- Save / Save & Connect / Connect buttons — [~] Connect only
- Connection error banner with retry — [x]
- "The connection form is disabled when the connection string cannot be parsed" — [ ]

### 2.2 Connection management

| Item | Status |
|---|---|
| Saved connections persisted, secrets in the OS keychain | [x] DPAPI |
| Recents vs Favourites | [~] favourite flag, no recents split |
| Import saved connections (file + decryption password) | [ ] |
| Export saved connections (file + encryption password, "Remove Secrets") | [ ] |
| Protect Connection String Secrets preference | [ ] |
| Connection info modal (version, topology, host, Atlas details) | [~] plain text dialog |
| Multiple simultaneous connections | [x] |
| Connection limit preference | [ ] |

---

## 3. Workspaces

### 3.1 Welcome (`compass-welcome`)

| Item | Status |
|---|---|
| "Welcome to Compass" with the product illustration | [~] wrong illustration |
| "Add new connection" button | [x] |
| Atlas panel: "New to Compass and don't have a cluster?" + CREATE FREE CLUSTER | [x] |
| Active-connection list once connections exist | [ ] |
| First-run modal: privacy settings, telemetry opt-in | [ ] |

### 3.2 My Queries (`compass-saved-aggregations-queries`)

| Item | Status |
|---|---|
| Grid of saved queries and pipelines | [x] |
| Search box | [x] |
| Filter by Database / Collection / "All databases" / "All collections" | [ ] |
| Sort by Name / Last Modified | [ ] |
| Card actions: Open in…, Copy, Rename, Delete | [~] Open and Delete only |
| "Open in" prompts for a connection when ambiguous | [ ] |
| Empty state: "No saved queries yet." | [x] |

### 3.3 Databases (`databases-collections-list`)

| Item | Status |
|---|---|
| Table: Database name, Storage size, Data size, Collections, Indexes | [x] |
| Sortable columns | [ ] |
| Virtualised rows | [ ] |
| Breadcrumb (connection) | [x] |
| Create database / Refresh / Open MongoDB shell | [x] |
| Delete database from the row | [x] |
| "Looks like your cluster is empty" zero state | [~] plain text |
| "Unable to display databases and collections" error state | [~] error bar |
| `too-many-collections` insight chip | [ ] |
| Load sample data banner (Atlas) | [ ] |

### 3.4 Collections (`databases-collections-list`)

| Item | Status |
|---|---|
| Table: Collection name, Properties, Storage size, Data size, Documents, Avg. document size, Indexes, Total index size | [x] |
| Property badges: view, timeseries, capped, clustered, collation, Queryable Encryption | [x] |
| Badge tooltips (collation options, view source, bucket stats) | [~] view source only |
| Storage size tooltip splitting total / used / free | [x] |
| Sortable columns · virtualised rows | [ ] |
| Breadcrumb (connection → database) | [x] |
| Create collection / Refresh / Open MongoDB shell / Delete | [x] |

### 3.5 Collection (`compass-collection`)

| Item | Status |
|---|---|
| Breadcrumb: connection → database → collection (→ view source) | [x] |
| Badges: READ-ONLY, Time-Series Collection, CLUSTERED, Queryable Encryption, View | [~] one combined badge |
| Sub-tabs: **Documents · Aggregations · Schema · Indexes · Validation** | [~] 9 flat tabs instead |
| Global Writes sub-tab (Atlas sharded) | [ ] |
| Insight chips for view pipelines ($lookup, $text/$regex) | [ ] |
| "Create view" / "Edit view" / "Duplicate view" flows | [ ] |
| Modals scoped to the tab: Explain Plan, Export to Language | [~] both are tabs, not modals |
| Mock data generator (AI) | [ ] |

### 3.6 Shell (`compass-shell`)

| Item | Status |
|---|---|
| Real **mongosh** — full JS runtime in a worker thread | [ ] driver-backed interpreter only |
| Autocomplete, syntax highlighting, multi-line editing | [ ] |
| History, up/down recall | [~] history only |
| Output formatting matching mongosh | [~] plain text |
| "Shell Info" panel | [ ] |
| Resizable drawer at the bottom of a collection tab | [ ] |
| `use(<ns>)` pre-evaluated when opened from a namespace | [~] database scope only |

### 3.7 Performance (`compass-serverstats`)

| Item | Status |
|---|---|
| Live charts: **operations**, **read & write**, **network**, **memory** | [~] numeric tiles, no charts |
| Pause / resume sampling | [x] |
| Slow operations list | [x] |
| Kill operation | [x] |
| Hottest collections ("Top") | [ ] |
| Chart detail view on hover | [ ] |
| Error state when the server denies `serverStatus` | [x] |

### 3.8 Data Modeling (`compass-data-modeling`) — [ ] entirely missing

- Diagram list, search diagrams, create / rename / delete
- Select connection + database, choose collections, sample size
- Automatic relationship inference ("Analyzing collection schemas…", "Inferring relationships…")
- Diagram editor: collection cards, fields, drag, zoom, undo/redo
- Collection / Field / Relationship configuration side drawers
- Local and foreign cardinality, local/foreign field pickers
- Notes on entities
- Export as PNG / JSON / MDM file; import MDM file

---

## 4. Documents (`compass-crud`)

### 4.1 Query bar (`compass-query-bar`)

| Item | Status |
|---|---|
| Filter editor with `Type a query: { field: 'value' }` placeholder | [x] |
| Options: Project, Sort, Max Time MS, Collation, Skip, Limit, Index Hint | [x] |
| Expand / collapse options toggle | [x] |
| Find / Reset buttons | [x] |
| Explain button with dropdown: Visual tree, Raw output, Interpret (AI) | [~] single button → tab |
| Query history popover: recent + favourites | [x] |
| Save current query as favourite ("Favorite Name") | [x] |
| Copy Query to Clipboard / Delete Query from List | [~] delete only |
| **Visual query builder** — Field / Operator / Value rows, `=`, `!=`, `<`, `<=`, `>`, `>=`, `in`, `not in`, `does not exist`; combine with AND/OR; drag-and-drop fields into Projection / Sort / Skip / Limit | [ ] |
| Show / Hide query builder toggle | [ ] |
| AI: natural-language → query, feedback buttons | [ ] |
| Per-field "add to query" from a document value | [ ] |
| Syntax highlighting + autocomplete in the editor (CodeMirror) | [~] highlight only |

### 4.2 Toolbar

| Item | Status |
|---|---|
| Add Data ▾ → Insert document, Import JSON or CSV file, Generate mock data script | [~] no mock data |
| Update / Delete ▾ → Bulk update documents, Bulk delete documents | [x] |
| Export Data ▾ → Export query results, Export the full collection | [x] |
| Export Code (export query to language) | [x] |
| Documents-per-page selector (25 / 50 / 75 / 100) | [x] |
| `start – end of count` with "N/A" tooltip when the count timed out | [~] no N/A handling |
| Refresh documents | [x] |
| Previous / Next page | [x] |
| Output Options ▾ → Expand all documents, Collapse all documents | [x] |
| View switcher: Document list · E-JSON · Table | [x] |
| Outdated-results warning banner | [ ] |
| maxTimeMS-exceeded hint on timeout | [ ] |
| Insight chips (`query-executed-without-index`, `bloated-document`, `unbound-array`) | [~] one scan warning |

### 4.3 Document list view

| Item | Status |
|---|---|
| Keyline card per document | [x] |
| Field rows: line number, indent, caret, bold key, value, type | [x] |
| 25 fields then "Show N more fields" / "Show fewer fields" | [x] |
| Hover actions: Edit, Copy, Clone, Remove | [x] |
| Copy document as EJSON / as Shell Syntax | [~] one Copy |
| Inline field editing with type dropdown per field | [ ] |
| Add / remove field while editing | [ ] |
| Revert a single field change | [ ] |
| Edit footer: "Document modified", Cancel / Update | [~] simpler |
| Delete confirmation footer on the card | [~] dialog instead |
| Context menu on a document | [ ] |
| Encrypted-field markers | [ ] |
| Date/time picker for date fields | [ ] |
| "This collection has no data" zero state | [~] plain |

### 4.4 E-JSON view — [x] read-only JSON per document; [ ] per-document editing

### 4.5 Table view (`table-view`)

| Item | Status |
|---|---|
| Spreadsheet grid of documents (ag-grid) | [~] simple grid |
| Row number column, row actions column | [ ] |
| Per-cell editing with a type dropdown | [ ] |
| Add field column | [ ] |
| Expand nested document / array into its own breadcrumb level | [ ] |
| Breadcrumb for the drilled-into path | [ ] |

### 4.6 Writes

| Item | Status |
|---|---|
| Insert document (JSON and field-by-field modes) | [~] JSON only |
| Insert validation errors and CSFLE banner | [~] errors only |
| Bulk update modal: update spec, affected count, preview | [~] no preview |
| Bulk delete modal: affected count, typed confirmation | [x] |
| Clone document | [x] |
| Toasts reporting the result, with undo where possible | [ ] |

---

## 5. Aggregations (`compass-aggregations`)

| Item | Status |
|---|---|
| Stage cards: operator picker, editor, enable/disable, collapse | [x] |
| Add stage / Add stage before / Add stage after / Delete stage | [~] add and delete |
| Drag to reorder stages | [~] up/down buttons |
| Auto-preview per stage with Stage Input / Stage Output counts | [~] preview only |
| Toggle Auto Preview | [x] |
| **Focus mode** — full-screen single stage, Edit previous / next stage | [ ] |
| **Stage Wizard** side panel with use cases: match conditions, group by field, compute values in groups, subset by rank, include/exclude fields, sort, lookup, `$search` text | [ ] |
| Search for a Stage | [ ] |
| Text (pipeline-as-code) mode | [x] |
| Run aggregation / Export pipeline results | [x] |
| Explain aggregation (visual tree / raw / interpret) | [~] raw summary |
| **Saved pipelines**: Save, Save as, Open saved pipelines | [ ] |
| Create view from pipeline / Update view | [ ] |
| Export to language | [x] |
| Results: Document list / JSON list toggle, paging, count results, Refresh | [~] list only |
| Collapse / expand all fields and documents | [ ] |
| More Settings: sample size, preview limits, comment mode | [ ] |
| `$out` / `$merge` write detection and confirmation | [x] |
| `$search` helpers: index picker, "index does not exist" banner, stale-results banner, diagnose button | [ ] |
| `$rerank` banners | [ ] |
| Insight chips (`aggregation-executed-without-index`, `$lookup` in stage, `$text`/`$regex` in stage) | [ ] |
| Pipeline input documents preview ("Stage Input") | [ ] |

---

## 6. Schema (`compass-schema`)

| Item | Status |
|---|---|
| Sample-based analysis with a query filter | [x] |
| Per-field: name, type(s), presence % | [x] |
| Mixed-type warning | [x] |
| **Minicharts by type**: bar charts for strings/numbers, date histogram, boolean split | [~] text bars |
| **Unique-value bubbles** for low-cardinality fields | [ ] |
| **Coordinates minichart** — map for GeoJSON fields | [~] separate Map tab |
| Document / array miniature charts for nested shapes | [ ] |
| Click a chart segment to add it to the query | [ ] |
| **Export JSON Schema** (standard and legacy modals) | [ ] |
| Share Schema as JSON (Legacy) from the Collection menu | [ ] |
| Sampling documentation link and "Do not show me this message again" | [ ] |
| Zero state when the collection is empty | [~] plain |

---

## 7. Indexes (`compass-indexes`)

| Item | Status |
|---|---|
| Segmented control inside the tab: **Standard Index / Search Index** | [ ] two separate tabs |
| Standard index table: Name, Type, Size, Usage, Properties, drop action | [~] no usage stats |
| Sort and search indexes ("Find by index name") | [ ] |
| Index build-in-progress and failed states | [ ] |
| Index detail drawer | [ ] |
| **Create index modal**: field + direction rows via combobox, add/remove rows | [ ] raw JSON keys |
| Index name override | [x] |
| Options: unique, TTL (`expireAfterSeconds`), partial filter expression, custom collation, sparse | [x] |
| Options: wildcard projection, columnstore projection, hidden, case-insensitive | [ ] |
| "Build in rolling process" (Atlas) | [ ] |
| Create Atlas **Search Index** with template dropdown (dynamic / static mappings) | [~] basic template |
| Create **Vector Search Index** | [ ] |
| Auto-embedded vector search indexes | [ ] |
| Search index table: name, status, fields | [x] |
| Zero states: "No standard indexes", "No search indexes yet" | [~] plain |
| "View Indexes" jump from aggregation insight | [ ] |
| Incompatible-view banners | [ ] |

---

## 8. Validation (`compass-schema-validation`)

| Item | Status |
|---|---|
| JSON Schema rules editor | [x] |
| Validation **action**: Error / Warning | [x] |
| Validation **level**: Off / Moderate / Strict | [x] |
| Sampled documents that **would pass** | [x] |
| Sampled documents that **would fail** | [x] |
| Apply / Cancel with "Updating validation…" state | [x] |
| "Create validation rules" zero state | [~] plain |
| Documentation links for actions and levels | [ ] |
| Read-only banner for views | [ ] |

---

## 9. Explain plan (`compass-explain-plan`)

| Item | Status |
|---|---|
| Opened as a **modal** from the query bar / aggregation toolbar | [ ] it is a tab |
| Summary: documents returned, documents examined, index keys examined, execution time, sorted in memory | [x] |
| **Visual stage tree** with per-stage cards | [~] indented list |
| Zoom in / zoom out on the tree | [ ] |
| Side summary panel | [~] inline |
| Raw output (full explain JSON) | [x] |
| "Interpret" — AI explanation | [ ] |
| `explain-plan-without-index` insight | [x] warning |
| "Cannot visualize" banner for unsupported plans | [ ] |

---

## 10. Export to language (`compass-export-to-language`)

| Item | Status |
|---|---|
| Languages: **Java, Node, C#, Python, Ruby, Go, Rust, PHP** | [~] 9 of mine, different set |
| Include Import Statements | [ ] |
| Include Driver Syntax | [ ] |
| Use Builders (Java/C#) | [ ] |
| Copy to clipboard | [x] |
| Works for both queries and pipelines | [x] |

---

## 11. Import / Export data (`compass-import-export`)

**Import** — [~] file + format only
- Select JSON or CSV to import; file picker
- CSV: Delimiter (Comma, Tab, Semicolon, Space), "Ignore empty strings", "Stop on errors"
- **Preview table of the first rows with a per-field type dropdown and include/exclude checkboxes**
- "Select all fields"
- Progress modal with abort
- Result toast: inserted / failed counts, error list

**Export** — [~] path + format + "export all"
- Export query results vs full collection
- Export File Type: JSON or CSV
- JSON format: Default / Relaxed EJSON / Canonical EJSON
- **Field selection list with "Add field" for fields not in the sample**
- "Escape formulae in data" (CSV injection protection)
- Progress modal with abort
- Result toast with "Show file" action

---

## 12. Settings (`compass-settings`) — [ ] entirely missing

Modal with a sidebar of groups:

**General** — Compass UI Theme (Light/Dark/OS); Default Sort for Query Bar (`_id: 1` / `_id: -1`); Show Database and Collection Statistics; Enable Geographic Visualizations; Enable MongoDB Shell; Enable Atlas Search Indexes; Enable import/export; Enable "Run Pipeline"; Enable explain plan; Enable Automatic Updates; Show Quit Confirmation Dialog; Maximum open connections; Infer namespaces from privileges; Timezone; Legacy UUID encoding (C#, Java, Python, raw); Upper limit for maxTimeMS; Install Compass as URL Protocol Handler

**Theme** — Light / Dark / OS

**Privacy** — Enable network traffic other than to the MongoDB database; Give Product Feedback; Enable Usage Statistics; Enable Automatic Updates; Protect Connection String Secrets

**Proxy** — No proxy / System / Custom (URL, auth, exclusions)

**OIDC** — Stay logged in with OIDC; Show Device Auth Flow Checkbox; Browser command to use for authentication; Show Kerberos Password Field

**Gen AI** — Enable AI Features; project- and org-level opt-in; Enable AI Assistant; tool calling

**Feature Preview** — Show Developer Feature Flags; Enable DevTools; CSFLE Schema Map Debugging; rolling index builds; Global Writes tab; restore previous tabs on startup; `$rerank` stage UI; vector search index UI; Atlas Connection Error Debugger

---

## 13. Cross-cutting

### 13.1 Insights / performance signals (`compass-components/signals.tsx`) — [ ] except one

Chips with an explanation and a "Learn more" link:
`aggregation-executed-without-index`, `query-executed-without-index`, `explain-plan-without-index`,
`atlas-text-regex-usage-in-view`, `non-atlas-text-regex-usage-in-view`, `lookup-in-view`,
`atlas-with-search-text-regex-usage-in-stage`, `atlas-without-search-text-regex-usage-in-stage`,
`non-atlas-text-regex-usage-in-stage`, `lookup-in-stage`, `atlas-text-regex-usage-in-query`,
`bloated-document`, `too-many-indexes`, `too-many-collections`, `unbound-array`, `rerank-without-search`

Plus "Introducing insights" / "See insights in action" onboarding popover.

### 13.2 Other shared behaviour

| Item | Status |
|---|---|
| Right-click context menus across tree, tabs, documents, toolbars | [~] tree and tabs only |
| Toast notifications with actions (copy, show file, undo) | [ ] |
| Guide cues (first-run coach marks) | [ ] |
| Right-hand drawer (assistant, index details) | [ ] |
| Find in page (Ctrl+F) with match count and next/previous | [ ] |
| Error boundaries per workspace, so one pane cannot take the app down | [ ] |
| Structured log file, "Open Log File" | [ ] |
| Telemetry events | [ ] intentionally |
| Keyboard navigation and ARIA roles throughout | [~] names added, roles not |
| Zoom in / out / actual size | [ ] |
| Empty-state illustrations ("zero graphics") per pane | [ ] |

### 13.3 AI (`compass-generative-ai`, `compass-assistant`) — [ ] entirely missing

- Natural-language → query in the query bar; → pipeline in the aggregation builder
- "Aggregation generated" / "No content generated" states, positive/negative feedback
- MongoDB Assistant drawer: chat, suggested prompts, suggested actions, clear chat, tool calling with confirmation
- Explain "Interpret"
- Mock data generator script

### 13.4 Atlas (`atlas-service`) — [ ] entirely missing

- Atlas sign-in / sign-out, account chip
- Cluster metadata on connections, cluster-state badges
- Links out to cluster overview, monitoring, query insights, performance advisor
- Load sample data banner
- Global Writes (shard key creation, zone mapping, chunk pre-splitting)

---

## Summary of the largest gaps

1. **Application menu bar** — none of it exists.
2. **mongosh** — the shell is a driver-backed interpreter with no JS engine.
3. **Settings** — the whole modal and every preference.
4. **Connection form** — four of six tabs (Authentication, TLS/SSL, In-Use Encryption, Advanced).
5. **Visual query builder** — the field/operator/value UI in the Documents tab.
6. **Aggregation builder depth** — stage wizard, focus mode, saved pipelines, `$search` helpers.
7. **Schema minicharts and JSON Schema export.**
8. **Index creation UI** — field builder and the wider option set; vector search.
9. **Import/export depth** — preview, per-field types, field selection, progress and abort.
10. **Data Modeling workspace** — the entire ERD feature.
11. **Insights / signals** — sixteen advisory chips.
12. **Table view** — real editable grid with nested drill-down.
13. **AI and Atlas** — both absent end to end.
