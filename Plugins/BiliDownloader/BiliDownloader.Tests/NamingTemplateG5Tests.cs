using BiliDownloader.Models;
using BiliDownloader.Services.Naming;
using BiliDownloader.Services.Persistence;

namespace BiliDownloader.Tests;

/// <summary>
/// G5 命名模板与文件名安全器测试。
/// 覆盖：FileNameSanitizer、NamingTemplateEngine、DownloadPreset 模型、PresetStore。
/// </summary>
public class NamingTemplateG5Tests
{
    #region FileNameSanitizer 测试

    [Fact]
    public void 非法字符_替换为下划线()
    {
        var result = FileNameSanitizer.Sanitize("a\\b/c:d*e?f\"g<h>i|j");
        Assert.DoesNotContain("\\", result);
        Assert.DoesNotContain("/", result);
        Assert.DoesNotContain(":", result);
        Assert.DoesNotContain("*", result);
        Assert.DoesNotContain("?", result);
        Assert.DoesNotContain("\"", result);
        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
        Assert.DoesNotContain("|", result);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("COM9")]
    [InlineData("LPT1")]
    [InlineData("LPT9")]
    public void Windows保留名_追加下划线(string reservedName)
    {
        var result = FileNameSanitizer.Sanitize(reservedName);
        Assert.Equal($"{reservedName}_", result);
    }

    [Fact]
    public void 保留名_不区分大小写()
    {
        Assert.Equal("con_", FileNameSanitizer.Sanitize("con"));
        Assert.Equal("Con_", FileNameSanitizer.Sanitize("Con"));
    }

    [Fact]
    public void 尾部点号_被移除()
    {
        var result = FileNameSanitizer.Sanitize("test...");
        Assert.Equal("test", result);
    }

    [Fact]
    public void 尾部空格_被移除()
    {
        var result = FileNameSanitizer.Sanitize("test   ");
        Assert.Equal("test", result);
    }

    [Fact]
    public void 空输入_回退download()
    {
        Assert.Equal("download", FileNameSanitizer.Sanitize(""));
        Assert.Equal("download", FileNameSanitizer.Sanitize("   "));
        Assert.Equal("download", FileNameSanitizer.Sanitize(null!));
    }

    [Fact]
    public void 纯非法字符_替换为下划线()
    {
        // 设计思考：非法字符被替换为下划线，结果非空所以不回退
        var result = FileNameSanitizer.Sanitize("\\/:*?\"<>|");
        Assert.Equal("_________", result); // 9 个非法字符 → 9 个下划线
    }

    [Fact]
    public void 中文标题_保持不变()
    {
        var result = FileNameSanitizer.Sanitize("【教程】Avalonia 入门");
        Assert.Equal("【教程】Avalonia 入门", result);
    }

    [Fact]
    public void 超长路径_截断文件名并追加哈希()
    {
        var longDir = new string('D', 200);
        var longName = new string('N', 100);
        var result = FileNameSanitizer.EnsurePathLength(longDir, longName, ".mp4");

        // 结果应该更短
        Assert.True(result.Length < longName.Length);
        // 包含哈希后缀（_xxxxxx 格式）
        Assert.Contains("_", result);
    }

    [Fact]
    public void 路径长度验证_正常路径返回true()
    {
        Assert.True(FileNameSanitizer.IsPathLengthValid("C:\\test\\file.mp4"));
    }

    [Fact]
    public void 路径长度验证_超长路径返回false()
    {
        var longPath = new string('A', 300);
        Assert.False(FileNameSanitizer.IsPathLengthValid(longPath));
    }

    [Fact]
    public void 不同标题截断后_哈希不同()
    {
        var dir = new string('D', 200);
        var name1 = new string('A', 100);
        var name2 = new string('B', 100);

        var result1 = FileNameSanitizer.EnsurePathLength(dir, name1, ".mp4");
        var result2 = FileNameSanitizer.EnsurePathLength(dir, name2, ".mp4");

        Assert.NotEqual(result1, result2);
    }

    #endregion

    #region NamingTemplateEngine.Render 测试

