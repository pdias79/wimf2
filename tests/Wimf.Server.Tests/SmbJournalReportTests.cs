using System.Text;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class SmbJournalReportTests
{
    [Fact]
    public void CountsRawEventsAndDirectoriesWithoutAssumingCompleteness()
    {
        var file = Path.GetTempFileName();
        try
        {
            static string Line(string type, string path) =>
                "2026-10-18T09:00:00.0000000+00:00\t" + type + "\t" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(path));
            File.WriteAllLines(file,
            [
                Line("session-start", "root"),
                Line("created", Path.Combine("root", "one.txt")),
                Line("created", Path.Combine("root", "one.txt")),
                Line("renamed", Path.Combine("root", "one.txt") + " => " + Path.Combine("other", "two.txt")),
                Line("error", "InternalBufferOverflowException"),
                Line("session-end", "root"),
                "broken"
            ]);
            var report = SmbJournalReport.Analyze(file);
            Assert.Equal(3, report.Notifications);
            Assert.Equal(2, report.DistinctPaths);
            Assert.Equal(2, report.AffectedDirectories);
            Assert.Equal(1, report.SessionStarts);
            Assert.Equal(1, report.SessionEnds);
            Assert.Equal(1, report.Errors);
            Assert.Equal(1, report.MalformedLines);
        }
        finally { File.Delete(file); }
    }
}
