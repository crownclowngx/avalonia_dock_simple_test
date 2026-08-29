using System.Globalization;
using System.Xml.Linq;

namespace MyAvaloniaManagement.Gate;

internal sealed record TestCounts(int Passed, int Failed, int Skipped);

internal static class TestEvidenceReader
{
    public static TestCounts ReadTrx(string path)
    {
        var document = XDocument.Load(path);
        var counters = document.Descendants().SingleOrDefault(element => element.Name.LocalName == "Counters") ??
            throw new GateFailureException($"TRX 缺少 Counters：{path}。");
        return new(
            Attribute(counters, "passed"),
            Attribute(counters, "failed"),
            Attribute(counters, "notExecuted"));
    }

    public static CoverageEvidence ReadCoverage(string path)
    {
        var root = XDocument.Load(path).Root ??
            throw new GateFailureException($"Cobertura 为空：{path}。");
        return new(
            Math.Round(100 * DoubleAttribute(root, "line-rate"), 2),
            Math.Round(100 * DoubleAttribute(root, "branch-rate"), 2));
    }

    private static int Attribute(XElement element, string name) =>
        int.Parse(element.Attribute(name)?.Value ?? "0", CultureInfo.InvariantCulture);

    private static double DoubleAttribute(XElement element, string name) =>
        double.Parse(element.Attribute(name)?.Value ?? "0", CultureInfo.InvariantCulture);
}
