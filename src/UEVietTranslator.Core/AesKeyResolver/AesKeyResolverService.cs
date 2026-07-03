using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using UEVietTranslator.Core.Common;

namespace UEVietTranslator.Core.AesKeyResolver;

/// <summary>
/// Quét file .exe tìm ứng viên AES-256 key (32 byte) dựa trên entropy, rồi
/// xác thực từng ứng viên bằng cách submit thẳng cho CUE4Parse thử mount thật
/// — KHÔNG tự parse header/index pak và so khớp magic number bằng tay như
/// phác thảo ban đầu ở docs/DOMAIN_KNOWLEDGE.md §2 (đã lỗi thời). CUE4Parse
/// đã biết cách decrypt + validate index đúng cho từng version pak/IoStore;
/// viết lại logic đó thủ công vừa dễ sai vừa trùng lặp code không cần thiết.
/// Xem docs/DECISIONS.md#adr-006 cho lý do đầy đủ.
/// </summary>
public sealed class AesKeyResolverService : IAesKeyResolver
{
    // AES-256 key = 32 byte — kích thước window quét qua từng byte của .exe.
    private const int KeyLengthBytes = 32;

    // Ngưỡng Shannon entropy (bit/byte). LƯU Ý QUAN TRỌNG: entropy tối đa lý
    // thuyết KHÔNG phải log2(256)=8.0 — con số đó chỉ đúng khi mẫu đủ lớn để
    // phủ hết 256 giá trị byte. Window ở đây chỉ có 32 byte (bằng đúng
    // KeyLengthBytes), nên số giá trị PHÂN BIỆT tối đa có thể xuất hiện
    // trong 1 window cũng chỉ là 32 → entropy tối đa thật sự = log2(32) =
    // 5.0 (đạt được khi cả 32 byte đều khác nhau). 32 byte sinh ngẫu nhiên
    // thật (đúng là AES key) thường có entropy quanh 4.85-5.0 (theo lý
    // thuyết birthday paradox, kỳ vọng có 1-2 cặp byte trùng giá trị ngay cả
    // khi hoàn toàn ngẫu nhiên — không nhất thiết phải đủ cả 32 giá trị khác
    // nhau). Chọn ngưỡng 4.0 để có biên an toàn khá rộng bên dưới mức "trông
    // giống ngẫu nhiên" (~4.85+), tránh bỏ sót key thật vì ngưỡng quá khắt
    // khe — chấp nhận việc này sẽ cho qua nhiều candidate không phải key hơn
    // (code/text/padding thường có entropy thấp hơn hẳn, dưới ~3.5-4.0 do
    // lặp byte nhiều), nhưng KHÔNG sao vì mỗi candidate đều được validate
    // thật bằng CUE4Parse ngay sau đó (rẻ, nhanh) — xem docs/DECISIONS.md#adr-006.
    // CÓ THỂ cần điều chỉnh thêm khi test với Dragonwilds thật, xem
    // docs/DOMAIN_KNOWLEDGE.md mục "Cạm bẫy đã biết".
    private const double EntropyThreshold = 4.0;

    // Đọc .exe theo chunk, không load hết vào RAM — .exe game có thể vài
    // trăm MB tới hơn 1GB. Kỹ thuật carry phần đuôi chunk giống
    // GameProfileDetector.DetectEngineVersionAsync, để không bỏ lỡ window bị
    // cắt ngang ranh giới chunk.
    private const int ScanChunkSize = 4 * 1024 * 1024;
    private const int ScanOverlap = KeyLengthBytes - 1;

    // Hải Long đã xác nhận ưu tiên "chắc chắn tìm được key" hơn tốc độ (xem
    // docs/DECISIONS.md#adr-006) — nên KHÔNG giới hạn số candidate được THỬ
    // (mọi window entropy cao đều được submit cho CUE4Parse thử thật). Giới
    // hạn dưới đây CHỈ áp dụng cho số candidate CHƯA validate được giữ lại
    // để trả về cho UI hiển thị khi quét xong mà không tìm ra key nào khớp —
    // tránh tràn bộ nhớ/UI nếu .exe có vùng entropy cao lớn (VD: chữ ký số
    // Authenticode ở cuối file exe đã ký).
    private const int UnvalidatedSampleCap = 50;

