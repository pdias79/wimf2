using System.Text.RegularExpressions;
using Microsoft.AspNetCore.ResponseCompression;
using Wimf.Catalog;
using Wimf.Search;
using Wimf.Server;

// Inspect whether filenames can be split losslessly into base name plus extension.
if (args.Length > 0 && args[0] == "--filename-suffix-profile")
{
    if (args.Length != 3 || args[1] != "--config")
    {
        Console.Error.WriteLine("Usage: wimfd --filename-suffix-profile --config config.json");
        return 2;
    }
    try
    {
        var configuration = DaemonOptions.Parse(["--config", args[2]]);
        var catalogs = configuration.Catalogs.Concat(configuration.Sources)
            .Select(LegacyCatalogReader.ReadFile).ToArray();
        var report = FilenameSuffixProfile.Analyze(catalogs);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report, WimfJsonContext.Default.SuffixReport));
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                               InvalidDataException or ArgumentException)
    {
        Console.Error.WriteLine("[FILENAME-SUFFIX-PROFILE-ERROR] " + ex.Message);
        return 2;
    }
}

// Read-only feasibility check for byte-sized extension IDs.
if (args.Length > 0 && args[0] == "--extension-id-profile")
{
    if (args.Length != 3 || args[1] != "--config")
    {
        Console.Error.WriteLine("Usage: wimfd --extension-id-profile --config config.json");
        return 2;
    }
    try
    {
        var configuration = DaemonOptions.Parse(["--config", args[2]]);
        var catalogs = configuration.Catalogs.Concat(configuration.Sources)
            .Select(LegacyCatalogReader.ReadFile).ToArray();
        var report = ExtensionIdFeasibility.Analyze(catalogs);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report, WimfJsonContext.Default.ExtensionIdReport));
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                               InvalidDataException or ArgumentException)
    {
        Console.Error.WriteLine("[EXTENSION-ID-PROFILE-ERROR] " + ex.Message);
        return 2;
    }
}

// Isolated multi-catalog profile; never starts watchers or changes the catalog files.
if (args.Length > 0 && args[0] == "--catalog-memory-profile")
{
    if (args.Length != 3 || args[1] != "--config")
    {
        Console.Error.WriteLine("Usage: wimfd --catalog-memory-profile --config config.json");
        return 2;
    }
    try
    {
        // Resolve paths exactly as the daemon does, but do not start it.
        var configuration = DaemonOptions.Parse(["--config", args[2]]);
        var catalogPaths = configuration.Catalogs.Concat(configuration.Sources).ToArray();
        var report = CatalogMemoryProfile.Measure(catalogPaths);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report, WimfJsonContext.Default.Report));
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                               InvalidDataException or ArgumentException)
    {
        Console.Error.WriteLine("[CATALOG-MEMORY-PROFILE-ERROR] " + ex.Message);
        return 2;
    }
}

// Isolated memory benchmark. Never starts the daemon or modifies catalogs.
if (args.Length > 0 && args[0] == "--catalog-memory")
{
    if (args.Length != 2)
    {
        Console.Error.WriteLine("Usage: wimfd --catalog-memory CATALOG.txt[.gz]");
        return 2;
    }
    try
    {
        var result = CatalogMemoryBenchmark.Measure(args[1]);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, WimfJsonContext.Default.Snapshot));
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                               InvalidDataException or ArgumentException)
    {
        Console.Error.WriteLine("[CATALOG-MEMORY-ERROR] " + ex.Message);
        return 2;
    }
}

