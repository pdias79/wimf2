# Live server memory diagnostic (PR #109)

Start the server normally with the project configuration:

```powershell
dotnet run --project src/Wimf.Server -c Release -- --config ".\\wimf.json"
```

Query `GET /api/memory-diagnostics` using the same host and port as the WIMF UI, for example:

```powershell
Invoke-RestMethod "http://localhost:PORT/api/memory-diagnostics" | ConvertTo-Json
```

Repeat after startup, search, and catalog refresh; preserve the responses for comparison.

The endpoint is **read-only**: no GC collection is forced, catalogs are not reloaded, no indexing is triggered. It reports catalog count, **entry** count (files and directories), managed heap live estimate (`GC.GetTotalMemory(false)`), GC heap size/committed bytes, working set/private bytes, and collection counts. Metrics have different meanings and must not be subtracted from independent isolated-benchmark metrics as if they described the same allocation category. The endpoint has no authentication added by this PR: use only on trusted/local interfaces; do not expose diagnostic endpoints publicly.

No memory optimization or idle unloading is introduced here.
