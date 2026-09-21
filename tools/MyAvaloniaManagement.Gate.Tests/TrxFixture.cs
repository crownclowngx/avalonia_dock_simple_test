using System.Xml.Linq;

namespace MyAvaloniaManagement.Gate.Tests;

/// <summary>按实际 VSTest/xUnit 的最小完整报告构造输入；测试分别破坏字段，避免无效夹具导致负向断言假通过。</summary>
internal static class TrxFixture
{
    internal static XDocument Create(int passed = 1, int failed = 0, int skipped = 0, bool namespaced = true)
    {
        XNamespace ns = namespaced ? "http://microsoft.com/schemas/VisualStudio/TeamTest/2010" : "";
        var outcomes = Enumerable.Repeat("Passed", passed).Concat(Enumerable.Repeat("Failed", failed))
            .Concat(Enumerable.Repeat("NotExecuted", skipped)).ToArray();
        return new XDocument(new XElement(ns + "TestRun",
            new XElement(ns + "Times", new XAttribute("start", "2026-09-21T01:00:00.0000000+00:00"),
                new XAttribute("finish", "2026-09-21T01:00:02.0000000+00:00")),
            new XElement(ns + "Results", outcomes.Select((outcome, index) => new XElement(ns + "UnitTestResult",
                new XAttribute("executionId", $"execution-{index}"), new XAttribute("testName", $"case-{index}"),
                new XAttribute("outcome", outcome), new XAttribute("duration", TimeSpan.FromMilliseconds(index + 1).ToString("c"))))),
            new XElement(ns + "ResultSummary", new XAttribute("outcome", failed == 0 ? "Completed" : "Failed"),
                new XElement(ns + "Counters", new XAttribute("total", outcomes.Length),
                    new XAttribute("executed", passed + failed), new XAttribute("passed", passed),
                    new XAttribute("failed", failed), new XAttribute("notExecuted", skipped)))));
    }
}
