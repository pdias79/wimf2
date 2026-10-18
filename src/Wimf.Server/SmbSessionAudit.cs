namespace Wimf.Server;

/// <summary>Session boundary diagnostics, not evidence of exhaustive SMB coverage.</summary>
public static class SmbSessionAudit
{
    public sealed record Result(int Starts, int Ends, int Errors, int OpenSessions,
        int Malformed, string? LastStartUtc, bool FullScanRequired);

    public static Result Analyze(string journalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);
        int starts = 0, ends = 0, errors = 0, open = 0, malformed = 0;
        string? lastStart = null;
        foreach (var line in File.ReadLines(journalPath))
        {
            if (!line.StartsWith("S\t", StringComparison.Ordinal)) continue;
            var fields = line.Split('\t');
            if (fields.Length != 3 || !DateTimeOffset.TryParseExact(fields[2], "O",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out _))
            {
                malformed++;
                continue;
            }
            switch (fields[1])
            {
                case "start": starts++; open++; lastStart = fields[2]; break;
                case "end": ends++; if (open > 0) open--; else malformed++; break;
                case "error": errors++; break;
                default: malformed++; break;
            }
        }
        // Stop/start gaps, legacy logs, network failures and unobserved changes
        // always require a full reconciliation, even with balanced sessions.
        return new Result(starts, ends, errors, open, malformed, lastStart, true);
    }
}
