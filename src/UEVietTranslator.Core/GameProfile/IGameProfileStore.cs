using UEVietTranslator.Core.Common;
using UEVietTranslator.Core.LocalizationDiscovery;

namespace UEVietTranslator.Core.GameProfile;

/// <summary>
/// GameProfile đã lưu kèm các AES key đã validate được (rỗng nếu game không
/// mã hoá) và danh sách file ngôn ngữ người dùng đã xác nhận thủ công (rỗng
/// nếu chưa chạy bước xác nhận — xem <see cref="IGameProfileStore.SaveConfirmedLocalizationFilesAsync"/>).
/// Xem docs/DECISIONS.md#adr-007 và #adr-009 cho thiết kế đầy đủ.
/// </summary>
public sealed record StoredGameProfile(
    GameProfile Profile,
    IReadOnlyList<string> ValidatedAesKeys,
    IReadOnlyList<ConfirmedLocalizationFile> ConfirmedLocalizationFiles);

/// <summary>
/// Lưu/đọc lại GameProfile + AES key đã xác nhận + lựa chọn file ngôn ngữ đã
/// xác nhận ra đĩa, để không phải chạy lại GameProfileDetector +
/// AesKeyResolver + xác nhận thủ công từ đầu mỗi lần mở app cho cùng 1 game.
/// Xem docs/DECISIONS.md#adr-007 (vị trí file, quy ước đặt tên, và lý do
/// "chưa có profile" là <see cref="Result.Failure"/> — giống hệt cách các
/// module khác trong Core coi "chưa tìm thấy" là 1 luồng lỗi dự kiến được,
/// không dùng kiểu trả về nullable riêng) và #adr-009 (vì sao lựa chọn file
/// ngôn ngữ có method lưu riêng thay vì gộp vào <see cref="SaveAsync"/>).
/// </summary>
public interface IGameProfileStore
{
    Task<Result> SaveAsync(
        GameProfile profile,
        IReadOnlyList<string> validatedAesKeys,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ghi đè TOÀN BỘ danh sách file ngôn ngữ đã xác nhận cho profile đã lưu
    /// ở <paramref name="gameDirectory"/> — KHÔNG đụng tới
    /// <c>Profile</c>/<c>ValidatedAesKeys</c> đã lưu trước đó. Thất bại nếu
    /// chưa có profile nào lưu cho thư mục này (phải gọi <see cref="SaveAsync"/>
    /// trước — thứ tự tự nhiên của pipeline: detect+key xong mới tới xác nhận
    /// file ngôn ngữ).
    /// </summary>
    Task<Result> SaveConfirmedLocalizationFilesAsync(
        string gameDirectory,
        IReadOnlyList<ConfirmedLocalizationFile> confirmedFiles,
        CancellationToken cancellationToken);

    Task<Result<StoredGameProfile>> LoadAsync(
        string gameDirectory,
        CancellationToken cancellationToken);
}
