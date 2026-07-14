# Roadmap

> Cập nhật checkbox khi hoàn thành. Đây là nơi Hải Long theo dõi tiến độ
> nhanh mà không cần đọc code. Mỗi lần hoàn thành 1 mục, commit kèm cập nhật
> file này trong cùng lần commit.

## Pha 0 — Khung sườn (scaffold)
- [x] Cấu trúc solution (Core / App / Cli / Tests)
- [x] CLAUDE.md + docs nền (kiến trúc, domain knowledge, ADR log)
- [x] Build thành công lần đầu trên máy có .NET SDK thật (2026-07-03, cài
      .NET 8 SDK 8.0.422 qua `dotnet-install.sh`; `dotnet build` toàn solution
      pass, 4/4 test trong `UEVietTranslator.Core.Tests` pass — xem
      ADR-003 và mục "Cạm bẫy đã biết" trong `docs/DOMAIN_KNOWLEDGE.md` về
      warning `MSB3246` vô hại liên quan `CUE4Parse-Natives.dll`)
- [x] Thêm reference CUE4Parse + UAssetAPI qua NuGet, verify restore được
      (CUE4Parse `1.2.2`, UAssetAPI `1.1.0` — lý do chọn version xem ADR-003)

## Pha 1 — Nhận diện game & Unpack cơ bản (không AES)
- [x] `GameProfile`: detect UE version từ `.exe`, tìm `.pak`/`.utoc+.ucas`
      trong `Content/Paks/` (2026-07-03 — detect pak/IoStore đã có từ Pha 0
      scaffold; vừa thêm detect UE version bằng cách quét chuỗi build-version
      ASCII nhúng trong `.exe`, xem comment trong `GameProfileDetector.cs`.
      Test bằng fixture tự tạo, **CHƯA test với file `.exe` thật của
      Dragonwilds** — cần Hải Long tự chạy CLI `detect` để xác nhận)
- [x] `Unpacking.CUE4ParseProvider`: unpack thử không key, list được cây thư
      mục asset (2026-07-03 — dùng `DefaultFileProvider` của CUE4Parse:
      `Initialize()` quét pak/IoStore, `Mount()` không key (đúng scope Pha 1),
      liệt kê `provider.Files` thành virtual path. **QUYẾT ĐỊNH KIẾN TRÚC:**
      Pha 1 CHỈ liệt kê virtual path, KHÔNG ghi bytes asset ra đĩa — xem
      ADR-004 (lý do: tránh extract hàng chục-trăm GB chỉ để tìm vài file
      ngôn ngữ; đây là quyết định tự đưa ra khi hỏi Hải Long không kịp phản
      hồi, cần review lại ADR-004). Có test tích hợp gọi CUE4Parse thật với
      pak rác để xác nhận Result.Failure thay vì crash — **CHƯA test với
      Dragonwilds thật**)
- [x] `Cli`: lệnh test thủ công `unpack <đường dẫn game>` để Hải Long tự chạy
      thử với Dragonwilds (2026-07-03 — `dotnet run --project src/UEVietTranslator.Cli
      -- unpack "<đường dẫn>"`; đã smoke-test bằng thư mục giả lập, in ra
      UE version/pak format/tổng số asset + danh sách sơ bộ file `.locres`
      nếu có. **CẦN Hải Long tự chạy với Dragonwilds thật và báo lại output**)

## Pha 2 — AES Key Resolution
- [x] `AesKeyResolver`: quét `.exe` tìm candidate key (2026-07-03 — quét
      từng byte, tính Shannon entropy window 32 byte, ngưỡng `4.0` — xem
      comment trong `AesKeyResolverService.cs` về lý do 4.0 chứ không phải
      7.5/8.0 đã tính nhầm lúc đầu. Benchmark thật: ~44.5s/100MB kịch bản xấu
      nhất, xem docs/DOMAIN_KNOWLEDGE.md)
- [x] Validate candidate (2026-07-03 — **ĐỔI HƯỚNG so với phác thảo ban đầu**:
      không tự parse pak index + so khớp magic number, mà submit thẳng cho
      CUE4Parse thử mount thật qua `provider.SubmitKeysAsync` dựa trên
      `provider.RequiredKeys` — xem ADR-006. Hỗ trợ multi-key cơ bản: quét
      tiếp tìm key cho các guid còn thiếu sau khi tìm được 1 key, không dừng
      ngay lần đầu tìm thấy)
