using UEVietTranslator.Core.Common;
using UEVietTranslator.Core.GameProfile;

namespace UEVietTranslator.Core.Repacking;

/// <summary>
/// Đóng gói lại asset đã sửa thành pak/IoStore mới. Với luồng CUE4Parse,
/// việc ghi lại có thể cần công cụ ngoài (UnrealPak) tuỳ theo mức hỗ trợ
/// ghi của CUE4Parse tại thời điểm implement — cần xác nhận cụ thể ở Pha 4,
/// ghi quyết định vào docs/DECISIONS.md khi chốt cách làm.
/// </summary>
public interface IRepackService
{
    Task<Result> RepackAsync(
        GameProfile.GameProfile gameProfile,
        string modifiedAssetsDirectory,
        string outputPakPath,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken);
}

/// <summary>Placeholder — implement ở Pha 4.</summary>
public sealed class RepackService : IRepackService
{
    public Task<Result> RepackAsync(
        GameProfile.GameProfile gameProfile, string modifiedAssetsDirectory, string outputPakPath,
        IProgress<ProgressInfo>? progress, CancellationToken cancellationToken) =>
        throw new NotImplementedException("RepackService chưa implement — xem docs/ROADMAP.md Pha 4.");
}
