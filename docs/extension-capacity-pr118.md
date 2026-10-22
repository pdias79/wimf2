# PR #118 — extension capacity policy

New full-scan catalogs reserve IDs 0–14 for `""`, `folder`, `pdf`, `doc`, `docx`, `txt`, `jpg`, `jpeg`, `gif`, `png`, `ppt`, `pptx`, `xls`, `xlsx`, `pps`. All new distinct extensions share the remaining 241 identifiers. Names remain unchanged, and matching during indexing is case-insensitive.

Once the per-catalog 256-extension limit is reached, full scans skip a file requiring a previously unknown extension and emit a `Skipped extension table limit (256)` warning through the existing skipped-entry reporting mechanism, visible in catalog management and its skipped-file report.

Incremental updates use the same limit. Extensions already present in a legacy catalog are grandfathered in, even if there were over 256; a newly encountered extension is refused when the set is at capacity. Incremental warnings are currently written to standard error and an optional callback.

The in-memory catalog reserves IDs 0–14 for the fixed entries while retaining the `ushort`/`int` compatibility fallback for existing catalogs containing more extensions or case-distinct variants. **This PR does not remove the compatibility fallback**: doing so would hide existing catalog files without changing the disk format. Disk catalog format remains unchanged.

Validation before merge: Windows/Ubuntu tests including boundary case with 243 new extensions, existing catalog read/search, full scan and incremental scan, warnings in the catalog panel, and memory profiling on `wimf.json`.
