# Shared catalogs and per-host incremental journals — PR #102

## Observed implementation (20 October 2026)

- All cooperating WIMF2 writers acquire the catalog's shared OS/SMB byte-range lock for their publication. That lock prevents simultaneous *cooperating* writers, not missed SMB watcher events.
- The cursor `.<catalog-name>.incremental-cursor` is stored beside the **shared catalog** and contains a SHA-256 signature of the absolute journal path, an acknowledged byte offset, and the catalog modification-time ticks.
- The watcher journals are ordinarily stored in per-host local application data. Different hosts normally have different signatures but overwrite the *same* cursor marker when targeting a common catalog.
- If the signature or catalog timestamp does not match, `Pending` and `Publish` fall back to offset 0. This is conservative replay, not lossless distributed coordination. Alternating writers can needlessly replay entire journals, causing additional I/O and possibly long delays. Catalog mtime alone is not a collision-resistant catalog generation identifier.
- The watcher only provides notifications; reconnection does not establish completeness. The existing full reconciliation schedule must not be accelerated after a watcher error, missed event or cursor mismatch.

## Regression coverage added

`MultiJournalPublicationTests` exercises two separate journal paths feeding one catalog with sequential lock acquisition: (1) both hosts add different files and host A replays its earlier journal after B's publication; (2) a deletion published by A remains deleted when B subsequently publishes and A replays. The tests check the final catalog contents rather than only the acknowledgement offsets.

**Scope limitation:** these deterministic tests do not emulate real SMB lease/disconnection semantics or two simultaneous hosts. They neither prove no missed notifications nor establish correctness for every rename, journal rotation, timestamp collision, network failure or interleaving.

## Recommended next design step (not implemented by this tests-only PR)

Decouple cursor ownership from the shared catalog: associate a durable per-host cursor with its journal and a strong catalog generation/reference; atomically publish catalog generation metadata with the catalog, and reject any incremental publication that cannot prove a safe base and adequate journal continuity. Migration from the existing shared marker and offline compaction need specific compatibility tests. Do not automatically run a full scan when this validation fails; report the quarantine and wait for the regular configured full scan or an explicit manual request.

Until that redesign and physical two-machine SMB validation are complete, **do not claim full multi-host incremental consistency**.