// Select validated incremental candidate or a complete fallback, always to a NEW output.
if (args.Length > 0 && args[0] == "--incremental-safe-refresh")
{
    if (args.Length != 8 || args[2] != "--catalog" || args[4] != "--journal" || args[6] != "--output")
    {
        Console.Error.WriteLine("Usage: --incremental-safe-refresh ROOT --catalog ORIGINAL --journal FILE --output NEW_FILE");
        return 2;
    }
    try
    {
        var result = IncrementalSafeRefresh.Run(args[1], args[3], args[5], args[7]);
        Console.WriteLine($"[INCREMENTAL-SAFE-REFRESH] mode={result.Mode} | entries={result.Entries} | output={result.Output} | productionUnchanged=True");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
    {
        Console.Error.WriteLine("[INCREMENTAL-SAFE-REFRESH-ERROR] " + ex.Message);
        return 2;
    }
}

// Safe fallback output for a full scan; never overwrites the source catalog.
if (args.Length > 0 && args[0] == "--incremental-fallback")
{
    if (args.Length != 6 || args[2] != "--catalog" || args[4] != "--output")
    {
        Console.Error.WriteLine("Usage: --incremental-fallback ROOT --catalog ORIGINAL --output NEW_FILE");
        return 2;
    }
    try
    {
        var result = IncrementalFallback.BuildFull(args[1], args[3], args[5]);
        Console.WriteLine($"[INCREMENTAL-FALLBACK] mode={result.Mode} | entries={result.Entries} | output={result.Output} | productionUnchanged=True");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
    {
        Console.Error.WriteLine("[INCREMENTAL-FALLBACK-ERROR] " + ex.Message);
        return 2;
    }
}

// Compare an in-memory incremental projection against a disposable full scan.
if (args.Length > 0 && args[0] == "--incremental-candidate")
{
    if ((args.Length != 6 && args.Length != 8) || args[2] != "--catalog" || args[4] != "--journal" ||
        (args.Length == 8 && args[6] != "--candidate-output"))
    {
        Console.Error.WriteLine("Usage: --incremental-candidate ROOT --catalog CATALOG --journal FILE [--candidate-output NEW_FILE]");
        return 2;
    }
    try
    {
        Console.WriteLine("[CANDIDATE-START] Projecting journal hints and building a disposable full reference.");
        var report = IncrementalCandidateComparison.Compare(args[1], args[3], args[5],
            onMismatch: Console.WriteLine, candidateOutput: args.Length == 8 ? args[7] : null);
        Console.WriteLine($"[CANDIDATE-RESULT] candidate={report.Candidate} | reference={report.Reference} | missing={report.Missing} | extra={report.Extra} | different={report.Different} | invalid={report.Invalid} | hints={report.Hints} | equal={report.Equal} | fullScanRequired={report.FullScanRequired}");
        Console.WriteLine("[CANDIDATE-RESULT] Read-only prototype. Equality does not certify future SMB notification completeness.");
        return report.Equal ? 0 : 1;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                               or InvalidDataException or FormatException or OverflowException)
    {
        Console.Error.WriteLine("[CANDIDATE-ERROR] " + ex.Message);
        return 2;
    }
}

// A disposable baseline -> watch -> audit workflow. Never updates production catalogs.
if (args.Length > 0 && args[0] == "--smb-controlled-test")
{
    if (args.Length != 2)
    {
        Console.Error.WriteLine("Usage: --smb-controlled-test ROOT");
        return 2;
    }
    using var experimentStop = new CancellationTokenSource();
    ConsoleCancelEventHandler experimentCancel = (_, e) => { e.Cancel = true; experimentStop.Cancel(); };
    Console.CancelKeyPress += experimentCancel;
    try
    {
        return await SmbControlledExperiment.RunAsync(args[1], experimentStop.Token);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                               or InvalidDataException)
    {
        Console.Error.WriteLine("[CONTROLLED-ERROR] " + ex.Message);
        return 2;
    }
    finally { Console.CancelKeyPress -= experimentCancel; }
}

// Report watcher session boundaries without treating them as a coverage guarantee.
if (args.Length > 0 && args[0] == "--smb-sessions")
{
    if (args.Length != 2)
    {
        Console.Error.WriteLine("Usage: --smb-sessions JOURNAL");
        return 2;
    }
    try
    {
        var report = SmbSessionAudit.Analyze(args[1]);
        Console.WriteLine($"[SMB-SESSIONS] starts={report.Starts} | ends={report.Ends} | errors={report.Errors} | open={report.OpenSessions} | malformed={report.Malformed} | lastStartUtc={report.LastStartUtc ?? "none"} | fullScanRequired={report.FullScanRequired}");
        Console.WriteLine("[SMB-SESSIONS] Session boundaries do not prove notification completeness.");
        return report.Errors == 0 && report.OpenSessions == 0 && report.Malformed == 0 ? 0 : 1;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
    {
        Console.Error.WriteLine("[SMB-SESSIONS-ERROR] " + ex.Message);
        return 2;
    }
}

// Audit how many real changes are covered by recorded SMB notifications.
if (args.Length > 0 && args[0] == "--incremental-audit")
{
    if ((args.Length != 6 && args.Length != 8) || args[2] != "--catalog" ||
        args[4] != "--journal" || (args.Length == 8 && args[6] != "--last-records"))
    {
        Console.Error.WriteLine("Usage: --incremental-audit ROOT --catalog CATALOG --journal FILE [--last-records N]");
        return 2;
    }
    try
    {
        Console.WriteLine("[INCREMENTAL-AUDIT-START] Running full reference scan; catalogs remain untouched.");
        var last = args.Length == 8 ? int.Parse(args[7], System.Globalization.CultureInfo.InvariantCulture) : 0;
        var report = IncrementalCoverageAudit.Analyze(args[1], args[3], args[5], last,
            onUncovered: path => Console.WriteLine($"[INCREMENTAL-UNCOVERED] {path}"));
        Console.WriteLine($"[INCREMENTAL-AUDIT] changes={report.Changes} | covered={report.Covered} | uncovered={report.Uncovered} | invalid={report.InvalidRecords} | fullScanRequired={report.FullScanRequired}");
        Console.WriteLine("[INCREMENTAL-AUDIT] Diagnostic only. SMB change notifications are not a reliable publication source.");
        return report.Uncovered == 0 && report.InvalidRecords == 0 ? 0 : 1;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                               or InvalidDataException or FormatException or OverflowException)
    {
        Console.Error.WriteLine("[INCREMENTAL-AUDIT-ERROR] " + ex.Message);
        return 2;
    }
}

// Compare an existing catalog against a disposable full-scan reference.
if (args.Length > 0 && args[0] == "--incremental-reference")
{
    if (args.Length != 4 || args[2] != "--catalog")
    {
        Console.Error.WriteLine("Usage: --incremental-reference ROOT --catalog CATALOG");
        return 2;
    }
    try
    {
        Console.WriteLine("[INCREMENTAL-REFERENCE-START] Building disposable full-scan reference; production catalog is read-only.");
        var result = IncrementalReferenceComparison.Compare(args[1], args[3]);
        Console.WriteLine($"[INCREMENTAL-REFERENCE] previous={result.Previous} | reference={result.Reference} | added={result.Added} | removed={result.Removed} | modified={result.Modified} | unchanged={result.Unchanged} | fullScanRequired={result.FullScanRequired}");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                               or InvalidDataException or OverflowException)
    {
        Console.Error.WriteLine("[INCREMENTAL-REFERENCE-ERROR] " + ex.Message);
        return 2;
    }
}

// Read-only comparison of deleted/renamed paths with an existing catalog.
if (args.Length > 0 && args[0] == "--incremental-impact")
{
    if ((args.Length != 6 && args.Length != 8) || args[2] != "--journal" ||
        args[4] != "--catalog" || (args.Length == 8 && args[6] != "--last-records"))
    {
        Console.Error.WriteLine("Usage: --incremental-impact ROOT --journal FILE --catalog CATALOG [--last-records N]");
        return 2;
    }
    try
    {
        var last = args.Length == 8 ? int.Parse(args[7], System.Globalization.CultureInfo.InvariantCulture) : 0;
        var report = IncrementalCatalogImpact.Analyze(args[1], args[3], args[5], last,
            onImpact: Console.WriteLine);
        Console.WriteLine($"[INCREMENTAL-IMPACT] paths={report.Paths} | catalogEntries={report.CatalogEntries} | exact={report.ExactMatches} | descendants={report.DescendantMatches} | unmatched={report.Unmatched} | invalid={report.InvalidRecords} | fullScanRequired=True");
        Console.WriteLine("[INCREMENTAL-IMPACT] Read-only. No catalog changed; event history is not exhaustive.");
        return report.InvalidRecords == 0 ? 0 : 1;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                               or FormatException or OverflowException or InvalidDataException)
    {
        Console.Error.WriteLine("[INCREMENTAL-IMPACT-ERROR] " + ex.Message);
        return 2;
    }
}

// Experimental targeted enumeration: read-only, never publishes a catalog.
if (args.Length > 0 && args[0] == "--incremental-preview")
{
    if ((args.Length != 4 && args.Length != 6) || args[2] != "--journal" ||
        (args.Length == 6 && args[4] != "--last-records"))
    {
        Console.Error.WriteLine("Usage: --incremental-preview ROOT --journal FILE [--last-records N]");
        return 2;
    }
    try
    {
        var lastRecords = args.Length == 6 ? int.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture) : 0;
        Console.WriteLine($"[INCREMENTAL-PREVIEW-START] root={args[1]} | lastRecords={(lastRecords == 0 ? "all" : lastRecords.ToString())}");
        var result = IncrementalScanPreview.Analyze(args[1], args[3], lastRecords,
            onTarget: target => Console.WriteLine($"[INCREMENTAL-TARGET] {target}"),
            onProgress: (files, folders) => Console.WriteLine($"[INCREMENTAL-PROGRESS] files={files} | folders={folders}"));
        Console.WriteLine($"[INCREMENTAL-PREVIEW] directories={result.Directories} | files={result.Files} | folders={result.Folders} | skipped={result.Skipped} | invalid={result.InvalidRecords} | deletes={result.Deletes} | renames={result.Renames} | unknown={result.Unknown} | fullScanRequired={result.FullScanRequired}");
        Console.WriteLine("[INCREMENTAL-PREVIEW] Read-only experiment. No catalog was modified; a complete scan is still required.");
        return result.Skipped == 0 && result.InvalidRecords == 0 ? 0 : 1;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException)
    {
        Console.Error.WriteLine("[INCREMENTAL-PREVIEW-ERROR] " + ex.Message);
        return 2;
    }
}

// Analyze a diagnostic SMB log without treating it as a complete change history.
if (args.Length > 0 && args[0] == "--watch-report")
{
    if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
    {
        Console.Error.WriteLine("Usage: --watch-report FILE");
        return 2;
    }
    try
    {
        var report = SmbJournalReport.Analyze(args[1]);
        Console.WriteLine($"[WATCH-REPORT] notifications={report.Notifications} | files={report.DistinctPaths} | directories={report.AffectedDirectories} | starts={report.SessionStarts} | ends={report.SessionEnds} | errors={report.Errors} | malformed={report.MalformedLines}");
        Console.WriteLine("[WATCH-REPORT] Diagnostic only; no completeness guarantee; full scan required for reconciliation.");
        foreach (var directory in report.DirectoryCounts.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).Take(20))
            Console.WriteLine($"[WATCH-DIR] count={directory.Value} | {directory.Key}");
        return report.Errors == 0 && report.MalformedLines == 0 && report.SessionStarts == report.SessionEnds ? 0 : 1;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
    {
        Console.Error.WriteLine("[WATCH-REPORT-ERROR] " + ex.Message);
        return 2;
    }
}

// Experimental read-only SMB notification probe. Does not update catalogs.
if (args.Length > 0 && args[0] == "--watch-root")
{
    if ((args.Length != 2 && args.Length != 4) || string.IsNullOrWhiteSpace(args[1]) ||
        (args.Length == 4 && (args[2] != "--watch-log" || string.IsNullOrWhiteSpace(args[3]))))
    {
        Console.Error.WriteLine("Usage: --watch-root DIRECTORY [--watch-log FILE] (stop with Ctrl+C)");
        return 2;
    }

    try
    {
        var root = Path.GetFullPath(args[1]);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Directory not found: {root}");
        using var watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                           NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024
        };
        // Optional append-only diagnostic trail; never a complete change journal.
        using var log = args.Length == 4
            ? new StreamWriter(new FileStream(Path.GetFullPath(args[3]),
                FileMode.Append, FileAccess.Write, FileShare.Read, 4096,
                FileOptions.WriteThrough)) { AutoFlush = true }
            : null;
        var logLock = new object();
        long errors = 0;
        void Log(string action, string path)
        {
            if (log is null) return;
            lock (logLock)
            {
                try
                {
                    // Base64 makes arbitrary file paths unambiguous in tab-separated records.
                    var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(path));
                    log.WriteLine($"{DateTimeOffset.UtcNow:O}\t{action}\t{encoded}");
                }
                catch (IOException ex)
                {
                    Interlocked.Increment(ref errors);
                    Console.Error.WriteLine($"[WATCH-ERROR] Journal unavailable: {ex.Message}; full rescan required.");
                }
            }
        }
        long observed = 0;
        long retained = 0;
        // Time-based de-duplication is diagnostic only: it must never be used as
        // proof that no filesystem change happened.
        var recent = new Dictionary<(string Action, string Path), DateTimeOffset>();
        var sync = new object();
        var window = TimeSpan.FromMilliseconds(500);
        void Record(string action, string path)
        {
            Interlocked.Increment(ref observed);
            Log(action, path); // Keep raw notifications, including duplicates.
            var now = DateTimeOffset.UtcNow;
            var shouldPrint = false;
            lock (sync)
            {
                // Avoid unbounded growth during long-running monitoring.
                if (recent.Count > 10000)
                {
                    foreach (var expiredKey in recent.Where(item => now - item.Value > window)
                                 .Select(item => item.Key).ToArray())
                        recent.Remove(expiredKey);
                }
                var key = (action, OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path);
                if (!recent.TryGetValue(key, out var previous) || now - previous >= window)
                {
                    recent[key] = now;
                    shouldPrint = true;
                    Interlocked.Increment(ref retained);
                }
            }
            if (shouldPrint)
                Console.WriteLine($"[WATCH] {DateTimeOffset.Now:O} | {action} | {path}");
        }
        watcher.Created += (_, e) => Record("created", e.FullPath);
        watcher.Changed += (_, e) => Record("changed", e.FullPath);
        watcher.Deleted += (_, e) => Record("deleted", e.FullPath);
        watcher.Renamed += (_, e) => Record("renamed", $"{e.OldFullPath} => {e.FullPath}");
        watcher.Error += (_, e) =>
        {
            Interlocked.Increment(ref errors);
            Log("error", e.GetException().GetType().Name);
            Console.Error.WriteLine($"[WATCH-ERROR] {e.GetException().Message}; notifications may have been lost; a full rescan is required.");
        };
        using var shutdown = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            shutdown.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            Log("session-start", root);
            watcher.EnableRaisingEvents = true;
            Console.WriteLine($"[WATCH-START] {root} | observer only; no catalog changes; Ctrl+C to stop");
            try { await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token); }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
            watcher.EnableRaisingEvents = false;
            Log("session-end", root);
            Console.WriteLine($"[WATCH-SUMMARY] events={Interlocked.Read(ref observed)} | grouped={Interlocked.Read(ref retained)} | duplicates={Interlocked.Read(ref observed) - Interlocked.Read(ref retained)} | errors={Interlocked.Read(ref errors)} | optional raw log, no replay");
            return Volatile.Read(ref errors) == 0 ? 0 : 1;
        }
        finally { Console.CancelKeyPress -= handler; }
    }
    catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine("[WATCH-ERROR] " + ex.Message);
        return 2;
    }
}