    public async Task<Result<IReadOnlyList<AesKeyCandidate>>> ResolveAsync(
        string executablePath,
        string paksDirectory,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(executablePath))
            return Result<IReadOnlyList<AesKeyCandidate>>.Failure(
                $"Không tìm thấy file thực thi: {executablePath}");

        if (!Directory.Exists(paksDirectory))
            return Result<IReadOnlyList<AesKeyCandidate>>.Failure(
                $"Không tìm thấy thư mục Paks: {paksDirectory}");

        using var provider = new DefaultFileProvider(
            paksDirectory, SearchOption.AllDirectories, versions: null, StringComparer.OrdinalIgnoreCase);

        try
        {
            provider.Initialize();
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<AesKeyCandidate>>.Failure(
                $"Quét thư mục Paks thất bại: {ex.Message}");
        }

        // provider.Initialize() đã đọc xong header của từng archive nên đã
        // biết chính xác archive nào mã hoá và cần guid nào — nếu rỗng, có 2
        // khả năng: game không mã hoá (người gọi nên thử IUnpackProvider
        // không key trước, xem docs/DOMAIN_KNOWLEDGE.md §2), hoặc
        // paksDirectory sai. Cả 2 đều là lỗi dự kiến được, không phải bug.
        if (provider.RequiredKeys.Count == 0)
            return Result<IReadOnlyList<AesKeyCandidate>>.Failure(
                "Không có archive nào báo cần AES key trong thư mục Paks này — " +
                "game có thể không mã hoá (hãy thử unpack không key trước), " +
                "hoặc thư mục Paks không đúng.");

        var validated = new List<AesKeyCandidate>();
        var unvalidatedSample = new List<AesKeyCandidate>();
        var seenHexKeys = new HashSet<string>();
        // Bảng đếm tần suất byte dùng chung cho mọi window trong lần gọi
        // này — cấp phát 1 lần, KHÔNG dùng static/shared giữa các lần gọi
        // (AesKeyResolverService đăng ký singleton trong DI, dùng static
        // field ở đây sẽ không an toàn nếu có 2 lần ResolveAsync chạy đồng
        // thời).
        var histogram = new int[256];

        await using var stream = File.OpenRead(executablePath);
        var totalLength = stream.Length;
        var buffer = new byte[ScanChunkSize];
        var carry = Array.Empty<byte>();
        long scannedSoFar = 0;
        var lastReportedPercent = -1.0;

