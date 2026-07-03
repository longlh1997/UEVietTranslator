using System.Text.Json;
using System.Text.Json.Serialization;
using UEVietTranslator.Core.Common;

namespace UEVietTranslator.Core.GameProfile;

/// <summary>
/// Lưu profile dạng JSON trong thư mục "profiles/" cạnh file thực thi đang
/// chạy — KHÔNG lưu trong thư mục cài game (Steam có thể coi là file lạ khi
/// verify integrity, thư mục cài game cũng có thể read-only). Ghi đè hoàn
/// toàn mỗi lần <see cref="SaveAsync"/> — đây là cache trạng thái mới nhất,
/// không phải audit log giữ lịch sử. Xem docs/DECISIONS.md#adr-007.
/// </summary>
public sealed class GameProfileStore : IGameProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }, // PakFormat đọc/ghi dạng "IoStore" thay vì số, dễ đọc khi mở file thủ công
    };

    private readonly string _profilesDirectory;

    public GameProfileStore() : this(Path.Combine(AppContext.BaseDirectory, "profiles"))
    {
    }

    // Cho phép test tự trỏ vào 1 thư mục tạm thay vì phụ thuộc
    // AppContext.BaseDirectory (khó kiểm soát/dọn dẹp trong test).
    internal GameProfileStore(string profilesDirectory)
    {
        _profilesDirectory = profilesDirectory;
    }

    public async Task<Result> SaveAsync(
        GameProfile profile,
        IReadOnlyList<string> validatedAesKeys,
        CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_profilesDirectory);

            var stored = new StoredGameProfile(profile, validatedAesKeys);
            var filePath = GetFilePath(profile.GameDirectory);

            await using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, stored, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Lưu profile thất bại: {ex.Message}");
        }
    }

    public async Task<Result<StoredGameProfile>> LoadAsync(
        string gameDirectory,
        CancellationToken cancellationToken)
    {
        var filePath = GetFilePath(gameDirectory);

        // "Chưa có profile nào lưu" là luồng bình thường (lần đầu setup 1
        // game), không phải bug — coi như Result.Failure giống mọi lỗi dự
        // kiến được khác trong Core, xem IGameProfileStore.
        if (!File.Exists(filePath))
            return Result<StoredGameProfile>.Failure(
                $"Chưa có profile nào được lưu cho thư mục game này: {gameDirectory}");

        try
        {
            await using var stream = File.OpenRead(filePath);
            var stored = await JsonSerializer.DeserializeAsync<StoredGameProfile>(
                stream, JsonOptions, cancellationToken).ConfigureAwait(false);

            if (stored is null)
                return Result<StoredGameProfile>.Failure(
                    $"File profile rỗng hoặc không đọc được: {filePath}");

            return Result<StoredGameProfile>.Success(stored);
        }
        catch (Exception ex)
        {
            return Result<StoredGameProfile>.Failure(
                $"Đọc profile thất bại (file có thể bị hỏng): {filePath} — {ex.Message}");
        }
    }

    // Tên file lấy từ tên thư mục game (dễ đọc cho Hải Long khi mở thư mục
    // profiles/ ra xem thủ công), KHÔNG theo hash. Chấp nhận rủi ro hiếm gặp
    // 2 đường dẫn game khác nhau trùng tên thư mục (xem docs/DECISIONS.md#adr-007)
    // — chưa xử lý vì use-case hiện tại là 1 người dùng, xử lý 1 game/lần.
    // Đuôi ".gameprofile.json" khớp rule trong .gitignore để không commit
    // nhầm key game bản quyền lên Git.
    private string GetFilePath(string gameDirectory)
    {
        var folderName = Path.GetFileName(
            gameDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(folderName))
            folderName = "unknown-game";

        var invalidChars = Path.GetInvalidFileNameChars();
        var slug = new string(folderName.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray())
            .Replace(' ', '_');

        return Path.Combine(_profilesDirectory, $"{slug}.gameprofile.json");
    }
}