- [x] Lưu key theo `GameProfile` kèm hash `.exe` (2026-07-03 — thêm
      `IGameProfileStore`/`GameProfileStore` trong module `GameProfile`, xem
      ADR-007 cho thiết kế đầy đủ đã bàn với Hải Long trước khi code: JSON tại
      `profiles/<slug-tên-thư-mục-game>.gameprofile.json` cạnh app, ghi đè,
      "chưa có" là `Result.Failure` giống mọi lỗi dự kiến khác trong Core.
      CLI `resolve-key` đã tự gọi `SaveAsync` khi tìm được key. **CHƯA** wire
      tự động `LoadAsync` vào `unpack`/`detect` để tái sử dụng — để dành Pha 6)
- [x] Cập nhật `docs/DOMAIN_KNOWLEDGE.md` mục "Cạm bẫy đã biết" với benchmark
      hiệu năng thực tế. **CHƯA có phát hiện thực tế từ Dragonwilds** (game có
      mã hoá hay không, key cụ thể...) — cần Hải Long tự chạy
      `dotnet run --project src/UEVietTranslator.Cli -- resolve-key "<đường dẫn>"`
      (sau khi đã xác nhận qua lệnh `unpack` rằng game này CÓ mã hoá) và báo
      lại kết quả.

## Pha 3 — Phát hiện & xác nhận file ngôn ngữ

**Kiến trúc đã chốt (2026-07-03, xem ADR-005 — đã thảo luận trực tiếp với
Hải Long, ưu tiên đơn giản/ổn định hơn hiệu năng vì pipeline chỉ chạy 1-vài
lần cho mỗi game):** mount pak/IoStore **1 lần cho mỗi bước xử lý**, không
giữ session sống xuyên suốt cả pipeline, KHÔNG remount lại theo từng file
riêng lẻ (remount theo từng file trong hàng nghìn `.uasset` sẽ mất hàng giờ
thay vì vài phút — bị loại vì quá chậm một cách không cần thiết). Interface
`IUnpackProvider.InspectPackagesAsync(gameProfile, aesKeyHex, virtualPaths,
progress, ct)` đã được thêm (xem `Unpacking/IUnpackProvider.cs`) — mount
đúng 1 lần cho cả lô path truyền vào, trả về `PackageExportSummary` (tên
class từng export, KHÔNG đọc pixel/vertex/text thật). **CHƯA implement**
(`CUE4ParseProvider.InspectPackagesAsync` đang throw `NotImplementedException`)
— đây là việc cần làm ở Pha 3.

- [x] Implement `CUE4ParseProvider.InspectPackagesAsync` (2026-07-09 — mount
      1 lần qua helper `MountProviderAsync` mới (tách ra dùng chung với
      `UnpackAsync`, tránh lặp code Initialize/SubmitKeys/Mount), sau đó gọi
      `provider.TryLoadPackage` cho từng virtual path trong lô, đọc
      `UObject.ExportType` của từng export — KHÔNG ghi gì ra đĩa. Package lỗi
      parse trả về `ExportClassNames` rỗng thay vì fail cả lô, đúng thiết kế
      trong `PackageExportSummary`. Đã build + test thành công trên máy thật
      (2026-07-14, .NET 8 SDK 8.0.422) — API CUE4Parse `1.2.2` khớp giả định
      ban đầu, không cần sửa gì.)
