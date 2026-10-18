using System.Text;
using Wimf.Catalog;

namespace Wimf.Server;

/// <summary>Controlled, standalone SMB coverage experiment; does not mutate production catalogs.</summary>
public static class SmbControlledExperiment
{
    public static async Task<int> RunAsync(string rootPath, CancellationToken stop)
    {
        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var temp = Path.Combine(Path.GetTempPath(), "wimf-controlled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var baseline = Path.Combine(temp, "baseline.txt.gz");
        var journal = Path.Combine(temp, "events.tsv");
        try
        {
            Console.WriteLine("[CONTROLLED-BASELINE] Scanning SMB root before surveillance; do not modify the share yet.");
            LegacyCatalogWriter.Build(root, baseline);
            Console.WriteLine("[CONTROLLED-BASELINE] Complete. Starting watcher...");
            var sync = new object();
            var errors = 0;
            long events = 0;
            using (var log = new StreamWriter(new FileStream(journal, FileMode.CreateNew,
                FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough), new UTF8Encoding(false))
                { AutoFlush = true })
            using (var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024
            })
            {
                void Record(string action, string path)
                {
                    lock (sync)
                    {
                        try
                        {
                            log.WriteLine("E\t" + action + "\t" +
                                Convert.ToBase64String(Encoding.UTF8.GetBytes(path)));
                            log.Flush();
                            events++;
                        }
                        catch (IOException ex)
                        {
                            errors++;
                            Console.Error.WriteLine("[CONTROLLED-ERROR] Journal write: " + ex.Message);
                        }
                    }
                }
                watcher.Created += (_, e) => Record("created", e.FullPath);
                watcher.Changed += (_, e) => Record("changed", e.FullPath);
                watcher.Deleted += (_, e) => Record("deleted", e.FullPath);
                watcher.Renamed += (_, e) =>
                {
                    Record("renamed-from", e.OldFullPath);
                    Record("renamed-to", e.FullPath);
                };
                watcher.Error += (_, e) =>
                {
                    lock (sync) errors++;
                    Console.Error.WriteLine("[CONTROLLED-ERROR] Watcher: " + e.GetException().Message);
                };
                watcher.EnableRaisingEvents = true;
                Console.WriteLine("[CONTROLLED-READY] Make test changes from another PC now; press Ctrl+C to stop.");
                try { await Task.Delay(Timeout.InfiniteTimeSpan, stop); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                watcher.EnableRaisingEvents = false;
                lock (sync) log.Flush();
            }
            Console.WriteLine($"[CONTROLLED-STOP] events={events} | errors={errors} | auditing against a complete reference...");
            var result = IncrementalCoverageAudit.Analyze(root, baseline, journal,
                onUncovered: path => Console.WriteLine("[CONTROLLED-UNCOVERED] " + path));
            Console.WriteLine($"[CONTROLLED-RESULT] changes={result.Changes} | covered={result.Covered} | uncovered={result.Uncovered} | invalid={result.InvalidRecords} | watcherErrors={errors} | fullScanRequired=True");
            Console.WriteLine("[CONTROLLED-RESULT] Diagnostic only; neither complete notification coverage nor incremental publication is guaranteed.");
            return errors == 0 && result.InvalidRecords == 0 && result.Uncovered == 0 ? 0 : 1;
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
