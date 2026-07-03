# Nhật ký quyết định kiến trúc (ADR Log)

> Mỗi quyết định kỹ thuật có đánh đổi đáng kể được ghi lại đây theo format
> ngắn gọn. Mục đích: để Claude Code ở phiên sau không lặp lại việc "đề xuất
> lại" một hướng đã bị bác bỏ, và để Hải Long tra lại lý do khi cần.

## ADR-001: Dùng C#/.NET thống nhất (Core + Avalonia), không dùng Python UI + subprocess C#

**Ngày:** 2026-07-03
**Bối cảnh:** Cần chọn giữa (a) toàn bộ stack .NET (Avalonia UI gọi thẳng Core
cùng process), hoặc (b) giữ UI Python/CustomTkinter quen thuộc, gọi Core C#
qua subprocess + JSON qua stdout.
**Quyết định:** Chọn (a).
**Lý do:**
- CUE4Parse và UAssetAPI là thư viện .NET, không có binding chính thức cho
  Python. Dùng subprocess nghĩa là phải tự viết lớp serialize/deserialize
  cho mọi lời gọi, tăng bề mặt lỗi.
- Xử lý file lớn (hàng chục GB) cần progress reporting real-time và khả năng
  cancel giữa chừng — làm việc này qua ranh giới process (stdout/stdin) dễ
  vỡ (buffer, encoding, treo process không kill được).
- Ưu tiên của dự án là **độ ổn định** (theo yêu cầu trực tiếp của Hải Long),
  không phải tốc độ phát triển ban đầu.
**Đánh đổi chấp nhận:** Hải Long không rành Avalonia/C# UI, sẽ đóng vai trò
review thay vì tự sửa UI trực tiếp. Chấp nhận được vì Claude là dev chính.

## ADR-002: FModel fallback là thao tác THỦ CÔNG, không tự động hoá

**Ngày:** 2026-07-03
**Bối cảnh:** Khi CUE4Parse fail (game dùng encryption/container lạ chưa
được support), cần phương án dự phòng.
**Quyết định:** App KHÔNG tự động gọi FModel (CLI hay automation). Người dùng
tự mở FModel, tự export thủ công, rồi trỏ app vào thư mục đã export.
**Lý do:**
- FModel là GUI app, không có CLI ổn định để tự động hoá đáng tin cậy.
- Nếu CUE4Parse (lõi mà FModel cũng dùng) đã fail, khả năng cao FModel cũng
  fail tương tự hoặc cần thao tác thủ công đặc thù (chọn version, nhập key
  thủ công, xử lý mapping file...) mà việc tự động hoá không đáng effort so
  với lợi ích — đây được kỳ vọng là trường hợp hiếm gặp, không phải luồng
  chính.
**Đánh đổi chấp nhận:** Trải nghiệm không liền mạch 100% ở fallback path,
nhưng đổi lại tránh việc tool phụ thuộc vào automation dễ vỡ của 1 GUI app
bên thứ ba.

## ADR-003: Chốt version CUE4Parse 1.2.2 và UAssetAPI 1.1.0

**Ngày:** 2026-07-03
**Bối cảnh:** `Core.csproj` scaffold ban đầu để `Version="*"` cho CUE4Parse và
UAssetAPI vì lúc viết không có mạng tới nuget.org để kiểm tra version thật.
Lần build đầu tiên (có mạng) cần chốt version cụ thể.
**Quyết định:**
- `CUE4Parse` → `1.2.2` (KHÔNG dùng bản mới nhất trên NuGet là
  `1.2.2.202607`, phát hành 2026-07-01).
