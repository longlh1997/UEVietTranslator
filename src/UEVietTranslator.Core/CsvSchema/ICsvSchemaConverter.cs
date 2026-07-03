using UEVietTranslator.Core.AssetIO;
using UEVietTranslator.Core.Common;

namespace UEVietTranslator.Core.CsvSchema;

public enum TranslationStatus { Untranslated, MachineTranslated, Reviewed }

/// <summary>
/// 1 dòng trong CSV chuẩn hoá. Đây là schema thống nhất kế thừa từ tool
/// Unity trước đó của Hải Long — giữ nguyên các cột này để có thể tái dùng
/// quy trình review/dịch quen thuộc.
/// </summary>
public sealed record CsvRow(
    string SourceFile,
    string Namespace,
    string Key,
    string SourceText,
    string? Context,
    string TranslatedText,
    TranslationStatus Status);

/// <summary>
/// Convert 2 chiều giữa <see cref="TextEntry"/> (đọc từ asset) và CSV chuẩn
/// hoá dùng cho bước dịch/review. CSV là "single source of truth" trong giai
/// đoạn dịch — người dùng chỉnh sửa CSV, không chỉnh trực tiếp asset.
/// </summary>
public interface ICsvSchemaConverter
{
    Task<Result> ExportAsync(
        IReadOnlyDictionary<string, IReadOnlyList<TextEntry>> entriesBySourceFile,
        string outputCsvPath,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<CsvRow>>> ImportAsync(string csvPath, CancellationToken cancellationToken);
}

/// <summary>Placeholder — implement ở Pha 4. Dùng CsvHelper (đã thêm vào Core.csproj).</summary>
public sealed class CsvSchemaConverter : ICsvSchemaConverter
{
    public Task<Result> ExportAsync(
        IReadOnlyDictionary<string, IReadOnlyList<TextEntry>> entriesBySourceFile,
        string outputCsvPath, CancellationToken cancellationToken) =>
        throw new NotImplementedException("CsvSchemaConverter chưa implement — xem docs/ROADMAP.md Pha 4.");

    public Task<Result<IReadOnlyList<CsvRow>>> ImportAsync(string csvPath, CancellationToken cancellationToken) =>
        throw new NotImplementedException("CsvSchemaConverter chưa implement — xem docs/ROADMAP.md Pha 4.");
}