    [Fact]
    public void 渲染_title变量()
    {
        var ctx = new NamingContext { Title = "测试标题" };
        Assert.Equal("测试标题", NamingTemplateEngine.Render("{title}", ctx));
    }

    [Fact]
    public void 渲染_index变量()
    {
        var ctx = new NamingContext { Index = 5 };
        Assert.Equal("5", NamingTemplateEngine.Render("{index}", ctx));
    }

    [Fact]
    public void 渲染_bv变量()
    {
        var ctx = new NamingContext { Bvid = "BV1xx411c7mD" };
        Assert.Equal("BV1xx411c7mD", NamingTemplateEngine.Render("{bv}", ctx));
    }

    [Fact]
    public void 渲染_up变量()
    {
        var ctx = new NamingContext { UpName = "某UP主" };
        Assert.Equal("某UP主", NamingTemplateEngine.Render("{up}", ctx));
    }

    [Fact]
    public void 渲染_date变量()
    {
        var ctx = new NamingContext { PublishDate = new DateTime(2026, 7, 21) };
        Assert.Equal("2026-07-21", NamingTemplateEngine.Render("{date}", ctx));
    }

    [Fact]
    public void 渲染_date为null时_输出回退名()
    {
        // 设计思考：{date} 为 null 时渲染为空串，但 FileNameSanitizer 会将空串回退为 "download"
        var ctx = new NamingContext { PublishDate = null };
        Assert.Equal("download", NamingTemplateEngine.Render("{date}", ctx));
    }

    [Fact]
    public void 渲染_series变量()
    {
        var ctx = new NamingContext { SeriesTitle = "Avalonia系列" };
        Assert.Equal("Avalonia系列", NamingTemplateEngine.Render("{series}", ctx));
    }

    [Fact]
    public void 渲染_多变量组合()
    {
        var ctx = new NamingContext { Index = 3, Title = "教程" };
        Assert.Equal("3.教程", NamingTemplateEngine.Render("{index}.{title}", ctx));
    }

    [Fact]
    public void 渲染_纯文本无变量()
    {
        var ctx = new NamingContext();
        Assert.Equal("固定名称", NamingTemplateEngine.Render("固定名称", ctx));
    }

    [Fact]
    public void 渲染_空模板使用默认()
    {
        var ctx = new NamingContext { Index = 1, Title = "测试" };
        Assert.Equal("1.测试", NamingTemplateEngine.Render("", ctx));
    }

    [Fact]
    public void 渲染_特殊字符自动清理()
    {
        var ctx = new NamingContext { Title = "a/b:c" };
        var result = NamingTemplateEngine.Render("{title}", ctx);
        Assert.DoesNotContain("/", result);
        Assert.DoesNotContain(":", result);
    }

