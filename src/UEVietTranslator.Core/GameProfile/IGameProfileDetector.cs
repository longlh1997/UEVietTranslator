using UEVietTranslator.Core.Common;

namespace UEVietTranslator.Core.GameProfile;

/// <summary>
/// Quét 1 thư mục cài game để nhận diện thông tin cần cho các bước sau
/// (unpack, tìm AES key...). Không tự parse asset — chỉ nhận diện cấu trúc
/// thư mục và file thực thi. Xem docs/DOMAIN_KNOWLEDGE.md §1.
/// </summary>
public interface IGameProfileDetector
{
    /// <summary>
    /// Quét <paramref name="gameDirectory"/> tìm .exe, tìm Content/Paks/,
    /// phân loại .pak vs .utoc+.ucas. KHÔNG cố gắng đọc nội dung pak ở bước
    /// này (đó là việc của <c>Unpacking</c>) — chỉ nhận diện cấu trúc.
    /// </summary>
    Task<Result<GameProfile>> DetectAsync(string gameDirectory, CancellationToken cancellationToken);
}
