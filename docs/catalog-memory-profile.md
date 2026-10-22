# Measuring memory with all catalogs (PR #108)

Run a **new process** per measurement, without the WIMF daemon actively rewriting catalogs:

```powershell
dotnet run --project src/Wimf.Server -c Release -- --catalog-memory-profile "C:\\catalogs\\one.txt.gz" "C:\\catalogs\\two.txt.gz"
```

Repeat three times. Use `dotnet run --no-build` for repetitions.

This diagnostic loads the selected catalogs into one process (as the server does), forces a collection to sample retained managed memory, and performs a single deliberately unlikely literal search with a one-result cap, scanning the entire loaded snapshot without materializing full paths for every entry. It reports catalog count, record count, aggregate disk size, managed heap and process working-set samples before/after load and after search, elapsed times and per-thread search allocations.

**Important limitations:** these snapshots are not an object heap profiler. The current `SequentialSearchEngine` scans array-backed catalog entries rather than keeping a separate persistent inverted search index. The measurement excludes the web server, background watchers, loading/refresh overlap, API caches, concurrent requests, and .NET process lifetime behavior. Working set is OS-dependent, and the forced GC makes timings unsuitable for user-latency predictions. The no-match search is one workload, not a comprehensive search benchmark.

No idle-unload mechanism is introduced; this PR establishes a repeatable baseline first. Preserve Windows/Linux CI and test realistic catalogs before designing unload/preload behavior.