// Isolated comparison: never overwrites a configured production catalog.
if (args.Length > 0 && args[0] == "--benchmark-root")
{
    if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
    {
        Console.Error.WriteLine("Usage: --benchmark-root DIRECTORY [--workers 1,16,50] [--repeats 3] [--exclude PATH]...");
        return 2;
    }
    try
    {
        var modes = new[] { 1, 16, 50 };
        var repeats = 3;
        var exclusions = new List<string>();
        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--workers" when i + 1 < args.Length:
                    modes = args[++i].Split(',', StringSplitOptions.TrimEntries)
                        .Select(int.Parse).ToArray();
                    break;
                case "--repeats" when i + 1 < args.Length:
                    repeats = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--exclude" when i + 1 < args.Length:
                    exclusions.Add(args[++i]);
                    break;
                default:
                    throw new ArgumentException("Unknown benchmark argument: " + args[i]);
            }
        }
        if (repeats is < 1 or > 20 || modes.Length is < 1 or > 10 ||
            modes.Any(mode => mode is < 1 or > 50))
            throw new ArgumentException("Use 1-20 repeats and 1-10 worker modes between 1 and 50.");
        // Reverse the mode order every other round to reduce warm-cache bias.
        var measurements = modes.Distinct().ToDictionary(mode => mode, _ => new List<long>());
        var entryCounts = new HashSet<int>();
        var temp = Path.Combine(Path.GetTempPath(), "wimf-benchmark-" + Guid.NewGuid().ToString("N") + ".txt.gz");
        try
        {
            for (var round = 0; round < repeats; round++)
            {
                foreach (var mode in round % 2 == 0 ? modes : modes.Reverse())
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var count = LegacyCatalogWriter.Build(args[1], temp, excludePaths: exclusions,
                        directoryPrefetch: mode);
                    watch.Stop();
                    entryCounts.Add(count);
                    measurements[mode].Add(watch.ElapsedMilliseconds);
                    Console.WriteLine($"[BENCH] workers={mode} | passage={round + 1}/{repeats} | total={watch.ElapsedMilliseconds} ms | elements={count}");
                    File.Delete(temp);
                }
            }
            var medians = new Dictionary<int, long>();
            foreach (var (mode, durations) in measurements)
            {
                var ordered = durations.OrderBy(value => value).ToArray();
                var median = ordered[(ordered.Length - 1) / 2];
                medians[mode] = median;
                Console.WriteLine($"[BENCH-SUMMARY] workers={mode} | min={ordered[0]} ms | mediane={median} ms | max={ordered[^1]} ms | moyenne={(long)durations.Average()} ms | essais={durations.Count}");
            }
            if (entryCounts.Count != 1)
            {
                Console.WriteLine("[BENCH-WARNING] Le nombre d’éléments varie entre les passages ; vérifier les modifications de l’arborescence avant de comparer les performances.");
                return 1;
            }
            // Prefer fewer workers when the median is within 5% of the fastest.
            // This is guidance only: never change the production configuration.
            var fastest = medians.Values.Min();
            var recommended = medians
                .Where(pair => pair.Value <= fastest * 1.05)
                .OrderBy(pair => pair.Key)
                .First();
            Console.WriteLine($"[BENCH-RECOMMENDATION] workers={recommended.Key} | mediane={recommended.Value} ms | meilleure_mediane={fastest} ms | tolerance=5% | appliquer_manuellement_dans_wimf.json");
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return 0;
    }
    catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
                               or InvalidDataException or OverflowException or FormatException)
    {
        Console.Error.WriteLine("Benchmark failed: " + ex.Message);
        return 2;
    }
}

