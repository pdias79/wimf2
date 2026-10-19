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

### Immediate catalog refresh after successful indexing (PR #33)

When a scheduled scan or a manual **Lancer une indexation** operation successfully generates or updates its output catalog, Wimf now immediately calls the normal catalog refresh operation, publishing the new catalog in the search snapshot **without waiting for `refreshSeconds`**. The same change is visible to the Web UI through its existing background status/catalog polling (up to 15 seconds while the tab is visible).

The refresh is performed only after the writer finishes publishing the output; readers never observe a partially written catalog. Other loaded catalogs remain searchable during the scan. Reload problems are logged separately and do not mark an otherwise successful indexing operation as failed. The `refreshSeconds` timer remains responsible for detecting external catalog changes and retrying unavailable paths. No configuration changes are required.

### Live scan progress (PR #34)

While an indexing job runs, **Catalogues et actualisation** shows the number of filesystem entries scanned and elapsed time, refreshed through the existing 15-second UI polling. The counter comes directly from the legacy writer, both when creating a catalog and updating an existing one, and is reported every 256 records plus at successful completion to avoid per-record UI overhead. It includes cataloged files and folders; excluded/inaccessible entries are not counted. The figure is **not a percentage** because a full pre-count would require an additional traversal of a large disk or network share. The start time is recorded when a job begins, including manually requested runs. Searches continue to use the last valid catalog until the new scan completes.

### Configurer les workers d'indexation

Chaque entrée de `indexing` dans `wimf.json` accepte désormais un champ facultatif `workers` (entre **1 et 50**). Sa valeur par défaut est **1**, qui conserve le parcours séquentiel historique. Pour un partage réseau, on peut tester `"workers": 16` :

```json
{
  "indexing": [
    {
      "root": "\\\\192.168.1.200\\BACKUP_PC_Cecile",
      "output": "WIMF_DATABASE_BackupCecile.txt.gz",
      "intervalDays": 7,
      "workers": 16
    }
  ]
}
```

Le nombre de workers s'applique au catalogue concerné lors des indexations manuelles ou planifiées. Redémarrer le serveur après une modification du fichier. Les modes parallèles utilisent un parcours BFS, et peuvent modifier l'ordre des identifiants internes par rapport au parcours DFS à un worker.


### Benchmark comparatif des workers

Sans démarrer le serveur et **sans remplacer les catalogues configurés**, la commande suivante exécute trois passages avec 1, 16 et 50 workers :

```powershell
dotnet run --project src/Wimf.Server -c Release --no-build -- --benchmark-root "\\\\192.168.1.200\\BACKUP_PC_Cecile" --workers 1,16,50 --repeats 3
```

Les passages alternent l'ordre des configurations pour limiter les biais liés au cache réseau. Chaque passage écrit temporairement un catalogue compressé dans le répertoire temporaire du système, puis le supprime. Les lignes `[BENCH]` donnent la durée de chaque passage, et `[BENCH-SUMMARY]` affiche le minimum, la médiane basse (si nombre pair), le maximum et la moyenne en millisecondes. La commande accepte aussi plusieurs options `--exclude PATH`, comme lors d'une indexation ordinaire. Elle n'utilise pas le fichier `wimf.json` et ne modifie pas l'historique d'indexation.


#### Recommandation du nombre de workers

À la fin de `--benchmark-root`, la ligne `[BENCH-RECOMMENDATION]` indique le plus petit nombre de workers testé dont la médiane ne dépasse pas 105 % de la meilleure médiane. Le choix évite d'augmenter la concurrence pour un gain marginal. Il s'agit d'une **suggestion**, jamais d'une modification automatique de `wimf.json`. Si le nombre d'éléments varie entre les passages, le benchmark signale `[BENCH-WARNING]`, termine avec le code 1 et ne formule pas de recommandation : stabiliser l'arborescence puis recommencer. Des décomptes identiques ne garantissent toutefois pas que les fichiers sont restés identiques.


