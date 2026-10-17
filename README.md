# Wimf 2

Wimf 2 is a cross-platform file catalog indexing and search daemon under development. The original VB.NET application is at [pdias79/WIMF](https://github.com/pdias79/WIMF).

## Goals

- Native Windows and Linux distribution, small binary and bounded memory use.
- Read existing WIMF gzip/plaintext catalogs without changing the legacy producer.
- Build, publish, synchronize, and search multi-million-entry catalogs.
- HTTP API and lightweight embedded web interface.
- Compare in-memory and indexed search before selecting a new persistent format.

## Current milestone

The .NET 10 solution implements **legacy catalog reading, path reconstruction and sequential in-memory search**. The daemon, synchronization, persistent indexing and HTTP API remain future work.

### Search API

`Wimf.Search` depends only on `Wimf.Catalog` and the standard library. It provides a sequential reference for future benchmarks, without UI or hosting dependencies:

```csharp
var results = new SequentialSearchEngine().Search(catalogs, new SearchQuery
{
    Text = "annual report",
    Extensions = [".pdf"],
    MinSize = 1024,
    MaxResults = 100
}, cancellationToken);
```

Import `Wimf.Search` to use these types. All whitespace-separated terms must occur in the file name, in any order, using ordinal case-insensitive comparison. Double quotes group a phrase, so `"annual report" draft` requires the exact contiguous substring `annual report` plus the term `draft` (in any order). Spaces inside quotes are significant, and an unmatched quote returns an invalid-query error. In regex mode quotes are treated as ordinary regex characters and do not group search terms. Empty text matches all files. Directories are excluded. Extension filters accept a leading dot and ignore case; size and modification-date bounds are inclusive. Filters combine with AND. Results retain their source catalog and original record ID, in input catalog and record order; the limit applies across all catalogs (zero returns no results).

Set `UseRegex = true` to treat `Text` as a case-insensitive, culture-invariant regular expression. `RegexTimeout` defaults to 100 milliseconds per match and must be finite and positive. Invalid patterns and regex timeouts propagate to the caller. Cancellation is checked at each record; an active regex match can run until its timeout. Paths are reconstructed only for retained results, preserving the catalog path convention. Invalid retained paths propagate the catalog reader's exception. Searches return a materialized list; failures and cancellation do not return partial results. Catalogs and query collections must remain unchanged during a search.

### Build and test

Install the .NET 10 SDK, then run:

```bash
dotnet build Wimf.slnx
dotnet test Wimf.slnx
```

Tests use xUnit and tiny in-memory fixtures; no real catalogs are committed.

### Format details

A catalog begins with five UTF-8 lines: signature `[WIMF Catalog]`, catalog name, indexed root directory, creation timestamp in Unix seconds, and whitespace-separated extensions. Subsequent lines have `name;extension;size;parent;date`.

- Record IDs are 1-based ordinals of the original data lines; root has ID 0.
- `folder` denotes a directory.
- Record date is a Unix-day count (days since 1970-01-01).
- File names may contain spaces and `#`.
- Malformed lines are reported and retained as empty ID slots to prevent subsequent parent references from shifting.
- The importer supports plain text and `.gz`, validating gzip by stream decompression when requested.

Catalog entries are stored in memory only for this compatibility milestone. Large-scale index format and resource budgets will be benchmarked separately.

### HTTP daemon (development)

The first HTTP daemon runs locally only, on `http://127.0.0.1:5080`. Supply existing plaintext or Gzip catalogs with one or more `--catalog` arguments:

```bash
dotnet run --project src/Wimf.Server -- --catalog /path/to/catalog.txt.gz
```

Endpoints:
- `GET /api/health`: status and number of loaded catalogs.
- `GET /api/catalogs`: loaded catalog metadata, entry counts, diagnostic counts.
- `POST /api/search`: searches files using the existing sequential engine; no filesystem file download endpoint.

Example body:

```json
{"text":"report","extensions":["pdf"],"maxResults":25}
```

The daemon loads all catalog records into memory at startup. It has no authentication, no automatic synchronization, and must **not** be exposed outside localhost. It currently supports at most 1,000 results per request. Native AOT compatibility is a design target; native publication has not been validated yet.

### Embedded Web interface

Run the daemon with a catalog:

```powershell
dotnet run --project src/Wimf.Server -- --catalog "C:\\path\\to\\catalog.txt.gz"
```

Open **http://127.0.0.1:5080/** in a browser. The standalone page is served directly by the daemon: no Node.js, external CDN or asset folder. It supports search terms, regex, comma-separated extension filters, a result limit, catalog status, and copying result paths. The UI is in French and does not open or download indexed files.

**Security:** bound to localhost only. Do not expose the daemon over a network or through a proxy until authentication, authorization and request limits have been reviewed. Catalogs are still entirely loaded in memory at startup. This milestone does not implement cache synchronization or the final disk index.


### Local catalog cache (initial milestone)

Catalog sources may live on Windows UNC network shares or locally mounted network paths. Use `--source` to copy them into a private local directory; `--catalog` still reads a file directly as before.

```powershell
dotnet run --project src/Wimf.Server -- --source "\\\\fileserver\\share\\catalog.gz" --cache-dir "C:\\Wimf2\\cache"
```

Repeat `--source` for multiple catalogs. `--cache-dir` is required when using `--source`. The application does not create network credentials or mount shares; the current user must already have read access. The local cache uses a SHA-256 filename derived from each absolute source path, preventing collisions between similarly named catalogs.

At **daemon startup**, the application compares each source file's length and last-modified UTC timestamp with the cached copy. If unchanged, it reuses the cache. Otherwise it copies into a temporary file, parses and validates the catalog, then atomically replaces the cached file. If the source is unavailable or the new copy is invalid, it falls back to a previously valid cached file. Sources with neither a readable original nor valid cache are logged and skipped without stopping other sources. Temporary files are removed after errors.

Important limits: there is **no background polling**, file locking, remote-change notification, checksum comparison, cache eviction, or automatic index rebuild yet. Length + timestamp may miss a change if a producer overwrites a file without changing those values. Source files should be published atomically by the producer. The cache directory may contain private catalog filenames and paths and should be protected with OS filesystem permissions. A live daemon does not reload updated catalogs until restart.


### Live catalog refresh

Network sources supplied with `--source` can be refreshed while the daemon runs. The default interval is **300 seconds**. Use `--refresh-seconds 60` for one-minute refresh or `--refresh-seconds 0` to disable scheduled refresh. Each refresh is serialized, validates updated catalogs through the existing cache, then swaps one immutable catalog-array snapshot; searches that have already started continue using their previous snapshot.

The browser includes an **Actualiser les catalogues** button and a last-refresh indicator. The same operation is available via `POST /api/refresh`, with status from `GET /api/refresh-status`. Both endpoints are deliberately available only on localhost because the daemon has no authentication.

```powershell
dotnet run --project src/Wimf.Server -- --source "\\\\server\\share\\catalog.gz" --cache-dir "C:\\Wimf2\\cache" --refresh-seconds 60
```

Failed sources retain their last usable in-memory version. A source can reappear on a later refresh. Direct `--catalog` files remain static; only `--source` files are polled. Polling can still perform expensive file-system operations and parser work and does not yet support progress reporting or configuration through the browser.

### Performance baseline: synthetic benchmark

Use the standalone .NET 10 benchmark tool in **Release** mode. It generates WIMF UTF-8 catalog records in a streaming source, loads them with the production legacy reader, then measures median sequential-search latency and process working set. The benchmark is not an index format or a replacement for the application.

```powershell
dotnet run --project benchmarks/Wimf.Benchmarks -c Release -- --count 100000 --iterations 5
dotnet run --project benchmarks/Wimf.Benchmarks -c Release -- --count 1000000 --iterations 7
```

Optional: benchmark an existing *local* catalog, without committing or uploading it:

```powershell
dotnet run --project benchmarks/Wimf.Benchmarks -c Release -- --catalog "C:\\local\\catalog.gz" --iterations 7
```

The tool reports catalog-load time, process working-set size, and search medians for absent, rare, common, filtered and regex queries. Common searches are limited to 100 results by default, so they do **not** necessarily scan the entire catalog. The absent query exercises full scanning; rare matches depend on the synthetic distribution. Source generation happens during load timing and makes load figures less directly comparable to real disk/UNC reads. Do not compare results across architectures, SDK configurations or memory-pressure conditions without documenting them.

Benchmarking millions of entries can consume substantial memory and CPU. Start with 100,000 records. No real filenames or private paths are recorded in this repo, and the tool makes no network requests.


### Managed memory and refresh profiling

The benchmark also reports total process allocations during load, median allocations per search, collection counts (gen0/1/2), current managed heap (via `GC.GetTotalMemory(false)`), and GC heap statistics. These complement working set; the latter is **process physical memory**, not just managed objects. GC heap-size/fragmentation values describe the **most recent collection** and can lag behind current allocations.

Use `--profile-refresh` to load a second complete catalog while retaining the first. This **simulates the double-catalog memory pressure** of a live refresh, including the second load allocation and observed process peak, without network access or running `LiveCatalogStore` itself. A peak working set is a process-wide high watermark, not necessarily the instantaneous double-catalog memory. For realistic peak usage on UNC sources, run the daemon separately and monitor its process memory during a real refresh.

```powershell
dotnet run --project benchmarks/Wimf.Benchmarks -c Release -- --count 100000 --iterations 5 --profile-refresh
dotnet run --project benchmarks/Wimf.Benchmarks -c Release -- --count 1000000 --iterations 7 --profile-refresh
dotnet run --project benchmarks/Wimf.Benchmarks -c Release -- --catalog "C:\\local\\catalog.gz" --iterations 7 --profile-refresh
```

Measurements do not force collection inside timed regions. Allocated bytes are total-process counters (including threads other than the benchmark thread); numbers represent measurement overhead plus application allocations, not retained memory. The first load may include JIT and one-time runtime setup.


### Refresh optimization: reuse unchanged catalogs

After the first successful load, scheduled and manual refreshes compare the network source with its local cached copy using file length and UTC modification time. If those metadata match, the **already parsed in-memory catalog instance is reused** rather than parsed again. When every catalog instance is unchanged, the published search snapshot is also reused, avoiding churn and the temporary doubling of managed catalog memory. Changed sources still use staging, validation and atomic cache replacement, then publish a new search snapshot. Already-running searches keep their original snapshot.

If a source becomes unavailable after a successful load, Wimf retains the previously validated in-memory instance, even if the on-disk cache later becomes unavailable. On cold startup the local cache still has to be parsed and validated. Failures continue to be reported as unavailable sources.

**Limitation:** source-change detection relies on length plus modification timestamp, not a content hash; a replacement with identical values will be treated as unchanged. Do not mutate source catalogs in place without updating metadata; publish completed catalogs atomically.


### Compact in-memory catalog representation

The legacy reader now stores per-record **value slots** instead of keeping a separate `CatalogEntry` object for every file. Slots preserve the original 1-based record IDs, including malformed gaps. Repeated extension strings share one canonical instance per catalog. The public `Catalog.GetEntry()` and `EnumerateEntries()` interfaces continue to return `CatalogEntry` objects on demand.

`SequentialSearchEngine` reads the compact slots without materializing entries for nonmatching files; full entry objects and paths are created only for retained results. Date filters still use the original Unix-day timestamps.

This change aims to reduce resident memory for large catalogs. It changes internal memory representation, so validate equivalence and compare benchmarks for load time, heap and full-scan search before merging. `GetEntry()` callers that repeatedly request every record now allocate objects on demand instead of reading preexisting references; this trade-off should be measured before adopting it in other performance-sensitive code.


### Lower-allocation legacy parsing

The catalog reader parses semicolon-delimited entry lines directly from `ReadOnlySpan<char>` slices, rather than creating a five-element string array and multiple temporary strings for every row with `string.Split`. Integer fields are parsed directly from spans. A per-catalog ordinal extension pool uses span-based dictionary lookup, allocating an extension string only on its first occurrence. Filenames are still retained as strings.

The reader continues to reject records with too few/many separators, invalid numbers, blank names, out-of-range timestamps and negative parent IDs while preserving original row IDs and diagnostics. The file format, plain-text/Gzip support and public catalog API remain unchanged.

Benchmark the change against the merged PR #12 before adopting it:

```powershell
dotnet run --project benchmarks/Wimf.Benchmarks -c Release -- --count 100000 --iterations 5 --profile-refresh
dotnet run --project benchmarks/Wimf.Benchmarks -c Release -- --count 1000000 --iterations 7 --profile-refresh
```

Compare cumulative **load allocations**, loading latency, GC collections, retained heap and query timings. Synthetic load timing also includes generation of rows by the benchmark stream; a real catalog provides a complementary measurement.


### Hierarchical benchmark scenarios

The synthetic benchmark can now generate **directory records** before file records, including a deterministic nested spine and side branches. Each file receives a valid parent folder ID, with varying path depths. The `--count` argument is the **total number of records**, including folders; `--folders` selects how many records are folders and `--depth` sets the spine depth (1 to 64, no greater than folder count). The default `--folders 0` preserves the previous flat baseline.

```powershell
# Flat baseline (comparable with PR #13 results)
dotnet run --project benchmarks/Wimf.Benchmarks -c Release -- --count 1000000 --iterations 7 --profile-refresh

# 1 million records: 10,000 directories and 990,000 files, up to 9 folder levels
dotnet run --project benchmarks/Wimf.Benchmarks -c Release -- --count 1000000 --folders 10000 --depth 8 --iterations 7 --profile-refresh

# An alternative shallower hierarchy
dotnet run --project benchmarks/Wimf.Benchmarks -c Release -- --count 100000 --folders 2000 --depth 3 --iterations 5
```

Beyond the existing load, memory, GC, and search metrics, the benchmark prints `GetFullPath` timings and allocated bytes for representative file IDs at different depths. It verifies that repeated calls return the same path. For existing `--catalog` input it samples up to three valid file records. This is a deterministic structural stress test, not a faithful distribution of real-world folder sizes or filename lengths. Compare matching record counts and hardware; hierarchical runs contain fewer files and may therefore produce different search hit counts. No catalog files are written or uploaded.


### Lower-allocation path reconstruction

`Catalog.GetFullPath(id)` now measures the parent-chain length in one bounded traversal, then fills a single output string using `string.Create`. This removes the per-call `List<string>`, `HashSet<int>`, and intermediate joined string. Cycle detection is preserved by rejecting parent chains longer than the total record count; missing parents and non-directory parents still fail. Paths remain valid for Windows catalog roots on Linux, Unix roots, trailing separators, Unicode names, and malformed legacy entries.

The benchmark measures `GetFullPath` using batches of 1,000 calls per iteration to avoid rounding sub-microsecond results to zero. It reports **microseconds per call** and **bytes allocated per call**. The synthetic second load in `--profile-refresh` now uses the same folder count and depth as the first; earlier hierarchy results accidentally compared a folder tree with a flat replacement, so do not use previous second-load comparisons as strict like-for-like baselines.

```powershell
dotnet run --project benchmarks/Wimf.Benchmarks -c Release -- --count 1000000 --folders 10000 --depth 8 --iterations 7 --profile-refresh
```

Use a previous commit on `main` for an apples-to-apples before/after path comparison; existing times reported in milliseconds from single calls are too coarse for reliable microsecond comparisons.


### Refresh and search robustness regression tests

The server tests include an automated concurrency scenario in which several searches and refresh calls overlap against a locally staged catalog replacement. Every captured search snapshot must remain internally consistent: no mixture of entry counts, catalog names or search paths, and previously captured snapshots must still work after publication.

A separate two-source scenario verifies that a valid update can be published while a corrupt source keeps its last validated in-memory catalog. It checks the failed-source count, preservation of the on-disk cache, removal of temporary files, and recovery on the next valid source update. Cold-start corruption must not publish an unvalidated cache. Search tests verify that malformed/cyclic parent chains are rejected when selected, without breaking unrelated searches.

These tests use temporary files and deterministic assertions rather than fixed timing thresholds; they do not require UNC shares or external services. Run them with:

```powershell
dotnet test Wimf.slnx -c Release
```


### Size and date filters in the embedded Web UI

The embedded search form exposes the size and modification-date filters already supported by `POST /api/search`. Set minimum and/or maximum size in **bytes** (whole nonnegative integers), and/or choose inclusive **UTC calendar dates**. Leaving a filter blank does not constrain the search. Combining filters uses AND, as in the API.

The browser refuses reversed size/date ranges, negative or fractional size values, and byte values beyond JavaScript's safe integer precision. The ending date is sent as `23:59:59.999Z`, so a file modified on that day is included. These client-side safeguards complement existing API-side validation; API consumers may still supply 64-bit unsigned byte sizes via JSON.

No external JavaScript or CSS dependencies are introduced, and the UI remains restricted to the localhost daemon.



### Sorting and reading search results in the Web UI

The results table displays **modification dates** alongside file names, full paths, sizes and catalog names. A sorting selector can reorder the **already returned** results by file name (A–Z/Z–A), file size (ascending/descending) or modification date (oldest/newest). The default option keeps the catalog's original result order; tied values preserve that original ordering too.

Sorting happens entirely in the browser, with **no additional server requests**. It does not sort across matching files omitted by the API result limit (maximum 1,000) and does not change the order or semantics of `POST /api/search`. Date display uses UTC to remain consistent with the server's catalog timestamps. File names and paths continue to be inserted as text, never interpreted as HTML. Overlapping searches use the latest request result only.


### Browsing and exporting search results

The embedded UI now pages the **returned** results in groups of 25, 50 or 100, with previous/next controls. Paging and the existing local sorting are independent of the server; they do not request any extra catalog records. The search API still returns at most 1,000 hits per query.

Use **Exporter CSV** to save **all returned hits**, not just the currently visible page, in the selected sort order. Exported columns are catalog, original record ID, filename, full path, extension, byte size, and modification timestamp (UTC). The file uses UTF-8 with a BOM, semicolon separators, quoted fields, escaped quotes and CRLF newlines for common spreadsheet compatibility. Cells beginning with spreadsheet formula prefixes (including after whitespace) are prefixed with an apostrophe to reduce CSV formula-injection risk. The export is generated in the browser, without sending results to another service.

The CSV is an export of the current limited search response, **not** a complete database dump. Binary files are not accessed or exported.


### Local desktop actions (opt-in)

Restart the Wimf daemon with `--enable-file-actions` to allow per-result **Ouvrir**, **Dossier** and **Copier le fichier** buttons. They operate on the desktop session of the computer running the daemon; they cannot open a file in a browser on a different workstation. The existing **Copier le chemin** button still copies only text in the browser, without requiring the new option.

```powershell
dotnet run --project src/Wimf.Server -- --catalog "C:\\path\\to\\catalog.txt.gz" --enable-file-actions
```

- **Ouvrir** uses the operating system's default file association (Windows) or `xdg-open` (Linux). Known executable/script and shortcut extensions are intentionally blocked.
- **Dossier** selects the file in Windows Explorer; on Linux it opens the containing directory (without guaranteed selection).
- **Copier le fichier** places the *file reference* on the Windows system clipboard using the native `CF_HDROP` format. You can then paste it into a folder in Explorer. It does **not** copy the filename as text and does not copy bytes into a destination until you paste. The native file clipboard action is not yet available on Linux.
- **Copier le chemin** uses browser clipboard text and is available regardless of whether native actions are enabled.

The native action API is **disabled by default**, requires a request with the exact Wimf localhost origin and host, and accepts only catalog index + entry ID + expected path; it reconstructs the actual target from the current catalog instead of trusting a browser-provided path. It checks that a target is an existing indexed file, rooted under a local-system absolute catalog root, and rejects invalid/traversal filenames. The operation is a desktop-side effect, and the UI requires an explicit confirmation before invoking it. Remote/browser sessions cannot be assumed to access the same clipboard or Explorer instance.

Security note: file associations and external programs can still have side effects. Enable these functions **only** on a trusted, interactive desktop and do not expose or proxy the daemon beyond localhost. A catalog is metadata: a file may have moved or become unavailable since indexing; stale entries return an error. No file download or arbitrary path-opening endpoint is added.


### Selecting catalogs from the Web interface

Expand **Catalogues sélectionnés pour la recherche** to see the loaded catalog name, root path, record count and diagnostic count. Individual checkboxes and **Tout sélectionner / Tout désélectionner** determine which catalogs participate in the next search. All loaded catalogs are selected by default. An explicit empty selection returns no hits.

The `GET /api/catalogs` endpoint now includes a zero-based `index` for each entry. `POST /api/search` accepts an optional `catalogIndices` array alongside the existing filters. Omitting the property searches all catalogs (backward compatible), while an empty array searches none. Nonexistent, negative and duplicated indices receive HTTP 400; selections are searched in their original catalog order, regardless of checkbox order. Search responses continue to include `catalogIndex` for the existing desktop file actions.

After a manual source refresh, the UI reloads catalog metadata and retains the selection of catalogs with the same name and root. Selections are not persisted across browser restarts. A refresh performed by the automatic timer may change catalog indices until the browser fetches the new list; reload the page if the catalog set changes externally. This phase does not add or remove configured catalog sources at runtime.


### Compact search results: clickable filename

The results table intentionally omits the often very long **Chemin** column. Hover over a filename to display the complete indexed path as a native browser tooltip; keyboard users can also focus the filename control to inspect its accessible label. Clicking a filename requests **Ouvrir** directly, using the same opt-in local daemon action and catalog-backed path validation as the former Open button. There is no second confirmation dialog for this deliberate click. Native opening remains disabled unless the daemon was started with `--enable-file-actions`; unsupported executable/script types remain blocked.

The **Copier le chemin** action still copies the full path as text, while **Copier le fichier** remains a separate native Windows clipboard action. Neither CSV export nor the search JSON payload loses the full path.


### Search history and favorites

Expand **Historique et favoris de recherche** in the Web UI to find the most recent successful searches and a separate list of favorite search criteria. **Ajouter la recherche aux favoris** saves the filters currently visible in the form; **Restaurer** loads saved filters without automatically running a search. **Supprimer** removes one entry; **Effacer l’historique** clears only the history (not favorites), after confirmation.

Both lists are kept only in the browser's `localStorage`, with separate keys (`wimf2.history.v2` and `wimf2.favorites.v2`), a limit of 20 entries each, and duplicate history searches promoted to the top. This includes search text, regex, extensions, result limit, minimum size in Ko, date bounds and selected catalogs (stored by catalog name and root path, not transient indices). The history is written only after an API search succeeds; failed or cancelled searches are not recorded. If a saved catalog is no longer loaded, restoring the search selects only catalogs that still match. Data is not sent to a new service or synchronized across devices. Clearing browser site data also removes saved searches. Browsers that block local storage can use the session UI, but cannot persist changes.


### Minimum size filter in the Web UI

The Web UI offers **Taille min. (Ko)** but no maximum-size field. Enter a nonnegative whole number of kilobytes (1 Ko = 1,024 bytes); the browser converts it to bytes before submitting `minSize` to `POST /api/search`. The server-side API continues to accept byte-based `minSize` and `maxSize` properties for existing clients; only the Web form omits the maximum-size filter. Search history and favorites created with this version store the entered Ko value (storage keys ending in `.v2`); data stored under earlier keys is not automatically imported to avoid interpreting old byte values as Ko.


### Creating a WIMF catalog from a directory (PR #23)

The daemon executable now has a separate command-line mode for **building** a legacy-compatible catalog without starting the HTTP server:

```powershell
dotnet run --project src/Wimf.Server -- --build-root "C:\\Documents" --output "C:\\WimfCatalogs\\Documents.txt.gz" --name "Mes documents"
```

`--name` is optional (defaults to the directory name). Output ending in `.gz` uses gzip; otherwise it is plain UTF-8. The destination may be inside the indexed directory: the writer excludes both the existing output catalog and its temporary file to prevent self-indexing. Existing output is replaced only after writing a complete temporary catalog. A successful run prints the number of indexed entries.

The writer produces the existing five-line header and legacy `name;extension;size;parent;date` records, including folders and 1-based parent IDs. Record order is deterministic within each directory (ordinal filename sorting). It ignores reparse points / symbolic links to avoid recursive loops, uses modification dates as Unix UTC days, and does not read file contents. File or directory names containing semicolons or line breaks are skipped with a warning (including entire subtrees under incompatible directory names), because the historical format cannot encode those names losslessly. Inaccessible subdirectories and individual file entries are skipped with a warning on stderr, and the scan continues with readable siblings. The summary reports how many paths were skipped; these paths are absent from the catalog (some inaccessible directory entries may remain as empty folder records). If the root itself cannot be enumerated, or the output cannot be written, catalog generation fails. This first version performs a **full** scan; incremental catalog updates are a separate future change.

Load the generated catalog normally:

```powershell
dotnet run --project src/Wimf.Server -- --catalog "C:\\WimfCatalogs\\Documents.txt.gz" --enable-file-actions
```


### Updating an existing catalog and reporting changes (PR #24)

After generating a catalog with `--build-root`, run the command again with `--update-existing` to **compare the refreshed snapshot** with its previous content:

```powershell
dotnet run --project src/Wimf.Server -- --build-root "C:\" --output "C:\Wimf\DisqueC.txt.gz" --update-existing
```

You can also append `--update-existing` after `--name "Disque C"`. The existing file must be a valid legacy WIMF catalog for the same root path; mismatches and invalid entries are rejected before writing. If `--name` is omitted, the prior catalog name is preserved. This mode compares reconstructed paths and the original format's extension, size and UTC-day modification timestamp, then reports **added**, **modified**, **removed**, **unchanged** and **skipped** counts. The catalog itself is still rewritten atomically to the same legacy gzip/plaintext format, keeping it readable by the existing daemon and UI.

**Scope:** this is a change-aware *full rescan*, not yet a filesystem-journal-based or directory-skipping incremental scan. Every accessible directory is enumerated and file metadata checked; unchanged files are not opened or hashed. Consequently, files changed without a difference in length or whole UTC modification day can be counted as unchanged. Unavailable folders and incompatible names are skipped as in the initial build; their previously indexed files may be reported as removed and will not appear in the updated catalog. Check skipped-path warnings when comparing results. A future phase can introduce reliable true incremental traversal and persistent state.


### Persistent daemon settings (PR #25)

Instead of typing all catalog paths every time, place a `wimf.json` file alongside your catalogs:

```json
{
  "catalogs": ["WIMF_DATABASE_DisqueC.txt.gz", "WIMF_DATABASE_DisqueD.txt.gz"],
  "sources": [],
  "cacheDirectory": null,
  "refreshSeconds": 300
}
```

Start the local daemon with:

```powershell
dotnet run --project src/Wimf.Server -- --config "C:\Users\Philou\Documents\repos\wimf2\wimf.json" --enable-file-actions
```

- The `--config` filename is searched in the daemon's working directory and then its parent directories, so `--config wimf.json` works for a file at the repository root when using `dotnet run --project src/Wimf.Server`. The nearest matching configuration file wins. Paths within the JSON file can be **relative to that JSON file's directory** or absolute. For sources on a network share, use JSON-escaped Windows UNC paths, e.g. `"\\\\server\\share\\catalog.gz"`, and set a cache directory.
- `catalogs` points to directly loaded files; `sources` lists periodically refreshed source catalogs. `cacheDirectory` is required when there are sources. Set `refreshSeconds` to `0` to disable periodic refresh; otherwise it must be nonnegative. Omitted fields use defaults.
- Existing CLI arguments work as before. If `--catalog` or `--source` is supplied explicitly, it **replaces** the corresponding JSON array. `--cache-dir` and `--refresh-seconds` override their JSON values.
- **Security:** native file actions are never enabled by the config file. Explicitly pass `--enable-file-actions` on the command line. Unexpected JSON properties, including `enableFileActions`, are rejected rather than silently accepted.
- Settings are read at server startup; restart the daemon after editing the JSON. This PR does not introduce an API to edit configuration remotely.

An example is committed as `wimf.example.json`.


### Automatic reload of local catalogs (PR #27)

Catalog files declared with `--catalog` or under `catalogs` in `wimf.json` are now checked for changes at the same interval as network sources. No restart is required after replacing or updating a local catalog:

```powershell
dotnet run --project src/Wimf.Server -- --config "wimf.json" --refresh-seconds 30 --enable-file-actions
```

When a local file's modification timestamp or length changes, Wimf reloads it and atomically publishes the refreshed snapshot. Active searches continue using their existing immutable catalog instance. If a changed catalog is missing, malformed, or temporarily unreadable, Wimf logs an error and **retains the last valid in-memory version** until a subsequent refresh succeeds. Local catalog order and search catalog indices remain stable. Existing `POST /api/refresh` also checks local files immediately; `refreshSeconds: 0` disables the automatic timer but not manual refresh.

The periodic scan checks file metadata, **not the underlying filesystem**. Run `--build-root ... --update-existing` separately to regenerate the catalog after files change. A catalog replaced without changing either file length or modification timestamp may not be detected. The refresh API includes `failedLocalCatalogs` in its response to distinguish these failures from network source failures.


### Refresh status in the Web UI (PR #28)

The embedded UI already provides an **Actualiser les catalogues** button. It now distinguishes failures when refreshing **local catalogs** versus **network sources**. The GET `/api/refresh-status` response includes the latest `failedLocalCatalogs` and `failedSources` counts in addition to the last completion time. A background check every 15 seconds updates the timestamp, warnings and catalog selection list after the daemon's automatic refresh, without resetting an active search. The status check does not regenerate catalogs or trigger refresh itself.

When a source is unavailable, Wimf retains its last valid in-memory catalog; the UI warns that it was not reloaded. To test, temporarily make a local catalog unreadable or replace it with invalid data and click the refresh button, then restore it and refresh again. Existing file-search results remain visible until a new search is submitted.


### Explicit path exclusions during catalog creation (PR #30)

To omit known system directories during a whole-disk scan, repeat `--exclude`:

```powershell
dotnet run --project src/Wimf.Server -- `
  --build-root "C:\" `
  --output "C:\Wimf\DisqueC.txt.gz" `
  --name "Disque C" `
  --exclude '$Recycle.Bin' `
  --exclude 'System Volume Information'
```

The same exclusions work in update mode by appending `--update-existing`. Exclusion paths can be absolute (but must remain within the indexed root) or relative to the indexed root. An excluded file is omitted; an excluded directory and all its descendants are omitted. Matches are case-insensitive on Windows and case-sensitive on Linux. Prefixes are not enough: excluding `Private` does **not** exclude `PrivateAgain`. The indexed root cannot be excluded.

Intentional exclusions are not counted as inaccessible paths and are not stored in the catalog. Repeat the same flags on future updates to keep the same scope. Previously indexed entries that become excluded will be reported as removed.


### Scheduled indexing from wimf.json (PR #31)

An optional `indexing` array centralizes the roots, output catalog paths, exclusion rules and independent indexing intervals:

```json
{
  "catalogs": ["WIMF_DATABASE_DisqueC.txt.gz", "Archives.txt.gz"],
  "sources": [],
  "cacheDirectory": null,
  "refreshSeconds": 300,
  "indexing": [
    {
      "root": "C:\\",
      "output": "WIMF_DATABASE_DisqueC.txt.gz",
      "name": "Disque C",
      "exclude": ["$Recycle.Bin", "System Volume Information", "Windows\\Temp"],
      "intervalDays": 7
    },
    {
      "root": "D:\\Archives",
      "output": "Archives.txt.gz",
      "name": "Archives",
      "exclude": ["Temp"],
      "intervalHours": 12
    }
  ]
}
```

Each job must specify **exactly one** positive `intervalMinutes`, `intervalHours` or `intervalDays` (maximum 3650 days). For example, `"intervalMinutes": 5` runs a small-directory indexing job approximately every five minutes while `"intervalDays": 7` works for a large disk. Each catalog has its own schedule. Paths in `root` and `output` are relative to the JSON file location unless absolute; entries in `exclude` are relative to the corresponding **indexing root**, unless absolute. The output must also appear in `catalogs` and only one indexing job may own an output. Catalog files referenced in `catalogs` may be missing on startup **only when a corresponding `indexing` job owns that output**. Wimf starts with the existing catalogs and the scheduler creates the missing output in the background; it becomes searchable after the next catalog refresh (`refreshSeconds` or manual refresh). A missing or inaccessible catalog **without** an indexing job also no longer blocks startup: it remains unavailable until a subsequent refresh discovers it. Invalid existing catalog data may still be rejected as a configuration/data error. Config without `indexing` behaves as before.

When running, the scheduler checks due jobs about once a minute (so minute intervals are approximate, not second-precise), using each output catalog's last modification time as the last successful indexing time. If an existing catalog is overdue, the first update begins in the background shortly after daemon startup. A failed scan keeps the previous catalog intact where possible. A missing/offline indexing root is retried at the next minute tick; other scan failures are retried after at least one hour. Shutdown requests cancel indexing. Scans execute sequentially to avoid competing whole-disk workloads.

**Important:** This is a scheduled **full filesystem rescan**, not a journal-based incremental index. `--update-existing` compares changes but reads all file metadata and rewrites the catalog; lengthy scans can consume CPU, I/O and memory. Do not configure very short intervals for large disks. The existing `refreshSeconds` option controls when the Web server reloads the generated catalog, **not** when the disk is rescanned. The background indexing schedule is active only while Wimf is running; missed updates run after the next startup when the catalog is overdue. A manual update using the same output while scheduled indexing is running should be avoided.


### Offline catalogs during daemon startup (PR #31)

If a catalog listed in `catalogs` lives on a disconnected network drive or is missing, Wimf now starts with the catalogs that are readable. The unavailable catalog is reported in stderr and in `failedLocalCatalogs` on `/api/refresh-status`, but other catalog searches remain available. At every `refreshSeconds` interval (default 300 seconds), Wimf retries loading it. Once the remote share reconnects or the catalog is created, it is loaded automatically and appears in the Web UI without restarting. `POST /api/refresh` triggers an immediate check.

A catalog that was successfully loaded earlier stays searchable from the last valid in-memory snapshot when its underlying share disconnects. This behavior applies to both scheduled indexing outputs and ordinary paths in `catalogs`; no new JSON property is required. `refreshSeconds: 0` disables automatic retries, but manual refresh remains available. If all catalogs are unavailable, the server still starts with zero searchable catalogs. Network indexing itself will retry on its independent scheduling/error cadence.


#### Diagnose the first indexing of a network share

The new local `GET /api/indexing-status` endpoint returns each configured job's `name`, `root`, `output`, `state` (`pending`, `running`, `completed`, or `failed`), `lastSuccess` and `lastError`. In PowerShell, run `Invoke-RestMethod http://127.0.0.1:5080/api/indexing-status | Format-List` to see which indexing job has started and the last error if any. `pending` can mean an earlier sequential scan has not yet finished. The first indexing of an absent output starts when the root is available, and may take time on a remote disk. Before diagnosing the scheduler, verify a share path with `Test-Path -LiteralPath '\\\\192.168.1.1\\Disque dur'` (replace the address with the actual Freebox address). A disconnected root is retried approximately every minute, while catalog loading checks follow `refreshSeconds`.


### Visual monitoring of scheduled indexing (PR #32)

The local Web UI now has a **Catalogues et actualisation** button in the top header. It opens a separate status panel containing the **Actualiser les catalogues** button, catalog refresh status, and scheduled indexing progress. For each job declared in `indexing`, it shows the configured name, indexed root and catalog output, its current state (waiting, running, completed or failed), its last successful completion date and any reported error.

The panel polls the existing local `GET /api/indexing-status` endpoint every 15 seconds while the browser tab is visible. If `indexing` has no jobs, the panel remains visible and explains that no indexing tasks are configured (the daemon must be restarted after updating `wimf.json`). Status checks never start a disk scan and do not interrupt searches or the separate catalog refresh timer. Failed scans remain visible, including when network shares are disconnected; later retries update their state. Jobs run sequentially, so one long scan can leave subsequent jobs pending.

To test, open `http://127.0.0.1:5080` with several indexing tasks configured. Click **Catalogues et actualisation** in the header and check the displayed states while a short indexing task executes and finishes. Disconnect a test share to inspect the error and reconnect it to observe the retry. The state is read-only; `/api/indexing-status` remains available for scripts.

The catalog refresh button has moved out of **Résultats** into this upper panel; it still reloads the catalogs, and does not itself force an indexing rescan.

### Catalog management details and manual indexing (PR #32)

The **Catalogues et actualisation** panel now lists each configured local catalog, even if its file is currently unavailable. It displays the last successful catalog creation/update date from the catalog header and a **file count** (directories excluded; the count is cached for each immutable loaded catalog). For unavailable or not-yet-created catalogs, the date and count are shown as unavailable.

Catalogs with an `indexing` job additionally display the **next expected indexing date** based on the output catalog's last modification time plus `intervalMinutes`, `intervalHours` or `intervalDays`, and a **Lancer une indexation** button. The next date is indicative: actual scans can start later if another scan is running, the daemon is stopped, or a source is offline. The button requests an immediate **full rescan**, distinct from the general **Actualiser les catalogues** control (which only reloads generated catalog files). A manual rescan can be queued while another scan is running; duplicate manual requests for a job are rejected and the button is disabled while its job is running or already queued. Manual scan calls require the same-origin local UI, and scans remain sequential.

The management panel retrieves `GET /api/catalog-management` (metadata and planned scans) and calls `POST /api/indexing/{index}/start` for manual actions. When a scan completes, the daemon's existing catalog refresh mechanism loads it, or the user can select **Actualiser les catalogues**. This panel does not change the original read-only search endpoints.
