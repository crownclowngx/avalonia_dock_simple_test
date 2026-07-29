using System.Buffers.Binary;
using BiliDownloader.Services.Download.Extras;
using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.Tests;

public sealed class DependencyCompatibilityTests
{
    // 该字节样本按 Protobuf wire format 固定编码，不能在测试运行时通过 protobuf-net
    // 重新序列化生成，否则序列化和反序列化同时回归时测试仍可能错误通过。
    private static readonly byte[] DanmakuFixture =
    [
        0x0A, 0x29,
        0x08, 0x96, 0x01,
        0x10, 0xE2, 0x09,
        0x18, 0x01,
        0x20, 0x19,
        0x28, 0xFF, 0xFF, 0xFF, 0x07,
        0x32, 0x03, 0x61, 0x62, 0x63,
        0x3A, 0x06, 0xE6, 0xB5, 0x8B, 0xE8, 0xAF, 0x95,
        0x40, 0xC0, 0x90, 0xEE, 0x86, 0x06,
        0x50, 0x00,
        0x5A, 0x03, 0x31, 0x35, 0x30
    ];

    [Fact]
    public void 固定Protobuf字节可解码为预期弹幕()
    {
        var item = Assert.Single(ProtobufDanmakuDecoder.Decode(DanmakuFixture));

        Assert.Equal(150, item.Id);
        Assert.Equal(1250, item.Progress);
        Assert.Equal(1, item.Mode);
        Assert.Equal(25, item.Fontsize);
        Assert.Equal(16_777_215u, item.Color);
        Assert.Equal("abc", item.MidHash);
        Assert.Equal("测试", item.Content);
        Assert.Equal(1_625_000_000, item.Ctime);
        Assert.Equal("150", item.IdStr);
    }

    [Fact]
    public void 损坏Protobuf输入保持返回空集合的既有契约()
    {
        var result = ProtobufDanmakuDecoder.Decode([0x0A, 0x7F, 0x08]);

        Assert.Empty(result);
    }

    [Fact]
    public void 登录二维码编码结果是可解码的PNG()
    {
        var png = QrCodePngEncoder.Encode(
            "https://example.invalid/login?key=phase3");

        Assert.True(png.AsSpan().StartsWith(
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png, 12, 4));
        Assert.True(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)) > 0);
        Assert.True(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)) > 0);
    }
}
