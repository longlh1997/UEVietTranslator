# Kiến thức nền: Unreal Engine Pak/IoStore/Localization

> Mục đích: để Claude Code (và Hải Long khi review) không phải đoán lại các
> khái niệm này mỗi lần đụng vào `Unpacking/`, `AesKeyResolver/`, `AssetIO/`.
> Cập nhật file này khi phát hiện điều gì mới trong quá trình test với game
> thật (kể cả điều "hiển nhiên" — người review sau có thể không biết).

## 1. Hai định dạng đóng gói của UE

| | `.pak` (cũ) | `.utoc` / `.ucas` (IoStore, UE4.25+ trở đi, phổ biến ở UE5) |
|---|---|---|
| Cấu trúc | 1 file chứa mọi thứ, có header + index + data | `.utoc` = table of contents, `.ucas` = data thật, đi kèm `.upak`/`.pak` rỗng |
| Nhận diện | Đuôi `.pak` trong `Content/Paks/` | Có cặp file `.utoc`+`.ucas` cùng tên trong `Content/Paks/` |
| CUE4Parse | Hỗ trợ qua `DefaultFileProvider` | Hỗ trợ, cần trỏ đúng cả `.utoc` và `.ucas` |

Một game có thể dùng CẢ HAI cùng lúc (một số asset cũ đóng `.pak`, asset mới
đóng IoStore). Bước detect trong `GameProfile` phải quét cả hai loại trong
`Content/Paks/`, không giả định chỉ có một.

## 2. AES Key — bản chất và cách xử lý

- Nhiều pak/IoStore được mã hoá bằng **AES-256, 1 key duy nhất cho toàn bộ
  container** (không phải mã hoá riêng từng file). Key này được studio
  hardcode trong file thực thi game (`.exe` hoặc 1 `.dll` liên quan) dưới
  dạng mảng 32 byte.
- **Không phải game nào cũng mã hoá.** Luôn thử đọc pak/IoStore KHÔNG key
  trước — nếu đọc được index thì không cần key. Chỉ chạy bước tìm key khi
  bước này fail.
