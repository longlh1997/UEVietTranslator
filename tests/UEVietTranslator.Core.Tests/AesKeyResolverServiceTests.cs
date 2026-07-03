using System.Security.Cryptography;
using UEVietTranslator.Core.AesKeyResolver;
using Xunit;

namespace UEVietTranslator.Core.Tests;

public class AesKeyResolverServiceComputeEntropyTests
{
    [Fact]
    public void ComputeEntropy_32ByteGiongHetNhau_TraVe0()
    {
        var window = new byte[32];
        Array.Fill(window, (byte)0xAA);
        var histogram = new int[256];

        var entropy = AesKeyResolverService.ComputeEntropy(window, histogram);

        Assert.Equal(0.0, entropy, precision: 10);
        // histogram phải được dọn sạch về 0 sau khi tính xong, để dùng lại
        // cho window kế tiếp trong vòng quét thật.
        Assert.All(histogram, count => Assert.Equal(0, count));
    }

    [Fact]
    public void ComputeEntropy_32ByteDeuKhacNhau_TraVeDungLog2Cua32()
    {
        var window = new byte[32];
        for (byte i = 0; i < 32; i++)
            window[i] = i;
        var histogram = new int[256];

        var entropy = AesKeyResolverService.ComputeEntropy(window, histogram);

        // Entropy tối đa lý thuyết cho 1 window 32 byte là log2(32) = 5.0 —
        // KHÔNG phải log2(256) = 8.0, vì bị giới hạn bởi kích thước mẫu (32),
        // không phải kích thước bảng chữ cái byte (256). Đây chính là lỗi
        // suýt đưa vào code thật (ngưỡng ban đầu đặt 7.5, cao hơn cả mức tối
        // đa có thể đạt được) — test này khoá lại giá trị đúng.
        Assert.Equal(5.0, entropy, precision: 10);
    }

    [Fact]
    public void ComputeEntropy_DuLieuNgauNhienThat_CaoHonNguongLocEntropy()
    {
        // Mô phỏng đúng trường hợp 1 AES-256 key thật trong binary: 32 byte
        // sinh ngẫu nhiên bằng CSPRNG. Test này xác nhận dữ liệu kiểu này
        // luôn vượt ngưỡng lọc (EntropyThreshold = 4.0 trong
        // AesKeyResolverService) — nếu ngưỡng bị chỉnh sai lần nữa (VD ai đó
        // sửa lại thành > 5.0), test này sẽ bắt được ngay.
        var histogram = new int[256];

        for (var trial = 0; trial < 20; trial++)
        {
            var window = RandomNumberGenerator.GetBytes(32);
            var entropy = AesKeyResolverService.ComputeEntropy(window, histogram);
            Assert.True(entropy > 4.0, $"Entropy của dữ liệu ngẫu nhiên thật ({entropy}) phải > 4.0");
        }
    }

    [Fact]
    public void ComputeEntropy_CoMotCapByteTrung_VanTinhDungKhongDoubleCount()
    {
        // 30 byte khác nhau + 1 giá trị lặp lại 2 lần (31 category phân biệt,
        // 1 category có count=2, 30 category có count=1) — kiểm tra logic
        // "bỏ qua nếu histogram[b] == 0" không vô tình double-count hoặc bỏ
        // sót khi window có byte trùng giá trị.
        var window = new byte[32];
        for (byte i = 0; i < 31; i++)
            window[i] = i;
        window[31] = 0; // trùng với window[0]
        var histogram = new int[256];

        var entropy = AesKeyResolverService.ComputeEntropy(window, histogram);

        // Tính tay: 1 category count=2 (p=2/32), 30 category count=1 (p=1/32)
        var expected = -(2.0 / 32 * Math.Log2(2.0 / 32)) - 30 * (1.0 / 32 * Math.Log2(1.0 / 32));
        Assert.Equal(expected, entropy, precision: 10);
        Assert.All(histogram, count => Assert.Equal(0, count));
    }
}

public class AesKeyResolverServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public AesKeyResolverServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "uevt-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task ResolveAsync_ExeKhongTonTai_TraVeFailure()
    {
        var resolver = new AesKeyResolverService();

        var result = await resolver.ResolveAsync(
            Path.Combine(_tempRoot, "khong-ton-tai.exe"),
            _tempRoot,
            progress: null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Không tìm thấy file thực thi", result.Error);
    }

    [Fact]
    public async Task ResolveAsync_PaksDirectoryKhongTonTai_TraVeFailure()
    {
        var exePath = Path.Combine(_tempRoot, "Game.exe");
        File.WriteAllBytes(exePath, new byte[] { 0x4D, 0x5A });

        var resolver = new AesKeyResolverService();
        var result = await resolver.ResolveAsync(
            exePath,
            Path.Combine(_tempRoot, "khong-ton-tai"),
            progress: null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Không tìm thấy thư mục Paks", result.Error);
    }

    [Fact]
    public async Task ResolveAsync_PakRacKhongBaoCanKey_TraVeFailureRoRang()
    {
        // Cùng fixture "pak rác 1 byte" như CUE4ParseProviderTests — đã xác
        // nhận qua test đó là CUE4Parse Initialize() không throw với input
        // này và không archive nào báo IsEncrypted, nên RequiredKeys rỗng.
        // Test này xác nhận AesKeyResolverService xử lý đúng case đó (không
        // crash, trả về Result.Failure rõ ràng) — KHÔNG test được nhánh
        // "tìm thấy key đúng" vì CUE4Parse không có pak writer để tạo fixture
        // pak đã mã hoá hợp lệ (xem docs/ROADMAP.md Pha 2, cần Hải Long test
        // thủ công với Dragonwilds thật nếu game đó có mã hoá).
        var exeBytes = new byte[1024];
        RandomNumberGenerator.Fill(exeBytes); // có vùng entropy cao để chắc chắn scan có chạy qua logic tìm candidate
        var exePath = Path.Combine(_tempRoot, "Game.exe");
        File.WriteAllBytes(exePath, exeBytes);

        var paksDir = Path.Combine(_tempRoot, "Content", "Paks");
        Directory.CreateDirectory(paksDir);
        File.WriteAllBytes(Path.Combine(paksDir, "pakchunk0-Windows.pak"), new byte[] { 0x00 });

        var resolver = new AesKeyResolverService();
        var result = await resolver.ResolveAsync(exePath, paksDir, progress: null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Không có archive nào báo cần AES key", result.Error);
    }
}