- `UAssetAPI` → `1.1.0` (hiện là version duy nhất được publish lên NuGet).
**Lý do:** Bản `CUE4Parse` mới nhất (`1.2.2.202607`) chỉ build cho
`net10.0` (kiểm tra qua NuGet registration API, dependencyGroups chỉ có
`targetFramework: net10.0`) — không restore được cho project đang target
`net8.0` theo stack đã chốt ở ADR-001/CLAUDE.md §3. `1.2.2` là bản gần nhất
ngược về trước còn target `net8.0`.
**Đánh đổi chấp nhận:** Bỏ lỡ các cải tiến/fix của CUE4Parse từ `1.2.2` đến
`1.2.2.202607`. Nếu sau này muốn dùng bản mới hơn, cần nâng toàn bộ solution
lên `net10.0` trước (ảnh hưởng Avalonia/CommunityToolkit.Mvvm — cần đánh giá
lại, không làm ngầm trong lần sửa này).

## ADR-004: `CUE4ParseProvider` (Pha 1) chỉ liệt kê virtual path, KHÔNG extract toàn bộ asset ra đĩa

**Ngày:** 2026-07-03
**Bối cảnh:** `IUnpackProvider.UnpackAsync` (scaffold Pha 0) trả về
`UnpackedAssetRef(VirtualPath, ExtractedFilePath)` với `ExtractedFilePath`
không nullable — ngụ ý mọi asset phải được ghi ra đĩa ngay ở bước Unpack.
Một game UE5 hiện đại (như RuneScape: Dragonwilds) có thể có hàng chục-hàng
trăm GB asset (texture, mesh, audio...), trong khi file ngôn ngữ
(`.locres`/StringTable) chỉ chiếm phần rất nhỏ. Đã hỏi Hải Long chọn giữa
(a) chỉ liệt kê virtual path, không ghi gì ra đĩa, hay (b) extract toàn bộ
ngay — không có phản hồi kịp lúc, nên quyết định theo phương án được đề xuất
sẵn (a) và tự chịu trách nhiệm ghi lại ở đây để review sau.
**Quyết định:**
- Đổi `ExtractedFilePath` thành nullable (`string?`).
- `CUE4ParseProvider.UnpackAsync` (Pha 1) chỉ `Initialize()` + `Mount()` +
  liệt kê `provider.Files.Values` thành `VirtualPath`, luôn để
  `ExtractedFilePath = null`. Tham số `outputDirectory` giữ trong signature
  nhưng CHƯA dùng ở Pha 1.
- Việc đọc bytes thật của 1 asset cụ thể (khi `LocalizationDiscovery` ở
  Pha 3 đã khoanh vùng được candidate) sẽ cần 1 cơ chế đọc-theo-yêu-cầu
  riêng — CHƯA thiết kế, để lại làm việc của Pha 3/4.
**Lý do:** Ghi toàn bộ game ra đĩa chỉ để tìm vài file ngôn ngữ vừa lãng phí
thời gian (mount + decompress toàn bộ archive) vừa lãng phí dung lượng đĩa,
đi ngược nguyên tắc "không xây quá mức cần thiết" trong CLAUDE.md. Cách tiếp
cận giống các tool tương tự (FModel) — giữ file provider đã mount, đọc asset
theo yêu cầu thay vì dump hàng loạt.
**Đánh đổi chấp nhận:**
- `IUnpackProvider` hiện tại chưa có cách nào để bước sau (Pha 3/4) đọc bytes
  của 1 virtual path cụ thể — đây là lỗ hổng thiết kế đã biết, CẦN quay lại
  giải quyết khi bắt đầu `LocalizationDiscovery` (Pha 3). Rất có thể sẽ cần
  đổi từ model "unpack 1 lần trả về list" sang model "giữ session/provider
  mở xuyên suốt pipeline" — đây là thay đổi kiến trúc lớn hơn, PHẢI hỏi Hải
  Long trước khi làm, không tự quyết như quyết định (a) ở trên (quyết định
  đó nhỏ và dễ đảo ngược, còn thay đổi lifetime/session là lớn và ảnh hưởng
  nhiều module).

## ADR-005: Mount pak/IoStore theo TỪNG BƯỚC pipeline, không giữ session sống xuyên suốt, không remount theo từng file