// Standalone catalog-creation command: no web daemon is started.
if (args.Length > 0 && args[0] == "--build-root")
{
    const string usage = "Usage: wimfd --build-root DIRECTORY --output CATALOG.txt[.gz] [--name NAME] [--update-existing] [--exclude PATH]...";
    if (args.Length < 4 || args[2] != "--output")
    {
        Console.Error.WriteLine(usage);
        return 2;
    }
    try
    {
        string? name = null;
        var update = false;
        var exclusions = new List<string>();
        for (var i = 4; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--name" when name is null && i + 1 < args.Length &&
                    !args[i + 1].StartsWith("--", StringComparison.Ordinal):
                    name = args[++i];
                    break;
                case "--update-existing" when !update:
                    update = true;
                    break;
                case "--exclude" when i + 1 < args.Length &&
                    !args[i + 1].StartsWith("--", StringComparison.Ordinal):
                    exclusions.Add(args[++i]);
                    break;
                default:
                    throw new ArgumentException("Unknown, duplicated or incomplete option: " + args[i]);
            }
        }
        if (update)
        {
            var summary = LegacyCatalogUpdater.Update(args[1], args[3], name,
                onSkipped: Console.Error.WriteLine, excludePaths: exclusions);
            Console.WriteLine($"Updated {args[3]}: {summary.Total} entries; " +
                $"{summary.Added} added, {summary.Modified} modified, {summary.Removed} removed, " +
                $"{summary.Unchanged} unchanged; {summary.Skipped} paths skipped.");
        }
        else
        {
            var skipped = 0;
            var count = LegacyCatalogWriter.Build(args[1], args[3], name,
                onSkipped: warning => { skipped++; Console.Error.WriteLine(warning); },
                excludePaths: exclusions);
            Console.WriteLine($"Created {args[3]} with {count} entries; {skipped} paths skipped.");
        }
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                               or InvalidDataException or OverflowException)
    {
        Console.Error.WriteLine($"Catalog creation failed: {ex.Message}");
        return 2;
    }
}

DaemonOptions options;
try
{
    options = DaemonOptions.Parse(args);
}
catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
                           or System.Text.Json.JsonException or InvalidDataException)
{
    Console.Error.WriteLine($"Invalid server configuration: {ex.Message}");
    Console.Error.WriteLine("Usage: wimfd [--config config.json] [--catalog local.gz]... [--source network.gz]... [--cache-dir directory] [--refresh-seconds N] [--disable-file-actions] [--force-birthday-cake] [--tray-managed]");
    return 2;
}

var paths = options.Catalogs.ToList();
var sources = options.Sources.ToList();
var cacheDirectory = options.CacheDirectory;
var refreshSeconds = options.RefreshSeconds;
var enableFileActions = options.EnableFileActions;

LiveCatalogStore store;
try { store = new LiveCatalogStore(paths, sources, cacheDirectory,
    loadLocalFiles: options.CatalogMemory?.IdleUnloadEnabled == false,
    allowMissingCatalogs: options.Indexing?.Select(job => job.Output),
    tolerateUnavailableCatalogs: true); }
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                           or System.Text.DecoderFallbackException or ArgumentException)
{
    Console.Error.WriteLine($"Unable to load local catalogs: {ex.Message}");
    return 2;
}
if (options.CatalogMemory?.IdleUnloadEnabled == false)
{
    var initialRefresh = await store.RefreshAsync();
    Console.WriteLine($"Loaded {initialRefresh.CatalogCount} catalog(s).");
}
else Console.WriteLine("Catalog loading deferred until the web interface is opened.");

