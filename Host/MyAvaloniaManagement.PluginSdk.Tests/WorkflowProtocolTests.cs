using System.Text.Json;
using MyAvaloniaManagement.PluginSdk.Workflow;

namespace MyAvaloniaManagement.PluginSdk.Tests;

/// <summary>保护 Host 与 Studio 共用的 Workflow 协议语义。</summary>
public sealed class WorkflowProtocolTests
{
    private readonly WorkflowSchemaValidator _validator = new();
    private readonly WorkflowReferenceTypeSystem _types = new();

    [Fact]
    public void Unicode长度按Rune且超出Decimal范围被拒绝()
    {
        using var stringSchema = JsonDocument.Parse(
            """{"type":"object","properties":{"value":{"type":"string","maxLength":1}},"required":["value"],"additionalProperties":false}""");
        var emoji = JsonSerializer.SerializeToElement(new { value = "😀" });
        Assert.True(_validator.ValidateInstance(
            stringSchema.RootElement, emoji, WorkflowSchemaProfile.MaximumInputBytes).IsValid);

        using var numberSchema = JsonDocument.Parse(
            """{"type":"object","properties":{"value":{"type":"number"}},"required":["value"],"additionalProperties":false}""");
        using var huge = JsonDocument.Parse("{\"value\":1e100}");
        Assert.Contains(_validator.ValidateInstance(
                numberSchema.RootElement, huge.RootElement, WorkflowSchemaProfile.MaximumInputBytes).Issues,
            issue => issue.Code == "instance.type");
    }

    [Fact]
    public void Schema注册拒绝Decimal外边界和未知关键字()
    {
        using var schema = JsonDocument.Parse(
            """{"type":"object","properties":{"value":{"type":"number","minimum":1e100,"format":"x"}},"required":["value"],"additionalProperties":false}""");

        var result = _validator.ValidateSchema(schema.RootElement);

        Assert.Contains(result.Issues, issue => issue.Code == "schema.number.bounds");
        Assert.Contains(result.Issues, issue => issue.Code == "schema.keyword");
    }

    [Fact]
    public void 保守值域允许Integer到Number并拒绝范围Enum与对象扩张()
    {
        using var integer = JsonDocument.Parse("""{"type":"integer","minimum":1,"maximum":3}""");
        using var number = JsonDocument.Parse("""{"type":"number","minimum":0,"maximum":5}""");
        Assert.True(_types.ValidateAssignable(integer.RootElement, number.RootElement).IsValid);

        using var wide = JsonDocument.Parse("""{"type":"string"}""");
        using var enumeration = JsonDocument.Parse("""{"type":"string","enum":["a","b"]}""");
        Assert.Contains(_types.ValidateAssignable(wide.RootElement, enumeration.RootElement).Issues,
            issue => issue.Code == "reference.enum");

        using var sourceObject = JsonDocument.Parse(
            """{"type":"object","properties":{"known":{"type":"string"},"extra":{"type":"boolean"}},"required":["known"],"additionalProperties":false}""");
        using var targetObject = JsonDocument.Parse(
            """{"type":"object","properties":{"known":{"type":"string"}},"required":["known"],"additionalProperties":false}""");
        Assert.Contains(_types.ValidateAssignable(sourceObject.RootElement, targetObject.RootElement).Issues,
            issue => issue.Code == "reference.additional");
    }