**Ngày:** 2026-07-03
**Bối cảnh:** ADR-004 để lại 1 lỗ hổng: sau khi `CUE4ParseProvider.UnpackAsync`
(Pha 1) mount xong và liệt kê virtual path, nó `Dispose()` provider ngay —
không còn cách nào đọc lại nội dung asset. `LocalizationDiscoveryService`
(Pha 3) cần soi export table của hàng loạt file `.uasset` (tìm StringTable)
mà KHÔNG có tên file gợi ý sẵn như `.locres`. Có 3 phương án:
(a) giữ 1 `IFileProvider` sống xuyên suốt cả pipeline (Unpack → Discovery →
AssetIO), quản lý vòng đời qua 1 object session riêng;
(b) mount lại từ đầu mỗi lần cần đọc 1 file — đơn giản nhất về code nhưng
mount lại cho hàng nghìn file `.uasset` sẽ mất hàng giờ thay vì vài phút;
(c) mount đúng 1 lần cho MỖI BƯỚC xử lý (liệt kê / soi export / extract file
đã chọn), không giữ sống giữa các bước, không remount trong cùng 1 bước.
Đã thảo luận trực tiếp với Hải Long: ưu tiên **sự ổn định/đơn giản hơn hiệu
năng** (vì pipeline này chỉ chạy 1-vài lần cho mỗi game, không phải hot
path), nhưng remount theo từng file (b) bị xác nhận là quá chậm một cách
không cần thiết (hàng giờ) so với lợi ích thực tế thu được.
**Quyết định:** Chọn (c). Thêm `IUnpackProvider.InspectPackagesAsync(gameProfile,
aesKeyHex, IReadOnlyList<string> virtualPaths, progress, ct)` — nhận vào 1 lô
virtual path (do `LocalizationDiscoveryService` đã lọc rẻ theo tên trước),
mount **đúng 1 lần** bên trong method này, đọc export table (tên class của
từng export, KHÔNG đọc pixel/vertex/text thật) cho từng path trong lô, trả về
`PackageExportSummary` rồi đóng provider khi method trả kết quả.
`LocalizationDiscoveryService` tự quyết định export class nào tính là
StringTable — Unpacking chỉ chịu trách nhiệm "đọc được gì", không quyết định
"cái đó có phải file ngôn ngữ không".
**Lý do:**
- (a) bị loại vì tạo ra 1 object có trạng thái (stateful session) sống xuyên
  suốt nhiều module, cần App/Cli tự quản lý vòng đời (mở khi bắt đầu, đóng
  khi đổi game/thoát) — đúng thứ Hải Long muốn tránh (rủi ro dispose sai lúc,
  dùng provider đã đóng, phức tạp hơn cần thiết).
- (b) bị loại vì chi phí remount nhân với số lượng file `.uasset` cần soi
  (có thể hàng nghìn) khiến tổng thời gian từ "vài phút" thành "hàng giờ" —
  đây là mức chậm không hợp lý ngay cả khi ưu tiên sự đơn giản.
- (c) giữ đúng phong cách kiến trúc hiện tại của Core: mỗi method
  `Result<T>`-based tự mở tài nguyên, tự dùng, tự đóng trong 1 lần gọi —
  không có state chia sẻ giữa các lời gọi hay giữa các module — nhưng gộp
  nhiều lần đọc trong CÙNG 1 bước vào 1 lần mount duy nhất, tránh remount dư
  thừa.
**Đánh đổi chấp nhận:** Mỗi bước xử lý lớn (liệt kê, soi export, extract) vẫn
mount lại pak/IoStore riêng — tốn thêm vài giây đến vài chục giây mỗi lần so
với phương án giữ session sống liên tục, nhưng chấp nhận được vì tổng cộng
chỉ mount khoảng 2-3 lần cho cả pipeline của 1 game, không phải hàng nghìn
lần. `InspectPackagesAsync` CHƯA implement (Pha 1 chỉ thêm interface) — xem
docs/ROADMAP.md Pha 3.