- [x] `LocalizationDiscoveryService` quét theo 2 bước, chi phí khác nhau rõ rệt
      (2026-07-11):
  - **Bước 1 (rẻ — chỉ đọc virtual path, không đọc nội dung):** lọc theo đuôi
    `.locres` (confidence 1.0), hoặc nằm trong thư mục
    `Content/Localization/...` mà không phải `.locres` (confidence 0.6 — có
    thể là `.locmeta`/manifest chứ không phải file ngôn ngữ thật).
  - **Bước 2 (đắt hơn nhưng chỉ mount 1 lần — xem ADR-005):** với
    `.uasset`/`.umap` còn lại (không loại được ở bước 1), gọi
    `InspectPackagesAsync` MỘT LẦN cho cả lô, export class `StringTable`
    (confidence 0.9) hoặc `DataTable` (confidence 0.5, rộng hơn nên kém chắc
    chắn hơn) tính là `LocalizationFileKind.StringTableAsset`.
  - Fallback: đuôi text-like khác (`.json/.xml/.csv/.txt/.yaml/.yml`) ngoài
    thư mục Localization → `Unknown`, confidence thấp (0.2) — xem
    docs/DOMAIN_KNOWLEDGE.md §3c.
  - **QUYẾT ĐỊNH KIẾN TRÚC kèm theo:** đổi chữ ký
    `ILocalizationDiscoveryService.ScanAsync` để nhận thêm
    `IUnpackProvider unpackProvider, GameProfile gameProfile, string? aesKeyHex`
    (cần để gọi `InspectPackagesAsync` ở bước 2 — lỗ hổng đã ghi nhận ở
    ADR-004/ADR-005). `unpackProvider` nhận tường minh qua tham số (giống
    `UnpackAsync`), KHÔNG inject qua DI, vì chọn CUE4Parse chính hay FModel
    fallback là hành động người dùng (ADR-002) — nhất quán với cách
    `IUnpackProvider` đã được đăng ký keyed trong `CoreServiceCollectionExtensions`.
    Đây là mở rộng cơ học theo đúng thiết kế đã chốt ở ADR-005, không phải
    hướng mới nên không dừng lại hỏi trước khi làm.
  - Có 5 unit test dùng fake `IUnpackProvider` (không cần CUE4Parse thật) ở
    `LocalizationDiscoveryServiceTests.cs`. Đã build + test thành công trên
    máy thật (2026-07-14, .NET 8 SDK 8.0.422).
- [x] Cơ chế lưu lựa chọn của người dùng vào profile (không phải chọn lại mỗi
      lần) (2026-07-14 — xem ADR-009: thêm `ConfirmedLocalizationFile`,
      `StoredGameProfile.ConfirmedLocalizationFiles`, và
      `IGameProfileStore.SaveConfirmedLocalizationFilesAsync` (method riêng,
      không gộp vào `SaveAsync`). `SaveAsync` sửa để giữ nguyên lựa chọn đã
      xác nhận khi được gọi lại (VD: sau khi game update). Thêm CLI
      `discover`/`confirm-locfiles` để test được mà chưa cần UI. Build + 27/27
      test pass trên máy thật (2026-07-14, .NET 8 SDK 8.0.422) — bao gồm 3
      test mới cho `GameProfileStore`.)
