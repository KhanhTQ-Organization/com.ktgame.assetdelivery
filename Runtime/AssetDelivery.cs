using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace com.ktgame.assetdelivery
{
	public static partial class AssetDelivery
	{
		private const string LogTag = "[AssetDelivery]";

		private static bool _initialized;
		private static AssetDeliveryManifest _manifest;
		private static IAssetPackService _service;
		private static Func<IResourceLocation, string> _previousTransform;

		private static readonly object CacheLock = new object();
		private static readonly Dictionary<string, string> ResolvedBundlePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private static readonly HashSet<string> WarnedBundles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		private static readonly Dictionary<string, PackDownloadOperation> ActiveDownloads = new Dictionary<string, PackDownloadOperation>();
		private static readonly Dictionary<string, float> PackProgress = new Dictionary<string, float>();
		private static readonly HashSet<string> ConfirmedPacks = new HashSet<string>();
		private static readonly HashSet<string> PacksNotInBuild = new HashSet<string>();
		
		public static event Action<string, float> DownloadProgress;
		public static event Action<string> DownloadCompleted;
		public static event Action<string, string> DownloadFailed;
		
		public static IDownloadConfirmationHandler ConfirmationHandler { get; set; }

		public static bool VerboseLogging { get; set; }

		public static AssetDeliveryManifest Manifest
		{
			get
			{
				EnsureInitialized();
				return _manifest;
			}
		}

		public static IAssetPackService Service
		{
			get
			{
				EnsureInitialized();
				return _service;
			}
		}
		
		public static bool PacksEnabled
		{
			get
			{
				EnsureInitialized();
				return _manifest.packsEnabled && _service.IsSupported;
			}
		}

		/// <summary>
		/// Khởi tạo thủ công (tuỳ chọn). Gọi trước khi load Addressables nếu muốn thay manifest/service,
		/// ví dụ dùng plugin Google.Play.AssetDelivery hoặc giả lập trong test.
		/// </summary>
		public static void Initialize(AssetDeliveryManifest manifest = null, IAssetPackService service = null)
		{
			_initialized = false;
			_manifest = manifest;
			_service = service;
			ClearResolvedCache();
			EnsureInitialized();
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			_initialized = false;
			_manifest = null;
			_service = null;
			_previousTransform = null;
			ClearResolvedCache();
			lock (CacheLock) WarnedBundles.Clear();
			ActiveDownloads.Clear();
			PackProgress.Clear();
			ConfirmedPacks.Clear();
			PacksNotInBuild.Clear();
			DownloadProgress = null;
			DownloadCompleted = null;
			DownloadFailed = null;
			ConfirmationHandler = null;
			ResetBackgroundState();
			AssetDeliveryRunner.ResetStatics();
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void AutoInitialize()
		{
			EnsureInitialized();
		}

		private static void EnsureInitialized()
		{
			if (_initialized) return;
			_initialized = true;

			_manifest ??= AssetDeliveryManifest.LoadFromResources();
			_service ??= CreateDefaultService();

			if (Addressables.InternalIdTransformFunc != TransformInternalId)
			{
				_previousTransform = Addressables.InternalIdTransformFunc;
				Addressables.InternalIdTransformFunc = TransformInternalId;
			}

			if (Application.isPlaying)
			{
				_ = AssetDeliveryRunner.Instance;
			}

			Log($"Khởi tạo: packsEnabled={_manifest.packsEnabled}, {_manifest.packs.Count} pack, service={_service.GetType().Name}.");
		}

		private static IAssetPackService CreateDefaultService()
		{
#if UNITY_ANDROID && !UNITY_EDITOR
			return new AndroidAssetPackService();
#else
			return new NullAssetPackService();
#endif
		}

		#region Tra cứu
		
		public static string GetPackNameForKey(string key)
		{
			return Manifest.FindPackByKey(key)?.name;
		}

		public static string GetPackNameForBundle(string bundleFileName)
		{
			return Manifest.FindPackByBundle(bundleFileName)?.name;
		}

		public static bool IsPackAvailable(string packName)
		{
			if (string.IsNullOrEmpty(packName) || !PacksEnabled) return true;

			var pack = _manifest.FindPack(packName);
			if (pack == null || !pack.delivery.RequiresDownload()) return true;
			if (ConfirmedPacks.Contains(packName)) return true;

			if (!string.IsNullOrEmpty(_service.GetPackPath(packName)))
			{
				ConfirmedPacks.Add(packName);
				return true;
			}

			return false;
		}

		public static bool IsKeyAvailable(string key)
		{
			return IsPackAvailable(GetPackNameForKey(key));
		}

		public static float GetPackProgress(string packName)
		{
			if (IsPackAvailable(packName)) return 1f;
			return PackProgress.TryGetValue(packName, out var p) ? p : 0f;
		}
		
		public static List<string> GetMissingPacks()
		{
			var result = new List<string>();
			foreach (var pack in Manifest.packs)
			{
				if (pack != null && !IsPackAvailable(pack.name)) result.Add(pack.name);
			}

			return result;
		}

		#endregion

		#region Tải
		
		public static PackDownloadOperation DownloadForKey(string key, bool interactive = true)
		{
			var packName = GetPackNameForKey(key);
			return string.IsNullOrEmpty(packName)
				? PackDownloadOperation.FromCompleted(null)
				: DownloadPack(packName, interactive);
		}

		public static PackDownloadOperation DownloadPack(string packName, bool interactive = true)
		{
			EnsureInitialized();
			if (IsPackAvailable(packName))
			{
				return PackDownloadOperation.FromCompleted(packName);
			}

			if (ActiveDownloads.TryGetValue(packName, out var existing))
			{
				if (interactive && !existing.IsInteractive)
				{
					existing.IsInteractive = true;
					if (existing.Status == AssetPackStatus.WaitingForWifi) AskCellular(existing);
				}

				return existing;
			}

			var op = new PackDownloadOperation(packName, interactive);
			if (PacksNotInBuild.Contains(packName))
			{
				Finish(op, PackDownloadResult.NotInBuild, $"Pack '{packName}' không có trong bản build này.");
				return op;
			}

			ActiveDownloads[packName] = op;
			Log($"Bắt đầu tải '{packName}' ({(interactive ? "interactive" : "ngầm")}).");

			try
			{
				_service.Download(packName, progress => AssetDeliveryRunner.Post(() => OnDownloadUpdate(op, progress)));
			}
			catch (Exception e)
			{
				Finish(op, PackDownloadResult.Failed, $"Không gọi được dịch vụ tải cho '{packName}': {e.Message}");
			}

			return op;
		}

		public static void CancelDownload(string packName)
		{
			if (!ActiveDownloads.TryGetValue(packName, out var op)) return;
			_service.Cancel(packName);
			Finish(op, PackDownloadResult.Canceled, "Đã huỷ.");
		}

		private static void OnDownloadUpdate(PackDownloadOperation op, AssetPackProgress progress)
		{
			if (op.IsDone) return;

			op.Report(progress);
			PackProgress[op.PackName] = op.Progress;
			DownloadProgress?.Invoke(op.PackName, op.Progress);

			switch (progress.Status)
			{
				case AssetPackStatus.Completed:
					Finish(op, PackDownloadResult.Succeeded, null);
					break;

				case AssetPackStatus.Failed:
					if (progress.IsPackUnavailable)
					{
						PacksNotInBuild.Add(op.PackName);
						Finish(op, PackDownloadResult.NotInBuild, $"Pack '{op.PackName}' không có trong bản build này ({progress.Error}).");
					}
					else
					{
						Finish(op, PackDownloadResult.Failed, $"Tải '{op.PackName}' thất bại: {progress.Error ?? "không rõ lỗi"}.");
					}

					break;

				case AssetPackStatus.Canceled:
					Finish(op, PackDownloadResult.Canceled, "Đã huỷ.");
					break;

				case AssetPackStatus.WaitingForWifi:
					if (op.IsInteractive) AskCellular(op);
					break;
			}
		}

		private static void AskCellular(PackDownloadOperation op)
		{
			if (op.ConfirmationRequested || op.IsDone) return;
			op.ConfirmationRequested = true;
			Log($"'{op.PackName}' đang chờ WiFi, hỏi người chơi cho tải bằng dữ liệu di động.");

			void OnResult(bool allowed)
			{
				AssetDeliveryRunner.Post(() =>
				{
					if (allowed || op.IsDone) return;
					_service.Cancel(op.PackName);
					Finish(op, PackDownloadResult.Declined, "Người chơi từ chối tải bằng dữ liệu di động.");
				});
			}

			if (ConfirmationHandler != null) ConfirmationHandler.RequestCellularDownload(op.PackName, _service, OnResult);
			else _service.RequestMobileDataPermission(OnResult);
		}

		private static void Finish(PackDownloadOperation op, PackDownloadResult result, string error)
		{
			if (op.IsDone) return;
			ActiveDownloads.Remove(op.PackName);

			if (result == PackDownloadResult.Succeeded)
			{
				ClearResolvedCache();
				ConfirmedPacks.Add(op.PackName);
				PackProgress[op.PackName] = 1f;
				Log($"Tải '{op.PackName}' xong.");
				op.Complete(result, null);
				DownloadProgress?.Invoke(op.PackName, 1f);
				DownloadCompleted?.Invoke(op.PackName);
				return;
			}

			Debug.LogWarning($"{LogTag} {error}");
			op.Complete(result, error);
			if (op.IsInteractive) DownloadFailed?.Invoke(op.PackName, error);
		}

		#endregion

		#region Chuyển hướng bundle

		private static string TransformInternalId(IResourceLocation location)
		{
			string id = _previousTransform != null ? _previousTransform(location) : location.InternalId;

			try
			{
				if (string.IsNullOrEmpty(id) || _manifest == null || !_manifest.packsEnabled || _service == null || !_service.IsSupported)
				{
					return id;
				}

				int query = id.IndexOf('?');
				string clean = query >= 0 ? id.Substring(0, query) : id;
				if (!clean.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase)) return id;

				int slash = Math.Max(clean.LastIndexOf('/'), clean.LastIndexOf('\\'));
				string fileName = slash >= 0 ? clean.Substring(slash + 1) : clean;

				var pack = _manifest.FindPackByBundle(fileName);
				if (pack == null || !pack.delivery.RequiresDownload()) return id;

				lock (CacheLock)
				{
					if (ResolvedBundlePaths.TryGetValue(fileName, out var cached) && File.Exists(cached)) return cached;
				}

				string resolved = ResolveBundlePath(pack.name, fileName);
				if (!string.IsNullOrEmpty(resolved))
				{
					lock (CacheLock) ResolvedBundlePaths[fileName] = resolved;
					Log($"Chuyển '{fileName}' -> '{resolved}'.");
					return resolved;
				}

				bool firstWarning;
				lock (CacheLock) firstWarning = WarnedBundles.Add(fileName);
				if (firstWarning)
				{
					Debug.LogWarning($"{LogTag} Không tìm thấy '{fileName}' trong pack '{pack.name}' (pack chưa tải hoặc build lệch catalog). Hãy gọi DownloadPack trước khi load.");
				}
			}
			catch (Exception e)
			{
				Debug.LogWarning($"{LogTag} TransformInternalId lỗi: {e.Message}");
			}

			return id;
		}

		private static string ResolveBundlePath(string packName, string fileName)
		{
			string packPath = _service.GetPackPath(packName);
			if (string.IsNullOrEmpty(packPath) || !Directory.Exists(packPath)) return null;

			string sub = string.IsNullOrEmpty(_manifest.bundleSubdirectory) ? string.Empty : _manifest.bundleSubdirectory;
			string[] candidates =
			{
				Path.Combine(packPath, sub, fileName),
				Path.Combine(packPath, "assets", sub, fileName),
				Path.Combine(packPath, fileName),
			};

			foreach (var candidate in candidates)
			{
				if (File.Exists(candidate)) return candidate;
			}

			try
			{
				var matches = Directory.GetFiles(packPath, fileName, SearchOption.AllDirectories);
				if (matches.Length > 0) return matches[0];
			}
			catch (Exception e)
			{
				Debug.LogWarning($"{LogTag} Lỗi tìm '{fileName}' trong '{packPath}': {e.Message}");
			}

			return null;
		}

		private static void ClearResolvedCache()
		{
			lock (CacheLock) ResolvedBundlePaths.Clear();
		}

		#endregion

		internal static void Log(string message)
		{
			if (VerboseLogging) Debug.Log($"{LogTag} {message}");
		}
	}
}
