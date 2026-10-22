# PR #112 — compact in-memory extension IDs

All legacy `.txt` and `.txt.gz` catalogs keep their original format. Upon loading, each catalog builds a case-sensitive table of distinct extension strings, and stores each record's extension index in a separate array.

- Up to 256 distinct extensions: `byte[]`.
- Up to 65,536 distinct extensions: `ushort[]`.
- Beyond 65,536: `int[]` (lossless fallback).

Other stored record fields use a 24-byte structure instead of the former 32-byte structure. The separate extension arrays require approximately 1 or 2 additional bytes per slot for the catalogs observed in `wimf.json`. Theoretical persistent savings: 7 bytes per entry for seven byte-ID catalogs (9,110,439 entries), 6 bytes per entry for the ushort catalog (769,303 entries), about 65 MiB total. Values are estimates and exclude allocation overhead and temporary load-time structures.

There is no change to the public `CatalogEntryData` API, file names, extension casing, paths, filter semantics, incremental updater, or SMB publication rules.

After merge, compare three independent runs against previous baseline:

```powershell
dotnet run --project src/Wimf.Server -c Release -- --catalog-memory-profile --config ".\\wimf.json"
```

Also run `dotnet test wimf.slnx -c Release` and exercise extension filtering and path generation in the UI. Peak memory during reload may temporarily include both the original and new representation; measure this separately before deploying memory-constrained machines.
