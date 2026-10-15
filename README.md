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
