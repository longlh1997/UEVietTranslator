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
      trong `PackageExportSummary`. **CHƯA build/test được trong sandbox này**
      (không có .NET SDK/mạng — đúng ràng buộc ở CLAUDE.md §4). Cần Hải Long
      chạy `dotnet build` + `dotnet test` trên máy thật để bắt lỗi tên API
      CUE4Parse (`TryLoadPackage`, `IPackage.GetExports()`, `UObject.ExportType`)
      nếu version `1.2.2` khác với giả định.)
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
    `LocalizationDiscoveryServiceTests.cs`. **CHƯA build/test được trong
    sandbox này** (không có .NET SDK — CLAUDE.md §4). Cần Hải Long chạy
    `dotnet build` + `dotnet test` trên máy thật.
- [ ] Cơ chế lưu lựa chọn của người dùng vào profile (không phải chọn lại mỗi
      lần)
- [ ] UI (Avalonia) màn hình xác nhận/tick chọn thủ công

## Pha 4 — Extract → CSV → Dịch → Repack
- [ ] `AssetIO`: đọc/ghi StringTable qua UAssetAPI; đọc/ghi `.locres`
- [ ] `CsvSchema`: convert 2 chiều asset ↔ CSV chuẩn (key, source, context,
      translation, status)
- [ ] `Translation`: tích hợp Gemini API (theo pattern tool Unity cũ)
- [ ] `Repacking`: ghi lại pak/IoStore từ asset đã sửa
- [ ] Test full pipeline end-to-end với Dragonwilds

## Pha 5 — FModel Fallback Path (ADR-002)
- [ ] `Unpacking.FModelFallbackAdapter`: đọc thư mục do người dùng export sẵn
      bằng FModel, tiếp tục pipeline từ bước LocalizationDiscovery
- [ ] UI: màn hình hướng dẫn thủ công khi CUE4Parse fail

## Pha 6 — Đóng gói & UI hoàn chỉnh
- [ ] Avalonia UI dạng wizard đầy đủ các bước
- [ ] Publish self-contained exe
- [ ] Test với ít nhất 1 game UE khác ngoài Dragonwilds để xác nhận tool
      "dùng chung được", không chỉ hoạt động với 1 game

---

**Trạng thái hiện tại:** Pha 0 đã xong hoàn toàn (2026-07-03) — build +
test đã verify trên máy thật. Bước tiếp theo: bắt đầu Pha 1
(`GameProfile` detect + `Unpacking.CUE4ParseProvider` unpack thử với
RuneScape: Dragonwilds).
