using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UEVietTranslator.Core.Common;

namespace UEVietTranslator.Core.GameProfile;

/// <summary>
/// Cài đặt mặc định của <see cref="IGameProfileDetector"/>. Chỉ dùng
/// System.IO thuần — KHÔNG phụ thuộc CUE4Parse ở bước này, vì mục tiêu chỉ
/// là nhận diện cấu trúc thư mục/file, chưa cần đọc nội dung pak.
/// </summary>
public sealed class GameProfileDetector : IGameProfileDetector
{
    public async Task<Result<GameProfile>> DetectAsync(string gameDirectory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(gameDirectory))
            return Result<GameProfile>.Failure($"Thư mục game không tồn tại: {gameDirectory}");

        var exePath = FindMainExecutable(gameDirectory);
        if (exePath is null)
            return Result<GameProfile>.Failure(
                "Không tìm thấy file .exe nào trong thư mục game. " +
                "Hãy trỏ đúng thư mục gốc chứa game (chứa file .exe chính).");

        var paksDirectory = FindPaksDirectory(gameDirectory);
        if (paksDirectory is null)
            return Result<GameProfile>.Failure(
                "Không tìm thấy thư mục 'Paks' (thường nằm ở " +
                "<Game>/Content/Paks/). Đây có thể không phải game Unreal Engine, " +
                "hoặc cấu trúc thư mục khác thường — cần kiểm tra thủ công.");

        var pakFormat = DetectPakFormat(paksDirectory);
        if (pakFormat == PakFormat.Unknown)
            return Result<GameProfile>.Failure(
                $"Tìm thấy thư mục Paks ({paksDirectory}) nhưng không có file " +
                ".pak hay .utoc/.ucas nào bên trong.");

        var exeHash = await ComputeFileHashAsync(exePath, cancellationToken);
        var engineVersion = await DetectEngineVersionAsync(exePath, cancellationToken);

        var profile = new GameProfile(
            GameDirectory: gameDirectory,
            ExecutablePath: exePath,
            ExecutableHash: exeHash,
            EngineVersion: engineVersion,
            PakFormat: pakFormat,
            PaksDirectory: paksDirectory);

        return Result<GameProfile>.Success(profile);
    }

    /// <summary>
    /// Heuristic tìm .exe chính: game UE thường có nhiều .exe phụ (crash
    /// reporter, EOS bootstrapper, launcher...). Ưu tiên .exe lớn nhất nằm
    /// ngoài các thư mục con rõ ràng là công cụ phụ trợ.
    /// </summary>
    private static string? FindMainExecutable(string gameDirectory)
    {
        var ignoredNameFragments = new[] { "crashreport", "battleye", "easyanticheat", "eos", "redist", "vcredist", "ue4prereq", "ue5prereq" };

        var candidates = Directory.EnumerateFiles(gameDirectory, "*.exe", SearchOption.AllDirectories)
            .Where(path => !ignoredNameFragments.Any(fragment =>
                path.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => f.Length)
            .ToList();

        return candidates.FirstOrDefault()?.FullName;
    }

    private static string? FindPaksDirectory(string gameDirectory)
    {
        // Cấu trúc chuẩn UE: <Game>/<ProjectName>/Content/Paks/
        // Tìm bất kỳ thư mục nào tên "Paks" nằm dưới 1 thư mục "Content".
        return Directory.EnumerateDirectories(gameDirectory, "Paks", SearchOption.AllDirectories)
            .FirstOrDefault(dir =>
                string.Equals(Path.GetFileName(Path.GetDirectoryName(dir)), "Content", StringComparison.OrdinalIgnoreCase));
    }

    private static PakFormat DetectPakFormat(string paksDirectory)
    {
        var files = Directory.EnumerateFiles(paksDirectory, "*", SearchOption.AllDirectories).ToList();

        var hasLegacyPak = files.Any(f => f.EndsWith(".pak", StringComparison.OrdinalIgnoreCase));
        var hasIoStore = files.Any(f => f.EndsWith(".utoc", StringComparison.OrdinalIgnoreCase))
                          && files.Any(f => f.EndsWith(".ucas", StringComparison.OrdinalIgnoreCase));

        return (hasLegacyPak, hasIoStore) switch
        {
            (true, true) => PakFormat.Both,
            (true, false) => PakFormat.LegacyPak,
            (false, true) => PakFormat.IoStore,
            _ => PakFormat.Unknown,
        };
    }

    private static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes);
    }

    // UnrealBuildTool nhúng 1 chuỗi build-version dạng ASCII vào .exe để phục
    // vụ crash reporting, ví dụ: "5.3.2-25738069+++UE5+Release-5.3". Chuỗi
    // này tồn tại trong hầu hết game UE4/UE5 build ở chế độ Release/Shipping,
    // kể cả khi không có source engine. Dùng cách quét chuỗi này thay vì đọc
    // PE version resource của .exe vì: (1) PE VERSIONINFO không đáng tin cậy
    // cross-platform trên Linux, (2) nhiều game không set VERSIONINFO nhưng
    // vẫn có chuỗi build-version này. Nếu không tìm thấy, trả về null — đây
    // là optional, KHÔNG làm DetectAsync thất bại. Xem docs/DOMAIN_KNOWLEDGE.md
    // mục "Cạm bẫy đã biết".
    private static readonly Regex EngineVersionPattern = new(
        @"\+\+\+UE[45]\+Release-(?<version>\d+\.\d+)", RegexOptions.Compiled);

    // Đọc theo chunk thay vì load cả .exe vào RAM — file .exe của game UE
    // hiện đại có thể vài trăm MB tới hàng GB. Giữ lại phần đuôi mỗi chunk
    // (overlap) để không bỏ lỡ pattern bị cắt ngang ở ranh giới 2 chunk.
    private const int EngineVersionScanChunkSize = 64 * 1024;
    private const int EngineVersionScanOverlap = 64; // đủ dài hơn pattern dài nhất có thể khớp

    private static async Task<string?> DetectEngineVersionAsync(string exePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(exePath);
        var buffer = new byte[EngineVersionScanChunkSize];
        var carry = Array.Empty<byte>();

        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, EngineVersionScanChunkSize), cancellationToken);
            if (bytesRead == 0)
                break;

            var window = new byte[carry.Length + bytesRead];
            carry.CopyTo(window, 0);
            Array.Copy(buffer, 0, window, carry.Length, bytesRead);

            // ASCII encoding thay ký tự >127 thành '?' thay vì lỗi — đủ dùng
            // vì ta chỉ tìm 1 pattern ASCII thuần, không cần decode chính xác
            // toàn bộ binary.
            var text = Encoding.ASCII.GetString(window);
            var match = EngineVersionPattern.Match(text);
            if (match.Success)
                return match.Groups["version"].Value;

            carry = window.Length > EngineVersionScanOverlap
                ? window[^EngineVersionScanOverlap..]
                : window;
        }

        return null;
    }
}
