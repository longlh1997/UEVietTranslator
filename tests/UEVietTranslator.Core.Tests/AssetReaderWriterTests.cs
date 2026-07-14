using UEVietTranslator.Core.AssetIO;
using UEVietTranslator.Core.LocalizationDiscovery;
using Xunit;

namespace UEVietTranslator.Core.Tests;

public class AssetReaderWriterTests : IDisposable
{
    private readonly string _tempDir;

    public AssetReaderWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "uevt-assetio-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // Đây là bài test QUAN TRỌNG NHẤT cho .locres — không có file .locres
    // thật để đối chiếu (xem docs/DECISIONS.md#adr-010), nên cách verify tốt
    // nhất hiện có là round-trip: ghi bằng LocresBinaryFormat.Write (tự viết)
    // rồi đọc lại bằng CUE4Parse.FTextLocalizationResource (thư viện cộng
    // đồng tin cậy) qua chính AssetReaderWriter — nếu 2 chiều khớp nhau, ít
    // nhất khẳng định writer tự viết đúng theo cách CUE4Parse hiểu, dù vẫn
    // CHƯA chắc engine thật của UE đọc được (cần Hải Long test với game
    // thật).
    [Fact]
    public async Task Locres_GhiRoiDoc_TraVeDungEntryVoiKyTuTiengViet()
    {
        var writer = new AssetReaderWriter();
        var filePath = Path.Combine(_tempDir, "vi.locres");

        var entries = new List<TextEntry>
        {
            new("Namespace1", "Key_A", "Xin chào, đây là bản dịch tiếng Việt!"),
            new("Namespace1", "Key_B", "Chuỗi rỗng test:"),
            new("Namespace2", "Key_C", "Namespace khác nhau vẫn phải tách đúng"),
        };

        var writeResult = await writer.WriteAsync(
            filePath, LocalizationFileKind.Locres, engineVersionHint: null, entries, CancellationToken.None);
        Assert.True(writeResult.IsSuccess, writeResult.Error);

        var readResult = await writer.ReadAsync(
            filePath, LocalizationFileKind.Locres, engineVersionHint: null, CancellationToken.None);
        Assert.True(readResult.IsSuccess, readResult.Error);

        var readBack = readResult.Value!;
        Assert.Equal(entries.Count, readBack.Count);
        foreach (var original in entries)
        {
            var match = readBack.SingleOrDefault(e => e.Namespace == original.Namespace && e.Key == original.Key);
            Assert.NotNull(match);
            Assert.Equal(original.SourceText, match!.SourceText);
        }
    }

    [Fact]
    public async Task Locres_GhiRoiDoc_ChuoiRong_KhongCrash()
    {
        var writer = new AssetReaderWriter();
        var filePath = Path.Combine(_tempDir, "empty.locres");

        var entries = new List<TextEntry> { new("NS", "EmptyKey", string.Empty) };

        var writeResult = await writer.WriteAsync(
            filePath, LocalizationFileKind.Locres, engineVersionHint: null, entries, CancellationToken.None);
        Assert.True(writeResult.IsSuccess, writeResult.Error);

        var readResult = await writer.ReadAsync(
            filePath, LocalizationFileKind.Locres, engineVersionHint: null, CancellationToken.None);
        Assert.True(readResult.IsSuccess, readResult.Error);
        Assert.Equal(string.Empty, readResult.Value!.Single().SourceText);
    }

    [Fact]
    public async Task StringTable_ThieuEngineVersionHint_TraVeFailureRoRangKhongCanMoFile()
    {
        var writer = new AssetReaderWriter();

        // filePath không tồn tại — nếu code cố mở file trước khi check
        // engineVersionHint thì test này sẽ fail vì exception khác, không
        // phải Failure message mong đợi. Xác nhận thứ tự check đúng: validate
        // input trước khi I/O.
        var result = await writer.ReadAsync(
            "/khong/ton/tai.uasset", LocalizationFileKind.StringTableAsset, engineVersionHint: null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("engine version", result.Error);
    }

    [Fact]
    public async Task StringTable_EngineVersionKhongMapDuoc_TraVeFailureRoRang()
    {
        var writer = new AssetReaderWriter();

        var result = await writer.ReadAsync(
            "/khong/ton/tai.uasset", LocalizationFileKind.StringTableAsset, engineVersionHint: "999.999", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("VER_UE999_999", result.Error);
    }
}
