using UEVietTranslator.Core.Common;
using UEVietTranslator.Core.GameProfile;

namespace UEVietTranslator.Core.Unpacking;

/// <summary>
/// 1 asset được liệt kê từ pak/IoStore sau khi mount.
/// </summary>
/// <param name="VirtualPath">Đường dẫn ảo của asset bên trong pak/IoStore (VD:
/// "GameProject/Content/Localization/Game/vi/Game.locres"), dùng để gọi lại
/// <see cref="IUnpackProvider"/> đọc bytes thật khi cần.</param>
/// <param name="ExtractedFilePath">
/// Đường dẫn file thật trên đĩa NẾU asset đã được ghi ra
/// (null nếu chưa). Ở Pha 1, <see cref="CUE4ParseProvider"/> CHỈ liệt kê
/// virtual path, KHÔNG ghi bytes ra đĩa cho mọi asset — 1 game UE5 hiện đại
/// có thể có hàng chục-hàng trăm GB asset trong khi file ngôn ngữ chỉ chiếm
/// phần rất nhỏ, ghi hết ra đĩa ngay từ bước unpack là lãng phí thời gian +
/// dung lượng. Việc đọc bytes thật của 1 asset cụ thể (sau khi
/// LocalizationDiscovery hoặc người dùng đã chọn) sẽ dùng 1 cơ chế đọc theo
/// yêu cầu (on-demand) được thiết kế ở Pha 3/4 — xem docs/ROADMAP.md.
/// </param>
public sealed record UnpackedAssetRef(string VirtualPath, string? ExtractedFilePath);

/// <summary>
/// Thông tin export NHẸ của 1 package (<c>.uasset</c>/<c>.umap</c>) — chỉ
/// tên class của từng export (VD: "StringTable", "Texture2D", "DataTable"),
/// KHÔNG chứa dữ liệu thật (không có pixel, vertex, text...). Dùng để
/// <c>LocalizationDiscoveryService</c> (Pha 3) tự quyết định file nào đáng
/// nghi là StringTable mà không cần <see cref="IUnpackProvider"/> đọc/ghi
/// toàn bộ nội dung asset ra đĩa. Xem docs/DECISIONS.md#adr-005.
/// </summary>
/// <param name="VirtualPath">Đường dẫn ảo của package đã soi.</param>
/// <param name="ExportClassNames">
/// Tên class của từng export trong package. Rỗng nếu package không đọc được
/// (lỗi parse, version không khớp, thiếu mapping...) — đây KHÔNG phải lỗi
/// nghiêm trọng, chỉ đơn giản là package đó không xác định được, người gọi
/// tự quyết định bỏ qua hay báo cho người dùng.
/// </param>
public sealed record PackageExportSummary(string VirtualPath, IReadOnlyList<string> ExportClassNames);

/// <summary>
/// Trừu tượng hoá nguồn asset đã unpack — có 2 cài đặt:
/// <see cref="CUE4ParseProvider"/> (luồng chính, tự động) và
/// <see cref="FModelFallbackAdapter"/> (đọc thư mục người dùng đã tự export
/// bằng FModel — xem docs/DECISIONS.md#adr-002, đây là thao tác THỦ CÔNG,
/// KHÔNG tự động hoá việc gọi FModel).
/// </summary>
public interface IUnpackProvider
{
    /// <param name="outputDirectory">
    /// Thư mục dự phòng cho asset được ghi ra đĩa. Pha 1 chưa dùng tham số
    /// này (xem <see cref="UnpackedAssetRef"/>) — giữ lại trong signature vì
    /// bước extract-theo-yêu-cầu ở Pha 3/4 sẽ cần đến.
    /// </param>
    Task<Result<IReadOnlyList<UnpackedAssetRef>>> UnpackAsync(
        GameProfile.GameProfile gameProfile,
        string? aesKeyHex,
        string outputDirectory,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Mount pak/IoStore ĐÚNG 1 LẦN cho cả lô <paramref name="virtualPaths"/>,
    /// rồi đọc export table (KHÔNG đọc toàn bộ nội dung/pixel/vertex) của
    /// từng package trong danh sách đó — dùng bởi <c>LocalizationDiscoveryService</c>
    /// (Pha 3) để quét hàng loạt <c>.uasset</c> tìm StringTable mà không phải
    /// remount lại pak/IoStore cho từng file (chi phí mount có thể vài giây
    /// đến vài chục giây, remount theo từng file trong hàng nghìn file sẽ
    /// thành hàng giờ — xem docs/DECISIONS.md#adr-005). KHÔNG ghi gì ra đĩa.
    /// </summary>
    Task<Result<IReadOnlyList<PackageExportSummary>>> InspectPackagesAsync(
        GameProfile.GameProfile gameProfile,
        string? aesKeyHex,
        IReadOnlyList<string> virtualPaths,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken);
}

/// <summary>Placeholder — implement ở Pha 5. Xem docs/DECISIONS.md#adr-002.</summary>
public sealed class FModelFallbackAdapter : IUnpackProvider
{
    public Task<Result<IReadOnlyList<UnpackedAssetRef>>> UnpackAsync(
        GameProfile.GameProfile gameProfile,
        string? aesKeyHex,
        string outputDirectory,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException(
            "FModelFallbackAdapter chưa implement — xem docs/ROADMAP.md Pha 5.");

    public Task<Result<IReadOnlyList<PackageExportSummary>>> InspectPackagesAsync(
        GameProfile.GameProfile gameProfile,
        string? aesKeyHex,
        IReadOnlyList<string> virtualPaths,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException(
            "FModelFallbackAdapter chưa implement — xem docs/ROADMAP.md Pha 5.");
}
