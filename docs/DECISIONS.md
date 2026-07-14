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

## ADR-008: `LocalizationDiscoveryService.ScanAsync` nhận `IUnpackProvider` + `GameProfile` + AES key qua tham số, không qua DI

**Ngày:** 2026-07-11
**Bối cảnh:** Khi implement bước 2 của `LocalizationDiscoveryService` (soi
export table các `.uasset`/`.umap` không loại được ở bước 1 rẻ), service cần
gọi `IUnpackProvider.InspectPackagesAsync(gameProfile, aesKeyHex, ...)` —
nhưng chữ ký cũ của `ScanAsync` chỉ nhận `unpackedAssets, progress, ct`,
không có cách nào lấy `gameProfile`/`aesKeyHex`/provider cần dùng. Đây là lỗ
hổng đã ghi nhận trước ở ADR-004/ADR-005 nhưng chưa giải quyết chữ ký cụ thể.
**Quyết định:** Thêm 3 tham số vào `ScanAsync`: `IUnpackProvider
unpackProvider, GameProfile gameProfile, string? aesKeyHex`. `unpackProvider`
nhận tường minh theo tham số do người gọi truyền vào (giống cách
`UnpackAsync` được gọi), KHÔNG inject qua constructor/DI.
**Lý do:** `IUnpackProvider` được đăng ký trong DI dưới 2 key (`"primary"` =
CUE4Parse, `"fmodel-fallback"` = FModel) — việc chọn dùng cái nào là hành
động của người dùng (ADR-002: fallback KHÔNG tự động), nên
`LocalizationDiscoveryService` không được tự ý resolve 1 trong 2 qua DI theo
kiểu ngầm định. Nhận tường minh qua tham số giữ tính nhất quán với
`IUnpackProvider.UnpackAsync` và giữ `LocalizationDiscoveryService` không có
constructor dependency nào cần đăng ký thêm.
**Đánh đổi chấp nhận:** Người gọi (Cli/App, hoặc pipeline điều phối ở Pha
6) phải tự truyền đúng `unpackProvider` đã chọn ở bước Unpack trước đó —
chưa có cơ chế nào ép buộc 2 bước dùng cùng 1 provider ngoài kỷ luật code ở
lớp gọi; chấp nhận được vì cùng mức rủi ro với việc truyền `gameProfile`/
`aesKeyHex` thủ công đã tồn tại sẵn giữa các bước khác trong pipeline.

## ADR-009: Lưu lựa chọn file ngôn ngữ đã xác nhận qua method riêng `SaveConfirmedLocalizationFilesAsync`, không gộp vào `SaveAsync`

**Ngày:** 2026-07-14
**Bối cảnh:** Pha 3 cần cơ chế lưu lại danh sách file ngôn ngữ người dùng đã
xác nhận (từ gợi ý của `LocalizationDiscoveryService.ScanAsync`) vào profile,
để không phải chạy lại bước xác nhận thủ công mỗi lần mở app cho cùng 1 game.
Đã bàn trực tiếp với Hải Long trước khi code (giống ADR-007).
**Quyết định:**
- Thêm `ConfirmedLocalizationFile(string Path, LocalizationFileKind Kind)`
  trong module `LocalizationDiscovery` (khác `LocalizationFileCandidate` ở
  chỗ bỏ `Confidence` — đã confirm thì không cần độ tin cậy nữa).
- `StoredGameProfile` thêm field `ConfirmedLocalizationFiles`.
- Thêm method riêng `IGameProfileStore.SaveConfirmedLocalizationFilesAsync(
  gameDirectory, confirmedFiles, ct)` thay vì mở rộng chữ ký `SaveAsync` hiện
  có — method này ghi đè toàn bộ danh sách nhưng KHÔNG đụng tới
  `Profile`/`ValidatedAesKeys` đã lưu, và fail rõ ràng nếu chưa có profile
  nào lưu (đúng thứ tự pipeline: detect+resolve-key xong mới tới xác nhận
  file ngôn ngữ).
- `SaveAsync` (đã có từ ADR-007) khi được gọi lại (VD: game update, chạy lại
  resolve-key) phải LOAD lại profile cũ trước để giữ nguyên
  `ConfirmedLocalizationFiles` đã có, không ghi đè mất.