        while (true)
        {
            if (provider.RequiredKeys.Count == 0)
                break; // đã tìm đủ key cho mọi archive, không cần quét tiếp

            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, ScanChunkSize), cancellationToken)
                .ConfigureAwait(false);
            if (bytesRead == 0)
                break;

            var window = new byte[carry.Length + bytesRead];
            carry.CopyTo(window, 0);
            Array.Copy(buffer, 0, window, carry.Length, bytesRead);

            for (var i = 0; i + KeyLengthBytes <= window.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (provider.RequiredKeys.Count == 0)
                    break;

                // KHÔNG gán Span<byte> vào 1 biến sống qua await bên dưới —
                // Span là ref struct, trình biên dịch C# không cho phép nó
                // "sống" qua điểm await trong async method (trừ khi bật
                // preview language feature). Gọi AsSpan(...) trực tiếp trong
                // từng biểu thức để span chỉ tồn tại tạm thời, dùng xong ngay.
                if (ComputeEntropy(window.AsSpan(i, KeyLengthBytes), histogram) < EntropyThreshold)
                    continue;

                var hexKey = Convert.ToHexString(window.AsSpan(i, KeyLengthBytes));
                if (!seenHexKeys.Add(hexKey))
                    continue; // đã thử đúng giá trị 32-byte này rồi (trùng lặp trong binary)

                var mountedCount = await TryValidateCandidateAsync(provider, hexKey).ConfigureAwait(false);
                if (mountedCount > 0)
                {
                    // Vẫn tiếp tục quét thay vì return ngay — 1 game có thể
                    // multi-key (nhiều guid khác nhau, xem
                    // docs/DOMAIN_KNOWLEDGE.md §2), key vừa tìm được có thể
                    // chỉ giải quyết 1 phần provider.RequiredKeys. Vòng lặp
                    // sẽ tự dừng sớm khi RequiredKeys rỗng (2 chỗ check ở
                    // trên).
                    validated.Add(new AesKeyCandidate(hexKey, Validated: true));
                }
                else if (unvalidatedSample.Count < UnvalidatedSampleCap)
                {
                    unvalidatedSample.Add(new AesKeyCandidate(hexKey, Validated: false));
                }
            }

            scannedSoFar += bytesRead;
            var percent = totalLength == 0 ? 100 : scannedSoFar * 100.0 / totalLength;
            if (percent - lastReportedPercent >= 1.0 || bytesRead < ScanChunkSize)
            {
                progress?.Report(new ProgressInfo(
                    percent, $"Đang quét .exe tìm AES key... ({validated.Count} key đã xác nhận)"));
                lastReportedPercent = percent;
            }

            carry = window.Length > ScanOverlap ? window[^ScanOverlap..] : window;
        }

        // Có key xác nhận được thì trả về đúng những key đó (bỏ qua sample
        // chưa validate) — khớp đúng hợp đồng của IAesKeyResolver.ResolveAsync.
        return Result<IReadOnlyList<AesKeyCandidate>>.Success(
            validated.Count > 0 ? validated : unvalidatedSample);
    }

    private static async Task<int> TryValidateCandidateAsync(DefaultFileProvider provider, string hexKey)
    {
        try
        {
            var aesKey = new FAesKey(hexKey);
            var pairs = provider.RequiredKeys.Select(guid => new KeyValuePair<FGuid, FAesKey>(guid, aesKey));
            return await provider.SubmitKeysAsync(pairs).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 1 candidate lỗi (hiếm — VD lỗi nội bộ CUE4Parse với 1 archive
            // cụ thể) không nên làm hỏng cả quá trình quét hàng loạt candidate
            // — bỏ qua, coi như candidate này không hợp lệ, thử tiếp.
            return 0;
        }
    }

    // Shannon entropy tính trên 1 window (thường 32 byte). histogram được
    // gọi truyền vào từ ResolveAsync (dùng chung xuyên suốt 1 lần quét, xem
    // comment ở nơi khởi tạo) — reset về 0 ngay trong lúc tính, không cần
    // Array.Clear(256) riêng mỗi window (window chỉ có tối đa 32 giá trị byte
    // khác nhau nên chỉ cần dọn đúng những ô đã dùng).
    // internal (không private) để test trực tiếp được — xem
    // AesKeyResolverServiceTests, phần entropy là logic dễ tính sai (off-by-
    // one, quên giới hạn log2(32) thay vì log2(256)...) nên cần test riêng
    // biệt, không chỉ test gián tiếp qua ResolveAsync.
    internal static double ComputeEntropy(ReadOnlySpan<byte> window, int[] histogram)
    {
        foreach (var b in window)
            histogram[b]++;

        var entropy = 0.0;
        foreach (var b in window)
        {
            var count = histogram[b];
            if (count == 0)
                continue; // đã tính entropy cho giá trị byte này rồi (window có byte trùng giá trị)

            var p = (double)count / window.Length;
            entropy -= p * Math.Log2(p);
            histogram[b] = 0; // dọn về 0 luôn — vừa tránh double-count vừa reset sẵn cho window kế tiếp
        }

        return entropy;
    }
}