var builder = WebApplication.CreateSlimBuilder(Array.Empty<string>());
// Avoid verbose ASP.NET Core request/endpoint logs during frequent UI polling.
// Warnings and errors remain visible; explicit [BENCH] console output is unaffected.
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls("http://127.0.0.1:5080");
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, WimfJsonContext.Default));

// Compress sizeable JSON responses only when clients negotiate gzip or Brotli.
builder.Services.AddResponseCompression(compression =>
{
    compression.EnableForHttps = false; // HTTP localhost only.
    compression.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json"]);
});
var app = builder.Build();
app.UseResponseCompression();

// The UI and API are deliberately bound to localhost until authentication is implemented.
app.MapGet("/favicon.svg", () => Results.Content(WebUi.LogoSvg, "image/svg+xml; charset=utf-8"));
var engine = new SequentialSearchEngine();
var catalogLifecycle = options.CatalogMemory?.IdleUnloadEnabled == false
    ? null
    : new CatalogIdleLifecycle(store,
        TimeSpan.FromMinutes(options.CatalogMemory?.IdleTimeoutMinutes ?? 15));
if (catalogLifecycle is not null)
    app.Lifetime.ApplicationStopped.Register(() => catalogLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult());

void WakeCatalogs()
{
    if (catalogLifecycle is null) return;
    _ = catalogLifecycle.TouchAsync().ContinueWith(task =>
        Console.Error.WriteLine("[CATALOG-RELOAD] " + task.Exception?.GetBaseException().Message),
        TaskContinuationOptions.OnlyOnFaulted);
}
app.MapGet("/", () =>
{
    WakeCatalogs();
    return WebUi.GetPage(options.ForceBirthdayCake);
});


app.MapPost("/api/catalog-input", async () =>
{
    if (options.CatalogMemory?.ReloadOnSearchInput != false)
        WakeCatalogs();
    return Results.NoContent();
});
app.MapGet("/api/catalog-memory-state", () =>
    Results.Json(new CatalogMemoryState(catalogLifecycle?.IsUnloaded ?? false,
        catalogLifecycle?.IsLoading ?? false), WimfJsonContext.Default.CatalogMemoryState));


app.MapGet("/api/memory-diagnostics", () =>
    Results.Json(ServerMemoryDiagnostics.Capture(store.Snapshot), WimfJsonContext.Default.Sample));

// Explicit POST avoids a disruptive full collection during routine polling.
// Accessible only on the existing localhost-bound diagnostic server.
app.MapPost("/api/memory-diagnostics/collect", () =>
    Results.Json(ServerMemoryDiagnostics.CaptureAfterFullCollection(store.Snapshot),
        WimfJsonContext.Default.Comparison));

app.MapGet("/api/health", () =>
    Results.Json(new HealthResponse("ok", store.Snapshot.Length, enableFileActions), WimfJsonContext.Default.HealthResponse));

app.MapGet("/api/file-type-icon/{extension}", (string extension) =>
{
    var icon = FileTypeIcons.GetIcon(extension);
    return icon is null
        ? Results.NotFound()
        : Results.File(icon, "image/x-icon");
});

// Lightweight metadata persists when large catalog entry arrays are unloaded.
CatalogSummary[] lastCatalogSummaries = store.Snapshot.Select((c, index) =>
    new CatalogSummary(index, c.Header.Name, c.Header.RootPath, c.RecordCount,
        c.Diagnostics.Count, c.Header.CreatedAt)).ToArray();
app.MapGet("/api/catalogs", () =>
{
    // Read-only metadata queries must never wake catalogs or reset idle activity.
    // Only page opens and search-input changes count as user activity.
    var snapshot = store.Snapshot;
    if (!store.IsUnloaded)
    {
        var latest = snapshot.Select((c, index) =>
            new CatalogSummary(index, c.Header.Name, c.Header.RootPath, c.RecordCount,
                c.Diagnostics.Count, c.Header.CreatedAt)).ToArray();
        Volatile.Write(ref lastCatalogSummaries, latest);
    }
    return Results.Json(Volatile.Read(ref lastCatalogSummaries),
        WimfJsonContext.Default.CatalogSummaryArray);
});

app.MapPost("/api/search", async (HttpContext http, SearchRequest request, CancellationToken cancellationToken) =>
{
    if (request.Text is null || request.Text.Length > 1024 ||
        request.MaxResults < 0 || request.MaxResults > 10000 ||
        request.Extensions is { Length: > 100 } ||
        request.CatalogIndices is { Length: > 1000 })
        return Results.Json(new ErrorResponse("Invalid query or result limit."),
            WimfJsonContext.Default.ErrorResponse, statusCode: 400);

    try
    {
        var query = new SearchQuery
        {
            Text = request.Text,
            UseRegex = request.UseRegex,
            IgnoreSeparators = request.IgnoreSeparators,
            RegexTimeout = TimeSpan.FromMilliseconds(100),
            Extensions = request.Extensions ?? [],
            MinSize = request.MinSize,
            MaxSize = request.MaxSize,
            ModifiedFrom = request.ModifiedFrom,
            ModifiedTo = request.ModifiedTo,
            MaxResults = request.MaxResults == 0 ? 0 : request.MaxResults + 1
        };
        using var searchLease = catalogLifecycle is null ? null :
            await catalogLifecycle.BeginSearchAsync(cancellationToken);
        var snapshot = store.Snapshot;
        var indices = CatalogSelection.Resolve(snapshot.Length, request.CatalogIndices);
        var selected = indices.Select(index => snapshot[index]).ToArray();
        var searchTimer = System.Diagnostics.Stopwatch.StartNew();
        var results = engine.Search(selected, query, cancellationToken);
        searchTimer.Stop();
        http.Response.Headers["Server-Timing"] = $"search;dur={searchTimer.Elapsed.TotalMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}";
        var truncated = results.Count > request.MaxResults;
        var hits = results.Take(request.MaxResults).Select(r => new SearchHit(
            r.Catalog.Header.Name, indices[Array.IndexOf(selected, r.Catalog)], r.Entry.Id, r.Entry.Name, r.FullPath,
            r.Entry.Extension, r.Entry.Size, r.Entry.ModifiedAt)).ToArray();
        return Results.Json(new SearchResponse(hits.Length, hits, truncated),
            WimfJsonContext.Default.SearchResponse);
    }
    catch (ArgumentException ex)
    {
        return Results.Json(new ErrorResponse(ex.Message),
            WimfJsonContext.Default.ErrorResponse, statusCode: 400);
    }
    catch (RegexMatchTimeoutException)
    {
        return Results.Json(new ErrorResponse("Regex evaluation timed out."),
            WimfJsonContext.Default.ErrorResponse, statusCode: 422);
    }
    catch (InvalidDataException)
    {
        return Results.Json(new ErrorResponse("A matching catalog entry has an invalid path."),
            WimfJsonContext.Default.ErrorResponse, statusCode: 422);
    }
});

