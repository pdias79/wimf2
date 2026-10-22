# PR #110: compact in-memory entry timestamps

Each `CatalogEntryData` formerly held two string references, a `ulong`, an `int` and a `long`. With 64-bit alignment this costs 40 bytes per slot. Unix timestamps are already stored as whole **days** and every date supported by `DateTimeOffset` fits in a signed 32-bit day count. The new layout stores the day count in an `int` while retaining the public `long ModifiedUnixDays` property and constructor argument.

Expected saving: **8 bytes per slot**, around **75.4 MiB for 9,880,138 entries**. This is a theoretical managed array saving, not an established change in total process resident memory. Verify with `--catalog-memory-profile --config ".\\wimf.json"` three times before and after on the same catalogs. Test search results and incremental updates unchanged.

No change to the on-disk catalog format, SMB coordination, scheduler, search semantics, or API.
