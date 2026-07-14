using System.Text.Json;
using UEVietTranslator.Core.Common;

namespace UEVietTranslator.Core.Translation;

/// <param name="ApiKey">Gemini API key, dạng plaintext trên đĩa — chấp nhận được vì đây là máy cá nhân 1 người dùng, cùng model rủi ro với AES key trong GameProfileStore (xem docs/DECISIONS.md#adr-007).</param>
/// <param name="Model">Tên model Gemini dùng để dịch (VD "gemini-2.0-flash"). Cho phép đổi model qua config mà không cần sửa code khi Google đổi tên/deprecate model.</param>
public sealed record GeminiSettings(string ApiKey, string Model);

/// <summary>
/// Lưu/đọc Gemini API key theo lựa chọn của Hải Long: file JSON riêng cạnh
/// app (giống cách <c>GameProfileStore</c> lưu AES key — xem
/// docs/DECISIONS.md#adr-011), KHÔNG dùng biến môi trường.
/// </summary>
public interface IGeminiSettingsStore
{
    Task<Result> SaveAsync(GeminiSettings settings, CancellationToken cancellationToken);

    Task<Result<GeminiSettings>> LoadAsync(CancellationToken cancellationToken);
}

public sealed class GeminiSettingsStore : IGeminiSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;

    public GeminiSettingsStore() : this(Path.Combine(AppContext.BaseDirectory, "config", "gemini-settings.json"))
    {
    }

    // Cho phép test tự trỏ vào 1 file tạm — giống lý do trong GameProfileStore.
    internal GeminiSettingsStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<Result> SaveAsync(GeminiSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await using var stream = File.Create(_filePath);
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken).ConfigureAwait(false);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Lưu cấu hình Gemini thất bại: {ex.Message}");
        }
    }

    public async Task<Result<GeminiSettings>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return Result<GeminiSettings>.Failure(
                $"Chưa cấu hình Gemini API key — dùng CLI 'set-gemini-key' để lưu, hoặc tự tạo file: {_filePath}");

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var settings = await JsonSerializer.DeserializeAsync<GeminiSettings>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (settings is null || string.IsNullOrWhiteSpace(settings.ApiKey))
                return Result<GeminiSettings>.Failure($"File cấu hình Gemini rỗng hoặc thiếu ApiKey: {_filePath}");

            return Result<GeminiSettings>.Success(settings);
        }
        catch (Exception ex)
        {
            return Result<GeminiSettings>.Failure($"Đọc cấu hình Gemini thất bại: {_filePath} — {ex.Message}");
        }
    }
}