// File actions are explicitly opt-in: a website must not be allowed to activate
// local file operations merely because the daemon listens on 127.0.0.1.
app.MapPost("/api/file-action", (HttpContext context, FileActionRequest request) =>
{
    IResult Error(string message, int status) =>
        Results.Json(new ErrorResponse(message), WimfJsonContext.Default.ErrorResponse, statusCode: status);

    if (!enableFileActions)
        return Error("File actions are disabled. Restart without --disable-file-actions.", 403);

    var origin = context.Request.Headers.Origin.ToString();
    var site = context.Request.Headers["Sec-Fetch-Site"].ToString();
    if (!string.Equals(origin, "http://127.0.0.1:5080", StringComparison.Ordinal) ||
        !string.Equals(context.Request.Host.Value, "127.0.0.1:5080", StringComparison.OrdinalIgnoreCase) ||
        (site.Length > 0 && site != "same-origin") ||
        context.Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
        return Error("File action requests must originate from the local Wimf page.", 403);

    if (request.Action is not ("open" or "reveal" or "copy"))
        return Error("Unknown file action.", 400);

    try
    {
        var path = LocalFileActions.Resolve(store.Snapshot, request.CatalogIndex, request.Id, request.FullPath);
        LocalFileActions.Execute(request.Action, path);
        return Results.Json(new FileActionResponse(true), WimfJsonContext.Default.FileActionResponse);
    }
    catch (FileNotFoundException) { return Error("The indexed file is no longer accessible.", 404); }
    catch (InvalidOperationException) { return Error("Search results are stale or opening this file is prohibited.", 409); }
    catch (PlatformNotSupportedException) { return Error("This action is unavailable in the current desktop session.", 501); }
    catch (Exception ex) when (ex is ArgumentException or InvalidDataException or UnauthorizedAccessException
                               or IOException or System.ComponentModel.Win32Exception)
    {
        return Error("Unable to perform this action on the indexed file.", 422);
    }
});

app.MapGet("/api/refresh-status", () =>
    Results.Json(new RefreshStatus(store.Snapshot.Length, store.SourceCount, store.LastRefresh,
        store.LastSummary?.FailedSources ?? 0, store.LastSummary?.FailedLocalCatalogs ?? 0),
        WimfJsonContext.Default.RefreshStatus));

app.MapPost("/api/refresh", async (CancellationToken cancellationToken) =>
{
    var summary = await store.RefreshAsync(cancellationToken);
    return Results.Json(summary, WimfJsonContext.Default.RefreshSummary);
});

if ((sources.Count > 0 || paths.Count > 0) && refreshSeconds > 0)
{
    var shutdown = app.Lifetime.ApplicationStopping;
    _ = Task.Run(async () =>
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(refreshSeconds));
            while (await timer.WaitForNextTickAsync(shutdown))
            {
                try { await store.RefreshAsync(shutdown); }
                catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { break; }
                catch (Exception ex) { Console.Error.WriteLine($"Catalog refresh failed: {ex.Message}"); }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
    }, shutdown);
}

CatalogIndexScheduler? indexingScheduler = null;
app.MapGet("/api/indexing-status", () =>
    Results.Json(indexingScheduler?.Status ?? [], WimfJsonContext.Default.IndexingJobStatusArray));

app.MapGet("/api/indexing-history", () =>
    Results.Json(indexingScheduler?.History ?? [], WimfJsonContext.Default.IndexingRunHistoryEntryArray));

// Count entries only once per immutable catalog snapshot, not every UI poll.
var catalogStatistics = new System.Runtime.CompilerServices.ConditionalWeakTable<
    Wimf.Catalog.Catalog, CatalogExtensionStatistics>();
// Catalog management includes configured local files that have not yet been generated.
app.MapGet("/api/catalog-management", () =>
{
    var jobs = indexingScheduler?.Status ?? [];
    var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    var managed = store.LocalCatalogStatus.Select(entry =>
    {
        var catalog = entry.Catalog;
        var index = Array.FindIndex(jobs, job => comparer.Equals(job.Output, entry.Path));
        var job = index >= 0 ? jobs[index] : null;
        return new ManagedCatalogStatus(
            catalog?.Header.Name ?? job?.Name ?? Path.GetFileName(entry.Path),
            entry.Path, catalog?.Header.RootPath ?? job?.Root,
            catalog is null ? null : catalogStatistics.GetValue(catalog, CatalogExtensionStatistics.Calculate).FileCount,
            catalog?.Header.CreatedAt, catalog is not null,
            index >= 0 ? index : null, job?.State, job?.NextUpdate, job?.LastError,
            job?.CanStart ?? false, job?.ScannedEntries ?? 0, job?.StartedAt,
            job?.LastSuccess, job?.SkippedEntries ?? 0, job?.SkippedDetails,
            job?.EffectiveExclusions, job?.QueuedAt, job?.ScanMetrics, job?.EnumeratedDirectories ?? 0,
            CatalogLockMarker.Read(entry.Path),
            catalog is null ? null : catalogStatistics.GetValue(catalog, CatalogExtensionStatistics.Calculate).Extensions);
    }).ToArray();
    return Results.Json(managed, WimfJsonContext.Default.ManagedCatalogStatusArray);
});

// Download the complete server-side TXT report, without exposing arbitrary disk paths.
app.MapGet("/api/indexing/{index:int}/skipped-report", (int index) =>
{
    var stream = indexingScheduler?.OpenSkippedReport(index);
    if (stream is null) return Results.NotFound();
    return Results.File(stream, "text/plain; charset=utf-8",
        $"wimf2-elements-ignores-{index}.txt");
});