**Lý do:** Tách method vì `SaveAsync` và `SaveConfirmedLocalizationFilesAsync`
được gọi ở 2 bước khác nhau của pipeline, do 2 module khác nhau gọi
(`AesKeyResolver`/CLI `resolve-key` vs. `LocalizationDiscovery`/CLI
`discover`+`confirm-locfiles` hoặc UI xác nhận ở Pha 6) — gộp chung 1 chữ ký
sẽ ép người gọi ở bước resolve-key phải biết/truyền cả dữ liệu xác nhận file
ngôn ngữ (chưa tồn tại ở bước đó), ngược lại với tinh thần "mỗi module giao
tiếp qua interface riêng" ở CLAUDE.md §5.6.
**Đánh đổi chấp nhận:**
- 2 lần đọc-ghi file JSON riêng biệt cho 2 bước thay vì 1 lần duy nhất —
  chấp nhận được vì file profile nhỏ (vài KB), không phải đường I/O nóng.
- CLI thêm lệnh `confirm-locfiles <path>:<kind> ...` dạng scriptable (không
  tương tác) để test được `SaveConfirmedLocalizationFilesAsync` mà không cần
  chờ UI Avalonia — đây KHÔNG phải màn hình xác nhận thật cho người dùng
  cuối, chỉ là công cụ test module giống `detect`/`unpack`/`resolve-key`.
  Màn hình xác nhận/tick chọn thật vẫn để dành cho Pha 3 mục UI riêng.

## ADR-010: `IUnpackProvider.ExtractFilesAsync` (extract theo yêu cầu) + `AssetReaderWriter` — `.locres` ghi bằng binary writer tự viết theo `ELocResVersion.Legacy`, StringTable qua UAssetAPI

**Ngày:** 2026-07-14
**Bối cảnh:** Pha 4 cần đọc/ghi thật nội dung 2 loại file ngôn ngữ
(`LocalizationFileKind.Locres` và `.StringTableAsset`). Phát hiện 2 vấn đề
lúc bắt tay implement, cả 2 đều chưa có quyết định trước đó:

