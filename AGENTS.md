# Wimf 2 — engineering rules

- Target .NET 10, Windows and Linux, with eventual Native AOT deployment.
- Prioritize compact binaries, bounded allocations, efficient indexing, and reliable unattended operation.
- Keep the historic WIMF catalog format readable; never renumber records when parsing malformed input.
- In legacy catalogs, record ids start at 1 (root id 0); dates in entry records are Unix days, header creation date is Unix seconds.
- Paths in catalogs may be Windows paths even when the program runs on Linux. Do not normalize or access these paths while parsing.
- Make network synchronization and catalog publication resilient: stage, validate and atomically activate when feasible.
- Do not commit credentials, private network paths, production catalogs, or personal file names.
- Prefer standard-library dependencies; any third-party dependency requires a justification and AOT compatibility review.
- Keep domain and catalog reader independent of HTTP hosting and UI.
- Run `dotnet test Wimf.slnx` before publishing changes where SDK is available.
- Do not modify the historic repository.
