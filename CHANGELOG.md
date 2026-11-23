# 1.0.0 (2026-11-23)


### Bug Fixes

* release package ([4930aef](https://github.com/KhanhTQ-Organization/com.ktgame.assetdelivery/commit/4930aefb2ccd5ceb29c763a2b8d4ff67242144e1))

# Changelog

## [1.0.0] - 2026-11-23
### Added
- Rule polymorphic: `GroupPackRule` (group bất kỳ → một pack) và `PartitionedPackRule` (tự chia nhiều pack theo NumberedKey / Label / Folder partitioner, delivery policy).
- Xuất `{pack}.androidpack` + `build.gradle`, sinh manifest runtime từ kết quả build Addressables.
- Runtime `AssetDelivery`: chuyển hướng bundle qua `InternalIdTransformFunc`, tải pack (interactive / ngầm, retry, hỏi quyền 4G) bằng `UnityEngine.Android.AndroidAssetPacks`.
- Validator + chặn build Android khi content lệch, phân tích asset bị nhân bản giữa các group.
- Pipeline build AAB/APK có hook, CLI, cửa sổ Editor (menu Ktgame ▸ Asset Delivery).
