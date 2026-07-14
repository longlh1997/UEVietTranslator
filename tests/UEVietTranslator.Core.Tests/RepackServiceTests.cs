using UEVietTranslator.Core.GameProfile;
using UEVietTranslator.Core.Repacking;
using Xunit;

namespace UEVietTranslator.Core.Tests;

// Test này chạy fake "repak"/"retoc" bằng shell script — CHỈ chạy được trên
// macOS/Linux (môi trường dev hiện tại). Trên Windows thật, RepackService
// vẫn gọi ProcessStartInfo y hệt nhưng cần fake .bat/.exe tương ứng — không
// phải giới hạn của RepackService, chỉ là giới hạn của cách giả lập binary
// ngoài trong test này. Xem docs/DECISIONS.md#adr-012.
public class RepackServiceTests : IDisposable
{
    private readonly string _tempDir;

    public RepackServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "uevt-repack-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static GameProfile.GameProfile MakeProfile(PakFormat format, string? engineVersion = "5.3") => new(
        GameDirectory: "/game",
        ExecutablePath: "/game/Game.exe",
        ExecutableHash: "HASH",
        EngineVersion: engineVersion,
        PakFormat: format,
        PaksDirectory: "/game/Content/Paks");

    // Tạo 1 shell script thực thi được, giả lập hành vi CLI thật: nhận thư
    // mục input theo đúng convention của repak thật (tên thư mục -> tên file
    // output .pak cạnh nó), ghi vài byte giả làm nội dung pak.
    private string CreateFakeRepakScript(bool succeed)
    {
        var scriptPath = Path.Combine(_tempDir, "fake-repak.sh");
        var script = succeed
            ? "#!/bin/sh\n# args: pack -v <dirname>\nDIRNAME=\"$3\"\necho \"fake pak content\" > \"$DIRNAME.pak\"\nexit 0\n"
            : "#!/bin/sh\necho 'gia lap loi repak' >&2\nexit 1\n";
        File.WriteAllText(scriptPath, script);
        MakeExecutable(scriptPath);
        return scriptPath;
    }

    private string CreateFakeRetocScript()
    {
        var scriptPath = Path.Combine(_tempDir, "fake-retoc.sh");
        // args: to-zen <pakPath> <utocPath> --version <ver>
        var script = "#!/bin/sh\necho \"fake utoc content\" > \"$3\"\necho \"fake ucas content\" > \"${3%.utoc}.ucas\"\nexit 0\n";
        File.WriteAllText(scriptPath, script);
        MakeExecutable(scriptPath);
        return scriptPath;
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return;
        var psi = new System.Diagnostics.ProcessStartInfo("chmod", $"+x \"{path}\"") { UseShellExecute = false };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit();
    }

    [Fact]
    public async Task RepackAsync_LegacyPak_GoiRepakThanhCong_TaoDungFilePak()
    {
        if (OperatingSystem.IsWindows())
            return; // xem ghi chú đầu file — bỏ qua trên Windows CI nếu có.

        var service = new RepackService();
        var assetsDir = Path.Combine(_tempDir, "assets");
        Directory.CreateDirectory(assetsDir);
        await File.WriteAllTextAsync(Path.Combine(assetsDir, "dummy.locres"), "content");

        var outputPakPath = Path.Combine(_tempDir, "output", "VietnameseMod.pak");
        var repakScript = CreateFakeRepakScript(succeed: true);

        var result = await service.RepackAsync(
            MakeProfile(PakFormat.LegacyPak), assetsDir, outputPakPath,
            repakScript, retocExecutablePath: null, progress: null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(File.Exists(outputPakPath));
    }

    [Fact]
    public async Task RepackAsync_RepakThatBai_TraVeFailureRoRangKemStderr()
    {
        if (OperatingSystem.IsWindows())
            return;

        var service = new RepackService();
        var assetsDir = Path.Combine(_tempDir, "assets");
        Directory.CreateDirectory(assetsDir);
        var outputPakPath = Path.Combine(_tempDir, "output", "Mod.pak");
        var repakScript = CreateFakeRepakScript(succeed: false);

        var result = await service.RepackAsync(
            MakeProfile(PakFormat.LegacyPak), assetsDir, outputPakPath,
            repakScript, retocExecutablePath: null, progress: null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("gia lap loi repak", result.Error);
    }

    [Fact]
    public async Task RepackAsync_IoStore_GoiCaRepakVaRetoc_TaoDungFileUtocUcas()
    {
        if (OperatingSystem.IsWindows())
            return;

        var service = new RepackService();
        var assetsDir = Path.Combine(_tempDir, "assets");
        Directory.CreateDirectory(assetsDir);
        await File.WriteAllTextAsync(Path.Combine(assetsDir, "dummy.locres"), "content");

        var outputPath = Path.Combine(_tempDir, "output", "VietnameseMod.utoc");
        var repakScript = CreateFakeRepakScript(succeed: true);
        var retocScript = CreateFakeRetocScript();

        var result = await service.RepackAsync(
            MakeProfile(PakFormat.IoStore), assetsDir, outputPath,
            repakScript, retocScript, progress: null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(File.Exists(outputPath));
        Assert.True(File.Exists(Path.ChangeExtension(outputPath, ".ucas")));
    }

    [Fact]
    public async Task RepackAsync_PakFormatUnknown_TraVeFailureRoRangKhongGoiSubprocess()
    {
        var service = new RepackService();
        var assetsDir = Path.Combine(_tempDir, "assets");
        Directory.CreateDirectory(assetsDir);

        var result = await service.RepackAsync(
            MakeProfile(PakFormat.Unknown), assetsDir, Path.Combine(_tempDir, "out.pak"),
            repakExecutablePath: "/khong/ton/tai/repak", retocExecutablePath: null, progress: null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Unknown", result.Error);
    }
}