// Manual scans are restricted to requests originating from the local Wimf page.
app.MapPost("/api/indexing/{index:int}/start", (HttpContext context, int index) =>
{
    if (indexingScheduler is null || index < 0 || index >= indexingScheduler.Status.Length)
        return Results.NotFound();
    var origin = context.Request.Headers.Origin.ToString();
    var site = context.Request.Headers["Sec-Fetch-Site"].ToString();
    if (!string.Equals(origin, "http://127.0.0.1:5080", StringComparison.Ordinal) ||
        !string.Equals(context.Request.Host.Value, "127.0.0.1:5080", StringComparison.OrdinalIgnoreCase) ||
        (site.Length > 0 && site != "same-origin"))
        return Results.StatusCode(403);
    return indexingScheduler.TryQueueManual(index, app.Lifetime.ApplicationStopping)
        ? Results.StatusCode(202) : Results.StatusCode(409);
});

// Explicit user action only: queue each configured full scan through the existing scheduler.
app.MapPost("/api/indexing/start-all", (HttpContext context) =>
{
    if (indexingScheduler is null) return Results.NotFound();
    var origin = context.Request.Headers.Origin.ToString();
    var site = context.Request.Headers["Sec-Fetch-Site"].ToString();
    if (!string.Equals(origin, "http://127.0.0.1:5080", StringComparison.Ordinal) ||
        !string.Equals(context.Request.Host.Value, "127.0.0.1:5080", StringComparison.OrdinalIgnoreCase) ||
        (site.Length > 0 && site != "same-origin"))
        return Results.StatusCode(403);
    var accepted = 0;
    for (var i = 0; i < indexingScheduler.Status.Length; i++)
        if (indexingScheduler.TryQueueManual(i, app.Lifetime.ApplicationStopping))
            accepted++;
    return Results.Text($"{{\"accepted\":{accepted},\"total\":{indexingScheduler.Status.Length}}}", "application/json");
});

app.MapPost("/api/indexing/{index:int}/cancel", (HttpContext context, int index) =>
{
    if (indexingScheduler is null || index < 0 || index >= indexingScheduler.Status.Length)
        return Results.NotFound();
    var origin = context.Request.Headers.Origin.ToString();
    var site = context.Request.Headers["Sec-Fetch-Site"].ToString();
    if (!string.Equals(origin, "http://127.0.0.1:5080", StringComparison.Ordinal) ||
        !string.Equals(context.Request.Host.Value, "127.0.0.1:5080", StringComparison.OrdinalIgnoreCase) ||
        (site.Length > 0 && site != "same-origin"))
        return Results.StatusCode(403);
    return indexingScheduler.TryCancel(index) ? Results.StatusCode(202) : Results.StatusCode(409);
});

// Monitoring is opt-in and observational: indexing continues to use full scans.
var smbWatchers = new System.Collections.Concurrent.ConcurrentDictionary<string, SmbBackgroundWatcher>(
    OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
var unavailableWatchRoots = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(
    OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
// Watch every indexed root, including local Windows disks. Explicit watchRoots
// are retained for roots without a scheduled indexing job.
var watchComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
var watchedRoots = (options.WatchRoots ?? [])
    .Concat(options.Indexing?.Select(job => job.Root) ?? [])
    .Distinct(watchComparer);
foreach (var root in watchedRoots)
{
    try
    {
        var watchStateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wimf2", "smb-watch");
        // Only compact before the watcher opens the journal. A root shared by
        // multiple configured catalogs is left untouched because their cursor
        // positions may differ.
        var consumers = options.Indexing?.Where(j => watchComparer.Equals(j.Root, root)).ToArray() ?? [];
        if (consumers.Length == 1)
        {
            var journal = SmbBackgroundWatcher.GetJournalPath(root, watchStateDirectory);
            try
            {
                if (OfflineJournalCompaction.TryCompact(consumers[0].Output, journal))
                    Console.WriteLine($"[JOURNAL-COMPACT] {root} | acknowledged prefix removed");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"[JOURNAL-COMPACT-ERROR] {root}: {ex.Message}");
            }
        }
        var watcher = new SmbBackgroundWatcher(root, watchStateDirectory);
        watcher.Start();
        smbWatchers.TryAdd(root, watcher);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
    {
        unavailableWatchRoots.TryAdd(root, 0);
        Console.Error.WriteLine($"[SMB-WATCH-ERROR] Cannot monitor '{root}': {ex.Message}; full scans remain enabled.");
    }
}
app.Lifetime.ApplicationStopping.Register(() =>
{
    foreach (var watcher in smbWatchers.Values) watcher.Dispose();
});

// Retry failed or initially unavailable watchers every minute.
// A recovery only resumes notification recording; full scans keep their JSON cadence.
_ = Task.Run(async () =>
{
    try
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(app.Lifetime.ApplicationStopping))
        {
            foreach (var watcher in smbWatchers.Values)
                watcher.TryRecover();
            foreach (var root in unavailableWatchRoots.Keys)
            {
                try
                {
                    var stateDirectory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Wimf2", "smb-watch");
                    var watcher = new SmbBackgroundWatcher(root, stateDirectory);
                    watcher.Start();
                    if (smbWatchers.TryAdd(root, watcher))
                        unavailableWatchRoots.TryRemove(root, out _);
                    else watcher.Dispose();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    Console.Error.WriteLine($"[SMB-WATCH-RETRY] {root}: {ex.Message}");
                }
            }
        }
    }
    catch (OperationCanceledException) when (app.Lifetime.ApplicationStopping.IsCancellationRequested) { }
});