### Targeted refresh after indexing (PR #34)

When one scheduled or manually requested scan completes, Wimf reloads **only that output catalog** into its searchable snapshot. Previously every indexing completion retried all local catalogs and network sources, potentially delaying a fast local indexing job when a disconnected or slow network share was configured. The dedicated reload is serialized with ordinary catalog refreshes, checks the file timestamp before/after parsing, preserves unaffected in-memory catalog instances, and updates the last-refresh timestamp used by the UI's periodic status polling.

The **Actualiser les catalogues** button and `refreshSeconds` timer remain full refresh operations and continue checking all local and network sources. Failure counters for unavailable local catalogs remain tracked individually across both kinds of refresh. There is no configuration change or new UI action.


### Diagnostic expérimental des notifications SMB

Pour vérifier sur un partage que les créations, modifications, suppressions et renommages provenant d'autres machines sont reçus par Windows, lancer ce diagnostic **en lecture seule**, sans démarrer le serveur ni écrire de catalogue :

```powershell
dotnet run --project src/Wimf.Server -c Release -- --watch-root "\\\\192.168.1.200\\BACKUP_PC_Cecile"
```

Modifier un fichier depuis un autre poste, puis arrêter par `Ctrl+C`. Les lignes `[WATCH]` consignent les notifications observées, `[WATCH-ERROR]` une erreur de surveillance et `[WATCH-SUMMARY]` les compteurs. Les événements bruts sont regroupés par type et chemin sur une fenêtre de 500 ms : `[WATCH-SUMMARY]` indique `events` (notifications reçues), `grouped` (notifications retenues) et `duplicates` (notifications masquées). Cette déduplication sert uniquement au diagnostic, sans supposer qu'un événement retenu correspond à une seule modification réelle. Les événements peuvent être multiples pour une seule modification. **Ne pas confondre absence de notification et absence de changements :** une déconnexion, un débordement du tampon ou une interruption peut faire perdre des événements, sans possibilité de rattrapage. Ce diagnostic est volontairement non persistant et ne déclenche aucune indexation ; la mise à jour fiable des catalogues continue à utiliser les scans complets. Éviter de le laisser actif sur des partages sensibles : les chemins sont affichés dans la console.

### Indexation incrémentale : conception et garanties (préparation PR #54)

**Situation actuelle.** `LegacyCatalogUpdater.Update` utilise `LegacyCatalogWriter.Build` pour effectuer un scan complet, puis compare l'ancien et le nouveau catalogue. Les compteurs ajoutés/modifiés/supprimés/inchangés ne constituent pas une détection incrémentale. Aucun champ `workers` ne change cette garantie.

**Objectif.** Éviter de parcourir tous les fichiers à chaque actualisation lorsque la source fournit un moyen fiable de détecter les changements, tout en préservant les résultats de recherche et la validité du catalogue après une interruption.

**Stratégie proposée, à réaliser dans des PR distinctes :**

1. Introduire une abstraction de détection des changements et une capacité déclarée par volume (journal fiable, notifications non exhaustives, ou scan complet obligatoire). Une source SMB/Freebox est en mode *scan complet* tant qu'aucun mécanisme de rattrapage fiable n'a été démontré.
2. Ajouter un état de synchronisation persistant par catalogue : identité de la racine et du volume, curseur du journal lorsqu'il existe, version du schéma et génération du catalogue associé. Ne jamais avancer le curseur avant publication atomique du catalogue.
3. Sur Windows local NTFS, étudier le journal USN et ses cas d'invalidation (troncature, changement d'identité de volume, droits insuffisants). Ne pas assimiler `FileSystemWatcher` à une source exhaustive de changements.
4. En cas de premier démarrage, perte de curseur, débordement, indisponibilité ou incohérence, revenir au scan complet, sans annoncer à tort une actualisation incrémentale réussie.
5. Comparer le contenu et les recherches après une séquence contrôlée : créations, suppressions, renommages/déplacements de dossiers, changements de taille/date, interruptions pendant l'écriture et reconnexions.

