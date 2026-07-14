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
    /// <param name="filePath">Đường dẫn file thật trên đĩa (đã extract qua <see cref="Unpacking.IUnpackProvider.ExtractFilesAsync"/>) — KHÔNG phải virtual path.</param>
    /// <param name="kind">Format của file — quyết định đọc bằng <c>.locres</c> parser hay UAssetAPI (StringTable).</param>
    /// <param name="engineVersionHint">
    /// <see cref="GameProfile.GameProfile.EngineVersion"/> (VD "5.3") — CHỈ cần
    /// cho <see cref="LocalizationFileKind.StringTableAsset"/> (UAssetAPI cần
    /// biết đúng version để parse property đúng layout binary). Bỏ qua với
    /// <see cref="LocalizationFileKind.Locres"/> — format `.locres` tự chứa
    /// version trong header, không phụ thuộc UE engine version.
    /// </param>
    Task<Result<IReadOnlyList<TextEntry>>> ReadAsync(
        string filePath,
        LocalizationFileKind kind,
        string? engineVersionHint,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ghi đè text đã dịch vào asset. <paramref name="translatedEntries"/>
    /// phải khớp Namespace+Key với entry gốc đọc ra từ <see cref="ReadAsync"/>
    /// — không khớp được thì bỏ qua entry đó và ghi log cảnh báo, KHÔNG fail
    /// toàn bộ thao tác (một vài entry lệch không nên chặn cả file). Ghi ĐÈ
    /// TRỰC TIẾP lên <paramref name="filePath"/> — <c>RepackService</c> (Pha 4,
    /// việc sau) sẽ lấy file đã sửa tại chính đường dẫn extract này để đóng
    /// gói lại, không tạo file mới ở nơi khác.
    /// </summary>
    Task<Result> WriteAsync(
        string filePath,
        LocalizationFileKind kind,
        string? engineVersionHint,
        IReadOnlyList<TextEntry> translatedEntries,
        CancellationToken cancellationToken);
}
