namespace Wimf.Server;

/// <summary>Experimental safe refresh to a NEW catalog file; never replaces production.
/// Any mismatch requires a full rebuild, never a partial candidate publication.</summary>
public static class IncrementalSafeRefresh
{
    public sealed record Result(string Mode, int Entries, string Output);

    public static Result Run(string root, string source, string journal, string output)
    {
        try
        {
            var candidate = IncrementalCandidateComparison.Compare(root, source, journal,
                candidateOutput: output);
            if (!candidate.Equal)
                throw new InvalidDataException("Incremental candidate did not match reference.");
            return new Result("candidate-validated", candidate.Candidate, Path.GetFullPath(output));
        }
        catch (InvalidDataException ex) when (
            ex.Message == "Candidate differs from full reference; no candidate file was published.")
        {
            var full = IncrementalFallback.BuildFull(root, source, output);
            return new Result("full-fallback", full.Entries, full.Output);
        }
    }
}
