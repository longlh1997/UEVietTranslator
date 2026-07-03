# CLAUDE.md — Context & Rules cho dự án UEVietTranslator

> File này là nguồn sự thật (source of truth) về ngữ cảnh dự án dành cho Claude
> (qua Claude Code) khi làm việc trên repo này. Đọc file này TRƯỚC khi sửa bất
> kỳ code nào. Nếu có mâu thuẫn giữa file này và trí nhớ/giả định của bạn, file
> này thắng. Nếu có thay đổi lớn về kiến trúc/quyết định kỹ thuật, PHẢI cập nhật
> lại file này và `docs/DECISIONS.md` trong cùng lần sửa — không để tài liệu bị
> lệch khỏi code thật.

## 1. Dự án này là gì

Công cụ desktop (Windows-first, cross-platform nếu khả thi) giúp Hải Long —
dev kiêm dịch giả localization game người Việt — thực hiện toàn bộ pipeline
Việt hóa game chạy trên **Unreal Engine 4/5** một cách bán tự động:

```
Unpack (pak/IoStore) → Tìm & giải mã AES key (nếu có) → Phát hiện file ngôn ngữ
→ Extract text ra CSV chuẩn hoá → Dịch (AI-assisted, Gemini) → Review thủ công
→ Ghi đè text đã dịch vào asset → Repack lại pak/IoStore
```

Game khởi điểm: **RuneScape: Dragonwilds** (Jagex, Unreal Engine 5). Nhưng mục
tiêu SẢN PHẨM là tool dùng chung được cho **bất kỳ game UE4/UE5 nào**, không
hardcode riêng cho một game. Người dùng (Hải Long) đã có tool tương tự cho
Unity (Python + CustomTkinter + UnityPy) — đây là bản dành cho hệ UE, không
phải port lại tool Unity.

## 2. Vai trò của Claude trong dự án này

- Hải Long là **reviewer + người ra quyết định kiến trúc/ý tưởng**, không tự
  tay viết phần lớn code.
- Claude (qua Claude Code) là **người phát triển chính**: viết code, chạy
  build/test, tự sửa lỗi biên dịch, đề xuất giải pháp kỹ thuật.
- Vì vậy: ưu tiên code **tự giải thích, có comment ở chỗ logic phức tạp**
  (đặc biệt là phần liên quan format binary UE), để Hải Long review được dù
  không rành sâu C#/CUE4Parse.
- Khi gặp quyết định kiến trúc có đánh đổi (trade-off) đáng kể, DỪNG lại hỏi
  Hải Long thay vì tự quyết rồi lặng lẽ đổi hướng.

## 3. Stack kỹ thuật đã CHỐT (không tự đổi nếu không hỏi trước)