**Critères de validation.** Un mode incrémental ne sera activé que lorsque les tests vérifient que le catalogue final correspond à un scan complet de référence (y compris les chemins des descendants de dossiers renommés), et qu'un arrêt/reprise n'entraîne aucune omission silencieuse. Les durées et le nombre d'éléments inspectés doivent être mesurés séparément ; un gain ne doit jamais masquer une perte d'exhaustivité.

**Non-objectifs du premier jalon.** Pas de changement automatique de stratégie pour la Freebox ou les partages SMB ; pas de remplacement de `--update-existing` avant validation. Le parcours complet reste le chemin de référence fiable.


### Journal facultatif des événements SMB (diagnostic)

Le diagnostic `--watch-root` accepte désormais `--watch-log FILE`. Exemple (choisir un chemin **local, en dehors du partage surveillé**) :

```powershell
dotnet run --project src/Wimf.Server -c Release -- --watch-root "\\\\192.168.1.200\\BACKUP_PC_Cecile" --watch-log "C:\\Wimf2\\smb-events.tsv"
```

Créer le répertoire parent du fichier de journal au préalable. Le journal est ajouté sans écraser les anciennes lignes et contient les **événements bruts**, même ceux regroupés à l'affichage. Chaque ligne contient l'heure UTC ISO 8601, le type d'événement et le chemin codé en Base64 UTF-8 (pour éviter les ambiguïtés de caractères spéciaux), séparés par tabulations. Les marqueurs `session-start`, `session-end` et `error` aident à identifier les périodes de surveillance. Une erreur d'écriture est signalée dans `[WATCH-ERROR]`. Protéger ce journal : il contient les chemins complets des fichiers.

**Limite critique :** ce journal n'offre *aucune* garantie de complétude ou de reprise ; une déconnexion, l'arrêt du processus ou une saturation du tampon peut masquer des modifications. Il ne sert pas d'entrée pour l'indexation et ne supprime jamais la nécessité des scans complets. Le mode sans `--watch-log` reste inchangé.


### Analyser le journal SMB (expérimental)

Sans ouvrir le partage ni modifier les catalogues, analyser le journal produit par `--watch-log` :

```powershell
dotnet run --project src/Wimf.Server -c Release -- --watch-report "C:\\Wimf2\\smb-events.tsv"
```

`[WATCH-REPORT]` indique le nombre de notifications brutes, chemins distincts, répertoires concernés, débuts et fins de session, erreurs et lignes malformées. Jusqu'à 20 répertoires les plus concernés sont affichés sous `[WATCH-DIR]`. Un renommage peut concerner deux dossiers. Un nombre de débuts/fins différent ou une erreur rend le diagnostic suspect (code de sortie 1). **Même sans erreur et avec des sessions équilibrées, les notifications ne sont pas exhaustives** : ce rapport ne lance aucune actualisation de catalogue et ne remplace pas un scan complet.


### Surveillance SMB intégrée au serveur (expérimental)

Ajouter facultativement `watchRoots` à `wimf.json` pour surveiller des partages pendant que le serveur Web reste actif :

```json
{
  "watchRoots": ["\\\\192.168.1.200\\BACKUP_PC_Cecile"]
}
```

Il s'agit d'un exemple de propriété à intégrer dans la configuration existante, **pas** d'un fichier complet à substituer à `wimf.json`. Le chemin JSON doit contenir des antislashs échappés (`\\\\`). Après redémarrage du serveur, des lignes `[SMB-WATCH]` signalent les surveillances démarrées et leur bilan à l'arrêt ; `[SMB-WATCH-ERROR]` indique les erreurs, notamment les pertes possibles d'événements. Un partage indisponible au démarrage est ignoré pour la surveillance, sans empêcher le serveur de démarrer.

