using UEVietTranslator.Core.Common;

namespace UEVietTranslator.Core.Repacking;

/// <summary>
/// Cài đặt <see cref="IRepackService"/> — xem doc trên interface và
/// docs/DECISIONS.md#adr-012 để hiểu vì sao phải gọi CLI ngoài thay vì thư
/// viện .NET thuần.
///
/// Luồng xử lý:
/// 1. LUÔN đóng gói `modifiedAssetsDirectory` thành 1 file `.pak` "Legacy"
///    trước bằng `repak pack` — kể cả khi game đích dùng IoStore, vì
///    `retoc to-zen` (bước 2) cần nhận input là `.pak`, không nhận thẳng thư
///    mục asset rời.
/// 2. Nếu <see cref="GameProfile.PakFormat"/> là IoStore/Both, convert file
///    `.pak` vừa tạo sang `.utoc`/`.ucas` bằng `retoc to-zen`.
/// </summary>
public sealed class RepackService : IRepackService
{
    public async Task<Result> RepackAsync(
        GameProfile.GameProfile gameProfile,
        string modifiedAssetsDirectory,
        string outputPakPath,
        string? repakExecutablePath,
        string? retocExecutablePath,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (gameProfile.PakFormat == GameProfile.PakFormat.Unknown)
            return Result.Failure("PakFormat của GameProfile là Unknown — không biết đóng gói dạng nào.");

        if (!Directory.Exists(modifiedAssetsDirectory))
            return Result.Failure($"Thư mục asset đã sửa không tồn tại: {modifiedAssetsDirectory}");

        var needsLegacyPakOutput = gameProfile.PakFormat is GameProfile.PakFormat.LegacyPak or GameProfile.PakFormat.Both;
        var needsIoStoreOutput = gameProfile.PakFormat is GameProfile.PakFormat.IoStore or GameProfile.PakFormat.Both;

        var legacyPakPath = needsLegacyPakOutput
            ? EnsureExtension(outputPakPath, ".pak")
            : Path.Combine(Path.GetTempPath(), "uevt-repack-intermediate", Path.GetFileNameWithoutExtension(outputPakPath) + ".pak");

        // repak (theo README công khai) suy ra tên file output từ TÊN THƯ
        // MỤC input (VD "repak pack -v mod" -> "mod.pak" cạnh thư mục "mod")
        // — không có flag đặt tên output tường minh được xác nhận. Để kiểm
        // soát chắc chắn tên/vị trí file output bất kể hành vi CLI cụ thể
        // của bản repak Hải Long cài, ta COPY asset vào 1 thư mục tạm có tên
        // TRÙNG với base filename mong muốn, chạy repak trong thư mục cha
        // của nó, rồi tự move kết quả tới đúng `legacyPakPath`.
        var workDir = Path.Combine(Path.GetTempPath(), "uevt-repack-" + Guid.NewGuid());
        var packFolderName = Path.GetFileNameWithoutExtension(legacyPakPath);
        var packSourceDir = Path.Combine(workDir, packFolderName);

        try
        {
            Directory.CreateDirectory(packSourceDir);
            progress?.Report(new ProgressInfo(-1, "Đang copy asset đã sửa vào thư mục tạm..."));
            CopyDirectoryRecursive(modifiedAssetsDirectory, packSourceDir);

            progress?.Report(new ProgressInfo(-1, "Đang chạy repak pack..."));
            var repakResult = await ExternalToolRunner.RunAsync(
                repakExecutablePath ?? "repak",
                $"pack -v \"{packFolderName}\"",
                workDir,
                cancellationToken).ConfigureAwait(false);
            if (!repakResult.IsSuccess)
                return Result.Failure($"repak pack thất bại: {repakResult.Error}");

            var producedPakPath = Path.Combine(workDir, packFolderName + ".pak");
            if (!File.Exists(producedPakPath))
                return Result.Failure(
                    $"repak pack chạy thành công (exit code 0) nhưng không thấy file output kỳ vọng: {producedPakPath}. " +
                    "Có thể bản repak Hải Long cài đặt tên/vị trí output khác — kiểm tra lại cú pháp CLI thật của bản đang dùng.");

            var legacyPakDirectory = Path.GetDirectoryName(legacyPakPath);
            if (!string.IsNullOrEmpty(legacyPakDirectory))
                Directory.CreateDirectory(legacyPakDirectory);
            File.Move(producedPakPath, legacyPakPath, overwrite: true);

            if (!needsIoStoreOutput)
                return Result.Success();

            var versionResult = ResolveRetocVersion(gameProfile.EngineVersion);
            if (!versionResult.IsSuccess)
                return Result.Failure(versionResult.Error!);

            var utocPath = EnsureExtension(outputPakPath, ".utoc");
            var utocDirectory = Path.GetDirectoryName(utocPath);
            if (!string.IsNullOrEmpty(utocDirectory))
                Directory.CreateDirectory(utocDirectory);

            progress?.Report(new ProgressInfo(-1, "Đang chạy retoc to-zen (convert sang IoStore)..."));
            var retocResult = await ExternalToolRunner.RunAsync(
                retocExecutablePath ?? "retoc",
                $"to-zen \"{legacyPakPath}\" \"{utocPath}\" --version {versionResult.Value}",
                workingDirectory: null,
                cancellationToken).ConfigureAwait(false);
            if (!retocResult.IsSuccess)
                return Result.Failure($"retoc to-zen thất bại: {retocResult.Error}");

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Repack thất bại: {ex.Message}");
        }
        finally
        {
            if (Directory.Exists(workDir))
            {
                try { Directory.Delete(workDir, recursive: true); }
                catch { /* dọn dẹp thư mục tạm thất bại không nên che lấp kết quả repack thật ở trên */ }
            }
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        foreach (var filePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, filePath);
            var destPath = Path.Combine(destDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            File.Copy(filePath, destPath, overwrite: true);
        }
    }

    private static string EnsureExtension(string path, string extension) =>
        Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase)
            ? path
            : Path.ChangeExtension(path, extension);

    // retoc dùng tên version dạng "UE{major}_{minor}" KHÔNG có tiền tố
    // "VER_"/"GAME_" (khác UAssetAPI/CUE4Parse) — xem ví dụ trong README:
    // "retoc to-zen legacy_P.pak iostore.utoc --version UE5_4". Không có
    // danh sách version hợp lệ chính thức để validate trước — để retoc tự
    // báo lỗi rõ ràng qua stderr nếu string sai, AN TOÀN hơn locres vì đây
    // là lỗi "thoát code khác 0 + thông báo rõ", không phải hỏng file âm
    // thầm.
    private static Result<string> ResolveRetocVersion(string? engineVersion)
    {
        if (string.IsNullOrWhiteSpace(engineVersion))
            return Result<string>.Failure(
                "Cần biết UE engine version (VD \"5.4\") để convert sang IoStore bằng retoc — GameProfile.EngineVersion đang trống.");

        var parts = engineVersion.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
            return Result<string>.Failure($"Không parse được engine version: '{engineVersion}'.");

        return Result<string>.Success($"UE{major}_{minor}");
    }
}