    [Fact]
    public void 保守值域覆盖Enum子集字符串数值数组和Required关系()
    {
        using var sourceEnum = JsonDocument.Parse("""{"type":"string","enum":["a"]}""");
        using var targetEnum = JsonDocument.Parse("""{"type":"string","enum":["b","a"]}""");
        Assert.True(_types.ValidateAssignable(sourceEnum.RootElement, targetEnum.RootElement).IsValid);

        using var sourceString = JsonDocument.Parse("""{"type":"string","minLength":2,"maxLength":4}""");
        using var targetString = JsonDocument.Parse("""{"type":"string","minLength":1,"maxLength":5}""");
        Assert.True(_types.ValidateAssignable(sourceString.RootElement, targetString.RootElement).IsValid);
        Assert.Contains(_types.ValidateAssignable(targetString.RootElement, sourceString.RootElement).Issues,
            issue => issue.Code == "reference.string.bounds");

        using var sourceArray = JsonDocument.Parse(
            """{"type":"array","minItems":1,"maxItems":2,"items":{"type":"integer","minimum":0,"maximum":3}}""");
        using var targetArray = JsonDocument.Parse(
            """{"type":"array","minItems":0,"maxItems":4,"items":{"type":"number","minimum":0,"maximum":5}}""");
        Assert.True(_types.ValidateAssignable(sourceArray.RootElement, targetArray.RootElement).IsValid);
        Assert.Contains(_types.ValidateAssignable(targetArray.RootElement, sourceArray.RootElement).Issues,
            issue => issue.Code is "reference.array.bounds" or "reference.type");

        using var sourceObject = JsonDocument.Parse(
            """{"type":"object","properties":{"value":{"type":"string"}},"required":[],"additionalProperties":false}""");
        using var targetObject = JsonDocument.Parse(
            """{"type":"object","properties":{"value":{"type":"string"}},"required":["value"],"additionalProperties":false}""");
        Assert.Contains(_types.ValidateAssignable(sourceObject.RootElement, targetObject.RootElement).Issues,
            issue => issue.Code == "reference.required");
    }

    [Fact]
    public void 实例校验覆盖RequiredAdditionalEnum数组范围字符串与Int64()
    {
        using var schema = JsonDocument.Parse("""
            {
              "type":"object",
              "properties":{
                "name":{"type":"string","minLength":2,"maxLength":3,"enum":["ab","abc"]},
                "items":{"type":"array","minItems":1,"maxItems":2,"items":{"type":"integer","minimum":0,"maximum":9}},
                "enabled":{"type":"boolean"},
                "none":{"type":"null"}
              },
              "required":["name","items","none"],
              "additionalProperties":false
            }
            """);
        using var invalid = JsonDocument.Parse(
            """{"name":"x","items":[-1,10,11],"enabled":"yes","extra":1}""");

        var issues = _validator.ValidateInstance(
            schema.RootElement, invalid.RootElement, WorkflowSchemaProfile.MaximumInputBytes).Issues;

        Assert.Contains(issues, issue => issue.Code == "instance.enum");
        Assert.Contains(issues, issue => issue.Code == "instance.string.bounds");
        Assert.Contains(issues, issue => issue.Code == "instance.array.bounds");
        Assert.Contains(issues, issue => issue.Code == "instance.number.bounds");
        Assert.Contains(issues, issue => issue.Code == "instance.type");
        Assert.Contains(issues, issue => issue.Code == "instance.additional");
        Assert.Contains(issues, issue => issue.Code == "instance.required" && issue.Path.EndsWith(".none", StringComparison.Ordinal));

        using var integerSchema = JsonDocument.Parse("""{"type":"integer"}""");
        using var outsideInt64 = JsonDocument.Parse("9223372036854775808");
        Assert.Contains(_validator.ValidateInstance(
                integerSchema.RootElement, outsideInt64.RootElement, 128).Issues,
            issue => issue.Code == "instance.type");
    }

    [Fact]
    public void Schema校验覆盖对象数组Enum边界描述与根类型错误()
    {
        using var invalid = JsonDocument.Parse("""
            {
              "type":"object",
              "description":1,
              "properties":{
                "array":{"type":"array","minItems":3,"maxItems":2,"items":{"type":"string","minLength":2,"maxLength":1}},
                "choice":{"type":"string","enum":["a","a"]},
                "broken":{"type":"object"}
              },
              "required":["missing","missing"],
              "additionalProperties":false
            }
            """);
        var issues = _validator.ValidateSchema(invalid.RootElement).Issues;
        Assert.Contains(issues, issue => issue.Code == "schema.description");
        Assert.Contains(issues, issue => issue.Code == "schema.array.bounds");
        Assert.Contains(issues, issue => issue.Code == "schema.bounds");
        Assert.Contains(issues, issue => issue.Code == "schema.enum");
        Assert.Contains(issues, issue => issue.Code == "schema.object");
        Assert.Contains(issues, issue => issue.Code == "schema.required");

        using var scalarRoot = JsonDocument.Parse("""{"type":"string"}""");
        Assert.Contains(_validator.ValidateSchema(scalarRoot.RootElement).Issues,
            issue => issue.Code == "schema.root");
    }

