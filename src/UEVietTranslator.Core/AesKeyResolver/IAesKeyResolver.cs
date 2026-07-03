using UEVietTranslator.Core.Common;

namespace UEVietTranslator.Core.AesKeyResolver;

/// <summary>1 key ứng viên/đã xác nhận, dạng hex string (64 ký tự = 32 byte AES-256).</summary>
public sealed record AesKeyCandidate(string HexKey, bool Validated);

/// <summary>
/// Tìm và xác thực AES key dùng để giải mã pak/IoStore của game, bằng cách
/// quét file thực thi. Xem docs/DOMAIN_KNOWLEDGE.md §2 trước khi implement —
/// đặc biệt lưu ý: PHẢI thử unpack không key trước khi gọi tới resolver này
/// (không phải game nào cũng mã hoá).
/// </summary>
public interface IAesKeyResolver
{
    /// <summary>
    /// Quét <paramref name="executablePath"/> tìm candidate key, rồi validate
    /// từng candidate bằng cách thử decrypt + so khớp magic number của
    /// <paramref name="paksDirectory"/>. Trả về key đầu tiên validate thành
    /// công (Validated = true), hoặc danh sách candidate chưa validate được
    /// nếu không cái nào khớp (để UI hiển thị cho người dùng tự thử/nhập tay).
    /// </summary>
    Task<Result<IReadOnlyList<AesKeyCandidate>>> ResolveAsync(
        string executablePath,
        string paksDirectory,
        IProgress<Common.ProgressInfo>? progress,
        CancellationToken cancellationToken);
}