**Sécurité et fiabilité :** cette fonction est opt-in, ne journalise pas les chemins individuels, ne met pas à jour les catalogues, ne persiste pas les notifications et ne remplace pas le scan complet. Un `FileSystemWatcher` sur SMB ne garantit ni la réception de toutes les notifications ni leur rattrapage après déconnexion. Désactiver en supprimant `watchRoots` ou en utilisant `[]`.


### Surveillance SMB persistante (expérimental)

Quand `watchRoots` est configuré, le serveur retient les répertoires concernés par les notifications dans un journal append-only local, sous `%LOCALAPPDATA%\\Wimf2\\smb-watch` sur Windows. Les renommages enregistrent les dossiers source et destination. Les chemins sont encodés en Base64 UTF-8 et peuvent révéler des noms confidentiels : protéger le dossier de journal.

Consulter l'état sur la page `http://127.0.0.1:5080/smb-watch` (actualisation automatique toutes les 2 secondes), ou obtenir les données textuelles ponctuelles à `http://127.0.0.1:5080/api/smb-watch`. Les journaux sont relus au démarrage. Une limite de 2 000 répertoires distincts s'applique en mémoire à chaque racine ; au-delà, il faut procéder à un scan complet. Cette première version conserve un historique append-only qui peut grossir au fil du temps ; ne pas l'utiliser comme catalogue d'événements garanti.

**Important :** `fullScanRequired=True` est volontaire et ne s'efface pas automatiquement : une écoute SMB (même persistée) ne couvre pas les arrêts ou déconnexions et ne prouve jamais l'exhaustivité. Cette PR ne modifie ni les catalogues ni le planificateur de scans complets ; une future PR définira la réconciliation et la remise à zéro contrôlée de cet état.


### Prévisualisation d'un scan incrémental (prototype non destructif)

La commande suivante relit les indications persistantes de la surveillance SMB, élimine les sous-répertoires déjà couverts par un parent, puis énumère les fichiers des répertoires indiqués **sans écrire ni remplacer de catalogue** :

```powershell
$journal = (Get-ChildItem "$env:LOCALAPPDATA\Wimf2\smb-watch\*.events" | Select-Object -First 1).FullName
dotnet run --project src/Wimf.Server -c Release -- --incremental-preview "\\\\192.168.1.200\BACKUP_PC_Cecile" --journal "$journal"
```

Pour plusieurs `watchRoots`, identifier d'abord le journal correspondant à la racine cible (nom SHA-256 de la racine normalisée). Les compteurs `[INCREMENTAL-PREVIEW]` décrivent uniquement les sous-arbres observés. Les chemins hors racine et enregistrements incorrects sont refusés ; les liens symboliques/reparse points ne sont pas parcourus ; les répertoires disparus ou illisibles sont signalés comme `skipped`. La commande renvoie un code non nul si des chemins sont invalides ou des éléments ignorés.

**Attention :** cette prévisualisation n'est pas encore un moteur d'écriture incrémentale. Les répertoires touchés peuvent être incomplets à cause des interruptions SMB, un fichier supprimé n'est plus énumérable, et `fullScanRequired` reste toujours vrai. Ne jamais utiliser cette sortie pour remplacer un scan complet, décider qu'un catalogue est synchronisé, ou supprimer des entrées existantes.


#### Progression et diagnostic des derniers événements

La commande `--incremental-preview` affiche maintenant immédiatement `[INCREMENTAL-PREVIEW-START]`, les répertoires retenus (`[INCREMENTAL-TARGET]`) puis `[INCREMENTAL-PROGRESS]` environ tous les 500 dossiers parcourus, avant le bilan final.

Pour **comparer un échantillon récent** aux anciens événements conservés dans le journal, ajouter :

