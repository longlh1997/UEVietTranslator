using UEVietTranslator.Core.GameProfile;
using UEVietTranslator.Core.Unpacking;
using Xunit;

namespace UEVietTranslator.Core.Tests;

public class FModelFallbackAdapterTests : IDisposable
{
    private readonly string _tempDir;

    public FModelFallbackAdapterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "uevt-fmodel-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // "GameDirectory" ở đây đóng vai trò thư mục FModel đã export — xem
    // docs/DECISIONS.md#adr-013.
    private GameProfile.GameProfile MakeProfile(string? engineVersion = "5.3") => new(
        GameDirectory: _tempDir,
        ExecutablePath: string.Empty,
        ExecutableHash: string.Empty,
        EngineVersion: engineVersion,
        PakFormat: PakFormat.Unknown,
        PaksDirectory: string.Empty);

    [Fact]
    public async Task UnpackAsync_LietKeDungVirtualPathVaTraLuonExtractedFilePath()
    {
        var localizationDir = Path.Combine(_tempDir, "GameProject", "Content", "Localization", "Game", "vi");
        Directory.CreateDirectory(localizationDir);
        var locresPath = Path.Combine(localizationDir, "Game.locres");
        await File.WriteAllTextAsync(locresPath, "fake");

        var adapter = new FModelFallbackAdapter();
        var result = await adapter.UnpackAsync(MakeProfile(), aesKeyHex: null, outputDirectory: "unused", progress: null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var asset = Assert.Single(result.Value!);
        Assert.Equal("GameProject/Content/Localization/Game/vi/Game.locres", asset.VirtualPath);
        Assert.Equal(locresPath, asset.ExtractedFilePath);
    }

    [Fact]
    public async Task UnpackAsync_ThuMucKhongTonTai_TraVeFailureRoRang()
    {
        var adapter = new FModelFallbackAdapter();
        var profile = MakeProfile() with { GameDirectory = Path.Combine(_tempDir, "khong-ton-tai") };

        var result = await adapter.UnpackAsync(profile, aesKeyHex: null, outputDirectory: "unused", progress: null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("không tồn tại", result.Error);
    }

    [Fact]
    public async Task ExtractFilesAsync_CopyDungFileVaGiuCauTrucThuMuc()
    {
        var subDir = Path.Combine(_tempDir, "Content", "UI");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(subDir, "Strings.locres"), "noi dung goc");

        var adapter = new FModelFallbackAdapter();
        var outputDir = Path.Combine(_tempDir, "..", "uevt-fmodel-extract-" + Guid.NewGuid());

        var result = await adapter.ExtractFilesAsync(
            MakeProfile(), aesKeyHex: null, ["Content/UI/Strings.locres"], outputDir, progress: null, CancellationToken.None);

        try
        {
            Assert.True(result.IsSuccess, result.Error);
            var asset = Assert.Single(result.Value!);
            Assert.NotNull(asset.ExtractedFilePath);
            Assert.Equal("noi dung goc", await File.ReadAllTextAsync(asset.ExtractedFilePath!));
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExtractFilesAsync_UassetCoUexpDiKem_CopyCaHai()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Table.uasset"), "uasset content");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Table.uexp"), "uexp content");

        var adapter = new FModelFallbackAdapter();
        var outputDir = Path.Combine(Path.GetTempPath(), "uevt-fmodel-extract2-" + Guid.NewGuid());

        var result = await adapter.ExtractFilesAsync(
            MakeProfile(), aesKeyHex: null, ["Table.uasset"], outputDir, progress: null, CancellationToken.None);

        try
        {
            Assert.True(result.IsSuccess, result.Error);
            // Chỉ 1 UnpackedAssetRef trả về (đúng path người gọi yêu cầu),
            // nhưng file .uexp phải được copy "mù" kèm theo để UAssetAPI đọc
            // đủ — xem comment trong FModelFallbackAdapter.
            Assert.Single(result.Value!);
            Assert.True(File.Exists(Path.Combine(outputDir, "Table.uasset")));
            Assert.True(File.Exists(Path.Combine(outputDir, "Table.uexp")));
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task InspectPackagesAsync_FileKhongTonTai_TraVeExportClassNamesRong()
    {
        var adapter = new FModelFallbackAdapter();

        var result = await adapter.InspectPackagesAsync(
            MakeProfile(), aesKeyHex: null, ["khong-ton-tai/Foo.uasset"], progress: null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(result.Value!.Single().ExportClassNames);
    }

    [Fact]
    public async Task InspectPackagesAsync_KhongCoEngineVersionHint_TraVeExportClassNamesRongKhongCrash()
    {
        var filePath = Path.Combine(_tempDir, "Foo.uasset");
        await File.WriteAllTextAsync(filePath, "not a real uasset");

        var adapter = new FModelFallbackAdapter();
        var result = await adapter.InspectPackagesAsync(
            MakeProfile(engineVersion: null), aesKeyHex: null, ["Foo.uasset"], progress: null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(result.Value!.Single().ExportClassNames);
    }
}
