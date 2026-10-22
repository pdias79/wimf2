# PR #116 — avoid a full-array copy while loading catalogues

The legacy reader previously kept parsed records in a `List<CatalogEntryData>`, then called `ToArray()` to validate them and pass them into the compact `Catalog` representation. This caused a second transient array of approximately 32 bytes per entry before constructing the final compact arrays.

The reader now validates the records in its existing list and passes the list as `IReadOnlyList<CatalogEntryData>` to the catalog constructor. Record IDs, malformed slots, parent reference validation and compact storage are unchanged. No changes to disk serialization, file names, search or SMB locking.

For ~9.88 million entries, removing the duplicate array potentially avoids an additional allocation of around 302 MiB during loading, though process peak/residency reduction depends on GC timing. This PR **does not** eliminate the reader's intermediate list, the compact constructor's temporary ID array, or short-lived filename strings. Persistent managed-memory estimates should remain approximately the same.

Verify on Windows using `dotnet test wimf.slnx -c Release`, three runs of `--catalog-memory-profile --config ".\\wimf.json"`, and `/api/memory-diagnostics` immediately after a cold server startup. Compare both working-set peaks and load durations. No forced collections are introduced into the production server.
