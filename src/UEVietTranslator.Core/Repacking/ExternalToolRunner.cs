using System.Diagnostics;
using System.Text;
using UEVietTranslator.Core.Common;

namespace UEVietTranslator.Core.Repacking;

/// <summary>
/// Chạy 1 CLI tool ngoài (repak/retoc) qua subprocess, capture stdout/stderr
/// để trả lỗi rõ ràng qua <see cref="Result"/> thay vì để exception mù mờ.
/// Dùng chung cho <see cref="RepackService"/> — xem docs/DECISIONS.md#adr-012
/// về lý do chấp nhận phụ thuộc subprocess cho riêng bước repack.
/// </summary>
internal static class ExternalToolRunner
{
    public static async Task<Result<string>> RunAsync(
        string executablePath,
        string arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            // Trường hợp phổ biến nhất: chưa cài / chưa có trong PATH — nói
            // rõ để Hải Long biết cần tải binary từ GitHub Releases của
            // trumank/repak hoặc trumank/retoc, không phải bug code.
            return Result<string>.Failure(
                $"Không chạy được '{executablePath}' — kiểm tra đã cài và có trong PATH chưa (hoặc truyền đường dẫn đầy đủ). Lỗi: {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
            return Result<string>.Failure(
                $"'{executablePath} {arguments}' thoát với mã lỗi {process.ExitCode}.\nstdout: {stdout}\nstderr: {stderr}");

        return Result<string>.Success(stdout.ToString());
    }
}
