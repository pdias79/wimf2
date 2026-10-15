# Wimf 2

Wimf 2 is a cross-platform file catalog indexing and search daemon under development. The original VB.NET application is at [pdias79/WIMF](https://github.com/pdias79/WIMF).

## Goals

- Native Windows and Linux distribution, small binary and bounded memory use.
- Read existing WIMF gzip/plaintext catalogs without changing the legacy producer.
- Build, publish, synchronize, and search multi-million-entry catalogs.
- HTTP API and lightweight embedded web interface.
- Compare in-memory and indexed search before selecting a new persistent format.

## Current milestone

This first change establishes a .NET 10 solution and implements **legacy catalog reading and path reconstruction**. It does **not** yet implement the daemon, synchronization, indexing, or HTTP search.

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