    [Theory]
    [InlineData("[]", "schema.node")]
    [InlineData("{}", "schema.type")]
    [InlineData("{\"type\":\"unknown\"}", "schema.type")]
    [InlineData("{\"type\":\"object\",\"properties\":{},\"required\":1,\"additionalProperties\":false}", "schema.required")]
    [InlineData("{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false,\"x\":1}", "schema.keyword")]
    [InlineData("{\"type\":\"array\",\"items\":{\"type\":\"string\"}}", "schema.array")]
    [InlineData("{\"type\":\"string\",\"minLength\":-1}", "schema.bounds")]
    [InlineData("{\"type\":\"number\",\"minimum\":2,\"maximum\":1}", "schema.number.bounds")]
    [InlineData("{\"type\":\"boolean\",\"enum\":[]}", "schema.enum")]
    [InlineData("{\"type\":\"integer\",\"enum\":[1,1]}", "schema.enum")]
    public void Schema畸形语料返回稳定代码(string json, string code)
    {
        using var schema = JsonDocument.Parse(json);
        Assert.Contains(_validator.ValidateSchema(schema.RootElement).Issues,
            issue => issue.Code == code);
    }

    [Fact]
    public void Schema资源预算覆盖总字节属性数和递归深度()
    {
        var oversized = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            description = new string('x', WorkflowSchemaProfile.MaximumSchemaBytes),
            properties = new { },
            required = Array.Empty<string>(),
            additionalProperties = false,
        });
        Assert.Contains(_validator.ValidateSchema(oversized).Issues,
            issue => issue.Code == "schema.budget");

        var properties = string.Join(',', Enumerable.Range(0, WorkflowSchemaProfile.MaximumProperties + 1)
            .Select(index => $"\"p{index}\":{{\"type\":\"string\"}}"));
        using var tooMany = JsonDocument.Parse(
            $"{{\"type\":\"object\",\"properties\":{{{properties}}},\"required\":[],\"additionalProperties\":false}}");
        Assert.Contains(_validator.ValidateSchema(tooMany.RootElement).Issues,
            issue => issue.Code == "schema.properties.budget");

        var nested = "{\"type\":\"string\"}";
        for (var index = 0; index < WorkflowSchemaProfile.MaximumDepth; index++)
        {
            nested = $"{{\"type\":\"array\",\"maxItems\":1,\"items\":{nested}}}";
        }
        using var tooDeep = JsonDocument.Parse(
            $"{{\"type\":\"object\",\"properties\":{{\"value\":{nested}}},\"required\":[\"value\"],\"additionalProperties\":false}}");
        Assert.Contains(_validator.ValidateSchema(tooDeep.RootElement).Issues,
            issue => issue.Code == "schema.node");
    }

    [Fact]
    public void 实例资源和重复属性语料返回稳定代码()
    {
        using var schema = JsonDocument.Parse(
            """{"type":"object","properties":{"value":{"type":"string"}},"required":["value"],"additionalProperties":false}""");
        using var duplicate = JsonDocument.Parse("""{"value":"a","value":"b"}""");
        Assert.Contains(_validator.ValidateInstance(schema.RootElement, duplicate.RootElement, 1024).Issues,
            issue => issue.Code == "instance.duplicate");

        var large = JsonSerializer.SerializeToElement(new { value = new string('x', 100) });
        Assert.Contains(_validator.ValidateInstance(schema.RootElement, large, 10).Issues,
            issue => issue.Code == "instance.budget");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _validator.ValidateInstance(schema.RootElement, large, 0));
    }

    [Fact]
    public void Descriptor校验覆盖敏感指针风险确认和展示预算()
    {
        using var input = JsonDocument.Parse(
            """{"type":"object","properties":{"secret":{"type":"string"}},"required":["secret"],"additionalProperties":false}""");
        using var output = JsonDocument.Parse(
            """{"type":"object","properties":{},"required":[],"additionalProperties":false}""");
        var descriptor = new WorkflowActionDescriptor(
            new("myavalonia.plugin.test.workflow.invalid"),
            new string('名', 129),
            new string('说', 513),
            input.RootElement,
            output.RootElement,
            WorkflowActionRiskFlags.DeletesLocalFiles,
            WorkflowActionConfirmationPolicy.OncePerRun,
            ["/missing", "/missing"]);

        var issues = _validator.ValidateDescriptor(descriptor).Issues;

        Assert.Contains(issues, issue => issue.Code == "descriptor.sensitive");
        Assert.Contains(issues, issue => issue.Code == "descriptor.confirmation");
        Assert.Contains(issues, issue => issue.Code == "descriptor.presentation");
    }

    [Fact]
    public void 合法Descriptor与敏感JsonPointer通过共享校验()
    {
        using var input = JsonDocument.Parse(
            """{"type":"object","properties":{"nested":{"type":"object","properties":{"a/b":{"type":"string"}},"required":["a/b"],"additionalProperties":false}},"required":["nested"],"additionalProperties":false}""");
        using var output = JsonDocument.Parse(
            """{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}""");
        var descriptor = new WorkflowActionDescriptor(
            new("myavalonia.plugin.test.workflow.valid"), "名称", "说明",
            input.RootElement, output.RootElement,
            WorkflowActionRiskFlags.HandlesSecret,
            WorkflowActionConfirmationPolicy.OncePerRun,
            ["/nested/a~1b"]);

        Assert.True(_validator.ValidateDescriptor(descriptor).IsValid);
    }

    [Fact]
    public void 静态与运行路径共享数组Segment且静态要求必需性和MinItems()
    {
        using var schema = JsonDocument.Parse(
            """{"type":"object","properties":{"optional":{"type":"string"},"items":{"type":"array","minItems":1,"maxItems":2,"items":{"type":"string"}}},"required":["items"],"additionalProperties":false}""");

        var optional = WorkflowReferencePath.ResolveGuaranteedSchemaPath(
            schema.RootElement, ["optional"]);
        var invalidIndex = WorkflowReferencePath.ResolveGuaranteedSchemaPath(
            schema.RootElement, ["items", "x"]);
        var guaranteed = WorkflowReferencePath.ResolveGuaranteedSchemaPath(
            schema.RootElement, ["items", "0"]);

        Assert.Equal(WorkflowReferencePathFailure.OptionalProperty, optional.Failure);
        Assert.Equal(WorkflowReferencePathFailure.InvalidArrayIndex, invalidIndex.Failure);
        Assert.True(guaranteed.Succeeded);

        var value = JsonSerializer.SerializeToElement(new { items = new[] { "ok" } });
        Assert.Equal("ok", WorkflowReferencePath.ResolveValuePath(
            value, ["items", "0"]).Value!.Value.GetString());
        Assert.Equal(WorkflowReferencePathFailure.ArrayIndexNotGuaranteed,
            WorkflowReferencePath.ResolveGuaranteedSchemaPath(
                schema.RootElement, ["items", "1"]).Failure);
        Assert.Equal(WorkflowReferencePathFailure.ArrayIndexOutOfRange,
            WorkflowReferencePath.ResolveValuePath(value, ["items", "1"]).Failure);
        Assert.Equal(WorkflowReferencePathFailure.NonContainer,
            WorkflowReferencePath.ResolveValuePath(value, ["items", "0", "child"]).Failure);
        Assert.Equal(WorkflowReferencePathFailure.MissingProperty,
            WorkflowReferencePath.ResolveGuaranteedSchemaPath(schema.RootElement, ["missing"]).Failure);
        Assert.Equal(WorkflowReferencePathFailure.MissingProperty,
            WorkflowReferencePath.ResolveValuePath(value, ["missing"]).Failure);
        Assert.Equal(WorkflowReferencePathFailure.InvalidArrayIndex,
            WorkflowReferencePath.ResolveValuePath(value, ["items", "-1"]).Failure);
        Assert.Equal(WorkflowReferencePathFailure.NonContainer,
            WorkflowReferencePath.ResolveGuaranteedSchemaPath(
                guaranteed.Value!.Value, ["child"]).Failure);

        using var malformedArray = JsonDocument.Parse("""{"type":"array","minItems":1}""");
        Assert.Equal(WorkflowReferencePathFailure.NonContainer,
            WorkflowReferencePath.ResolveGuaranteedSchemaPath(
                malformedArray.RootElement, ["0"]).Failure);
    }

    [Fact]
    public void Catalog改名只改变Presentation而语义变化改变Contract()
    {
        var original = Descriptor("名称", "说明", "string");
        var renamed = Descriptor("新名称", "新说明", "string");
        var changed = Descriptor("新名称", "新说明", "integer");

        var first = WorkflowCatalogRevisionCalculator.Calculate([original]);
        var presentation = WorkflowCatalogRevisionCalculator.Calculate([renamed]);
        var contract = WorkflowCatalogRevisionCalculator.Calculate([changed]);

        Assert.Equal(first.ContractRevision, presentation.ContractRevision);
        Assert.NotEqual(first.PresentationRevision, presentation.PresentationRevision);
        Assert.NotEqual(first.ContractRevision, contract.ContractRevision);
    }

    [Fact]
    public void Catalog哈希忽略Action属性Required和Enum的声明顺序()
    {
        var first = DescriptorWithSchema(
            """{"type":"object","properties":{"a":{"type":"string","enum":["x","y"]},"b":{"type":"integer"}},"required":["a","b"],"additionalProperties":false}""");
        var reordered = DescriptorWithSchema(
            """{"required":["b","a"],"additionalProperties":false,"properties":{"b":{"type":"integer"},"a":{"enum":["y","x"],"type":"string"}},"type":"object"}""");

        var left = WorkflowCatalogRevisionCalculator.Calculate([first]);
        var right = WorkflowCatalogRevisionCalculator.Calculate([reordered]);

        Assert.Equal(left.ContractRevision, right.ContractRevision);
        Assert.Equal(left.PresentationRevision, right.PresentationRevision);
    }

    [Fact]
    public void Catalog哈希包含敏感指针和嵌套数组说明()
    {
        using var input = JsonDocument.Parse(
            """{"type":"object","description":"输入","properties":{"secret":{"type":"string","description":"密钥"}},"required":["secret"],"additionalProperties":false}""");
        using var output = JsonDocument.Parse(
            """{"type":"object","properties":{"items":{"type":"array","description":"列表","maxItems":1,"items":{"type":"string","description":"项目"}}},"required":["items"],"additionalProperties":false}""");
        var descriptor = new WorkflowActionDescriptor(
            new("myavalonia.plugin.test.workflow.descriptions"), "名称", "说明",
            input.RootElement, output.RootElement, WorkflowActionRiskFlags.HandlesSecret,
            WorkflowActionConfirmationPolicy.OncePerRun, ["/secret"]);

        var revisions = WorkflowCatalogRevisionCalculator.Calculate([descriptor]);

        Assert.StartsWith("sha256:", revisions.ContractRevision, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", revisions.PresentationRevision, StringComparison.Ordinal);
    }

    private static WorkflowActionDescriptor Descriptor(
        string name,
        string description,
        string outputType)
    {
        using var input = JsonDocument.Parse(
            """{"type":"object","properties":{},"required":[],"additionalProperties":false}""");
        using var output = JsonDocument.Parse(
            """{"type":"object","properties":{"value":{"type":"TYPE","description":"字段"}},"required":["value"],"additionalProperties":false}"""
                .Replace("TYPE", outputType, StringComparison.Ordinal));
        return new(new("myavalonia.plugin.test.workflow.action"), name, description,
            input.RootElement, output.RootElement, WorkflowActionRiskFlags.None,
            WorkflowActionConfirmationPolicy.Never);
    }

    private static WorkflowActionDescriptor DescriptorWithSchema(string outputSchema)
    {
        using var input = JsonDocument.Parse(
            """{"type":"object","properties":{},"required":[],"additionalProperties":false}""");
        using var output = JsonDocument.Parse(outputSchema);
        return new(new("myavalonia.plugin.test.workflow.order"), "名称", "说明",
            input.RootElement, output.RootElement, WorkflowActionRiskFlags.None,
            WorkflowActionConfirmationPolicy.Never);
    }
}
