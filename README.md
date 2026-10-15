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
