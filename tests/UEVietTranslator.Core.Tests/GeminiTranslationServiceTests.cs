using System.Net;
using System.Text;
using System.Text.Json;
using UEVietTranslator.Core.Common;
using UEVietTranslator.Core.CsvSchema;
using UEVietTranslator.Core.Translation;
using Xunit;

namespace UEVietTranslator.Core.Tests;

public class GeminiTranslationServiceTests
{
    // Fake HttpMessageHandler — test logic của GeminiTranslationService (gộp
    // lô, parse response, xử lý lỗi từng phần) mà không gọi API Gemini thật.
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public List<HttpRequestMessage> Requests { get; } = new();

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_respond(request));
        }
    }

    private sealed class FakeSettingsStore : IGeminiSettingsStore
    {
        public GeminiSettings? Settings { get; set; } = new("fake-api-key", "gemini-2.0-flash");

        public Task<Result> SaveAsync(GeminiSettings settings, CancellationToken cancellationToken)
        {
            Settings = settings;
            return Task.FromResult(Result.Success());
        }

        public Task<Result<GeminiSettings>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Settings is null
                ? Result<GeminiSettings>.Failure("chưa cấu hình (fake)")
                : Result<GeminiSettings>.Success(Settings));
    }

    private static HttpResponseMessage MakeGeminiResponse(IReadOnlyList<string> translations)
    {
        var innerTextJson = JsonSerializer.Serialize(translations);
        var body = new
        {
            candidates = new[]
            {
                new { content = new { parts = new[] { new { text = innerTextJson } } } },
            },
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
    }

    private static CsvRow MakeRow(string key, string sourceText) =>
        new("f.locres", "NS", key, sourceText, null, string.Empty, TranslationStatus.Untranslated);

    [Fact]
    public async Task TranslateBatchAsync_KhongCoDongUntranslated_TraVeNguyenKhongGoiApi()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Không được gọi API"));
        var service = new GeminiTranslationService(new HttpClient(handler), new FakeSettingsStore());
        var rows = new List<CsvRow> { MakeRow("K1", "Hello") with { Status = TranslationStatus.Reviewed, TranslatedText = "Xin chào" } };

        var result = await service.TranslateBatchAsync(rows, progress: null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(rows, result.Value);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TranslateBatchAsync_ChuaCauHinhApiKey_TraVeFailureRoRang()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Không được gọi API"));
        var service = new GeminiTranslationService(new HttpClient(handler), new FakeSettingsStore { Settings = null });

        var result = await service.TranslateBatchAsync([MakeRow("K1", "Hello")], progress: null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("chưa cấu hình", result.Error);
    }

    [Fact]
    public async Task TranslateBatchAsync_ApiTraVeDungSoLuong_MapDungThuTuVaDanhDauMachineTranslated()
    {
        var handler = new FakeHttpMessageHandler(_ => MakeGeminiResponse(["Xin chào", "Tạm biệt"]));
        var service = new GeminiTranslationService(new HttpClient(handler), new FakeSettingsStore());
        var rows = new List<CsvRow> { MakeRow("K1", "Hello"), MakeRow("K2", "Goodbye") };

        var result = await service.TranslateBatchAsync(rows, progress: null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var translated = result.Value!;
        Assert.Equal("Xin chào", translated.Single(r => r.Key == "K1").TranslatedText);
        Assert.Equal("Tạm biệt", translated.Single(r => r.Key == "K2").TranslatedText);
        Assert.All(translated, r => Assert.Equal(TranslationStatus.MachineTranslated, r.Status));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TranslateBatchAsync_ApiTraVeSaiSoLuong_GiuNguyenLoUntranslated()
    {
        // API trả về 1 bản dịch trong khi gửi 2 dòng — không được ghép sai
        // thứ tự, phải bỏ qua cả lô và giữ nguyên trạng thái cũ.
        var handler = new FakeHttpMessageHandler(_ => MakeGeminiResponse(["Chỉ 1 kết quả"]));
        var service = new GeminiTranslationService(new HttpClient(handler), new FakeSettingsStore());
        var rows = new List<CsvRow> { MakeRow("K1", "Hello"), MakeRow("K2", "Goodbye") };

        var result = await service.TranslateBatchAsync(rows, progress: null, CancellationToken.None);

        // Toàn bộ lô (duy nhất) lỗi -> fail toàn bộ theo thiết kế "chỉ fail
        // nếu KHÔNG lô nào thành công".
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task TranslateBatchAsync_2Lo1LoLoi_TraVeSuccessVoiPhanConLaiVaGiuNguyenLoLoi()
    {
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            callCount++;
            return callCount == 1
                ? MakeGeminiResponse(Enumerable.Repeat("Đã dịch", 20).ToList())
                : new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("server error") };
        });
        var service = new GeminiTranslationService(new HttpClient(handler), new FakeSettingsStore());

        // 25 dòng, BatchSize=20 -> 2 lô (20 + 5).
        var rows = Enumerable.Range(1, 25).Select(i => MakeRow($"K{i}", $"Text {i}")).ToList();

        var result = await service.TranslateBatchAsync(rows, progress: null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var translated = result.Value!;
        Assert.Equal(25, translated.Count);
        Assert.Equal(20, translated.Count(r => r.Status == TranslationStatus.MachineTranslated));
        Assert.Equal(5, translated.Count(r => r.Status == TranslationStatus.Untranslated));
    }
}
