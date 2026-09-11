using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace MyAvaloniaManagement.Gate;

internal sealed record TestCounts(int Passed, int Failed, int Skipped);

internal static class TestEvidenceReader
{
    public static string FindCoverageReport(string directory)
    {
        // VSTest 同时保存 collector 输出及 TRX 的 In/<machine> 附件副本。
        // 只合并内容完全一致的副本；缺失或多份不同报告仍拒绝。
        var reports = Directory.GetFiles(directory, "coverage.cobertura.xml", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .GroupBy(path =>
            {
                using var stream = File.OpenRead(path);
                return Convert.ToHexString(SHA256.HashData(stream));
            }, StringComparer.Ordinal)
            .ToArray();
        if (reports.Length != 1)
        {
            throw new GateFailureException($"测试必须产生一份唯一内容的覆盖率报告，实际为 {reports.Length}：{directory}。");
        }
        return reports[0].First();
    }

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

    public static CoverageEvidence ReadHostCoverage(string path)
    {
        var document = XDocument.Load(path);
        var packages = document.Descendants("package").ToArray();
        if (packages.Length != 1 || packages[0].Attribute("name")?.Value != "MyAvaloniaManagement" ||
            !packages[0].Descendants("line").Any())
        {
            throw new GateFailureException("Host 覆盖率必须只包含非空的 MyAvaloniaManagement 主程序集。");
        }
        return ReadCoverage(path);
    }

    private static int Attribute(XElement element, string name) =>
        int.Parse(element.Attribute(name)?.Value ?? "0", CultureInfo.InvariantCulture);

    private static double DoubleAttribute(XElement element, string name) =>
        double.Parse(element.Attribute(name)?.Value ?? "0", CultureInfo.InvariantCulture);
}
