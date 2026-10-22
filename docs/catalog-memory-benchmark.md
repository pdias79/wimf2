# Catalog memory benchmark

Run in **Release** in a separate process, with the daemon stopped or at least without simultaneous catalog writes:

```powershell
dotnet run --project src/Wimf.Server -c Release -- --catalog-memory "C:\\path\\catalog.txt.gz"
```

The command loads **one existing catalog**, reports one JSON record, and exits without starting the server, touching cursors, triggering scans, or rewriting the catalog.

Fields:
- `Entries`: number of catalog records, including folders.
- `CatalogFileBytes`: compressed or plain file size.
- `ManagedBaselineBytes` and `ManagedLoadedBytes`: .NET heap bytes after explicit full collections before and after loading.
- `ManagedDeltaBytes`: difference, an **estimate**, potentially noisy for small catalogs; not a direct measurement of all memory retained by the catalog.
- `WorkingSetBaselineBytes` and `WorkingSetLoadedBytes`: process resident working-set measurements, affected by OS paging and runtime behavior.
- `LoadMilliseconds`: elapsed read/parse time, excludes the post-load forced collection.

For comparison, repeat each catalog measurement in a **fresh process** (at least three times). Report the median and compare by millions of records. Avoid drawing conclusions from small inputs or a single run. Also measure the complete running daemon separately (e.g. Windows Task Manager or Performance Monitor) to capture the search index and other long-lived allocations, which this isolated benchmark deliberately excludes.

No memory compression, inactivity eviction, or binary-format change is included here. The next optimization should be selected from measured results.