1. `IUnpackProvider` (Pha 1/3) chỉ có `UnpackAsync` (liệt kê virtual path,
   không ghi bytes) và `InspectPackagesAsync` (soi export class, không ghi
   bytes) — KHÔNG có cách nào lấy bytes thật của 1 file cụ thể ra đĩa để
   `AssetIO` đọc. Đây là lỗ hổng đã được dự trù từ trước (xem comment cũ
   trong `UnpackedAssetRef`: "cơ chế đọc theo yêu cầu... được thiết kế ở Pha
   3/4") nhưng chưa có chữ ký cụ thể.
2. Không có thư viện nào ghi được `.locres`: CUE4Parse chỉ đọc (read-only theo
   thiết kế), UAssetAPI không hỗ trợ format này (chỉ StringTable trong
   `.uasset`). Phải tự viết binary writer từ đầu.

**Quyết định:**
- Thêm `IUnpackProvider.ExtractFilesAsync(gameProfile, aesKeyHex,
  virtualPaths, outputDirectory, progress, ct)`: mount ĐÚNG 1 LẦN (giống
  `InspectPackagesAsync`, xem ADR-005), dùng
  `AbstractFileProvider.TrySaveAsset(virtualPath, out byte[])` của CUE4Parse
  để lấy bytes thật, ghi ra đĩa giữ nguyên cấu trúc thư mục con theo virtual
  path. Nếu 1 virtual path kết thúc bằng `.uasset`, TỰ ĐỘNG ghi kèm file
  `.uexp`/`.ubulk` cùng tên (nếu tồn tại trong `provider.Files`) dù người gọi
  không yêu cầu — UAssetAPI cần các file này nằm cạnh `.uasset` để tự tìm
  thấy khi parse export data tách file.
- `IAssetReaderWriter.ReadAsync`/`WriteAsync` thêm tham số
  `engineVersionHint` (map từ `GameProfile.EngineVersion`, VD "5.3") — chỉ
  cần cho `StringTableAsset` (UAssetAPI cần `EngineVersion` đúng để parse
  layout binary); `.locres` bỏ qua tham số này vì tự chứa version trong
  header. Nếu không map được đúng tên enum `VER_UE{major}_{minor}` của
  UAssetAPI, FAIL RÕ RÀNG thay vì đoán — khác với `CUE4ParseProvider.
  TryResolveEGame` (đọc, sai chỉ gây lỗi parse), ở đây là GHI: sai
  EngineVersion có thể làm hỏng layout binary asset.
- StringTable: đọc/ghi qua `UAssetAPI.UAsset` + `StringTableExport.Table`
  (kế thừa `TMap<FString,FString>`) — thư viện có sẵn cả đọc và ghi, `Write`
  tự lo lại `.uexp` nếu asset tách file, rủi ro thấp vì đã được cộng đồng
  UAssetAPI kiểm chứng rộng rãi.
- `.locres`: ĐỌC dùng thẳng `CUE4Parse.UE4.Localization.FTextLocalizationResource`
  (đã decompile để xác nhận đúng logic đọc — xem chi tiết dưới). GHI tự viết
  ở `LocresBinaryFormat.Write`, chọn ghi theo **`ELocResVersion.Legacy`**
  (version 0 — phiên bản ĐƠN GIẢN NHẤT, không phải `Optimized_CityHash64_UTF16`
  mà UE5 hiện đại thường tự sinh ra), vì Legacy:
  - Không có magic GUID/version byte ở đầu file.
  - `FTextKey` (namespace/key) chỉ là `FString` thuần, KHÔNG có trường hash
    (`StrHash`) — field này chỉ xuất hiện từ version ≥2 và đòi hỏi tính đúng
    CRC32/CityHash64 theo đúng biến thể nội bộ của UE, không có tài liệu
    chính thức để đối chiếu 100%.
  - `FEntry.LocalizedString` được ghi TRỰC TIẾP, không qua bảng string dùng
    chung có ref-count (bảng đó chỉ dùng từ version ≥1) — tránh 1 tầng gián
    tiếp phức tạp không cần thiết.
  - Đã decompile `FTextLocalizationResource` (constructor nhận `FArchive`)
    bằng `ilspycmd` để xác nhận: engine (qua lăng kính CUE4Parse) chấp nhận
    đọc version 0-3 (`if ((int)eLocResVersion > 3) throw`), tức Legacy vẫn là
    file `.locres` hợp lệ, chỉ đơn giản hơn.
  - `FEntry.SourceStringHash` (field LUÔN có mặt, mọi version) ghi bằng
    CRC32 chuẩn IEEE 802.3 của chính text — đây là suy đoán hợp lý nhất hiện
    có (không chắc UE dùng bảng CRC32 chuẩn hay bảng tự chế riêng của
    `FCrc`), nhưng theo hiểu biết hiện tại field này chỉ dùng để Editor cảnh
    báo "bản dịch cũ" (stale translation), KHÔNG dùng để tra cứu Namespace+Key
    lúc runtime (lookup theo string trực tiếp ở Legacy).
**Lý do:** Không có lựa chọn nào khác an toàn hơn để GHI `.locres` — không thư
viện nào hỗ trợ. Chọn Legacy để tối thiểu hoá số field cần đoán đúng thuật
toán hash nội bộ của UE (chỉ còn duy nhất `SourceStringHash`, rủi ro thấp
theo phân tích ở trên) thay vì cả `StrHash` + string pool ref-count nếu chọn
version mới hơn.
**Đánh đổi chấp nhận / rủi ro CHƯA verify:**
- **CHƯA test với file `.locres` thật + CHƯA load thử trong game.** Test tự
  động hiện có (`AssetReaderWriterTests.Locres_*`) chỉ là round-trip: ghi
  bằng writer tự viết rồi đọc lại bằng CUE4Parse — xác nhận 2 chiều nhất
  quán với nhau, KHÔNG xác nhận UE engine thật đọc được. Đây là rủi ro cao
  nhất còn lại trong toàn bộ AssetIO — **bắt buộc Hải Long test thực tế**
  (ghi file, repack, load trong game, xem text tiếng Việt có hiện đúng
  không) trước khi coi module này ổn định.
- Nếu game cụ thể (VD Dragonwilds) chỉ chấp nhận đọc `.locres` từ đúng
  `Optimized_CityHash64_UTF16` trở lên (không rõ có game nào chặn Legacy hay
  không, nhưng chưa loại trừ được khả năng này), sẽ cần quay lại implement
  version mới hơn — lúc đó bắt buộc phải có file `.locres` thật của game để
  đối chiếu hash byte-by-byte trước khi viết, không nên đoán tiếp.
- `ExtractFilesAsync` ghi `.uexp`/`.ubulk` "mù" (không biết chắc UAssetAPI
  version 1.1.0 có cần thêm phụ trợ nào khác không, VD `.ubulk` tách riêng
  cho asset lớn) — StringTable thường nhỏ nên khả năng cần `.ubulk` thấp,
  nhưng chưa loại trừ hoàn toàn.

## ADR-011: Gemini API key lưu ở file config JSON riêng (`config/gemini-settings.json`), không dùng biến môi trường

**Ngày:** 2026-07-14
**Bối cảnh:** Pha 4 cần cấu hình Gemini API key cho `GeminiTranslationService`.
Đã hỏi Hải Long cách tool Unity cũ làm để giữ nhất quán.
**Quyết định:** Hải Long chọn file config JSON riêng cạnh app (KHÔNG dùng env
var), giống hệt cơ chế `GameProfileStore` (ADR-007): lưu tại
`<AppContext.BaseDirectory>/config/gemini-settings.json`, gồm `ApiKey` +
`Model` (cho phép đổi model qua config khi Google đổi tên/deprecate model mà
không cần sửa code). Thêm `IGeminiSettingsStore` (module `Translation`) với
`SaveAsync`/`LoadAsync` theo đúng pattern `Result`/`Result<T>` nhất quán toàn
Core. CLI `set-gemini-key <apiKey> [model]` để Hải Long tự cấu hình.
**Lý do:** Theo lựa chọn trực tiếp của Hải Long. Cùng model rủi ro với AES key
trong `GameProfileStore` (plaintext trên đĩa, `.gitignore` loại trừ để tránh
commit nhầm — chấp nhận được vì máy cá nhân 1 người dùng).
**Đánh đổi chấp nhận:** File `config/gemini-settings.json` thêm vào
`.gitignore`.

## ADR-012: `RepackService` gọi CLI ngoài `repak`/`retoc` qua subprocess — ngoại lệ có chủ đích với ADR-001

**Ngày:** 2026-07-14
**Bối cảnh:** Pha 4 cần đóng gói lại asset đã sửa thành pak/IoStore. Research
cho thấy KHÔNG có thư viện .NET nào ghi được pak/IoStore trong stack hiện
tại:
- CUE4Parse: read-only hoàn toàn theo thiết kế, không có API ghi container
  nào.
- UAssetAPI: có API ghi `.pak` (`PakBuilder`/`PakWriter`) NHƯNG chỉ là lớp
  P/Invoke gọi vào 1 thư viện NATIVE ngoài (`RePakInterop`, field `NativeLib`)
  — thư viện native này KHÔNG được đóng gói kèm trong bản NuGet UAssetAPI
  1.1.0 (đã kiểm tra nội dung `.nupkg`: chỉ có `UAssetAPI.dll`, không có
  `.dll`/`.so`/`.dylib` native nào khác). Gọi API này sẽ crash lúc runtime vì
  thiếu native lib.
- KHÔNG có API ghi IoStore (`.utoc`/`.ucas`) ở bất kỳ đâu trong 2 thư viện
  đang dùng.

Đã research và xác nhận: `RePakInterop` của UAssetAPI thực chất là binding
tới crate Rust **`repak`** (github.com/trumank/repak, cùng tác giả với
`retoc` bên dưới) — dự án này có bản CLI standalone tải sẵn từ GitHub
Releases, hỗ trợ `pack` (đóng gói thư mục thành `.pak` Legacy). Cho IoStore,
tác giả `trumank` cũng có **`retoc`** (github.com/trumank/retoc), CLI có lệnh
`to-zen` chuyển đổi 1 file `.pak` Legacy sang `.utoc`/`.ucas` IoStore (ví dụ
trong README: `retoc to-zen legacy_P.pak iostore.utoc --version UE5_4`).

**Quyết định:** `RepackService` gọi 2 CLI này qua subprocess
(`System.Diagnostics.Process`, helper dùng chung `ExternalToolRunner`), KHÔNG
tự viết binary writer (quá rủi ro, xem ADR-010 làm ví dụ đối chứng — pak/
IoStore container phức tạp hơn `.locres` nhiều) và KHÔNG tự P/Invoke trực
tiếp vào native lib (không có sẵn, phải tự build từ source Rust — việc build
tooling nằm ngoài phạm vi hợp lý của 1 phiên code C#).

Luồng xử lý trong `RepackService.RepackAsync`:
1. LUÔN đóng gói `modifiedAssetsDirectory` thành `.pak` Legacy trước bằng
   `repak pack -v <tên-thư-mục>` — kể cả khi đích cuối là IoStore, vì
   `retoc to-zen` cần nhận input là `.pak`, không nhận thẳng thư mục rời. Vì
   README công khai của `repak` không xác nhận rõ cách chỉ định output path
   tường minh (chỉ có ví dụ suy ra tên output từ tên thư mục input, VD
   `repak pack -v mod` → `mod.pak`), ta COPY asset vào 1 thư mục tạm đặt tên
   TRÙNG base filename output mong muốn trước khi gọi `repak`, rồi tự
   `File.Move` kết quả tới đúng vị trí — né hoàn toàn sự không chắc chắn về
   cú pháp CLI thay vì đoán flag.
2. Nếu `GameProfile.PakFormat` là IoStore/Both, convert `.pak` vừa tạo sang
   `.utoc`/`.ucas` bằng `retoc to-zen ... --version UE{major}_{minor}`.
3. `ExternalToolRunner` capture đầy đủ stdout/stderr + exit code, trả
   `Result.Failure` rõ ràng khi subprocess lỗi (khác `.locres`: ở đây sai cú
   pháp/version sẽ FAIL RÕ RÀNG qua exit code khác 0, KHÔNG âm thầm sinh ra
   file hỏng — rủi ro thấp hơn nhiều so với ADR-010).

**Lý do:** Đã hỏi trực tiếp Hải Long trước khi code (2 câu hỏi riêng: có nên
dùng CLI ngoài không, sau đó Hải Long chủ động gợi ý `retoc` cho phần
IoStore). Đây là ngoại lệ CÓ CHỦ ĐÍCH, GIỚI HẠN PHẠM VI với ADR-001: ADR-001
cấm dùng subprocess-IPC làm KIẾN TRÚC CHÍNH của toàn bộ Core (tránh lỗi ở
ranh giới 2 runtime cho mọi thao tác), không cấm gọi 1 CLI tool xác định,
input/output rõ ràng, cho riêng 1 bước hẹp (repack) khi không còn lựa chọn
in-process nào khả thi.
**Đánh đổi chấp nhận / rủi ro CHƯA verify:**
- Hải Long phải tự tải `repak`/`retoc` từ GitHub Releases và đặt vào PATH
  (hoặc truyền đường dẫn đầy đủ qua tham số `repakExecutablePath`/
  `retocExecutablePath`) — KHÔNG bundle kèm app, giống tinh thần FModel
  fallback ở ADR-002.
- Cú pháp CLI cụ thể (`repak pack -v <dir>`, `retoc to-zen <pak> <utoc>
  --version <ver>`) suy ra từ đọc README công khai trên GitHub (không chạy
  thử binary thật trong sandbox này — không có mạng để tải binary lúc code).
  Unit test (`RepackServiceTests`) chỉ verify bằng SHELL SCRIPT giả lập hành
  vi mô tả trong README, KHÔNG phải chạy `repak`/`retoc` thật. **Hải Long BẮT
  BUỘC phải tự tải 2 binary thật, chạy thử `repack` CLI với asset đã sửa
  thật, xác nhận file `.pak`/`.utoc` sinh ra load được trong game** trước khi
  coi Pha 4 hoàn thành trọn vẹn.
- retoc version string (`UE{major}_{minor}`) suy ra từ 1 ví dụ duy nhất trong
  README (`UE5_4`) — chưa có danh sách đầy đủ version hợp lệ để đối chiếu.

## ADR-013: `FModelFallbackAdapter` tái diễn giải `GameProfile.GameDirectory` thành thư mục FModel đã export

**Ngày:** 2026-07-14
**Bối cảnh:** Pha 5 implement `FModelFallbackAdapter` (ADR-002: fallback khi
CUE4Parse fail, người dùng tự export bằng FModel). `IUnpackProvider` có chữ
ký DÙNG CHUNG cho cả `CUE4ParseProvider` và adapter này (ADR-008: người gọi
không cần biết đang dùng provider nào) — nhưng `GameProfile` có các field chỉ
có ý nghĩa với luồng chính (`ExecutablePath`, `PaksDirectory`, `PakFormat` —
suy ra từ quét `.exe`/`Content/Paks/` của game cài thật), trong khi thư mục
FModel export không có `.exe`/pak nào để `GameProfileDetector` quét ra các
field đó.
**Quyết định:** Khi dùng qua `FModelFallbackAdapter`, `GameProfile.GameDirectory`
được TÁI DIỄN GIẢI thành "thư mục gốc mà FModel đã export ra" (chứa cây
`GameName/Content/...` y hệt cấu trúc virtual path bên trong pak/IoStore, vì
đó là hành vi export mặc định của FModel). Người gọi (App/CLI ở Pha 6, hoặc
CLI test ở Pha 5) tự tạo 1 `GameProfile` "giả" cho trường hợp này — không
chạy qua `GameProfileDetector.DetectAsync` (sẽ fail vì không có `.exe`) — chỉ
cần `GameDirectory` (bắt buộc) và `EngineVersion` (cần cho `InspectPackagesAsync`
mở `.uasset` bằng UAssetAPI; người dùng tự gõ tay vì FModel hiển thị UE
version lúc export). `ExecutablePath`/`PaksDirectory`/`PakFormat`/`ExecutableHash`
không có ý nghĩa trong luồng này, để rỗng/`Unknown`.
`UnpackAsync` trả luôn `ExtractedFilePath` (khác `CUE4ParseProvider` — file đã
có sẵn trên đĩa, không cần bước `ExtractFilesAsync` riêng để có bytes thật,
nhưng vẫn implement đầy đủ `ExtractFilesAsync` để giữ đúng contract, chỉ đơn
giản là copy sang `outputDirectory`).
`InspectPackagesAsync` không mount pak nên không có cách đọc RIÊNG export
table (rẻ) như `CUE4ParseProvider` — phải mở FULL `UAsset` qua UAssetAPI cho
từng file. Logic mở `UAsset` + đọc `EngineVersion` từ string LẶP LẠI (không
gọi qua module `AssetIO`) — CLAUDE.md §5.6 cấm module gọi thẳng class cụ thể
của module khác, nên chấp nhận trùng lặp vài dòng code nhỏ giữa
`AssetReaderWriter.ResolveEngineVersion` và
`FModelFallbackAdapter.TryResolveEngineVersion` thay vì phá vỡ ranh giới
module.
**Lý do:** Đây là cách duy nhất giữ nguyên được chữ ký `IUnpackProvider` dùng
chung cho 2 provider (đúng tinh thần ADR-002/ADR-008) mà không phải thêm 1
tham số "đường dẫn export" riêng chỉ dùng cho 1 trong 2 implementation.
**Đánh đổi chấp nhận:** Tên field `GameDirectory` gây hiểu lầm khi đọc code ở
ngữ cảnh fallback (không phải "thư mục cài game" theo nghĩa đen) — chấp nhận
được vì đã ghi rõ trong XML doc của `FModelFallbackAdapter` và ADR này, và
đây là luồng hiếm gặp theo đúng kỳ vọng ở ADR-002. **CHƯA có UI/CLI hướng dẫn
người dùng tạo `GameProfile` "giả" này** — cần làm ở phần UI hướng dẫn thủ
công còn lại của Pha 5.

## Template cho ADR mới

```
## ADR-XXX: <tên ngắn>

**Ngày:** YYYY-MM-DD
**Bối cảnh:** ...
**Quyết định:** ...
**Lý do:** ...
**Đánh đổi chấp nhận:** ...
```
