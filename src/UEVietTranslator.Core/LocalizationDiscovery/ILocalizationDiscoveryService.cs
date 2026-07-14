using UEVietTranslator.Core.Common;
using UEVietTranslator.Core.Unpacking;

namespace UEVietTranslator.Core.LocalizationDiscovery;

public enum LocalizationFileKind
{
    Locres,
    StringTableAsset,
    Unknown, // file text-like nghi vấn nhưng chưa nhận diện được format — xem docs/DOMAIN_KNOWLEDGE.md §3c
}

/// <param name="Path">Đường dẫn file đã unpack.</param>
/// <param name="Kind">Loại file nhận diện được.</param>
/// <param name="Confidence">0-1, độ tin cậy của heuristic phát hiện — dùng để sắp xếp gợi ý cho người dùng, KHÔNG dùng để tự động chọn thay người dùng.</param>
public sealed record LocalizationFileCandidate(string Path, LocalizationFileKind Kind, double Confidence);

/// <summary>
/// 1 file ngôn ngữ đã được người dùng xác nhận thủ công (qua CLI/UI), khác
/// <see cref="LocalizationFileCandidate"/> ở chỗ không còn <c>Confidence</c>
/// — đã confirm rồi thì độ tin cậy không còn ý nghĩa. Đây là dữ liệu được
/// lưu lại vào <c>StoredGameProfile</c> để không phải chạy lại
/// <see cref="ILocalizationDiscoveryService.ScanAsync"/> và xác nhận lại từ
/// đầu mỗi lần mở app cho cùng 1 game — xem docs/DECISIONS.md#adr-009.
/// </summary>
public sealed record ConfirmedLocalizationFile(string Path, LocalizationFileKind Kind);

