using CsvHelper;
using CsvHelper.Configuration;
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

/// <summary>
/// Cài đặt <see cref="ICsvSchemaConverter"/> bằng CsvHelper. Rủi ro thấp —
/// không đụng format binary UE, chỉ là I/O text thuần theo schema tự định
/// nghĩa.
/// </summary>
public sealed class CsvSchemaConverter : ICsvSchemaConverter
{
    // record dùng nội bộ để CsvHelper tự map header <-> property theo tên
    // (case-insensitive mặc định) — KHÔNG dùng thẳng CsvRow vì CsvRow có
    // TranslationStatus dạng enum và TranslatedText/SourceFile không map gọn
    // gàng theo đúng thứ tự cột mong muốn nếu để CsvHelper tự suy luận.
    private sealed class CsvRecord
    {
        public string SourceFile { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string SourceText { get; set; } = string.Empty;
        public string? Context { get; set; }
        public string TranslatedText { get; set; } = string.Empty;
        public string Status { get; set; } = nameof(TranslationStatus.Untranslated);
    }

    public async Task<Result> ExportAsync(
        IReadOnlyDictionary<string, IReadOnlyList<TextEntry>> entriesBySourceFile,
        string outputCsvPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(outputCsvPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await using var writer = new StreamWriter(outputCsvPath);
            await using var csv = new CsvWriter(writer, CsvConfig);

            var records = entriesBySourceFile
                .SelectMany(kv => kv.Value.Select(entry => new CsvRecord
                {
                    SourceFile = kv.Key,
                    Namespace = entry.Namespace,
                    Key = entry.Key,
                    SourceText = entry.SourceText,
                    Context = null,
                    // TranslatedText để rỗng lúc export lần đầu — người dùng
                    // hoặc TranslationService (Pha 4) sẽ điền vào cột này ở
                    // bước dịch, KHÔNG copy sẵn SourceText vào đây (dễ nhầm
                    // "đã dịch" khi thực ra chỉ là text gốc).
                    TranslatedText = string.Empty,
                    Status = nameof(TranslationStatus.Untranslated),
                }));

            csv.Context.RegisterClassMap<CsvRecordMap>();
            await csv.WriteRecordsAsync(records, cancellationToken).ConfigureAwait(false);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Export CSV thất bại: {outputCsvPath} — {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<CsvRow>>> ImportAsync(string csvPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(csvPath))
            return Result<IReadOnlyList<CsvRow>>.Failure($"File CSV không tồn tại: {csvPath}");

        try
        {
            using var reader = new StreamReader(csvPath);
            using var csv = new CsvReader(reader, CsvConfig);
            csv.Context.RegisterClassMap<CsvRecordMap>();

            var rows = new List<CsvRow>();
            await foreach (var record in csv.GetRecordsAsync<CsvRecord>(cancellationToken).ConfigureAwait(false))
            {
                // Status trong CSV là text tự do do người dùng có thể gõ tay
                // (VD sửa "Untranslated" -> "Reviewed" trong Excel) — parse
                // lỗi thì fallback về Untranslated thay vì fail cả file, để 1
                // dòng gõ sai không chặn import toàn bộ CSV lớn.
                var status = Enum.TryParse<TranslationStatus>(record.Status, ignoreCase: true, out var parsed)
                    ? parsed
                    : TranslationStatus.Untranslated;

                rows.Add(new CsvRow(
                    record.SourceFile, record.Namespace, record.Key, record.SourceText,
                    record.Context, record.TranslatedText, status));
            }

            return Result<IReadOnlyList<CsvRow>>.Success(rows);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<CsvRow>>.Failure($"Import CSV thất bại: {csvPath} — {ex.Message}");
        }
    }

    private static readonly CsvConfiguration CsvConfig = new(System.Globalization.CultureInfo.InvariantCulture)
    {
        // Text dịch có thể chứa dấu phẩy/xuống dòng (VD hội thoại nhiều câu)
        // — CsvHelper tự quote khi cần theo chuẩn RFC 4180, không cần cấu
        // hình thêm gì đặc biệt ở đây ngoài Encoding UTF-8 mặc định của
        // StreamWriter/StreamReader (giữ dấu tiếng Việt).
    };

    private sealed class CsvRecordMap : ClassMap<CsvRecord>
    {
        public CsvRecordMap()
        {
            Map(m => m.SourceFile).Name("SourceFile");
            Map(m => m.Namespace).Name("Namespace");
            Map(m => m.Key).Name("Key");
            Map(m => m.SourceText).Name("SourceText");
            Map(m => m.Context).Name("Context");
            Map(m => m.TranslatedText).Name("TranslatedText");
            Map(m => m.Status).Name("Status");
        }
    }
}
