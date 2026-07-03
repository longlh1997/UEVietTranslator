using UEVietTranslator.Core.Common;
using UEVietTranslator.Core.LocalizationDiscovery;

namespace UEVietTranslator.Core.AssetIO;

/// <param name="Namespace">Namespace của entry (rỗng nếu format không có khái niệm namespace, VD StringTable).</param>
/// <param name="Key">Key định danh entry — dùng để ghi lại đúng vị trí khi repack.</param>
/// <param name="SourceText">Text gốc (thường tiếng Anh).</param>
public sealed record TextEntry(string Namespace, string Key, string SourceText);

/// <summary>
/// Đọc/ghi text entries từ file ngôn ngữ (.locres hoặc StringTable trong
/// .uasset qua UAssetAPI). 2 format khác nhau đáng kể ở tầng binary nhưng
/// phơi ra cùng 1 interface để CsvSchema không cần biết format gốc. Xem
/// docs/DOMAIN_KNOWLEDGE.md §3.
/// </summary>
public interface IAssetReaderWriter
{
    Task<Result<IReadOnlyList<TextEntry>>> ReadAsync(
        string filePath,
        LocalizationFileKind kind,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ghi đè text đã dịch vào asset. <paramref name="translatedEntries"/>
    /// phải khớp Namespace+Key với entry gốc đọc ra từ <see cref="ReadAsync"/>
    /// — không khớp được thì bỏ qua entry đó và ghi log cảnh báo, KHÔNG fail
    /// toàn bộ thao tác (một vài entry lệch không nên chặn cả file).
    /// </summary>
    Task<Result> WriteAsync(
        string filePath,
        LocalizationFileKind kind,
        IReadOnlyList<TextEntry> translatedEntries,
        CancellationToken cancellationToken);
}

/// <summary>Placeholder — implement ở Pha 4.</summary>
public sealed class AssetReaderWriter : IAssetReaderWriter
{
    public Task<Result<IReadOnlyList<TextEntry>>> ReadAsync(
        string filePath, LocalizationFileKind kind, CancellationToken cancellationToken) =>
        throw new NotImplementedException("AssetReaderWriter chưa implement — xem docs/ROADMAP.md Pha 4.");

    public Task<Result> WriteAsync(
        string filePath, LocalizationFileKind kind, IReadOnlyList<TextEntry> translatedEntries,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException("AssetReaderWriter chưa implement — xem docs/ROADMAP.md Pha 4.");
}