## ADR-006: Validate AES key candidate bằng cách submit thẳng cho CUE4Parse mount thật, không tự parse pak index + so khớp magic number

**Ngày:** 2026-07-03
**Bối cảnh:** Phác thảo ban đầu (scaffold Pha 0, `docs/DOMAIN_KNOWLEDGE.md`
§2) mô tả bước validate candidate key là "decrypt phần đầu container rồi so
khớp magic number đã biết của UE pak". Khi thực sự implement
`AesKeyResolverService` ở Pha 2, nhận ra cách này đòi hỏi tự parse binary
format của pak footer/index (offset, size, cờ mã hoá, hash...) bằng tay —
trong khi CUE4Parse (thư viện đã nhúng sẵn) đã tự làm đúng việc này: đọc
header lúc `Initialize()` để biết chính xác guid nào cần key
(`provider.RequiredKeys`), và `SubmitKeysAsync(guid, key)` tự thử
decrypt+parse index thật, trả về số archive mount thành công (0 nếu sai
key, ném/nuốt `InvalidAesKeyException` nội bộ).
**Quyết định:** `AesKeyResolverService` validate candidate bằng cách gọi
`provider.SubmitKeysAsync` (dùng `provider.RequiredKeys` để biết đúng guid
cần thử, KHÔNG giả định guid rỗng) và kiểm tra mounted count trả về > 0.
KHÔNG tự viết code parse pak/IoStore index hay so khớp magic number.
Đồng thời sửa 1 bug liên quan phát hiện trong lúc làm việc này:
`CUE4ParseProvider.UnpackAsync` (Pha 1) trước đó submit key theo
`FGuid(0,0,0,0)` (giả định "guid rỗng" là quy ước phổ biến) — đã sửa lại
dùng `provider.RequiredKeys` giống AesKeyResolverService, vì giờ đã biết
cách lấy guid CHÍNH XÁC thay vì đoán.
**Lý do:**
- Tránh viết lại (và có khả năng viết sai) 1 phần logic mà CUE4Parse đã làm
  đúng và được nhiều game thật kiểm chứng qua cộng đồng dùng thư viện này.
- Giảm bề mặt lỗi: không cần tự tính offset/size index, không cần biết magic
  number chính xác theo từng `EPakVersion` (tài liệu gốc còn ghi chú "xem
  code CUE4Parse để lấy giá trị chính xác" — nghĩa là bản thân lúc viết cũng
  chưa chắc chắn con số đó đúng).
- `provider.RequiredKeys` cho biết guid CHÍNH XÁC ngay sau `Initialize()`
  (đọc được từ `reader.IsEncrypted` + `reader.EncryptionKeyGuid` lúc đăng ký
  archive) — đáng tin cậy hơn hẳn giả định "guid rỗng" mà bản Pha 1 ban đầu
  dùng.
**Đánh đổi chấp nhận:** `AesKeyResolverService` phụ thuộc trực tiếp vào
CUE4Parse (không chỉ interface `IUnpackProvider` của module Unpacking) —
chấp nhận được vì đây là dùng chung 1 thư viện third-party ở tầng thấp, khác
với việc 1 module Core gọi thẳng class cụ thể của module Core khác (điều bị
cấm ở CLAUDE.md §5.6). Ngoài ra: quét entropy theo từng byte (stride=1,
không bỏ qua vị trí nào) đã benchmark thật — 100MB dữ liệu ngẫu nhiên hoàn
toàn (kịch bản xấu nhất) mất ~44.5 giây chỉ riêng phần entropy, ngoại suy
~2-8 phút cho 1 file `.exe` thật 300MB-1GB — chấp nhận được theo đúng ưu
tiên "ổn định hơn hiệu năng" mà Hải Long đã xác nhận (xem thảo luận dẫn tới
ADR-005, cùng tinh thần). Xem số liệu đầy đủ ở
docs/DOMAIN_KNOWLEDGE.md mục "Cạm bẫy đã biết".