| Thành phần | Lựa chọn | Lý do |
|---|---|---|
| Ngôn ngữ | C# / .NET 8 | CUE4Parse và UAssetAPI đều là thư viện .NET, cần gọi trực tiếp cùng process, không qua subprocess/IPC |
| UI Desktop | Avalonia (MVVM) | Cross-platform, cùng ngôn ngữ với Core → không cần serialize dữ liệu qua ranh giới process |
| Unpack/Parse chính | [CUE4Parse](https://github.com/FabianFG/CUE4Parse) | Thư viện lõi mà FModel dùng bên trong; nhúng thẳng thay vì gọi FModel như blackbox |
| Fallback unpack | FModel (thủ công, ngoài app) | Dùng khi CUE4Parse fail (game dùng encryption/container lạ). App chỉ đọc lại thư mục FModel đã export, KHÔNG tự động hoá bước này |
| Asset R/W (StringTable/DataTable) | [UAssetAPI](https://github.com/atenfyr/UAssetAPI) | |
| Dịch AI | Gemini API | Theo pattern đã dùng ở tool Unity trước đó |
| Kiến trúc | Core (class library, không phụ thuộc UI) + App (Avalonia) + Cli (console, để test Core độc lập) | Core phải build/test được mà không cần UI |

**KHÔNG** đổi sang Python/subprocess-IPC. Quyết định này đã được cân nhắc kỹ
(xem `docs/DECISIONS.md#adr-001`) vì lý do ổn định khi xử lý file lớn và tránh
lỗi ở ranh giới giữa 2 runtime.

## 4. Ràng buộc môi trường quan trọng

- Code được viết ban đầu trong sandbox **không có .NET SDK và không có mạng
  tới nuget.org**. Nghĩa là lần build/restore ĐẦU TIÊN thật sự phải chạy trên
  máy Hải Long hoặc trong phiên Claude Code có mạng đầy đủ. Đừng giả định code
  đã từng được compile-verify trừ khi có ghi chú ngược lại trong
  `docs/DECISIONS.md`.
- Khi build lần đầu, RẤT CÓ THỂ có lỗi biên dịch nhỏ (tên API CUE4Parse/
  UAssetAPI thay đổi giữa các version). Đây là việc bình thường cần sửa, không
  phải dấu hiệu kiến trúc sai.

## 5. Quy tắc bắt buộc khi viết code trong repo này

1. **Result pattern, không dùng exception cho luồng lỗi dự kiến được.** Mọi
   bước có thể fail theo cách "bình thường" (sai AES key, thiếu file, game
   version không nhận diện được...) phải trả về `Result<T>` (xem
   `Core/Common/Result.cs`) kèm lý do cụ thể, để UI hiển thị được thông báo rõ
   ràng thay vì crash. Exception chỉ dùng cho lỗi lập trình thật sự (bug).
2. **Core không được reference bất kỳ thứ gì thuộc Avalonia/UI.** Mọi thứ
   trong `Core/` phải test được bằng unit test hoặc qua `Cli/` mà không cần
   dựng UI.
3. **Mọi thao tác I/O nặng (unpack, parse asset lớn, ghi pak) phải là
   `async`, nhận `CancellationToken`, và report tiến độ qua `IProgress<T>`.**
   Không block UI thread.
4. **Không hardcode logic riêng cho Dragonwilds trong Core.** Dragonwilds chỉ
   là 1 `GameProfile` để test. Nếu phát hiện cần xử lý đặc thù, đưa vào cơ chế
   profile/config, không if-else theo tên game trong logic lõi.
5. **Comment giải thích "vì sao", không phải "cái gì"** ở những chỗ đụng vào
   format binary UE (pak header, IoStore container, locres structure...) —
   đây là phần Hải Long không rành, review sẽ dựa vào comment để hiểu.
6. **Mỗi module trong Core (`GameProfile`, `AesKeyResolver`, `Unpacking`,
   `LocalizationDiscovery`, `AssetIO`, `CsvSchema`, `Translation`,
   `Repacking`) giao tiếp qua interface**, đặt trong chính thư mục module đó.
   Không để module này gọi thẳng class cụ thể của module khác — tiêm qua DI.
7. Trước khi thêm dependency NuGet mới, kiểm tra đã có gói tương đương chưa,
   và ghi lý do chọn vào `docs/DECISIONS.md`.

## 6. Kiến thức nền cần biết trước khi động vào Core

Đọc `docs/DOMAIN_KNOWLEDGE.md` trước khi sửa `Unpacking/`, `AesKeyResolver/`,
hoặc `AssetIO/` — file đó giải thích pak vs IoStore, AES key, locres vs
StringTable, và các cạm bẫy thường gặp.

## 7. Trạng thái hiện tại & tài liệu liên quan

- Roadmap & pha hiện tại: `docs/ROADMAP.md`
- Quyết định kiến trúc đã chốt (ADR log): `docs/DECISIONS.md`
- Kiến thức nền UE pak/IoStore/AES/locres: `docs/DOMAIN_KNOWLEDGE.md`
- Coding style cụ thể: `docs/CODING_STANDARDS.md`

**Luôn cập nhật `docs/ROADMAP.md`** (đánh dấu pha nào xong) khi hoàn thành một
milestone — đây là cách Hải Long theo dõi tiến độ mà không cần đọc hết code.

## 8. Việc KHÔNG được tự ý làm

- Không tự đổi kiến trúc đã chốt ở mục 3 mà không hỏi.
- Không tự thêm tính năng tự động hoá bước unpack qua FModel (fallback path
  luôn là thao tác thủ công của người dùng — xem lý do ở ADR-002).
- Không xoá/viết đè lên comment domain-knowledge đã có mà không hiểu rõ lý do
  nó tồn tại — nhiều comment ghi lại bài học từ việc gặp game lỗi thật.
