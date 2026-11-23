## [1.0.2](https://github.com/KhanhTQ-Organization/com.ktgame.assetdelivery/compare/v1.0.1...v1.0.2) (2026-11-23)


### Bug Fixes

* add core game ktgame ([8ddd7c4](https://github.com/KhanhTQ-Organization/com.ktgame.assetdelivery/commit/8ddd7c4c959064172eda3fe900d89387d25a60ad))

## [1.0.1](https://github.com/KhanhTQ-Organization/com.ktgame.assetdelivery/compare/v1.0.0...v1.0.1) (2026-11-23)


### Bug Fixes

* big update ([4f902a4](https://github.com/KhanhTQ-Organization/com.ktgame.assetdelivery/commit/4f902a44c4ce14ad58e1812c479daeeca3f29736))

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