```powershell
dotnet run --project src/Wimf.Server -c Release --no-build -- --incremental-preview "\\\\192.168.1.200\\BACKUP_PC_Cecile" --journal "$journal" --last-records 20
```

`--last-records 20` analyse uniquement les 20 dernières lignes du journal persistant, sans modifier ni purger celui-ci. Ce filtrage est **une aide au diagnostic, pas un mécanisme de checkpoint/reprise**, car des modifications plus anciennes pourraient être omises. `fullScanRequired` reste toujours vrai. Le mode par défaut analyse tout le journal.


#### Événements SMB précis (version expérimentale)

Le serveur écrit maintenant dans le même journal `.events` les anciens enregistrements `D` (répertoires) **et** les nouveaux enregistrements `E` (action + chemin exact, en Base64 UTF-8). Pour tester, redémarrer le serveur avec la nouvelle version, modifier **un fichier existant** depuis un autre PC, puis arrêter le serveur et lancer `--incremental-preview` avec `--last-records 20`.

Lorsque des événements `E` figurent dans l'échantillon, les événements de fichiers sont traités individuellement sans parcourir le dossier parent. Les événements qui portent sur un dossier existant peuvent en revanche entraîner un parcours récursif. Les anciens journaux ne contenant que des `D` restent compatibles et peuvent encore sélectionner toute la racine. Les suppressions sont signalées comme éléments absents et nécessitent une réconciliation.

Les compteurs `files` représentent alors les fichiers inspectés, pas les fichiers modifiés. L'historique et le diagnostic ne permettent toujours pas de garantir l'exhaustivité ; aucun catalogue n'est mis à jour.


#### Nature des changements et chemins disparus

Après redémarrage du serveur sur cette version, un troisième enregistrement `K` complète les enregistrements historiques `D` et `E` : type d'action (`created`, `changed`, `deleted`, `renamed-from`, `renamed-to`), nature observée (`file`, `directory`, `unknown`), chemin Base64 UTF-8. Ce format reste rétrocompatible pour les prévisualisations des anciens journaux.

Le rapport `--incremental-preview` distingue `[FILE]`, `[DIRECTORY]` et `[UNKNOWN/MISSING]`, ainsi que les compteurs `deletes`, `renames` et `unknown`. Les fichiers et dossiers déjà disparus restent `unknown` si leur nature ne peut plus être déterminée de manière fiable ; un dossier supprimé pourrait contenir des descendants. La commande n'effectue **aucune suppression** dans les catalogues, même avec `skipped=1` ; `fullScanRequired=True` demeure obligatoire.


#### Comparaison des suppressions et déplacements avec un catalogue existant

La commande expérimentale `--incremental-impact` lit le journal SMB et un catalogue WIMF existant **sans modification** pour trouver les entrées affectées par un chemin supprimé ou renommé (côté source), y compris les descendants d'un dossier :

```powershell
dotnet run --project src/Wimf.Server -c Release --no-build -- --incremental-impact "\\\\192.168.1.200\\BACKUP_PC_Cecile" --journal "$journal" --catalog "CHEMIN_VERS_LE_CATALOGUE.txt.gz" --last-records 30
```

Choisir le catalogue correspondant exactement à la racine SMB : la commande refuse un catalogue d'une autre racine ou contenant des entrées invalides. `[CATALOG-EXACT]` indique les anciennes entrées correspondant au chemin ; `[CATALOG-DESCENDANT]` leurs descendants. `[INCREMENTAL-IMPACT]` fournit les compteurs de chemins et d'entrées concernées. Les formats `E` et `K` du même événement ne sont pas comptés deux fois.

**Limite :** c'est une estimation d'impact basée sur un journal de notifications potentiellement incomplet, pas une instruction de suppression, et le catalogue n'est jamais réécrit. Avant une publication incrémentale fiable, il faudra traiter les changements non observés, interruptions SMB, vérifications d'identité et validations comparatives avec un scan complet.


