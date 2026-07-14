using UEVietTranslator.Core.Common;
using UEVietTranslator.Core.GameProfile;
using UEVietTranslator.Core.LocalizationDiscovery;
using UEVietTranslator.Core.Unpacking;
using Xunit;

namespace UEVietTranslator.Core.Tests;

public class LocalizationDiscoveryServiceTests
{
    // Fake IUnpackProvider — test ScanAsync theo heuristic thuần tuý, không
    // cần CUE4Parse thật (không có pak hợp lệ để test fixture).
    // InspectPackagesAsync trả lại export class đã set trước theo virtual
    // path, mô phỏng kết quả mount thật sự từ CUE4ParseProvider.
    private sealed class FakeUnpackProvider : IUnpackProvider
    {
        public IReadOnlyDictionary<string, IReadOnlyList<string>> ExportsByPath { get; init; } =
            new Dictionary<string, IReadOnlyList<string>>();

        public IReadOnlyList<string>? ReceivedVirtualPaths { get; private set; }

        public Task<Result<IReadOnlyList<UnpackedAssetRef>>> UnpackAsync(
            GameProfile.GameProfile gameProfile,
            string? aesKeyHex,
            string outputDirectory,
            IProgress<ProgressInfo>? progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Không dùng trong test này.");

        public Task<Result<IReadOnlyList<PackageExportSummary>>> InspectPackagesAsync(
            GameProfile.GameProfile gameProfile,
            string? aesKeyHex,
            IReadOnlyList<string> virtualPaths,
            IProgress<ProgressInfo>? progress,
            CancellationToken cancellationToken)
        {
            ReceivedVirtualPaths = virtualPaths;

            var summaries = virtualPaths
                .Select(path => new PackageExportSummary(
                    path,
                    ExportsByPath.TryGetValue(path, out var exports) ? exports : Array.Empty<string>()))
                .ToList();

            return Task.FromResult(Result<IReadOnlyList<PackageExportSummary>>.Success(summaries));
        }

        public Task<Result<IReadOnlyList<UnpackedAssetRef>>> ExtractFilesAsync(
            GameProfile.GameProfile gameProfile,
            string? aesKeyHex,
            IReadOnlyList<string> virtualPaths,
            string outputDirectory,
            IProgress<ProgressInfo>? progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Không dùng trong test này.");
    }

    private static readonly GameProfile.GameProfile DummyProfile = new(
        GameDirectory: "C:/Game",
        ExecutablePath: "C:/Game/Game.exe",
        ExecutableHash: "deadbeef",
        EngineVersion: "5.3",
        PakFormat: PakFormat.IoStore,
        PaksDirectory: "C:/Game/Content/Paks");

    [Fact]
    public async Task ScanAsync_FileDuoiLocres_NhanDienNgayKhongCanSoiPackage()
    {
        var assets = new[]
        {
            new UnpackedAssetRef("GameProject/Content/Localization/Game/vi/Game.locres", null),
        };
        var provider = new FakeUnpackProvider();

        var result = await new LocalizationDiscoveryService().ScanAsync(
            provider, DummyProfile, aesKeyHex: null, assets, progress: null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var candidate = Assert.Single(result.Value!);
        Assert.Equal(LocalizationFileKind.Locres, candidate.Kind);
        Assert.Equal(1.0, candidate.Confidence);
        Assert.Null(provider.ReceivedVirtualPaths); // không gọi InspectPackagesAsync vì bước 1 đã đủ.
    }

    [Fact]
    public async Task ScanAsync_UassetCoExportStringTable_NhanDienQuaBuoc2()
    {
        const string path = "GameProject/Content/UI/Texts.uasset";
        var assets = new[] { new UnpackedAssetRef(path, null) };
        var provider = new FakeUnpackProvider
        {
            ExportsByPath = new Dictionary<string, IReadOnlyList<string>>
            {
                [path] = new[] { "StringTable" },
            },
        };

        var result = await new LocalizationDiscoveryService().ScanAsync(
            provider, DummyProfile, aesKeyHex: null, assets, progress: null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var candidate = Assert.Single(result.Value!);
        Assert.Equal(path, candidate.Path);
        Assert.Equal(LocalizationFileKind.StringTableAsset, candidate.Kind);
        Assert.Equal(new[] { path }, provider.ReceivedVirtualPaths);
    }

    [Fact]
    public async Task ScanAsync_UassetKhongPhaiStringTable_KhongDuaVaoKetQua()
    {
        const string path = "GameProject/Content/Textures/Rock.uasset";
        var assets = new[] { new UnpackedAssetRef(path, null) };
        var provider = new FakeUnpackProvider
        {
            ExportsByPath = new Dictionary<string, IReadOnlyList<string>>
            {
                [path] = new[] { "Texture2D" },
            },
        };

        var result = await new LocalizationDiscoveryService().ScanAsync(
            provider, DummyProfile, aesKeyHex: null, assets, progress: null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task ScanAsync_FileJsonNgoaiLocalizationFolder_DuaVaoUnknownDoTinCayThap()
    {
        var assets = new[] { new UnpackedAssetRef("GameProject/Config/strings_vi.json", null) };
        var provider = new FakeUnpackProvider();

        var result = await new LocalizationDiscoveryService().ScanAsync(
            provider, DummyProfile, aesKeyHex: null, assets, progress: null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var candidate = Assert.Single(result.Value!);
        Assert.Equal(LocalizationFileKind.Unknown, candidate.Kind);
        Assert.True(candidate.Confidence < 0.5);
    }

    [Fact]
    public async Task ScanAsync_InspectPackagesThatBai_TraVeFailure()
    {
        const string path = "GameProject/Content/UI/Texts.uasset";
        var assets = new[] { new UnpackedAssetRef(path, null) };
        var provider = new FailingInspectProvider();

        var result = await new LocalizationDiscoveryService().ScanAsync(
            provider, DummyProfile, aesKeyHex: null, assets, progress: null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("mount lỗi giả lập", result.Error);
    }

    private sealed class FailingInspectProvider : IUnpackProvider
    {
        public Task<Result<IReadOnlyList<UnpackedAssetRef>>> UnpackAsync(
            GameProfile.GameProfile gameProfile,
            string? aesKeyHex,
            string outputDirectory,
            IProgress<ProgressInfo>? progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<IReadOnlyList<PackageExportSummary>>> InspectPackagesAsync(
            GameProfile.GameProfile gameProfile,
            string? aesKeyHex,
            IReadOnlyList<string> virtualPaths,
            IProgress<ProgressInfo>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyList<PackageExportSummary>>.Failure("mount lỗi giả lập"));

        public Task<Result<IReadOnlyList<UnpackedAssetRef>>> ExtractFilesAsync(
            GameProfile.GameProfile gameProfile,
            string? aesKeyHex,
            IReadOnlyList<string> virtualPaths,
            string outputDirectory,
            IProgress<ProgressInfo>? progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
