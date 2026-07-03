using System.Text;
using UEVietTranslator.Core.GameProfile;
using Xunit;

namespace UEVietTranslator.Core.Tests;

public class GameProfileDetectorTests : IDisposable
{
    private readonly string _tempRoot;

    public GameProfileDetectorTests()
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
    public async Task DetectAsync_ThuMucKhongTonTai_TraVeFailure()
    {
        var detector = new GameProfileDetector();

        var result = await detector.DetectAsync(
            Path.Combine(_tempRoot, "khong-ton-tai"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("không tồn tại", result.Error);
    }

    [Fact]
    public async Task DetectAsync_KhongCoExe_TraVeFailure()
    {
        var detector = new GameProfileDetector();

        var result = await detector.DetectAsync(_tempRoot, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(".exe", result.Error);
    }

    [Fact]
    public async Task DetectAsync_CoExeVaLegacyPak_NhanDienDungPakFormat()
    {
        // Dựng cấu trúc thư mục UE giả lập tối thiểu:
        // <root>/Game.exe
        // <root>/GameProject/Content/Paks/pakchunk0-Windows.pak
        File.WriteAllBytes(Path.Combine(_tempRoot, "Game.exe"), new byte[] { 0x4D, 0x5A });

        var paksDir = Path.Combine(_tempRoot, "GameProject", "Content", "Paks");
        Directory.CreateDirectory(paksDir);
        File.WriteAllBytes(Path.Combine(paksDir, "pakchunk0-Windows.pak"), new byte[] { 0x00 });

        var detector = new GameProfileDetector();

        var result = await detector.DetectAsync(_tempRoot, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PakFormat.LegacyPak, result.Value!.PakFormat);
        Assert.Equal(paksDir, result.Value.PaksDirectory);
    }

    [Fact]
    public async Task DetectAsync_CoIoStoreFiles_NhanDienLaIoStore()
    {
        File.WriteAllBytes(Path.Combine(_tempRoot, "Game.exe"), new byte[] { 0x4D, 0x5A });

        var paksDir = Path.Combine(_tempRoot, "GameProject", "Content", "Paks");
        Directory.CreateDirectory(paksDir);
        File.WriteAllBytes(Path.Combine(paksDir, "global.utoc"), new byte[] { 0x00 });
        File.WriteAllBytes(Path.Combine(paksDir, "global.ucas"), new byte[] { 0x00 });

        var detector = new GameProfileDetector();

        var result = await detector.DetectAsync(_tempRoot, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PakFormat.IoStore, result.Value!.PakFormat);
    }

    [Fact]
    public async Task DetectAsync_ExeKhongCoBuildVersionString_EngineVersionLaNull()
    {
        File.WriteAllBytes(Path.Combine(_tempRoot, "Game.exe"), new byte[] { 0x4D, 0x5A, 0x00, 0x00 });

        var paksDir = Path.Combine(_tempRoot, "GameProject", "Content", "Paks");
        Directory.CreateDirectory(paksDir);
        File.WriteAllBytes(Path.Combine(paksDir, "pakchunk0-Windows.pak"), new byte[] { 0x00 });

        var detector = new GameProfileDetector();

        var result = await detector.DetectAsync(_tempRoot, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.EngineVersion);
    }

    [Fact]
    public async Task DetectAsync_ExeCoBuildVersionString_NhanDienDungEngineVersion()
    {
        // Mô phỏng chuỗi build-version mà UnrealBuildTool nhúng vào .exe —
        // xem comment trong GameProfileDetector.DetectEngineVersionAsync.
        var exeBytes = new byte[]
        {
            0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00,
        }
        .Concat(Encoding.ASCII.GetBytes("junk-before-marker"))
        .Concat(Encoding.ASCII.GetBytes("5.3.2-25738069+++UE5+Release-5.3"))
        .Concat(Encoding.ASCII.GetBytes("junk-after-marker"))
        .ToArray();
        File.WriteAllBytes(Path.Combine(_tempRoot, "Game.exe"), exeBytes);

        var paksDir = Path.Combine(_tempRoot, "GameProject", "Content", "Paks");
        Directory.CreateDirectory(paksDir);
        File.WriteAllBytes(Path.Combine(paksDir, "pakchunk0-Windows.pak"), new byte[] { 0x00 });

        var detector = new GameProfileDetector();

        var result = await detector.DetectAsync(_tempRoot, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("5.3", result.Value!.EngineVersion);
    }

    [Fact]
    public async Task DetectAsync_BuildVersionStringBiCatNgangRanhGioiChunk_VanNhanDienDuoc()
    {
        // Cố tình đặt marker tràn qua ranh giới chunk 64KB (xem
        // EngineVersionScanChunkSize trong GameProfileDetector) để xác nhận
        // logic "carry" phần đuôi chunk hoạt động đúng, không bỏ lỡ pattern
        // bị cắt ngang.
        const int chunkSize = 64 * 1024;
        var marker = Encoding.ASCII.GetBytes("1.2.3-999+++UE4+Release-4.27");

        var exeBytes = new byte[chunkSize * 2];
        Array.Fill(exeBytes, (byte)0x00);
        var markerStart = chunkSize - 10; // marker nằm vắt qua vị trí chunkSize
        marker.CopyTo(exeBytes, markerStart);

        File.WriteAllBytes(Path.Combine(_tempRoot, "Game.exe"), exeBytes);

        var paksDir = Path.Combine(_tempRoot, "GameProject", "Content", "Paks");
        Directory.CreateDirectory(paksDir);
        File.WriteAllBytes(Path.Combine(paksDir, "pakchunk0-Windows.pak"), new byte[] { 0x00 });

        var detector = new GameProfileDetector();

        var result = await detector.DetectAsync(_tempRoot, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("4.27", result.Value!.EngineVersion);
    }
}
