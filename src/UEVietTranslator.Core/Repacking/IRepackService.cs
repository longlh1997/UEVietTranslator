using UEVietTranslator.Core.Common;
using UEVietTranslator.Core.GameProfile;

namespace UEVietTranslator.Core.Repacking;

/// <summary>
/// Đóng gói lại asset đã sửa thành pak/IoStore mới.
///
/// KHÔNG có thư viện .NET nào ghi được pak/IoStore trong stack hiện tại —
/// CUE4Parse read-only, UAssetAPI có API ghi `.pak` (`PakBuilder`) nhưng chỉ
/// là lớp gọi P/Invoke vào 1 thư viện native ("repak", viết bằng Rust) mà
/// bản NuGet UAssetAPI 1.1.0 KHÔNG đóng gói kèm. Xem docs/DECISIONS.md#adr-012
/// — quyết định gọi 2 CLI ngoài (`repak`, `retoc`, cùng tác giả trumank trên
/// GitHub, bản CLI standalone tải từ Releases) qua subprocess, KHÔNG phải
/// P/Invoke trực tiếp hay tự viết writer. Đây là ngoại lệ CÓ CHỦ ĐÍCH với
/// ADR-001 (ADR-001 cấm kiến trúc subprocess-IPC CHO TOÀN BỘ CORE, không cấm
/// gọi 1 CLI tool xác định cho riêng bước repack — đã xác nhận với Hải Long).
/// </summary>
public interface IRepackService
{
    /// <param name="repakExecutablePath">Đường dẫn tới `repak` CLI (null = dùng "repak", tự tìm qua PATH hệ điều hành).</param>
    /// <param name="retocExecutablePath">Đường dẫn tới `retoc` CLI (null = dùng "retoc", tự tìm qua PATH). Chỉ cần khi <see cref="GameProfile.GameProfile.PakFormat"/> là IoStore hoặc Both.</param>
    Task<Result> RepackAsync(
        GameProfile.GameProfile gameProfile,
        string modifiedAssetsDirectory,
        string outputPakPath,
        string? repakExecutablePath,
        string? retocExecutablePath,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken);
}
