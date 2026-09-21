using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;
using System.Xml;

namespace MyAvaloniaManagement.Gate;

internal sealed record TestCounts(int Total, int Executed, int Passed, int Failed, int Skipped, int Other);
internal sealed record TestCaseEvidence(string Name, string Outcome, double Milliseconds);
internal sealed record TestRunEvidence(string Outcome, TestCounts Counts, string Sha256, double Milliseconds,
    TestCaseEvidence[] SlowTests, bool HasAbnormalCounters);

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

    /// <summary>
    /// 按本仓 VSTest 的运行结构读取一次不可变字节快照。计数必须与结果明细一致，
    /// 缺字段不再补零；是否允许失败/跳过由 Gate 政策决定，reader 保留真实结果供失败证据使用。
    /// </summary>
    public static TestRunEvidence ReadTrx(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var stream = new MemoryStream(bytes);
            var root = XDocument.Load(stream).Root ?? throw new FormatException("根节点缺失");
            if (root.Name.LocalName != "TestRun") throw new FormatException("根节点不是 TestRun");
            var ns = root.Name.Namespace;
            var summary = root.Elements(ns + "ResultSummary").Single();
            var counters = summary.Elements(ns + "Counters").Single();
            var results = root.Elements(ns + "Results").Single().Elements(ns + "UnitTestResult").ToArray();
            var counts = new TestCounts(Count(counters, "total"), Count(counters, "executed"),
                Count(counters, "passed"), Count(counters, "failed"), Count(counters, "notExecuted"),
                results.Count(result => Required(result, "outcome") is not ("Passed" or "Failed" or "NotExecuted")));
            // Executed 在本仓两个适配器中包含通过/失败，排除 NotExecuted；异常类别保留在 Other。
            if (results.Length != counts.Total || counts.Executed != counts.Total - counts.Skipped ||
                results.Count(result => Required(result, "outcome") == "Passed") != counts.Passed ||
                results.Count(result => Required(result, "outcome") == "Failed") != counts.Failed ||
                results.Count(result => Required(result, "outcome") == "NotExecuted") != counts.Skipped ||
                results.Select(result => Required(result, "executionId")).Distinct(StringComparer.Ordinal).Count() != results.Length)
                throw new FormatException("计数与结果明细不一致或执行标识重复");

            var abnormal = new[] { "error", "timeout", "aborted", "inconclusive", "passedButRunAborted", "notRunnable",
                "disconnected", "warning", "inProgress", "pending" }
                .Select(name => counters.Attribute(name) is null ? 0 : Count(counters, name)).Any(value => value != 0);
            var times = root.Elements(ns + "Times").Single();
            var start = DateTimeOffset.Parse(Required(times, "start"), CultureInfo.InvariantCulture);
            var finish = DateTimeOffset.Parse(Required(times, "finish"), CultureInfo.InvariantCulture);
            if (finish < start) throw new FormatException("运行结束早于开始");
            var cases = results.Select(result =>
            {
                var outcome = Required(result, "outcome");
                var duration = result.Attribute("duration")?.Value;
                var elapsed = duration is null && outcome != "Passed" ? TimeSpan.Zero
                    : TimeSpan.Parse(duration ?? throw new FormatException("通过结果缺少 duration"), CultureInfo.InvariantCulture);
                if (elapsed < TimeSpan.Zero) throw new FormatException("用例时长为负数");
                return new TestCaseEvidence(Required(result, "testName"), outcome, elapsed.TotalMilliseconds);
            }).ToArray();
            return new(Required(summary, "outcome"), counts, Convert.ToHexString(SHA256.HashData(bytes)),
                (finish - start).TotalMilliseconds, cases.OrderByDescending(item => item.Milliseconds)
                    .ThenBy(item => item.Name, StringComparer.Ordinal).Take(10).ToArray(), abnormal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException or
                                         FormatException or InvalidOperationException or OverflowException)
        {
            // 不回显 XML 原文或测试数据，只说明证据文件无法形成可信的完整结果。
            throw new GateFailureException($"TRX 缺失、损坏或结构不完整：{path}（{exception.GetType().Name}）。");
        }
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

    private static string Required(XElement element, string name) =>
        element.Attribute(name)?.Value is { Length: > 0 } value ? value : throw new FormatException($"缺少 {name}");

    private static int Count(XElement element, string name)
    {
        if (!int.TryParse(Required(element, name), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"非法计数 {name}");
        return value;
    }

    private static double DoubleAttribute(XElement element, string name) =>
        double.Parse(element.Attribute(name)?.Value ?? "0", CultureInfo.InvariantCulture);
}