- [x] UI (Avalonia) màn hình xác nhận/tick chọn thủ công (2026-07-14 — thay
      `MainWindow`/`MainWindowViewModel` placeholder Pha 0 bằng màn hình đơn
      thật: ô nhập thư mục game + AES key tuỳ chọn, nút "Quét" chạy
      detect→unpack→`ScanAsync` (tái dùng Core qua DI, keyed
      `IUnpackProvider` "primary" inject qua `[FromKeyedServices]`), danh
      sách candidate dạng `ItemsControl` với `CheckBox` + `ComboBox` chọn
      `Kind`, nút "Lưu lựa chọn" gọi `SaveConfirmedLocalizationFilesAsync`.
      Đây KHÔNG phải wizard đầy đủ — chỉ 1 màn hình đơn, sẽ tách thành 1 bước
      trong wizard ở Pha 6. Build sạch + 27/27 test pass trên máy thật. App
      đã chạy thử được (process sống, không crash, không lỗi binding XAML)
      nhưng **CHƯA verify được bằng mắt** — sandbox không có quyền
      Accessibility để đưa cửa sổ lên foreground chụp screenshot. Cần Hải
      Long tự chạy `dotnet run --project src/UEVietTranslator.App` và xác
      nhận layout hiển thị đúng.

## Pha 4 — Extract → CSV → Dịch → Repack
- [x] `AssetIO`: đọc/ghi StringTable qua UAssetAPI; đọc/ghi `.locres`
      (2026-07-14 — xem ADR-010 để đọc đầy đủ:
      - Thêm `IUnpackProvider.ExtractFilesAsync` (extract theo yêu cầu — lỗ
        hổng thiết kế còn sót từ Pha 1/3, nay mới cần dùng thật) — mount 1
        lần, ghi bytes thật ra đĩa qua `TrySaveAsset`, tự kèm `.uexp`/`.ubulk`
        nếu có để UAssetAPI tìm thấy.
      - StringTable: đọc/ghi qua `UAssetAPI.UAsset` + `StringTableExport.Table`
        — rủi ro THẤP, thư viện có sẵn cả 2 chiều, đã được cộng đồng kiểm
        chứng.
      - `.locres`: ĐỌC qua `CUE4Parse.FTextLocalizationResource` (đã
        decompile bằng `ilspycmd` để xác nhận đúng logic đọc). GHI: KHÔNG có
        thư viện nào hỗ trợ (CUE4Parse read-only, UAssetAPI không có format
        này) — tự viết binary writer (`LocresBinaryFormat.cs`) theo
        `ELocResVersion.Legacy` (phiên bản đơn giản nhất, né được phần hash
        nội bộ UE không có tài liệu chính thức để đối chiếu). **Đây là phần
        rủi ro nhất trong toàn bộ Pha 4 — CHƯA test với file `.locres` thật,
        CHƯA load thử trong game.** Test tự động hiện có chỉ là round-trip
        (ghi bằng writer tự viết, đọc lại bằng CUE4Parse) — xác nhận nhất
        quán nội bộ, KHÔNG xác nhận UE engine thật đọc được.
      - Build + 31/31 test pass trên máy thật (.NET 8 SDK 8.0.422). Thêm CLI
        `read-locfile <gameDir> <path>:<kind> [aesKeyHex]` để Hải Long tự
        test đọc file thật từ Dragonwilds — **CHƯA chạy thử với game thật.**
      - **VIỆC BẮT BUỘC trước khi coi AssetIO ổn định:** Hải Long cần ghi thử
        1 file `.locres`, repack (sau khi Pha 4 xong `Repacking`), load trong
        game, xác nhận text tiếng Việt hiển thị đúng. Nếu không hiện/hiện sai
        → xem docs/DOMAIN_KNOWLEDGE.md mục "Cạm bẫy đã biết" 2026-07-14 để
        biết hướng debug.)
- [x] `CsvSchema`: convert 2 chiều asset ↔ CSV chuẩn (key, source, context,
      translation, status) (2026-07-14 — dùng CsvHelper, rủi ro thấp vì
      không đụng binary UE. `ExportAsync` để `TranslatedText` rỗng lúc export
      lần đầu (không copy sẵn `SourceText` — tránh nhầm "đã dịch"). `ImportAsync`
      fallback `Status` về `Untranslated` nếu người dùng gõ tay sai giá trị
      trong Excel, không fail cả file. Test round-trip với text tiếng Việt có
      dấu phẩy/xuống dòng/dấu ngoặc kép — xác nhận CsvHelper tự quote đúng
      chuẩn RFC 4180.)
- [x] `Translation`: tích hợp Gemini API (2026-07-14 — xem ADR-011: API key
      lưu file config JSON riêng `config/gemini-settings.json` (Hải Long chọn,
      không dùng env var), CLI `set-gemini-key` để cấu hình. Dịch theo lô 20
      dòng/request, dùng Gemini "JSON mode" (`responseMimeType: application/json`
      + `responseSchema` ép kiểu `ARRAY<STRING>`) để tránh phải tự parse text
      tự do. 1 lô lỗi không chặn các lô khác — dòng lỗi giữ nguyên
      `Untranslated`, chỉ fail toàn bộ nếu MỌI lô đều lỗi. Test bằng
      `HttpMessageHandler` giả lập, KHÔNG gọi Gemini thật — **CHƯA test với
      API key thật**, cần Hải Long tự chạy `set-gemini-key` rồi thử dịch 1 lô
      nhỏ để xác nhận request/response schema đúng như Gemini API hiện tại.)
- [x] `Repacking`: ghi lại pak/IoStore từ asset đã sửa (2026-07-14 — xem
      ADR-012, quyết định quan trọng nhất Pha 4 ngoài rủi ro `.locres`:
      KHÔNG có thư viện .NET nào ghi được pak/IoStore (CUE4Parse read-only,
      UAssetAPI cần native lib không được đóng gói kèm) — `RepackService` gọi
      2 CLI ngoài `repak` (Legacy `.pak`) và `retoc` (`to-zen` convert sang
      IoStore) qua subprocess, đây là ngoại lệ có chủ đích/giới hạn phạm vi
      với ADR-001. **CHƯA test với binary `repak`/`retoc` thật** (sandbox
      không có mạng để tải lúc code) — test tự động chỉ dùng shell script giả
      lập hành vi mô tả trong README của 2 tool. **BẮT BUỘC**: Hải Long tự tải
      `repak`/`retoc` từ GitHub Releases (trumank/repak, trumank/retoc), đặt
      vào PATH, chạy thử `dotnet run --project src/UEVietTranslator.Cli --
      repack ...` với asset thật, xác nhận file `.pak`/`.utoc` sinh ra load
      được trong game.)
- [ ] Test full pipeline end-to-end với Dragonwilds — **CẦN Hải Long**: chạy
      trọn luồng detect → unpack → resolve-key → discover → confirm-locfiles
      → read-locfile → (sửa CSV/dịch) → repack → load trong game thật, xem
      text tiếng Việt hiển thị đúng. Đây là bước xác nhận cuối cùng cho toàn
      bộ rủi ro CHƯA verify đã liệt kê ở trên (`.locres` write, `repak`/`retoc`
      CLI, Gemini API schema thật).

## Pha 5 — FModel Fallback Path (ADR-002)
- [x] `Unpacking.FModelFallbackAdapter`: đọc thư mục do người dùng export sẵn
      bằng FModel, tiếp tục pipeline từ bước LocalizationDiscovery (2026-07-14
      — xem ADR-013: `GameProfile.GameDirectory` được TÁI DIỄN GIẢI thành
      "thư mục FModel đã export" khi dùng qua adapter này (không phải thư mục
      cài game thật) — giữ nguyên chữ ký `IUnpackProvider` dùng chung cho cả
      2 provider. `UnpackAsync` liệt kê file có sẵn trên đĩa và trả luôn
      `ExtractedFilePath` (không cần `ExtractFilesAsync` riêng để có bytes,
      nhưng vẫn implement đủ để giữ contract). `InspectPackagesAsync` mở FULL
      `UAsset` qua UAssetAPI cho từng file (không có cách đọc export table
      rẻ khi không mount pak — chấp nhận đánh đổi vì đây là luồng hiếm gặp).
      6 test, build + 51/51 test pass. CLI `discover-fallback <thư mục
      export> <engineVersion>` để test với dữ liệu FModel thật — **CHƯA test
      với FModel export thật** (chưa có game nào cần dùng fallback path để
      thử).)
- [x] UI: màn hình hướng dẫn thủ công khi CUE4Parse fail (2026-07-14 —
      checkbox "Dùng FModel fallback" trong màn hình Pha 3, hiện text hướng
      dẫn + đổi ô "AES key" thành ô "UE version" khi bật, dùng keyed
      `IUnpackProvider` "fmodel-fallback" thay vì "primary" và bỏ qua
      `IGameProfileDetector`. App chạy được không crash (verify qua process +
      log, KHÔNG verify được bằng mắt — xem ghi chú Pha 3 về giới hạn
      Accessibility của sandbox này).

## Pha 6 — Đóng gói & UI hoàn chỉnh
- [ ] Avalonia UI dạng wizard đầy đủ các bước
- [ ] Publish self-contained exe
- [ ] Test với ít nhất 1 game UE khác ngoài Dragonwilds để xác nhận tool
      "dùng chung được", không chỉ hoạt động với 1 game

---

**Trạng thái hiện tại (2026-07-14):** Pha 0-4 code đã xong hoàn toàn (trừ mục
"Test full pipeline end-to-end" — cần Hải Long), verify build + test trên máy
thật (.NET 8 SDK 8.0.422, 45/45 test pass). Pha 4 có 3 rủi ro CHƯA verify với
thực tế (xem chi tiết ở từng mục trên): (1) `.locres` write tự viết binary —
ADR-010, (2) `repak`/`retoc` CLI ngoài chưa chạy thử binary thật — ADR-012,
(3) Gemini API chưa gọi thật. Bước tiếp theo: Pha 5 (FModel fallback) hoặc
Pha 6 (UI wizard đầy đủ) — NÊN ưu tiên Hải Long tự test pipeline thật với
Dragonwilds trước khi đầu tư thêm code mới, vì cả 3 rủi ro trên đều có thể
đổi hướng thiết kế nếu phát hiện sai lúc test thật.

Vẫn cần Hải Long tự chạy CLI với Dragonwilds thật
(`detect`/`unpack`/`resolve-key`/`discover`/`read-locfile`/`repack`) VÀ tự
chạy thử app Avalonia (`dotnet run --project src/UEVietTranslator.App`) để
xác nhận màn hình xác nhận file ngôn ngữ hiển thị/hoạt động đúng — Claude
Code chưa verify được bằng mắt do sandbox không có quyền Accessibility để
chụp screenshot cửa sổ app thật (xem ghi chú ở mục UI Pha 3). Mọi test hiện có đều
dùng fixture giả lập, chưa test với game thật.