## ADR-007: `GameProfileStore` — lưu profile+key dạng JSON cạnh app, ghi đè, coi "chưa có" là `Result.Failure`

**Ngày:** 2026-07-03
**Bối cảnh:** Pha 2 cần lưu lại `GameProfile` + AES key đã validate để không
phải chạy lại `GameProfileDetector`/`AesKeyResolverService` (có thể mất vài
phút, xem ADR-006) mỗi lần mở app cho cùng 1 game. Đã bàn thiết kế trực tiếp
với Hải Long trước khi code.
**Quyết định:**
- Thêm `IGameProfileStore` (module `GameProfile`) với `SaveAsync`/`LoadAsync`,
  cả 2 đều theo `Result`/`Result<T>` — **không dùng kiểu trả về nullable**
  cho trường hợp "chưa có profile nào lưu", coi đó là 1 `Result.Failure`
  bình thường (Hải Long xác nhận muốn nhất quán `Result<T>` xuyên suốt, xem
  thảo luận trước ADR này).
- File lưu tại `<AppContext.BaseDirectory>/profiles/<slug>.gameprofile.json`
  — KHÔNG lưu trong thư mục cài game (Steam có thể coi là file lạ khi verify
  integrity; thư mục cài game cũng có thể read-only).
- Tên file lấy từ tên thư mục game, sanitize ký tự không hợp lệ, KHÔNG theo
  hash — ưu tiên dễ đọc thủ công hơn là tránh tuyệt đối trùng tên (chấp nhận
  rủi ro hiếm gặp 2 đường dẫn khác nhau trùng tên thư mục — chưa xử lý, xem
  "Đánh đổi chấp nhận").
- Ghi đè hoàn toàn mỗi lần `SaveAsync` — đây là cache trạng thái mới nhất,
  không phải audit log giữ lịch sử.
- KHÔNG lưu mapping "key nào ứng với guid nào" — lúc `LoadAsync` xong, nơi
  gọi tự thử lại TẤT CẢ key đã lưu với TẤT CẢ guid mà `provider.RequiredKeys`
  đang báo cần (số lượng nhỏ, phép thử chéo không đáng kể chi phí). Tránh để
  kiểu `FGuid` của CUE4Parse rò rỉ vào schema JSON của riêng Core.
- Serialize bằng `System.Text.Json` (có sẵn trong .NET runtime) — KHÔNG dùng
  `Newtonsoft.Json` dù nó đã có mặt gián tiếp qua CUE4Parse/UAssetAPI, vì đó
  là dependency bắc cầu (transitive), không nên dựa vào nếu Core không tự
  khai báo trực tiếp.
**Lý do:** Xem chi tiết từng điểm ở trên — đều là kết quả thảo luận trực
tiếp, không phải quyết định đơn phương.
**Đánh đổi chấp nhận:**
- Trùng tên thư mục game giữa 2 đường dẫn khác nhau sẽ ghi đè lẫn nhau —
  chưa xử lý, chấp nhận được vì use-case hiện tại là 1 người dùng, làm việc
  với 1 game tại 1 thời điểm.
- Không mã hoá file JSON trên đĩa (plaintext) — `.gitignore` loại trừ file
  này là để tránh commit nhầm lên Git, không phải chống người khác đọc được
  trên chính máy Hải Long, nên plaintext là đủ cho model rủi ro này.
- CLI `resolve-key` đã wire tự động gọi `SaveAsync` sau khi tìm được key —
  nhưng `unpack`/`detect` CHƯA tự động `LoadAsync` để tái sử dụng key đã lưu
  (người dùng vẫn phải tự truyền `aesKeyHex` thủ công cho `unpack`) — để dành
  cho lúc làm UI thật (Pha 6) hoặc khi có nhu cầu cụ thể hơn.

## Template cho ADR mới

```
## ADR-XXX: <tên ngắn>

**Ngày:** YYYY-MM-DD
**Bối cảnh:** ...
**Quyết định:** ...
**Lý do:** ...
**Đánh đổi chấp nhận:** ...
```
