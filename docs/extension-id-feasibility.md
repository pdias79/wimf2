# Byte extension IDs — feasibility (PR #111)

A byte can represent 256 distinct extension identifiers per catalog, including empty extensions, mixed-case variants, and the `folder` marker. The on-disk catalog must remain unchanged. Test the real configured catalogs:

```powershell
dotnet run --project src/Wimf.Server -c Release -- --extension-id-profile --config ".\\wimf.json"
```

The command only loads existing files to inspect each catalog's **exact**, case-sensitive distinct extension count and whether all can fit in 256 IDs. Missing files fail; nothing is indexed or rewritten. This is a prerequisite measurement, **not yet** an altered storage representation.

Important: replacing an 8-byte string reference with a byte inside a struct does **not** guarantee a 7-byte saving because alignment and padding apply. The next implementation should benchmark a split-array layout (compact metadata plus separate extension IDs), including lookup/reconstruction overhead and correct fallback when a catalog has more than 256 distinct values.