#### Détailler les chemins sans correspondance

`--incremental-impact` affiche désormais une ligne `[CATALOG-MISSING] CHEMIN` pour chaque chemin supprimé ou renommé (côté source) absent du catalogue. Ceci permet de comprendre les compteurs `unmatched` sans effectuer de modification de fichier. Le test automatisé inclut maintenant un scénario où un chemin n'a jamais figuré dans le catalogue, en complément du scénario de dossier supprimé avec descendant déjà indexé.


#### Comparaison avec un scan complet de référence (expérimental)

La commande suivante compare **en lecture seule** le catalogue existant à un scan complet de la racine, construit dans un répertoire temporaire indépendant :

```powershell
dotnet run --project src/Wimf.Server -c Release --no-build -- --incremental-reference "\\\\192.168.1.200\\BACKUP_PC_Cecile" --catalog "$catalogue"
```

Elle affiche les nombres d'entrées précédentes et actuelles, ainsi que les ajouts, suppressions, modifications et entrées inchangées. Le catalogue existant n'est jamais réécrit ; le catalogue de référence temporaire est supprimé à la fin. Ce scan complet peut être long sur SMB.

**Ce jalon ne construit pas encore un catalogue incrémental à partir du journal et ne valide donc pas sa fidélité.** Il établit uniquement une référence mesurable et une base de tests non destructifs pour la future comparaison incrémental/complet. Il peut manquer les exclusions propres à une indexation planifiée ; pour comparer des résultats équivalents, utiliser les mêmes paramètres d'exclusion dans une future validation.


#### Audit de couverture du journal SMB (sans modification de catalogue)

Comparer tous les changements constatés par un **scan complet de référence** aux chemins enregistrés dans le journal :

```powershell
dotnet run --project src/Wimf.Server -c Release --no-build -- --incremental-audit "\\\\192.168.1.200\\BACKUP_PC_Cecile" --catalog "$catalogue" --journal "$journal"
```

La sortie `[INCREMENTAL-UNCOVERED]` affiche les changements réels qui ne sont pas couverts par les chemins notifiés ; `[INCREMENTAL-AUDIT]` récapitule `changes`, `covered`, `uncovered` et `invalid`. L'option diagnostique `--last-records N` est également disponible. Un code de sortie 1 indique des changements non couverts ou des lignes invalides. **Attention :** même si `uncovered=0`, cela ne garantit aucunement que les notifications seront exhaustives lors d'une future déconnexion ni que les catalogues peuvent être mis à jour sans scan complet. La commande ne construit ni ne publie de catalogue incrémental.


#### Sessions SMB et protocole d'essai contrôlé

Le serveur ajoute désormais au journal `.events` des marqueurs `S` de début (`start`), d'arrêt (`end`) et d'erreur (`error`), horodatés en UTC. La commande suivante en donne le bilan :

```powershell
dotnet run --project src/Wimf.Server -c Release --no-build -- --smb-sessions "$journal"
```

Pour comparer une période maîtrisée : (1) créer un catalogue de référence de test **avant** les modifications, avec les mêmes exclusions que la configuration, (2) démarrer le serveur et vérifier le marqueur `start`, (3) effectuer des modifications depuis un autre PC, (4) arrêter proprement le serveur et vérifier `end`, (5) exécuter `--incremental-audit` avec ce catalogue témoin et le journal. **Ne pas utiliser un ancien catalogue de production comme référence temporelle de la session :** ses écarts peuvent provenir de périodes antérieures. Les arrêts/reprises restent des trous de couverture, et une session start/end sans erreur ne prouve pas l'exhaustivité des notifications. Aucun catalogue de production n'est modifié par ces outils.


#### Test SMB contrôlé en une seule commande

