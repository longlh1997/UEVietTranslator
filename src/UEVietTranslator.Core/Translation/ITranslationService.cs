using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UEVietTranslator.Core.Common;
using UEVietTranslator.Core.CsvSchema;

namespace UEVietTranslator.Core.Translation;

/// <summary>
/// Dịch các dòng CSV còn <see cref="TranslationStatus.Untranslated"/> qua
/// Gemini API (theo pattern đã dùng ở tool Unity trước đó của Hải Long).
/// Kết quả luôn đánh dấu <see cref="TranslationStatus.MachineTranslated"/>,
/// KHÔNG bao giờ tự đánh dấu <see cref="TranslationStatus.Reviewed"/> — đó
/// là hành động thủ công của người dùng.
/// </summary>
public interface ITranslationService
{
    Task<Result<IReadOnlyList<CsvRow>>> TranslateBatchAsync(
        IReadOnlyList<CsvRow> rows,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Gọi Gemini API (Generative Language API, endpoint
/// <c>generativelanguage.googleapis.com</c>) theo lô — mỗi request gửi
/// <see cref="BatchSize"/> dòng cùng lúc dưới dạng JSON array, dùng chế độ
/// "JSON mode" (<c>responseMimeType: application/json</c> +
/// <c>responseSchema</c>) để Gemini LUÔN trả về đúng 1 JSON array string
/// cùng độ dài — tránh phải tự parse text tự do (dễ vỡ nếu model thêm giải
/// thích/markdown quanh kết quả).
/// </summary>
public sealed class GeminiTranslationService : ITranslationService
{
    // Gửi nhiều dòng/request để giảm số lần gọi API (chi phí + rate limit),
    // nhưng không quá lớn để 1 request lỗi (timeout, model từ chối 1 phần
    // nội dung...) không làm mất công dịch của quá nhiều dòng cùng lúc.
    private const int BatchSize = 20;

    private readonly HttpClient _httpClient;
    private readonly IGeminiSettingsStore _settingsStore;

    public GeminiTranslationService(HttpClient httpClient, IGeminiSettingsStore settingsStore)
    {
        _httpClient = httpClient;
        _settingsStore = settingsStore;
    }

    public async Task<Result<IReadOnlyList<CsvRow>>> TranslateBatchAsync(
        IReadOnlyList<CsvRow> rows, IProgress<ProgressInfo>? progress, CancellationToken cancellationToken)
    {
        var untranslated = rows.Where(r => r.Status == TranslationStatus.Untranslated).ToList();
        var alreadyDone = rows.Where(r => r.Status != TranslationStatus.Untranslated).ToList();

        // Không có gì cần dịch thì khỏi đòi hỏi phải cấu hình Gemini API key
        // — tránh fail vô lý khi người dùng gọi lại TranslateBatchAsync trên
        // CSV đã dịch xong hết từ trước.
        if (untranslated.Count == 0)
            return Result<IReadOnlyList<CsvRow>>.Success(rows);

        var settingsResult = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!settingsResult.IsSuccess)
            return Result<IReadOnlyList<CsvRow>>.Failure(settingsResult.Error!);

        var settings = settingsResult.Value!;

        var result = new List<CsvRow>(alreadyDone);
        var errors = new List<string>();
        var chunks = Chunk(untranslated, BatchSize);
        var doneCount = 0;

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunkResult = await TranslateChunkAsync(chunk, settings, cancellationToken).ConfigureAwait(false);
            if (chunkResult.IsSuccess)
            {
                result.AddRange(chunkResult.Value!);
            }
            else
            {
                // 1 lô lỗi (mạng, rate limit, model từ chối...) không nên
                // chặn các lô còn lại — giữ nguyên các dòng của lô này ở
                // trạng thái cũ (Untranslated), người dùng chạy lại sau.
                errors.Add(chunkResult.Error!);
                result.AddRange(chunk);
            }

            doneCount += chunk.Count;
            progress?.Report(new ProgressInfo(
                doneCount * 100.0 / untranslated.Count, $"Đã dịch {doneCount}/{untranslated.Count} dòng"));
        }

        // Chỉ fail toàn bộ nếu KHÔNG lô nào thành công — còn lại (partial
        // success) vẫn trả Success kèm các dòng lỗi giữ nguyên Untranslated,
        // để người dùng thấy được phần đã dịch thay vì mất hết vì 1 lô lỗi.
        if (errors.Count == chunks.Count)
            return Result<IReadOnlyList<CsvRow>>.Failure(
                $"Tất cả {chunks.Count} lô dịch đều thất bại. Lỗi đầu tiên: {errors[0]}");

        return Result<IReadOnlyList<CsvRow>>.Success(result);
    }

    private async Task<Result<IReadOnlyList<CsvRow>>> TranslateChunkAsync(
        IReadOnlyList<CsvRow> chunk, GeminiSettings settings, CancellationToken cancellationToken)
    {
        var requestBody = BuildRequestBody(chunk);
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{settings.Model}:generateContent?key={settings.ApiKey}";

        HttpResponseMessage response;
        try
        {
            using var content = new StringContent(
                JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<CsvRow>>.Failure($"Gọi Gemini API thất bại (network): {ex.Message}");
        }

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return Result<IReadOnlyList<CsvRow>>.Failure(
                $"Gemini API trả lỗi {(int)response.StatusCode}: {responseText}");

        List<string>? translations;
        try
        {
            var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseText);
            var textJson = geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            if (textJson is null)
                return Result<IReadOnlyList<CsvRow>>.Failure(
                    $"Gemini không trả về nội dung dịch (có thể bị chặn bởi safety filter). Response thô: {responseText}");

            translations = JsonSerializer.Deserialize<List<string>>(textJson);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<CsvRow>>.Failure(
                $"Parse response Gemini thất bại (format không như kỳ vọng): {ex.Message}. Response thô: {responseText}");
        }

        if (translations is null || translations.Count != chunk.Count)
            return Result<IReadOnlyList<CsvRow>>.Failure(
                $"Gemini trả về {translations?.Count.ToString() ?? "null"} bản dịch, kỳ vọng {chunk.Count} — bỏ qua lô này để không ghép sai thứ tự.");

        var translated = chunk.Zip(translations, (row, text) => row with
        {
            TranslatedText = text,
            Status = TranslationStatus.MachineTranslated,
        }).ToList();

        return Result<IReadOnlyList<CsvRow>>.Success(translated);
    }

    // Prompt yêu cầu Gemini trả về ĐÚNG 1 JSON array string, cùng thứ tự và
    // số lượng với input — kết hợp responseSchema bên dưới để ép định dạng,
    // giảm rủi ro model tự ý thêm/bớt dòng hoặc lồng giải thích vào kết quả.
    private static object BuildRequestBody(IReadOnlyList<CsvRow> chunk)
    {
        var sourceTexts = chunk.Select(r => r.SourceText).ToList();
        var promptLines = new StringBuilder();
        promptLines.AppendLine(
            "Dịch các chuỗi text sau đây trong game sang tiếng Việt tự nhiên, giữ nguyên ý nghĩa, " +
            "giữ nguyên mọi placeholder/format code (VD {PlayerName}, %s, \\n, <tag>...) không dịch chúng. " +
            "Trả về JSON array of string, đúng thứ tự, đúng số lượng với input, không thêm giải thích:");
        promptLines.AppendLine(JsonSerializer.Serialize(sourceTexts));

        return new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = promptLines.ToString() } } },
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "ARRAY",
                    items = new { type = "STRING" },
                },
            },
        };
    }

    private static List<List<T>> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        var chunks = new List<List<T>>();
        for (var i = 0; i < source.Count; i += size)
            chunks.Add(source.Skip(i).Take(size).ToList());
        return chunks;
    }

    // DTO khớp response thật của Gemini generateContent — chỉ khai báo field
    // cần dùng, bỏ qua phần còn lại (safetyRatings, usageMetadata...).
    private sealed class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    private sealed class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
    }

    private sealed class GeminiContent
    {
        [JsonPropertyName("parts")]
        public List<GeminiPart>? Parts { get; set; }
    }

    private sealed class GeminiPart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
