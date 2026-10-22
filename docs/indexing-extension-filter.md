# Extension selection for indexed catalogs

Each `indexing[]` entry accepts `extensions`, an optional list of file extensions.

If **omitted**, the allowed extensions are `pdf`, `doc`, `docx`, `xls`, `xlsx`, `jpg`, `png`, `ppt`, `pptx`.

Example:

```json
{
  "catalogs": ["catalog.txt.gz"],
  "indexing": [
    {
      "root": "source",
      "output": "catalog.txt.gz",
      "fullScanIntervalDays": 7,
      "extensions": ["pdf", "docx", "xlsx"]
    }
  ]
}
```

Extensions are case-insensitive. An initial dot is allowed (e.g. `.PDF`). Duplicate values are normalized; malformed or empty names are rejected.

An explicitly empty array (`"extensions": []`) retains no files and therefore no directories.

Only directories containing a selected file somewhere below them are written into the resulting catalog. The indexed root is still present as catalog header metadata, not as a separate entry.

Both scheduled/manual full scans and watcher-driven incremental updates apply the same configured allowlist. During a full scan, the historical writer produces a private staged catalog, then a streaming pass removes excluded file records and empty directories and remaps legacy record parent IDs **before the safe publication step**. This preserves compatibility with the legacy catalog format. The incremental path similarly prunes stale unselected files and empty directories before writing its staged output.

Changing the allowlist does not trigger a full scan immediately. Existing catalog content is brought into compliance at the next configured full scan (or manual full scan); an incremental update may prune entries before then. Scan cadence, lock behavior and SMB watcher continuity rules are unchanged.

The full scan still enumerates the directory tree and initially writes a temporary intermediate catalog; extension filtering is not yet an optimization of network enumeration. Storage and memory costs should be benchmarked on production-scale catalogs before large deployments.