La commande expérimentale suivante réalise un scan complet témoin **avant** de commencer la surveillance, puis attend des modifications effectuées depuis un autre PC. Lorsque `[CONTROLLED-READY]` apparaît, effectuer les opérations de test ; arrêter ensuite avec `Ctrl+C`. Un second scan complet compare les résultats au journal recueilli **pendant cette session uniquement** :

```powershell
dotnet run --project src/Wimf.Server -c Release --no-build -- --smb-controlled-test "\\\\192.168.1.200\\BACKUP_PC_Cecile"
```

La sortie `[CONTROLLED-RESULT]` fournit `changes`, `covered`, `uncovered`, `invalid` et `watcherErrors`. Le test ne modifie pas les catalogues de production : son catalogue témoin et son journal sont temporaires et supprimés à la fin. **Ne modifier aucun fichier du partage avant `[CONTROLLED-READY]`**, sinon les différences peuvent ne pas être couvertes par le journal. Ne pas lancer en parallèle un autre processus WIMF2 utilisant la même racine pour un benchmark comparable. La surveillance SMB ne garantit toujours pas la réception des changements ; même avec `uncovered=0`, `fullScanRequired=True` reste obligatoire.


#### Comparaison d'un candidat incrémental en mémoire avec le scan complet

Cette commande expérimentale projette les chemins du journal SMB sur les entrées du catalogue existant **en mémoire**, examine les fichiers et sous-arbres concernés, et compare ensuite chaque chemin, extension, taille et jour de modification à un catalogue complet temporaire :

```powershell
dotnet run --project src/Wimf.Server -c Release --no-build -- --incremental-candidate "\\\\192.168.1.200\\BACKUP_PC_Cecile" --catalog "$catalogue" --journal "$journal"
```

La sortie `[CANDIDATE-RESULT]` indique `candidate`, `reference`, `missing`, `extra`, `different`, `invalid`, `hints` et `equal`. Le code de sortie vaut 1 si la comparaison échoue. Ce prototype ne produit **pas** de catalogue incrémental sérialisé et ne modifie pas le catalogue de production. Il ne tient pas encore compte des exclusions de l'indexation planifiée et les modifications faites pendant la comparaison peuvent créer des écarts. Des liens/reparse points, fichiers illisibles et dossiers devenus indisponibles entraînent une erreur ou demandent un scan complet.

**Même `equal=True` n'autorise aucune publication :** les notifications SMB peuvent être incomplètes et l'état `fullScanRequired=True` reste obligatoire.


#### Générer un catalogue candidat incrémental (PR #61, expérimental)

Avec un catalogue existant et un journal SMB, il est possible de matérialiser la projection dans un **nouveau** fichier, puis de la comparer avec un scan complet de référence :

```powershell
dotnet run --project src/Wimf.Server -c Release --no-build -- --incremental-candidate "\\\\192.168.1.200\\BACKUP_PC_Cecile" --catalog "$catalogue" --journal "$journal" --candidate-output "$env:TEMP\\wimf-candidat-test.txt.gz"
```

Le fichier candidat est refusé s'il existe déjà, s'il se trouve dans la racine indexée ou si sa structure est incohérente. Cette commande ne publie pas le résultat dans le catalogue de production : `equal=False` signale des écarts et l'obligation de réconciliation reste active. Éliminer manuellement le fichier temporaire de test lorsqu'il n'est plus utile.

**Sécurité renforcée PR #61 :** si le catalogue candidat diffère du scan complet (`missing`, `extra`, `different` ou `invalid` non nuls), `--candidate-output` refuse désormais de créer un fichier. Cela évite de conserver un candidat connu comme incohérent. Le résultat n'est pas pour autant une autorisation de publication automatique : la complétude des événements SMB et les modifications concurrentes restent à traiter.


#### Actualisation sûre vers un nouveau catalogue (expérimental)

```powershell
dotnet run --project src/Wimf.Server -c Release --no-build -- --incremental-safe-refresh "\\\\192.168.1.200\\BACKUP_PC_Cecile" --catalog "$catalogue" --journal "$journal" --output "$env:TEMP\\wimf-safe-refresh.txt.gz"
```