    [Fact]
    public void 渲染_100项性能小于5ms()
    {
        var contexts = Enumerable.Range(1, 100)
            .Select(i => new NamingContext { Index = i, Title = $"视频{i}" })
            .ToList();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var ctx in contexts)
        {
            NamingTemplateEngine.Render("{index}.{title}", ctx);
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 5, $"100次渲染耗时 {sw.ElapsedMilliseconds}ms，超过 5ms");
    }

    #endregion

    #region NamingTemplateEngine.Validate 测试

    [Fact]
    public void 验证_合法模板()
    {
        var result = NamingTemplateEngine.Validate("{index}.{title}");
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void 验证_未知变量()
    {
        var result = NamingTemplateEngine.Validate("{unknown}");
        Assert.False(result.IsValid);
        Assert.Contains("unknown", result.UnknownVariables);
    }

    [Fact]
    public void 验证_未闭合花括号()
    {
        var result = NamingTemplateEngine.Validate("{title");
        Assert.False(result.IsValid);
        Assert.Contains("没有对应的", result.ErrorMessage);
    }

    [Fact]
    public void 验证_空模板()
    {
        var result = NamingTemplateEngine.Validate("");
        Assert.False(result.IsValid);
        Assert.Equal("模板不能为空", result.ErrorMessage);
    }

    [Fact]
    public void 验证_仅空白()
    {
        var result = NamingTemplateEngine.Validate("   ");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void 验证_所有合法变量()
    {
        var result = NamingTemplateEngine.Validate("{title}{index}{bv}{up}{date}{series}");
        Assert.True(result.IsValid);
    }

    #endregion

    #region NamingTemplateEngine.Preview 测试

    [Fact]
    public void 预览_前3项()
    {
        var items = Enumerable.Range(1, 5)
            .Select(i => new NamingContext { Index = i, Title = $"视频{i}" })
            .ToList();

        var result = NamingTemplateEngine.Preview("{index}.{title}", items);

        Assert.Equal(3, result.Count);
        Assert.Equal("1.视频1", result[0]);
        Assert.Equal("2.视频2", result[1]);
        Assert.Equal("3.视频3", result[2]);
    }

    [Fact]
    public void 预览_不足3项()
    {
        var items = new List<NamingContext>
        {
            new() { Index = 1, Title = "唯一" }
        };

        var result = NamingTemplateEngine.Preview("{title}", items);
        Assert.Single(result);
    }

    [Fact]
    public void 预览_空列表()
    {
        var result = NamingTemplateEngine.Preview("{title}", new List<NamingContext>());
        Assert.Empty(result);
    }

    [Fact]
    public void 预览_经过Sanitize()
    {
        var items = new List<NamingContext>
        {
            new() { Title = "a/b:c" }
        };

        var result = NamingTemplateEngine.Preview("{title}", items);
        Assert.DoesNotContain("/", result[0]);
    }

    #endregion

    #region DownloadPreset 模型测试

    [Fact]
    public void 内置预设_兼容_字段正确()
    {
        var preset = BuiltInPresets.Compatible();
        Assert.Equal(BuiltInPresets.CompatId, preset.Id);
        Assert.Equal("兼容", preset.Name);
        Assert.True(preset.IsBuiltIn);
        Assert.Equal("720p", preset.QualityPreference);
        Assert.False(preset.DownloadDanmaku);
        Assert.False(preset.DownloadSubtitle);
        Assert.False(preset.DownloadCover);
        Assert.Equal("{index}.{title}", preset.NamingTemplate);
    }

    [Fact]
    public void 内置预设_质量_字段正确()
    {
        var preset = BuiltInPresets.Quality();
        Assert.Equal(BuiltInPresets.QualityId, preset.Id);
        Assert.Equal("质量", preset.Name);
        Assert.Equal("highest", preset.QualityPreference);
        Assert.True(preset.DownloadSubtitle);
        Assert.Equal("{title}", preset.NamingTemplate);
    }

    [Fact]
    public void 内置预设_归档_字段正确()
    {
        var preset = BuiltInPresets.Archive();
        Assert.Equal(BuiltInPresets.ArchiveId, preset.Id);
        Assert.Equal("归档", preset.Name);
        Assert.True(preset.UseGroupFolder);
        Assert.True(preset.DownloadDanmaku);
        Assert.True(preset.DownloadSubtitle);
        Assert.True(preset.DownloadCover);
        Assert.Equal("{bv}_{title}", preset.NamingTemplate);
    }

    [Fact]
    public void record相等性_字段相同则相等()
    {
        var a = BuiltInPresets.Compatible();
        var b = BuiltInPresets.Compatible();
        Assert.Equal(a, b);
    }

    [Fact]
    public void 自定义预设_复制并修改()
    {
        var original = BuiltInPresets.Quality();
        var custom = original with { Id = "custom_1", Name = "我的预设", IsBuiltIn = false };

        Assert.NotEqual(original.Id, custom.Id);
        Assert.Equal(original.QualityPreference, custom.QualityPreference);
        Assert.False(custom.IsBuiltIn);
    }

    [Fact]
    public void GetAll_返回3个内置预设()
    {
        var all = BuiltInPresets.GetAll();
        Assert.Equal(3, all.Count);
        Assert.All(all, p => Assert.True(p.IsBuiltIn));
    }

    #endregion

    #region PresetStore 测试

    [Fact]
    public async Task 预设存储_GetAll包含内置预设()
    {
        using var paths = new TestDataPaths();
        var settings = new SettingsStore(paths);
        await settings.InitAsync();
        var store = new PresetStore(paths);

        var all = await store.GetAllAsync();

        Assert.Equal(3, all.Count);
        Assert.Contains(all, p => p.Id == BuiltInPresets.CompatId);
        Assert.Contains(all, p => p.Id == BuiltInPresets.QualityId);
        Assert.Contains(all, p => p.Id == BuiltInPresets.ArchiveId);
    }

    [Fact]
    public async Task 预设存储_自定义预设CRUD往返()
    {
        using var paths = new TestDataPaths();
        var settings = new SettingsStore(paths);
        await settings.InitAsync();
        var store = new PresetStore(paths);

        var custom = new DownloadPreset
        {
            Id = "custom_test",
            Name = "测试预设",
            IsBuiltIn = false,
            QualityPreference = "1080p",
            NamingTemplate = "{bv}_{title}"
        };

        await store.SaveAsync(custom);

        var loaded = await store.GetByIdAsync("custom_test");
        Assert.NotNull(loaded);
        Assert.Equal("测试预设", loaded.Name);
        Assert.Equal("1080p", loaded.QualityPreference);

        // GetAll 应包含 4 个（3 内置 + 1 自定义）
        var all = await store.GetAllAsync();
        Assert.Equal(4, all.Count);

        // 删除
        await store.DeleteAsync("custom_test");
        var afterDelete = await store.GetByIdAsync("custom_test");
        Assert.Null(afterDelete);
    }

    [Fact]
    public async Task 预设存储_内置预设拒绝删除()
    {
        using var paths = new TestDataPaths();
        var settings = new SettingsStore(paths);
        await settings.InitAsync();
        var store = new PresetStore(paths);

        await store.DeleteAsync(BuiltInPresets.CompatId);

        // 内置预设仍然存在
        var preset = await store.GetByIdAsync(BuiltInPresets.CompatId);
        Assert.NotNull(preset);
    }

    [Fact]
    public async Task 预设存储_内置预设拒绝覆盖()
    {
        using var paths = new TestDataPaths();
        var settings = new SettingsStore(paths);
        await settings.InitAsync();
        var store = new PresetStore(paths);

        var modified = BuiltInPresets.Compatible() with { Name = "被修改" };
        await store.SaveAsync(modified);

        // 内置预设名称不变
        var preset = await store.GetByIdAsync(BuiltInPresets.CompatId);
        Assert.Equal("兼容", preset!.Name);
    }

    [Fact]
    public async Task 预设存储_GetById查找内置预设()
    {
        using var paths = new TestDataPaths();
        var settings = new SettingsStore(paths);
        await settings.InitAsync();
        var store = new PresetStore(paths);

        var preset = await store.GetByIdAsync(BuiltInPresets.ArchiveId);
        Assert.NotNull(preset);
        Assert.Equal("归档", preset.Name);
    }

    [Fact]
    public async Task 预设存储_重复保存覆盖()
    {
        using var paths = new TestDataPaths();
        var settings = new SettingsStore(paths);
        await settings.InitAsync();
        var store = new PresetStore(paths);

        var v1 = new DownloadPreset { Id = "dup", Name = "V1", IsBuiltIn = false };
        var v2 = new DownloadPreset { Id = "dup", Name = "V2", IsBuiltIn = false };

        await store.SaveAsync(v1);
        await store.SaveAsync(v2);

        var loaded = await store.GetByIdAsync("dup");
        Assert.Equal("V2", loaded!.Name);

        // 索引中只有一个
        var all = await store.GetAllAsync();
        Assert.Equal(4, all.Count); // 3 内置 + 1 自定义
    }

    #endregion

    #region GetSupportedVariables 测试

    [Fact]
    public void 支持的变量列表_包含6个变量()
    {
        var variables = NamingTemplateEngine.GetSupportedVariables();
        Assert.Equal(6, variables.Count);
        Assert.Contains(variables, v => v.Variable == "{title}");
        Assert.Contains(variables, v => v.Variable == "{index}");
        Assert.Contains(variables, v => v.Variable == "{bv}");
        Assert.Contains(variables, v => v.Variable == "{up}");
        Assert.Contains(variables, v => v.Variable == "{date}");
        Assert.Contains(variables, v => v.Variable == "{series}");
    }

    #endregion
}
