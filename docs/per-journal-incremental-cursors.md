# Per-journal incremental cursors (PR #103)

Each watcher journal now owns acknowledgement cursor files in the same directory:
`<journal-path>.<SHA256(uppercase-absolute-catalog-path)>.incremental-cursor`.

The old `.<catalog>.incremental-cursor` marker next to the shared catalog is deliberately ignored, not deleted, because it can belong to a different host. When upgrading, events in the local journal are conservatively replayed from offset zero until successfully acknowledged; old journal data may cause temporary extra work. Offline journal compaction reads and invalidates only the matching local cursor.

**Limitations:** a valid per-journal offset still relies on the catalog last-write timestamp and journal path signature. This does not establish causal consistency between independent SMB watcher journals, and file timestamps can collide. A watcher reconnecting after missed events still requires the next scheduled or explicitly requested full reconciliation. No automatic full scan is introduced. Keep physical multi-host SMB validation and catalog-generation protocol on the roadmap before claiming full distributed consistency.