if (options.Indexing is { Length: > 0 })
{
    var shutdown = app.Lifetime.ApplicationStopping;
    var incrementalMetrics = new System.Collections.Concurrent.ConcurrentDictionary<string,
        (int Events, int Distinct, int Paths)>(watchComparer);
    var historyFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Wimf2", "indexing-history.json");
    var fullScanEpochs = new System.Collections.Concurrent.ConcurrentDictionary<string, long>(watchComparer);
    indexingScheduler = new CatalogIndexScheduler(options.Indexing,
        beforeFull: job =>
        {
            var watcher = smbWatchers.Values.FirstOrDefault(w => watchComparer.Equals(w.Root, job.Root));
            if (watcher is null) return 0;
            fullScanEpochs[job.Output] = watcher.ContinuityEpoch;
            return IncrementalJournalPublisher.SnapshotLength(watcher.JournalPath);
        },
        afterFull: (job, boundary) =>
        {
            var watcher = smbWatchers.Values.FirstOrDefault(w => watchComparer.Equals(w.Root, job.Root));
            if (watcher is not null &&
                IncrementalJournalPublisher.AcknowledgeFullScan(job.Output, watcher.JournalPath, boundary) &&
                fullScanEpochs.TryRemove(job.Output, out var epoch))
                watcher.MarkFullScanReconciled(epoch);
        },
        incremental: async (job, token) =>
        {
            incrementalMetrics.TryRemove(job.Output, out _);
            var watcher = smbWatchers.Values.FirstOrDefault(w =>
                watchComparer.Equals(w.Root, job.Root) && w.CanPublishIncremental);
            if (watcher is null || !File.Exists(job.Output)) return false;
            // The current serializer intentionally refuses a staging file inside
            // the indexed root (notably C:\\). Keep periodic full scans there.
            var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(job.Root));
            var outputPath = Path.GetFullPath(job.Output);
            var pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var rootPrefix = Path.EndsInDirectorySeparator(rootPath)
                ? rootPath : rootPath + Path.DirectorySeparatorChar;
            if (outputPath.StartsWith(rootPrefix, pathComparison))
                return false;
            var result = await Task.Run(() => IncrementalJournalPublisher.Publish(
                job.Root, job.Output, watcher.JournalPath, token, job.Exclude,
                extensions: job.Extensions), token);
            incrementalMetrics[job.Output] = (result.EventRecords, result.DistinctPaths, result.Hints);
            if (result.Changed)
                Console.WriteLine($"[INCREMENTAL] {job.Root} | events={result.EventRecords} | distinct={result.DistinctPaths} | paths={result.Hints} | elapsed={result.ElapsedMilliseconds} ms | entries={result.Entries}");
            return result.Changed;
        },
        afterSuccess: async (job, token) =>
        {
            await store.RefreshLocalCatalogAsync(job.Output, token);
        },
        incrementalMetrics: job => incrementalMetrics.TryRemove(job.Output, out var metrics)
            ? metrics : (0, 0, 0),
        historyStore: new IndexingHistoryStore(historyFile));
    var scheduler = indexingScheduler;
    _ = Task.Run(async () =>
    {
        try { await scheduler.RunAsync(shutdown); }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (Exception ex) { Console.Error.WriteLine($"Indexing scheduler stopped: {ex.Message}"); }
    }, shutdown);
    Console.WriteLine($"Scheduled indexing configured for {options.Indexing.Length} catalog(s).");
}

// Pending notification hints, not yet acknowledged by an incremental publisher.
app.MapGet("/api/smb-pending", () =>
{
    var lines = smbWatchers.Values.Select(w =>
    {
        var job = options.Indexing?.FirstOrDefault(j => watchComparer.Equals(j.Root, w.Root));
        try
        {
            var pending = job is null ? null : IncrementalJournalPublisher.Pending(job.Output, w.JournalPath);
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(w.Root)) + "\t" +
                (pending?.Events ?? w.Observed).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\t" +
                (pending?.Directories ?? w.AffectedDirectories.Length).ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "\t" + (w.WatchFailed ? "error" : w.Recovered ? "recovered" : "active") + "\t" + w.Errors;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.Error.WriteLine($"[SMB-PENDING-ERROR] {w.Root}: {ex.Message}");
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(w.Root)) + "\t0\t0\terror";
        }
    })
        .Concat(unavailableWatchRoots.Keys.Select(root =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(root)) + "\t0\t0\terror"));
    return Results.Text(string.Join("\n", lines), "text/plain; charset=utf-8");
});

// Read-only size and acknowledgement diagnostics; no journal contents are modified.
app.MapGet("/api/smb-journal-storage", () =>
{
    var lines = smbWatchers.Values.Select(w =>
    {
        var job = options.Indexing?.FirstOrDefault(j => watchComparer.Equals(j.Root, w.Root));
        if (job is null) return null;
        try
        {
            var storage = JournalStorageStatus.Read(job.Output, w.JournalPath);
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(w.Root)) + "\t" +
                storage.Bytes + "\t" + storage.AcknowledgedBytes + "\t" +
                storage.PendingBytes + "\t" + (storage.CursorValid ? "valid" : "invalid");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[JOURNAL-STORAGE-ERROR] {w.Root}: {ex.Message}");
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(w.Root)) + "\t0\t0\t0\terror";
        }
    }).Where(line => line is not null);
    return Results.Text(string.Join("\n", lines), "text/plain; charset=utf-8");
});

app.MapGet("/api/smb-watch", () =>
{
    var lines = smbWatchers.Values.Select(w =>
        $"root={w.Root} | events={w.Observed} | directories={w.AffectedDirectories.Length} | errors={w.Errors} | fullScanRequired={w.NeedsFullScan}");
    return Results.Text(string.Join("\n", lines), "text/plain; charset=utf-8");
});

// Lightweight auto-refreshing diagnostic page. No indexing actions exposed.
app.MapGet("/smb-watch", () => Results.Content("""
<!doctype html>
<html lang="fr">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>WIMF2 — Surveillance SMB</title>
<style>
body { font: 16px system-ui, sans-serif; max-width: 950px; margin: 3rem auto; padding: 0 1rem; }
pre { white-space: pre-wrap; overflow-wrap: anywhere; background: #f3f4f6; padding: 1rem; border-radius: 8px; }
small { color: #555; } .error { color: #a00; }
</style></head>
<body>
<h1>Surveillance SMB</h1>
<p>État de diagnostic : les notifications ne garantissent pas l’exhaustivité. Un scan complet reste nécessaire.</p>
<pre id="state" role="status" aria-live="polite">Chargement…</pre>
<small id="updated">Actualisation toutes les 2 secondes</small>
<script>
async function refresh() {
  try {
    const response = await fetch('/api/smb-watch', { cache: 'no-store' });
    if (!response.ok) throw new Error('HTTP ' + response.status);
    const body = await response.text();
    document.getElementById('state').textContent = body || 'Aucune racine surveillée.';
    document.getElementById('state').className = '';
    document.getElementById('updated').textContent = 'Dernière actualisation : ' + new Date().toLocaleTimeString('fr-FR');
  } catch (error) {
    document.getElementById('state').className = 'error';
    document.getElementById('updated').textContent = 'Actualisation impossible : ' + error.message;
  }
}
refresh();
setInterval(refresh, 2000);
</script></body></html>
""", "text/html; charset=utf-8"));

Console.WriteLine($"Wimf daemon loaded {store.Snapshot.Length} catalog(s). Listening on http://127.0.0.1:5080");
if (enableFileActions) Console.WriteLine("Local file actions enabled (requires an interactive desktop session).");
// Only the tray-managed child listens on redirected stdin for graceful exit.
// The ordinary server mode never relies on console input.
if (options.TrayManaged)
{
    _ = Task.Run(() =>
    {
        while (true)
        {
            var command = Console.ReadLine();
            if (command is null) return;
            if (command == "quit")
            {
                app.Lifetime.StopApplication();
                return;
            }
        }
    });
}
await app.RunAsync();
return 0;
