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

Import `Wimf.Search` to use these types. All whitespace-separated words must occur in the file name, in any order, using ordinal case-insensitive comparison. Empty text matches all files. Directories are excluded. Extension filters accept a leading dot and ignore case; size and modification-date bounds are inclusive. Filters combine with AND. Results retain their source catalog and original record ID, in input catalog and record order; the limit applies across all catalogs (zero returns no results).

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