Le mode `candidate-validated` est utilisé uniquement si la projection correspond à la référence complète. Si des entrées diffèrent, le mode `full-fallback` génère automatiquement un catalogue complet dans le nouveau fichier de sortie. Le fichier de sortie **ne doit pas déjà exister** et n'est jamais publié à la place du catalogue initial. Le scan complet reste requis pour valider le candidat ; cette version privilégie la sûreté, sans prétendre accélérer le temps total d'actualisation. Les modifications concurrentes pendant les deux scans peuvent encore affecter la cohérence ; la gestion transactionnelle de ces cas reste un chantier.


#### Publication sécurisée des indexations planifiées (PR #62)

Les actualisations du planificateur (manuelles et planifiées) construisent désormais un **scan complet dans un fichier de staging à côté du catalogue cible**. Le fichier est relu et son nombre d'entrées vérifié avant un renommage de remplacement sur le même volume. En cas d'annulation, d'erreur ou d'entrées ignorées, le catalogue précédent est préservé et le staging supprimé. Les notifications SMB restent diagnostiques : aucune publication incrémentale sans garanties de continuité, et aucun gain de temps de scan complet n'est revendiqué à ce stade.

#### Fichiers ignorés et sécurité des indexations planifiées

Les noms contenant `;` ou un retour à la ligne ne sont pas représentables dans le format historique WIMF. Ils sont consignés dans le rapport `.skipped-report.txt`, mais ne bloquent plus la publication des entrées représentables (comportement compatible avec les scans historiques). En revanche, une erreur d'accès à un dossier ou fichier (`Skipped inaccessible ...`) bloque la publication et préserve le catalogue précédent. Le rapport reste accessible dans le panneau même après un échec. Pour déterminer pourquoi une indexation a été rejetée, consulter ce rapport associé au catalogue.


#### Accès refusés sur les disques système
Lorsqu'un catalogue précédent existe, les chemins devenus inaccessibles sont comparés à ses entrées. Un chemin protégé qui n'était pas présent dans l'ancien catalogue peut rester ignoré avec un diagnostic, comme lors des anciens scans. Si le chemin était indexé (ou possédait des descendants indexés), la publication est refusée pour éviter la disparition artificielle de ces entrées. Une première indexation avec erreurs d'accès reste refusée. Consulter le fichier `.skipped-report.txt` du catalogue pour identifier les chemins en cause.

#### Politique d'accès lors des scans planifiés

Les fichiers et sous-répertoires inaccessibles sont ignorés et **retirés du nouveau catalogue**, même s'ils figuraient auparavant. Les chemins concernés restent listés dans le rapport des entrées ignorées. Pour éviter une publication après une déconnexion du partage, WIMF2 vérifie l'accès effectif à la **racine** (énumération de premier niveau) avant le scan, après le scan et juste avant publication. Si cette vérification échoue, le catalogue précédent est préservé. Cette vérification de la racine ne garantit pas de détecter toute indisponibilité intermittente survenue entre deux vérifications.


#### Fiabilité SMB pendant les indexations (PR #63)
En cas d'erreur d'E/S lors de l'énumération d'un sous-dossier, WIMF tente une seconde énumération après 150 ms avant de le consigner comme inaccessible. Un refus de droits ne déclenche pas de nouvelle tentative. Quand un fichier ou sous-dossier est signalé inaccessible, le planificateur vérifie immédiatement l'accès à la racine ; une racine indisponible interrompt la publication et conserve l'ancien catalogue. Les fichiers réellement inaccessibles restent exclus du nouveau catalogue, conformément à la politique choisie. Ces contrôles ne prouvent pas la cohérence d'un instantané SMB si des modifications surviennent pendant le scan ; la journalisation des événements continue de servir au diagnostic.