/// <summary>
/// Quét cây asset đã unpack, GỢI Ý (không tự quyết định) file nào có khả
/// năng là file ngôn ngữ. Người dùng LUÔN phải xác nhận/chỉnh sửa lựa chọn
/// qua UI — đây là yêu cầu thiết kế cốt lõi của tool (không có cách nào biết
/// chắc 100% vị trí file ngôn ngữ chỉ từ heuristic). Xem
/// docs/DOMAIN_KNOWLEDGE.md §3.
/// </summary>
public interface ILocalizationDiscoveryService
{
    /// <param name="unpackProvider">
    /// Provider ĐÃ ĐƯỢC người gọi chọn (CUE4Parse chính hay FModel fallback)
    /// — xem docs/DECISIONS.md#adr-002: chọn fallback là hành động của người
    /// dùng, không phải logic tự động, nên KHÔNG inject qua DI ở đây mà nhận
    /// tường minh theo tham số, giống cách <c>UnpackAsync</c> được gọi.
    /// </param>
    /// <param name="gameProfile">Cần để <paramref name="unpackProvider"/> mount lại 1 lần soi export — xem ADR-005.</param>
    /// <param name="aesKeyHex">AES key (nếu có) dùng để mount, giống tham số cùng tên ở <see cref="IUnpackProvider"/>.</param>
    Task<Result<IReadOnlyList<LocalizationFileCandidate>>> ScanAsync(
        IUnpackProvider unpackProvider,
        GameProfile.GameProfile gameProfile,
        string? aesKeyHex,
        IReadOnlyList<UnpackedAssetRef> unpackedAssets,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Cài đặt <see cref="ILocalizationDiscoveryService"/> theo 2 bước chi phí
/// khác nhau — xem docs/ROADMAP.md Pha 3 và docs/DECISIONS.md#adr-005:
/// bước 1 chỉ đọc virtual path (rẻ), bước 2 mount 1 lần và soi export table
/// của các package còn lại chưa phân loại được (đắt hơn nhưng vẫn rẻ hơn
/// nhiều so với ghi asset ra đĩa).
/// </summary>
public sealed class LocalizationDiscoveryService : ILocalizationDiscoveryService
{
    // Đuôi file coi là "package" thật sự của UE, đáng để soi export table ở
    // bước 2. ".uexp"/".ubulk"/".ubulk" là file phụ đi kèm .uasset (property
    // tràn ra ngoài / bulk data), KHÔNG tự đứng riêng là 1 package đọc được
    // qua TryLoadPackage — bỏ qua để khỏi lãng phí lần soi.
    private static readonly string[] PackageExtensions = { ".uasset", ".umap" };

    // Đuôi "text-like" hay được game tự chế dùng cho hệ localization riêng
    // (không theo chuẩn locres/StringTable của UE) — xem
    // docs/DOMAIN_KNOWLEDGE.md §3c. Chỉ dùng làm gợi ý ĐỘ TIN CẬY THẤP, vì
    // phần lớn file các đuôi này trong 1 game thực ra không liên quan gì đến
    // ngôn ngữ (config, save data...).
    private static readonly string[] FallbackTextExtensions = { ".json", ".xml", ".csv", ".txt", ".yaml", ".yml" };

    // Tên export class được coi là StringTable/DataTable thật — dựa theo
    // docs/DOMAIN_KNOWLEDGE.md §3b. "StringTable" là chính xác nhất
    // (Engine.StringTable), "DataTable" rộng hơn (game có thể nhét text dịch
    // trong 1 DataTable tự chế không phải StringTable chuẩn) nên cho độ tin
    // cậy thấp hơn.
    private const string StringTableExportClass = "StringTable";
    private const string DataTableExportClass = "DataTable";

    public async Task<Result<IReadOnlyList<LocalizationFileCandidate>>> ScanAsync(
        IUnpackProvider unpackProvider,
        GameProfile.GameProfile gameProfile,
        string? aesKeyHex,
        IReadOnlyList<UnpackedAssetRef> unpackedAssets,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = new List<LocalizationFileCandidate>();
        var packagePathsForStep2 = new List<string>();

        // Bước 1 — rẻ, chỉ đọc virtual path.
        foreach (var asset in unpackedAssets)
        {
            var path = asset.VirtualPath;
            var isLocres = path.EndsWith(".locres", StringComparison.OrdinalIgnoreCase);
            var underLocalizationFolder = path.Contains("/Content/Localization/", StringComparison.OrdinalIgnoreCase);

            if (isLocres)
            {
                candidates.Add(new LocalizationFileCandidate(path, LocalizationFileKind.Locres, 1.0));
                continue;
            }

            if (underLocalizationFolder)
            {
                // Nằm trong thư mục Localization nhưng không phải .locres
                // (VD: .locmeta, .archive, manifest...) — vẫn đáng nghi
                // nhưng chưa chắc chắn bằng đuôi .locres.
                candidates.Add(new LocalizationFileCandidate(path, LocalizationFileKind.Locres, 0.6));
                continue;
            }

            if (PackageExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            {
                packagePathsForStep2.Add(path);
                continue;
            }

            if (FallbackTextExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                candidates.Add(new LocalizationFileCandidate(path, LocalizationFileKind.Unknown, 0.2));
        }

        // Bước 2 — mount đúng 1 lần cho toàn bộ lô .uasset/.umap còn lại.
        if (packagePathsForStep2.Count > 0)
        {
            var inspectResult = await unpackProvider.InspectPackagesAsync(
                gameProfile, aesKeyHex, packagePathsForStep2, progress, cancellationToken)
                .ConfigureAwait(false);

            if (!inspectResult.IsSuccess)
                return Result<IReadOnlyList<LocalizationFileCandidate>>.Failure(inspectResult.Error!);

            foreach (var summary in inspectResult.Value!)
            {
                if (summary.ExportClassNames.Contains(StringTableExportClass))
                    candidates.Add(new LocalizationFileCandidate(summary.VirtualPath, LocalizationFileKind.StringTableAsset, 0.9));
                else if (summary.ExportClassNames.Contains(DataTableExportClass))
                    candidates.Add(new LocalizationFileCandidate(summary.VirtualPath, LocalizationFileKind.StringTableAsset, 0.5));
            }
        }

        return Result<IReadOnlyList<LocalizationFileCandidate>>.Success(candidates);
    }
}
