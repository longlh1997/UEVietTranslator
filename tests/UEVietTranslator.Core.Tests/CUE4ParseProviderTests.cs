using UEVietTranslator.Core.GameProfile;
using UEVietTranslator.Core.Unpacking;
using Xunit;

namespace UEVietTranslator.Core.Tests;

// Test này gọi CUE4Parse thật (không mock) để xác nhận luồng
// Initialize -> Mount -> liệt kê Files không ném exception và trả về
// Result.Failure có thông điệp rõ ràng khi pak là rác/hỏng — CUE4Parse
// không cung cấp pak writer nên không tự tạo được fixture ".pak" hợp lệ để
// test luồng "thành công" ở đây. Luồng thành công thật sự cần verify thủ
// công với game thật (xem docs/ROADMAP.md Pha 1).
public class CUE4ParseProviderTests : IDisposable
{
    private readonly string _tempRoot;

    public CUE4ParseProviderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "uevt-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task UnpackAsync_PakLaRacKhongParseDuoc_TraVeFailureKhongNemException()
    {
        File.WriteAllBytes(Path.Combine(_tempRoot, "Game.exe"), new byte[] { 0x4D, 0x5A });

        var paksDir = Path.Combine(_tempRoot, "GameProject", "Content", "Paks");
        Directory.CreateDirectory(paksDir);
        File.WriteAllBytes(Path.Combine(paksDir, "pakchunk0-Windows.pak"), new byte[] { 0x00 });

        var detectResult = await new GameProfileDetector()
            .DetectAsync(_tempRoot, CancellationToken.None);
        Assert.True(detectResult.IsSuccess);

        var provider = new CUE4ParseProvider();
        var result = await provider.UnpackAsync(
            detectResult.Value!,
            aesKeyHex: null,
            outputDirectory: Path.Combine(_tempRoot, "out"),
            progress: null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Không mount được", result.Error);
    }
}
