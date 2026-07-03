# Coding Standards

## Ngôn ngữ & format
- C# 12 / .NET 8, `Nullable` enable ở mọi project.
- File-scoped namespace (`namespace Foo;` không dùng block `{}`).
- 4 space indent, `PascalCase` cho public member, `_camelCase` cho private field.

## Result Pattern (bắt buộc cho luồng lỗi dự kiến được)
Dùng `Result<T>` / `Result` trong `Core/Common/Result.cs` cho mọi thao tác có
thể fail theo cách "bình thường" (không phải bug):

```csharp
public async Task<Result<GameProfile>> DetectAsync(string gameDirectory, CancellationToken ct)
{
    if (!Directory.Exists(gameDirectory))
        return Result<GameProfile>.Failure("Thư mục game không tồn tại.");

    // ...

    return Result<GameProfile>.Success(profile);
}
```

UI đọc `result.IsSuccess` / `result.Error` để hiển thị, không bắt exception
cho các case này.

Exception chỉ ném khi đó thực sự là lỗi lập trình (null không hợp lệ, vi phạm
invariant nội bộ...), không phải để báo "AES key sai" hay "không tìm thấy
file ngôn ngữ".

## Async & Progress
Mọi method I/O nặng theo dạng:

```csharp
Task<Result<T>> DoWorkAsync(
    /* params */,
    IProgress<ProgressInfo>? progress = null,
    CancellationToken cancellationToken = default);
```

`ProgressInfo` (định nghĩa trong `Core/Common/`) chứa % hoàn thành + thông
điệp ngắn để hiển thị (VD: "Đang đọc pak 3/12...").

## Module & DI
- Mỗi thư mục module trong `Core/` có 1 file interface chính (VD:
  `IAesKeyResolver.cs` trong `AesKeyResolver/`).
- Đăng ký DI tập trung ở `Core/CoreServiceCollectionExtensions.cs`
  (`AddUeVietTranslatorCore(this IServiceCollection services)`), để cả
  `App` và `Cli` dùng chung 1 cách đăng ký.
- Module không gọi thẳng class cụ thể của module khác — chỉ qua interface.

## Comment
- Comment giải thích **lý do**, đặc biệt ở logic liên quan format binary UE.
  Ví dụ tốt: `// Magic number 0x5A6F12E1 xác nhận decrypt đúng key — xem
  docs/DOMAIN_KNOWLEDGE.md#2-aes-key`.
- Không cần comment mô tả lại điều code đã tự nói rõ (VD không cần
  `// tăng i lên 1` cho `i++`).

## Test
- Mọi logic trong `Core/` nên có test tương ứng trong
  `tests/UEVietTranslator.Core.Tests`, đặc biệt các phần dễ vỡ khi gặp game
  mới (parse header, detect version, CSV round-trip).
- Test không cần file game thật để chạy CI — dùng file mẫu nhỏ tự tạo/fixture,
  đánh dấu rõ test nào cần file game thật thì để trong thư mục riêng
  `tests/Manual/` kèm README giải thích cách chạy thủ công.
