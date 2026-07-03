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

/// <summary>Placeholder — implement ở Pha 4.</summary>
public sealed class GeminiTranslationService : ITranslationService
{
    public Task<Result<IReadOnlyList<CsvRow>>> TranslateBatchAsync(
        IReadOnlyList<CsvRow> rows, IProgress<ProgressInfo>? progress, CancellationToken cancellationToken) =>
        throw new NotImplementedException("GeminiTranslationService chưa implement — xem docs/ROADMAP.md Pha 4.");
}
