# Filename suffix feasibility — PR #113

The previous optimization stores extension IDs separately; this experiment measures whether keeping only the **base name** could save additional UTF-16 characters without changing the observable full filename.

```powershell
dotnet run --project src/Wimf.Server -c Release -- --filename-suffix-profile --config ".\\wimf.json"
```

The command is read-only and uses the catalog paths from the JSON configuration. It reports per-catalog entries, files, exact suffix matches, mismatches and potential UTF-16 byte savings.

A name is considered splittable only if it **strictly ends in** `.` followed by the exact, case-sensitive extension, and has at least one character before that separator. Directories, empty extensions and inconsistent legacy entries remain untouched. For example `a.tar.gz` plus `gz` splits into `a.tar` and `gz`, while `scan.PDF` plus `pdf` does not.

Reported UTF-16 bytes are an **upper-bound character payload estimate**, not a measured memory reduction: .NET string object alignment, search allocations, full-name reconstruction, and extra representation metadata may erase some savings. In particular, allocating a full name for every entry inspected by the search loop is not acceptable without a benchmarked allocation-free alternative.

**This PR does not change the catalog representation, search logic, disk files, SMB locks, scheduling, or API.** It establishes a safe baseline before an implementation PR.
