# PR #119 — on-demand full-GC comparison

Normal GET `/api/memory-diagnostics` remains non-invasive and does not trigger GC.

**Explicit diagnostic only:** POST `/api/memory-diagnostics/collect` takes a process-wide before snapshot, performs two blocking full compacting collections with a finalizer wait, then takes an after snapshot. It returns both `Sample` objects along with signed deltas for managed heap, working set and GC committed bytes.

Call only while Wimf2 is idle, outside searches and catalog refreshes, because a blocking collection stops managed threads and will affect latency. Localhost binding is unchanged; this endpoint is deliberately POST, not an automatic periodic poll. Unlike heap retention after forced collection, the process working set does not necessarily drop immediately.

PowerShell:
```powershell
Invoke-RestMethod -Method Post "http://localhost:5080/api/memory-diagnostics/collect" | ConvertTo-Json -Depth 5
```

Check whether `after.managedHeapBytes` approaches ~1,038 MiB for 8 catalogs / ~9.88M entries. A large drop indicates collectable allocations; a high post-GC heap indicates retained server-side objects. Repeat after idle to control for different concurrent server activity.

This diagnostic does not yet provide accurate per-catalog incremental-heap attribution; GC committed heap and working set are process-level measures and attributing them to individual catalogs without a controlled isolated load would be misleading.
