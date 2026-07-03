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
/// Quét cây asset đã unpack, GỢI Ý (không tự quyết định) file nào có khả
/// năng là file ngôn ngữ. Người dùng LUÔN phải xác nhận/chỉnh sửa lựa chọn
/// qua UI — đây là yêu cầu thiết kế cốt lõi của tool (không có cách nào biết
/// chắc 100% vị trí file ngôn ngữ chỉ từ heuristic). Xem
/// docs/DOMAIN_KNOWLEDGE.md §3.
/// </summary>
public interface ILocalizationDiscoveryService
{
    Task<Result<IReadOnlyList<LocalizationFileCandidate>>> ScanAsync(
        IReadOnlyList<UnpackedAssetRef> unpackedAssets,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken);
}

/// <summary>Placeholder — implement ở Pha 3.</summary>
public sealed class LocalizationDiscoveryService : ILocalizationDiscoveryService
{
    public Task<Result<IReadOnlyList<LocalizationFileCandidate>>> ScanAsync(
        IReadOnlyList<UnpackedAssetRef> unpackedAssets,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException(
            "LocalizationDiscoveryService chưa implement — xem docs/ROADMAP.md Pha 3.");
}
