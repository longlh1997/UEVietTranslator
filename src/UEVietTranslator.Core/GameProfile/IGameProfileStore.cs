using UEVietTranslator.Core.Common;

namespace UEVietTranslator.Core.GameProfile;

/// <summary>
/// GameProfile đã lưu kèm các AES key đã validate được (rỗng nếu game không
/// mã hoá). Xem docs/DECISIONS.md#adr-007 cho thiết kế đầy đủ.
/// </summary>
public sealed record StoredGameProfile(GameProfile Profile, IReadOnlyList<string> ValidatedAesKeys);

/// <summary>
/// Lưu/đọc lại GameProfile + AES key đã xác nhận ra đĩa, để không phải chạy
/// lại GameProfileDetector + AesKeyResolver từ đầu mỗi lần mở app cho cùng 1
/// game. Xem docs/DECISIONS.md#adr-007 (vị trí file, quy ước đặt tên, và lý
/// do "chưa có profile" là <see cref="Result.Failure"/> — giống hệt cách các
/// module khác trong Core coi "chưa tìm thấy" là 1 luồng lỗi dự kiến được,
/// không dùng kiểu trả về nullable riêng).
/// </summary>
public interface IGameProfileStore
{
    Task<Result> SaveAsync(
        GameProfile profile,
        IReadOnlyList<string> validatedAesKeys,
        CancellationToken cancellationToken);

    Task<Result<StoredGameProfile>> LoadAsync(
        string gameDirectory,
        CancellationToken cancellationToken);
}
