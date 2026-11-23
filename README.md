# com.ktgame.assetdelivery

Chia nội dung Addressables thành **Play Asset Delivery pack** để giảm dung lượng tải lần đầu trên Google Play, không cần server.

```
Addressables group          Asset pack (AAB)                  Runtime
Level_Base   ───────────▶   (base, có sẵn khi cài)
Level_Pack1  ───────────▶   level_pack1  fast-follow   ─┐
Level_Pack2  ───────────▶   level_pack2  on-demand     ─┼─▶ AssetDelivery tải pack +
...                                                     │   chuyển đường dẫn bundle
                            ktgame_asset_delivery_manifest.json (Resources)
```

## Cài đặt nhanh
1. Mở **Ktgame ▸ Asset Delivery ▸ Window**, bấm **Tạo settings** (`Assets/AssetDelivery/AssetDeliverySettings.asset`).
2. Thêm rule:
   - **+ Group → pack**: chọn một group bất kỳ, nó thành một asset pack (mặc định on-demand). Không đụng gì tới entry.
   - **+ Chia theo số (Level)**: tự chia entry ra nhiều pack theo số trong address.
3. Xem trước kết quả ngay trong cửa sổ.
4. **Build AAB** để lên Google Play, **Build APK** để test (mọi thứ nằm trong file .apk).

Yêu cầu: Unity 2022.3+, Addressables. Không cần plugin Google (runtime dùng `UnityEngine.Android.AndroidAssetPacks`).

## Rule
Danh sách `rules` là polymorphic: mỗi phần tử chọn một kiểu trong dropdown.

### GroupPackRule – group bất kỳ → một pack
| Field | Ý nghĩa |
|---|---|
| `groups` | Một hoặc nhiều Addressables group, gộp lại thành một pack |
| `packName` | Tên asset pack (a-z, 0-9, `_`) |
| `delivery` | install-time / fast-follow / on-demand |

Ví dụ: group `Costume_HD` → pack `costume_hd` on-demand. Game gọi `AssetDelivery.DownloadForKey("<address bất kỳ trong group>")` trước khi load.

### PartitionedPackRule – tự chia ra nhiều pack
Sở hữu group `baseGroup` và `packGroupPrefix + N`; group N thành pack `packNamePrefix + N`. Entry được tự chuyển group theo partitioner.

| Thành phần | Mặc định | Có sẵn |
|---|---|---|
| Partitioner (entry → index: -1 bỏ qua, 0 base, ≥1 pack) | `NumberedKeyPartitioner` `^Level(\d+)$`, base 100, mỗi pack 50 | `LabelPartitioner`, `FolderPartitioner` |
| Delivery policy (pack → mode) | `FastFollowFirstPolicy` (Pack1 fast-follow, còn lại on-demand) | `FixedDeliveryPolicy`, `PerPackDeliveryPolicy` |

`extraSourceGroups`: group khác cũng được quét, entry khớp rule sẽ được chuyển sang group của rule.

### Mở rộng
- Kiểu rule mới: kế thừa `AssetPackRule` (implement `CollectPlans`, `ClaimsGroup`, `ClaimsPackName`..., tuỳ chọn `Sync` nếu cần di chuyển entry).
- Cách chia mới cho PartitionedPackRule: kế thừa `PackPartitioner` hoặc `DeliveryPolicy`.

Class `[Serializable]` có constructor rỗng sẽ tự hiện trong dropdown.

## Pipeline build
`AssetDeliveryBuilder.Build(mode, options)`:
1. Hook `OnBeforeContent`
2. Đặt thiết lập Android (AAB/APK, symbols, Split Application Binary, Addressables *không* build lại khi build player)
3. AAB: chia group → build Addressables → chuyển bundle của group pack vào `Assets/CustomAssetPacks/{pack}.androidpack/src/main/assets/aa/Android` + `build.gradle` → ghi manifest
   APK: build Addressables → xoá pack → ghi manifest `packsEnabled=false`
4. Validate (lỗi thì dừng)
5. Hook `OnBeforePlayer` → `BuildPipeline.BuildPlayer` → hook `OnAfterPlayer`

Bấm **Build** trong Build Settings vẫn được, `AssetDeliveryPreprocessBuild` sẽ chặn nếu content chưa khớp.

### Hook của game (keystore, version, define...)
```csharp
public sealed class MyGameBuildHook : AssetDeliveryBuildHook
{
    public override void OnBeforeContent(AssetDeliveryBuildContext ctx)
    {
        var version = AssetDeliveryCommandLine.GetArg("-versionName");
        if (!string.IsNullOrEmpty(version)) PlayerSettings.bundleVersion = version;
        // set keystore ...
    }
}
```