- Cách tìm key, đã implement ở Pha 2 trong `AesKeyResolverService`
  (**LƯU Ý:** mục này ban đầu (Pha 0 scaffold) phác thảo bước 2 là "so khớp
  magic number thủ công" — đã bỏ hướng đó, xem lý do ở `docs/DECISIONS.md#adr-006`):
  1. Quét binary `.exe` theo từng byte (không bỏ qua vị trí nào — xem
     ADR-006 lý do ưu tiên quét đầy đủ hơn tốc độ), tính Shannon entropy cho
     mỗi window 32 byte. **Ngưỡng entropy tối đa lý thuyết cho window 32 byte
     là `log2(32) = 5.0`, KHÔNG phải `log2(256) = 8.0`** — dễ nhầm vì trực
     giác hay nghĩ theo bảng chữ cái byte (256 giá trị) thay vì kích thước
     mẫu thực tế (32). Ngưỡng đang dùng: `4.0`.
  2. Với mỗi candidate vượt ngưỡng entropy, **submit thẳng cho CUE4Parse thử
     mount thật** (`provider.SubmitKeysAsync`) — dựa trên `provider.RequiredKeys`
     (danh sách GUID mà CUE4Parse đã tự biết là cần key, đọc được ngay sau
     `Initialize()`, KHÔNG cần đoán guid rỗng). CUE4Parse tự lo việc
     decrypt+validate index đúng theo từng version pak/IoStore — không tự
     parse magic number bằng tay nữa.
  3. Key đúng = `SubmitKeysAsync` trả về mounted count > 0 cho guid đó. Lưu
     key này vào `GameProfile` kèm theo **hash của file .exe** (**CHƯA
     implement — xem docs/ROADMAP.md Pha 2**, `AesKeyResolverService` mới chỉ
     tìm+validate, chưa lưu) — vì nhiều game đổi key mỗi bản update, key cũ có
     thể không còn đúng sau khi game update.
- Một số game dùng **nhiều key** (multi-key, theo từng pak chunk khác nhau,
  thường thấy ở game live-service lớn). `AesKeyResolverService` ĐÃ hỗ trợ
  multi-key ở mức cơ bản: sau khi tìm được 1 key hợp lệ cho 1 guid, vòng quét
  KHÔNG dừng lại mà tiếp tục tìm key cho các guid còn thiếu trong
  `provider.RequiredKeys`, chỉ dừng khi hết guid cần giải quyết hoặc hết file
  để quét. Giới hạn đã biết: giả định các guid khác nhau có thể dùng GIÁ TRỊ
  KEY khác nhau tìm được độc lập qua entropy scan — chưa xử lý trường hợp
  cần biết trước "quan hệ" giữa các guid (VD: 1 vài game dùng sơ đồ dẫn xuất
  key phức tạp hơn single-random-key-per-guid).

## 3. Định dạng file ngôn ngữ — 2 khả năng chính

### a) `.locres` (Localization Resource)
- File nhị phân riêng, thường nằm ở
  `Content/Localization/<Namespace>/<LangCode>/<Namespace>.locres`.
- Chứa key-value: namespace + key → text đã dịch cho 1 ngôn ngữ.
- Đây là format "truyền thống" của UE, engine tự sinh ra qua Localization
  Dashboard trong Editor.

### b) StringTable trong `.uasset` (phổ biến hơn ở game UE5 gần đây)
- Text được nhúng trực tiếp trong asset dạng `DataTable`/`StringTable`
  (`Engine.StringTable` class), không tách file riêng theo ngôn ngữ.
- Muốn Việt hoá kiểu này thường phải: (1) thêm ngôn ngữ mới vào StringTable,
  hoặc (2) ghi đè trực tiếp text gốc nếu game không có hệ multi-language rõ
  ràng (một số game indie/early access làm vậy). Cách nào áp dụng được tuỳ
  vào cách game implement — **đây là lý do bắt buộc phải có bước
  `LocalizationDiscovery` cho người dùng tự xác nhận**, không thể đoán 100%
  tự động.
- UAssetAPI đọc/ghi được StringTable qua `Export` tương ứng, nhưng field names
  và class layout thay đổi giữa UE version — luôn kiểm tra lại với version
  cụ thể của game đang xử lý.

### c) Trường hợp khác cần tính đến
- Một số game tự chế hệ localization riêng (JSON/CSV đóng gói trong pak,
  không dùng hệ locres/StringTable chuẩn của UE). `LocalizationDiscovery`
  không được giả định chỉ có 2 dạng trên — cần có bước "quét file text-like
  bất kỳ" (JSON, XML, custom binary nhỏ) làm phương án cuối nếu không tìm
  thấy `.locres`/StringTable nào.

## 4. Cạm bẫy đã biết (cập nhật liên tục khi gặp thực tế)

- **2026-07-03 — Gói NuGet `CUE4Parse` chứa `CUE4Parse-Natives.dll` (native,
  không phải managed assembly) đặt trong `lib/net8.0/`.** Khi build, MSBuild
  cố đọc file này như 1 reference assembly quản lý (managed) và luôn in ra
  warning `MSB3246: Resolved file has a bad image, no metadata...`. Đây là
  warning **vô hại**, build vẫn thành công — nhưng hệ quả là file
  `CUE4Parse-Natives.dll` **không tự copy** vào thư mục output (`bin/...`)
  qua cơ chế reference thông thường. Nếu về sau code Core thực sự cần load
  native lib này lúc runtime (ví dụ decompress Oodle) mà gặp lỗi thiếu file/
  `DllNotFoundException`, đây là nguyên nhân đầu tiên cần kiểm tra — có thể
  cần thêm rule copy thủ công (`<None Include="...CUE4Parse-Natives.dll">`
  với `CopyToOutputDirectory`) trong `Core.csproj`. Chưa xử lý ở Pha 0 vì
  chưa có code nào gọi tới phần native.

- **2026-07-03 — Benchmark thực tế tốc độ quét entropy AES key
  (`AesKeyResolverService`).** Quét 100MB dữ liệu **ngẫu nhiên hoàn toàn**
  (kịch bản XẤU NHẤT có thể — 1 file `.exe` thật không bao giờ ngẫu nhiên
  100% xuyên suốt, phần lớn là code/text có entropy thấp) theo từng byte
  (stride=1) mất **~44.5 giây** chỉ riêng phần tính entropy (chưa tính bước
  validate qua CUE4Parse). Ngoại suy tuyến tính: 1 file `.exe` thật cỡ
  300MB-1GB sẽ mất khoảng **2-8 phút** cho phần entropy — nằm trong mức chấp
  nhận được theo quyết định ở ADR-006 (ưu tiên chắc chắn tìm được key hơn tốc
  độ, vì pipeline chỉ chạy 1-vài lần cho mỗi game). Nếu khi test với
  Dragonwilds thật mà thời gian vượt quá ~15-20 phút, đây là dấu hiệu cần
  xem lại (có thể do bước validate qua CUE4Parse tốn nhiều hơn dự kiến nếu
  file có vùng entropy cao lớn — VD chữ ký số Authenticode ở cuối file `.exe`
  đã ký — mỗi vùng như vậy có thể sinh ra hàng nghìn-hàng triệu candidate
  cần validate).

## 5. Việc cần xác nhận cụ thể cho RuneScape: Dragonwilds (chưa xác nhận)

- [ ] Dùng `.pak` hay IoStore hay cả hai?
- [ ] Có mã hoá AES không?
- [ ] File ngôn ngữ là `.locres` hay StringTable hay dạng khác?
- [ ] UE version chính xác (5.x nào)?

Đánh dấu khi xác nhận được, kèm cách xác nhận (VD: "mở bằng FModel thấy...").
