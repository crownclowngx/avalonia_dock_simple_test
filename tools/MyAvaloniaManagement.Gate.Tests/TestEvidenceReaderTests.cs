using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace MyAvaloniaManagement.Gate.Tests;

public sealed class TestEvidenceReaderTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "MAVG-trx-" + Guid.NewGuid().ToString("N"));

    public TestEvidenceReaderTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 完整结果提供同一文件摘要计数运行时间与最慢十项(bool namespaced)
    {
        var path = Save(TrxFixture.Create(passed: 12, namespaced: namespaced));
        var result = TestEvidenceReader.ReadTrx(path);
        GateRunner.AssertTests(result, "fixture");
        Assert.Equal(new TestCounts(12, 12, 12, 0, 0, 0), result.Counts);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), result.Sha256);
        Assert.Equal(2000, result.Milliseconds);
        Assert.Equal(10, result.SlowTests.Length);
        Assert.Equal("case-11", result.SlowTests[0].Name);
        Assert.Equal(12, result.SlowTests[0].Milliseconds);
    }

    [Theory]
    [InlineData("total", null)]
    [InlineData("executed", null)]
    [InlineData("passed", null)]
    [InlineData("failed", null)]
    [InlineData("notExecuted", null)]
    [InlineData("passed", "-1")]
    [InlineData("passed", "1.5")]
    [InlineData("passed", "2147483648")]
    [InlineData("passed", "abc")]
    [InlineData("passed", "")]
    [InlineData("total", "2")]
    [InlineData("executed", "0")]
    [InlineData("failed", "1")]
    [InlineData("notExecuted", "1")]
    [InlineData("timeout", "broken")]
    public void 计数缺失非法或不匹配不能补零(string attribute, string? value)
    {
        var document = TrxFixture.Create();
        Element(document, "Counters").SetAttributeValue(attribute, value);
        Assert.Throws<GateFailureException>(() => TestEvidenceReader.ReadTrx(Save(document)));
    }

    [Theory]
    [InlineData("ResultSummary")]
    [InlineData("Counters")]
    [InlineData("Results")]
    [InlineData("Times")]
    public void 关键节点缺失或重复均拒绝(string node)
    {
        var missing = TrxFixture.Create();
        Element(missing, node).Remove();
        Assert.Throws<GateFailureException>(() => TestEvidenceReader.ReadTrx(Save(missing)));
        var duplicate = TrxFixture.Create();
        var element = Element(duplicate, node);
        element.AddAfterSelf(new XElement(element));
        Assert.Throws<GateFailureException>(() => TestEvidenceReader.ReadTrx(Save(duplicate)));
    }

    [Theory]
    [InlineData("duration", null)]
    [InlineData("duration", "-00:00:01")]
    [InlineData("duration", "invalid")]
    [InlineData("testName", null)]
    [InlineData("outcome", null)]
    [InlineData("executionId", null)]
    public void 通过用例必须包含有效身份和时间(string attribute, string? value)
    {
        var document = TrxFixture.Create();
        Element(document, "UnitTestResult").SetAttributeValue(attribute, value);
        Assert.Throws<GateFailureException>(() => TestEvidenceReader.ReadTrx(Save(document)));
    }

    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("missing-detail")]
    [InlineData("missing-outcome")]
    [InlineData("missing-time")]
    [InlineData("invalid-time")]
    [InlineData("reverse-time")]
    public void 不完整运行结构被拒绝(string mode)
    {
        var document = TrxFixture.Create(passed: 2);
        var results = Element(document, "Results").Elements().ToArray();
        switch (mode)
        {
            case "duplicate-id": results[1].SetAttributeValue("executionId", results[0].Attribute("executionId")!.Value); break;
            case "missing-detail": results[1].Remove(); break;
            case "missing-outcome": Element(document, "ResultSummary").Attribute("outcome")!.Remove(); break;
            case "missing-time": Element(document, "Times").Attribute("finish")!.Remove(); break;
            case "invalid-time": Element(document, "Times").SetAttributeValue("finish", "invalid"); break;
            case "reverse-time": Element(document, "Times").SetAttributeValue("finish", "2026-09-20T01:00:00Z"); break;
        }
        Assert.Throws<GateFailureException>(() => TestEvidenceReader.ReadTrx(Save(document)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("<broken")]
    [InlineData("<Other />")]
    public void 文件缺失坏XML及错误根节点被拒绝(string? content)
    {
        var path = Path.Combine(directory, "missing.trx");
        if (content is not null) File.WriteAllText(path, content);
        Assert.Throws<GateFailureException>(() => TestEvidenceReader.ReadTrx(path));
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("failed")]
    [InlineData("skipped")]
    [InlineData("aborted")]
    [InlineData("other-result")]
    [InlineData("abnormal-counter")]
    [InlineData("exit-code")]
    public void 结构完整仍需满足全部通过且命令成功(string mode)
    {
        var document = mode switch
        {
            "zero" => TrxFixture.Create(passed: 0),
            "failed" => TrxFixture.Create(failed: 1),
            "skipped" => TrxFixture.Create(skipped: 1),
            _ => TrxFixture.Create(),
        };
        if (mode == "aborted") Element(document, "ResultSummary").SetAttributeValue("outcome", "Aborted");
        if (mode == "abnormal-counter") Element(document, "Counters").SetAttributeValue("aborted", 1);
        if (mode == "other-result")
        {
            Element(document, "UnitTestResult").SetAttributeValue("outcome", "Timeout");
            Element(document, "Counters").SetAttributeValue("passed", 0);
        }
        var result = TestEvidenceReader.ReadTrx(Save(document));
        Assert.Throws<GateFailureException>(() => GateRunner.AssertTests(result, "fixture", exitCode: mode == "exit-code" ? 1 : 0));
    }

    [Fact]
    public void 真实包要求恰好一项通过而普通套件允许多项()
    {
        GateRunner.AssertTests(Save(TrxFixture.Create()), "package", requireSingle: true);
        GateRunner.AssertTests(Save(TrxFixture.Create(passed: 2)), "unit");
        Assert.Throws<GateFailureException>(() => GateRunner.AssertTests(Save(TrxFixture.Create(passed: 2)), "package", true));
    }

    [Theory]
    [InlineData("error")]
    [InlineData("timeout")]
    [InlineData("aborted")]
    [InlineData("inconclusive")]
    [InlineData("passedButRunAborted")]
    [InlineData("notRunnable")]
    [InlineData("disconnected")]
    [InlineData("warning")]
    [InlineData("inProgress")]
    [InlineData("pending")]
    public void 非成功辅助计数不能被全通过主计数掩盖(string name)
    {
        var document = TrxFixture.Create();
        Element(document, "Counters").SetAttributeValue(name, 1);
        var result = TestEvidenceReader.ReadTrx(Save(document));
        Assert.True(result.HasAbnormalCounters);
        Assert.Throws<GateFailureException>(() => GateRunner.AssertTests(result, "fixture"));
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Timeout")]
    [InlineData("InProgress")]
    [InlineData("NotExecuted")]
    [InlineData("Unknown")]
    public void 未完成或非成功运行不能只看通过计数(string outcome)
    {
        var document = TrxFixture.Create();
        Element(document, "ResultSummary").SetAttributeValue("outcome", outcome);
        Assert.Throws<GateFailureException>(() => GateRunner.AssertTests(Save(document), "fixture"));
    }

    [Fact]
    public void 套件证据区分失败与未运行并保留结果身份()
    {
        var result = TestEvidenceReader.ReadTrx(Save(TrxFixture.Create(failed: 1)));
        var path = Path.Combine(directory, "summary.json");
        EvidenceWriter.Write(path, new TestSuiteSummary("run", 1, new("rev", "tree", false, 1, "input"),
            [new("first", "failed") { ExitCode = 1, Result = result, TrxPath = "tests/first/first.trx", Error = "failed" }, new("second")]));
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var suites = json.RootElement.GetProperty("suites").EnumerateArray().ToArray();
        Assert.Equal("failed", suites[0].GetProperty("status").GetString());
        Assert.Equal(result.Sha256, suites[0].GetProperty("result").GetProperty("sha256").GetString());
        Assert.Equal("not-run", suites[1].GetProperty("status").GetString());
        Assert.False(suites[1].TryGetProperty("exitCode", out _));
        Assert.False(suites[1].TryGetProperty("result", out _));
    }

    private string Save(XDocument document)
    {
        var path = Path.Combine(directory, "result.trx");
        document.Save(path);
        return path;
    }

    private static XElement Element(XDocument document, string name) =>
        document.Descendants(document.Root!.Name.Namespace + name).First();
}