### CLI
```
Unity -batchmode -quit -buildTarget Android -projectPath . \
  -executeMethod com.ktgame.assetdelivery.editor.AssetDeliveryCommandLine.BuildAab -outputPath Build/game.aab
```
Có thêm `BuildApk`, `PrepareAabContent`, `PrepareApkContent` (chỉ chuẩn bị content).

## Runtime
```csharp
using com.ktgame.assetdelivery;

// Trước khi vào level
if (!AssetDelivery.IsKeyAvailable("Level120"))
{
    var op = AssetDelivery.DownloadForKey("Level120");   // interactive: hỏi 4G nếu cần
    op.ProgressChanged += o => progressBar.value = o.Progress;
    yield return op;                                      // hoặc await op.ToUniTask()
    if (!op.Succeeded) { ShowError(op.Error); yield break; }
}
Addressables.LoadAssetAsync<GameObject>("Level120");

// Tải ngầm, ưu tiên các level kể từ level tiếp theo
AssetDelivery.StartBackgroundDownloads(new KeyOrderPriority(() => NextLevelKeys()));
```
- **Dependency giữa pack:** lúc build, package tính pack nào tham chiếu asset của pack nào (`dependsOn` trong manifest). `DownloadPack` / `DownloadForKey` tự tải kèm; `IsKeyAvailable` chỉ `true` khi pack và mọi pack phụ thuộc đều đã có.
- **Label và entry thư mục:** `DownloadForLabel(label)`, `IsLabelAvailable(label)`. Asset con của entry là thư mục (`Folder/x.png`) cũng tra được bằng `IsKeyAvailable`.
- `AssetDelivery.DownloadProgress / DownloadCompleted / DownloadFailed`: event toàn cục cho UI.
- `AssetDelivery.ConfirmationHandler`: tự hiện popup trước khi hỏi quyền dùng 4G.
- `AssetDelivery.Initialize(manifest, service)`: thay `IAssetPackService` (ví dụ dùng plugin Google.Play.AssetDelivery) hoặc giả lập khi test.
- Editor / iOS / bản APK: mọi pack coi như có sẵn.

## Giảm dung lượng thêm
**Tìm asset bị nhân bản** trong cửa sổ: liệt kê asset không thuộc group nào nhưng nhiều group (base/pack) cùng dùng. Addressables copy chúng vào từng bundle, nên đưa vào một group dùng chung trong base.

## Kiểm tra riêng của game
```csharp
public sealed class LevelPrefabAddressableCheck : IAssetDeliveryCheck
{
    public void Run(ValidationContext ctx) { /* ctx.Warning(...) / ctx.Error(...) */ }
}
```

## Chuyển từ PlayAssetDeliveryBuilder (fashiondom)
1. Thêm package, mở cửa sổ, **Tạo settings** rồi **+ Chia theo số (Level)** (tự đọc `level_pack_config.txt`). Tên group/pack giữ nguyên `Level_Base`, `Level_Pack<N>`, `level_pack<N>`, `Assets/CustomAssetPacks`.
2. Xoá `Assets/Editor/PlayAssetDeliveryBuilder.cs`.
3. `LevelPackLoader`: bỏ phần config/parse tên/`InternalIdTransformFunc`/tải pack; giữ API cũ làm adapter gọi `AssetDelivery.IsKeyAvailable`, `DownloadForKey`, `StartBackgroundDownloads(new KeyOrderPriority(...))`. Xoá `Assets/Resources/level_pack_config.txt`.
4. Phần keystore/version/CI chuyển thành `AssetDeliveryBuildHook` của game.
5. Build AAB, chạy thử bằng `bundletool` hoặc internal testing track.

## Lưu ý
- Bundle dùng chung do Addressables tự tạo (shader built-in, MonoScript) luôn giữ trong base, kể cả khi group mặc định được tick thành pack.
- Group trong base mà tham chiếu asset nằm trong pack sẽ bị cảnh báo khi Kiểm tra.
- Install-time pack: bundle được Play gộp vào assets của base nên runtime không chuyển hướng. Chưa test thực tế với Addressables, nên ưu tiên fast-follow/on-demand.
- Bundle của pack phải load sau khi pack đã tải; load sớm sẽ báo cảnh báo "Không tìm thấy ... trong pack".
- Content update (remote catalog) chưa được hỗ trợ: package dành cho bản build không có server.
