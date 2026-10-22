# PR #117 — adaptive extension identifiers during loading

Catalog construction previously allocated a full `int[]` of one index per entry even when all extension IDs would ultimately fit in `byte[]`, then allocated the final array and copied every index.

The constructor now starts with a `byte[]`, upgrades to `ushort[]` when a 257th unique extension appears, and upgrades to `int[]` if more than 65,536 distinct extensions are encountered. Previous values and malformed ID slots are preserved during transitions.

For ~9.88 million records, the byte-ID catalogs no longer require an additional transient ~4 bytes per record index array. In the `Disque C` catalog, widening temporarily allocates and copies the smaller arrays; total savings depend on GC behavior.

This is **a limited first step** in eliminating construction allocations, not yet a direct streaming parser. The reader still accumulates entries in a list before making the final compact representation. There is no change to catalog disk format, indexing policy, SMB locks, or full-name search semantics.

After merging, measure on `wimf.json`:
```powershell
dotnet build wimf.slnx -c Release
dotnet test wimf.slnx -c Release
1..3 | ForEach-Object {
    dotnet run --no-build --project src/Wimf.Server -c Release -- --catalog-memory-profile --config ".\\wimf.json"
}
```

Compare against the previous ~1,038 MiB post-collection managed footprint, ~4.65 s load, and ~291 ms no-match search; startup process working set may vary due to GC.
