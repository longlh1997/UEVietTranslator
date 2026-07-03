namespace UEVietTranslator.Core.Common;

/// <summary>
/// Thông tin tiến độ cho các tác vụ I/O nặng (unpack, extract, repack...).
/// Mọi method dạng này nhận <c>IProgress&lt;ProgressInfo&gt;?</c> — xem
/// docs/CODING_STANDARDS.md mục "Async & Progress".
/// </summary>
/// <param name="PercentComplete">0-100. Dùng -1 nếu không xác định được % (VD: đang quét, chưa biết tổng số file).</param>
/// <param name="Message">Thông điệp ngắn hiển thị cho người dùng, tiếng Việt.</param>
public readonly record struct ProgressInfo(double PercentComplete, string Message);
