using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class SmbSessionAuditTests
{
    [Fact]
    public void DetectsInterruptedAndBalancedSessionsWithoutClaimingCompleteness()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(file, [
                "S\tstart\t2026-10-18T12:00:00.0000000+00:00",
                "S\terror\t2026-10-18T12:00:01.0000000+00:00"
            ]);
            var interrupted = SmbSessionAudit.Analyze(file);
            Assert.Equal(1, interrupted.OpenSessions);
            Assert.Equal(1, interrupted.Errors);
            Assert.True(interrupted.FullScanRequired);
            File.AppendAllText(file, "S\tend\t2026-10-18T12:00:02.0000000+00:00" + Environment.NewLine);
            var closed = SmbSessionAudit.Analyze(file);
            Assert.Equal(0, closed.OpenSessions);
            Assert.Equal(1, closed.Starts);
            Assert.Equal(1, closed.Ends);
            Assert.True(closed.FullScanRequired);
        }
        finally { File.Delete(file); }
    }
}
