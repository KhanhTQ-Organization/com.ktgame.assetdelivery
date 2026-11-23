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
			_initializedTransformOnce = false;
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

			EnsureTransformInstalled();

			if (Application.isPlaying)
			{
				_ = AssetDeliveryRunner.Instance;
			}

			Log($"Initialized: packsEnabled={_manifest.packsEnabled}, {_manifest.packs.Count} pack, service={_service.GetType().Name}.");
		}

		private static void EnsureTransformInstalled()
		{
			Func<IResourceLocation, string> current = Addressables.InternalIdTransformFunc;
			Func<IResourceLocation, string> ours = TransformInternalId;
			if (current == ours) return;

			if (current != null && _initializedTransformOnce)
			{
				Debug.LogWarning($"{LogTag} Addressables.InternalIdTransformFunc was overridden, reinstalling (the new function is still called first).");
			}

			_previousTransform = current;
			Addressables.InternalIdTransformFunc = ours;
			_initializedTransformOnce = true;
		}

		private static bool _initializedTransformOnce;

		private static IAssetPackService CreateDefaultService()
		{
#if UNITY_ANDROID && !UNITY_EDITOR
			return new AndroidAssetPackService();
#else
			return new NullAssetPackService();
#endif
		}

		#region Lookup

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

		public static bool IsPackReady(string packName)
		{
			if (string.IsNullOrEmpty(packName) || !PacksEnabled) return true;
			foreach (var pack in _manifest.ResolveWithDependencies(packName))
			{
				if (!IsPackAvailable(pack.name)) return false;
			}

			return true;
		}

		public static bool IsKeyAvailable(string key)
		{
			return IsPackReady(GetPackNameForKey(key));
		}

		public static List<string> GetPackNamesForLabel(string label)
		{
			var result = new List<string>();
			foreach (var pack in Manifest.FindPacksByLabel(label)) result.Add(pack.name);
			return result;
		}

		public static bool IsLabelAvailable(string label)
		{
			foreach (var pack in GetPackNamesForLabel(label))
			{
				if (!IsPackReady(pack)) return false;
			}

			return true;
		}

		public static List<string> GetRequiredPacks(string packName)
		{
			var result = new List<string>();
			foreach (var pack in Manifest.ResolveWithDependencies(packName)) result.Add(pack.name);
			return result;
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

		#region Download

		public static PackDownloadOperation DownloadForKey(string key, bool interactive = true)
		{
			var packName = GetPackNameForKey(key);
			return string.IsNullOrEmpty(packName)
				? PackDownloadOperation.FromCompleted(null)
				: DownloadPack(packName, interactive);
		}

		public static PackDownloadOperation DownloadForLabel(string label, bool interactive = true)
		{
			EnsureInitialized();
			var required = new List<string>();
			foreach (var pack in GetPackNamesForLabel(label))
			{
				foreach (var name in GetRequiredPacks(pack))
				{
					if (!required.Contains(name)) required.Add(name);
				}
			}

			return DownloadMany(label, required, interactive);
		}

		public static PackDownloadOperation DownloadPack(string packName, bool interactive = true)
		{
			EnsureInitialized();
			EnsureTransformInstalled();
			var required = GetRequiredPacks(packName);
			if (required.Count == 0) required.Add(packName);
			return DownloadMany(packName, required, interactive);
		}

		private static PackDownloadOperation DownloadMany(string label, List<string> packs, bool interactive)
		{
			var missing = new List<string>();
			foreach (var pack in packs)
			{
				if (!string.IsNullOrEmpty(pack) && !IsPackAvailable(pack)) missing.Add(pack);
			}

			if (missing.Count == 0) return PackDownloadOperation.FromCompleted(label);
			if (missing.Count == 1 && missing[0] == label) return DownloadSingle(label, interactive);

			var aggregate = new PackDownloadOperation(label, interactive);
			var children = new List<PackDownloadOperation>();
			foreach (var pack in missing) children.Add(DownloadSingle(pack, interactive));

			void Refresh(PackDownloadOperation _)
			{
				if (aggregate.IsDone) return;
				float sum = 0f;
				long done = 0, total = 0;
				foreach (var child in children)
				{
					sum += child.Progress;
					done += child.BytesDownloaded;
					total += child.TotalBytes;
				}

				aggregate.Report(new AssetPackProgress
				{
					PackName = label,
					Status = AssetPackStatus.Downloading,
					Progress = sum / children.Count,
					BytesDownloaded = done,
					TotalBytes = total,
				});

				foreach (var child in children)
				{
					if (!child.IsDone) return;
				}

				var failed = children.Find(c => !c.Succeeded);
				if (failed == null) aggregate.Complete(PackDownloadResult.Succeeded, null);
				else aggregate.Complete(failed.Result, failed.Error);
			}

			foreach (var child in children)
			{
				child.ProgressChanged += Refresh;
				child.Completed += Refresh;
			}

			return aggregate;
		}

		private static PackDownloadOperation DownloadSingle(string packName, bool interactive)
		{
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
				Finish(op, PackDownloadResult.NotInBuild, $"Pack '{packName}' is not part of this build.");
				return op;
			}

			ActiveDownloads[packName] = op;
			Log($"Downloading '{packName}' ({(interactive ? "interactive" : "background")}).");

			try
			{
				_service.Download(packName, progress => AssetDeliveryRunner.Post(() => OnDownloadUpdate(op, progress)));
			}
			catch (Exception e)
			{
				Finish(op, PackDownloadResult.Failed, $"Failed to start download of '{packName}': {e.Message}");
			}

			return op;
		}

		public static void CancelDownload(string packName)
		{
			if (!ActiveDownloads.TryGetValue(packName, out var op)) return;
			_service.Cancel(packName);
			Finish(op, PackDownloadResult.Canceled, "Canceled.");
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
						Finish(op, PackDownloadResult.NotInBuild, $"Pack '{op.PackName}' is not part of this build ({progress.Error}).");
					}
					else
					{
						Finish(op, PackDownloadResult.Failed, $"Download of '{op.PackName}' failed: {progress.Error ?? "unknown error"}.");
					}

					break;

				case AssetPackStatus.Canceled:
					Finish(op, PackDownloadResult.Canceled, "Canceled.");
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
			Log($"'{op.PackName}' is waiting for Wi-Fi, asking the player to allow mobile data.");

			void OnResult(bool allowed)
			{
				AssetDeliveryRunner.Post(() =>
				{
					if (allowed || op.IsDone) return;
					_service.Cancel(op.PackName);
					Finish(op, PackDownloadResult.Declined, "The player declined downloading over mobile data.");
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
				Log($"Downloaded '{op.PackName}'.");
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

		#region Bundle redirection

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
					Log($"Redirect '{fileName}' -> '{resolved}'.");
					return resolved;
				}

				bool firstWarning;
				lock (CacheLock) firstWarning = WarnedBundles.Add(fileName);
				if (firstWarning)
				{
					Debug.LogWarning($"{LogTag} '{fileName}' not found in pack '{pack.name}' (pack not downloaded yet, or build does not match the catalog). Call DownloadPack before loading.");
				}
			}
			catch (Exception e)
			{
				Debug.LogWarning($"{LogTag} TransformInternalId failed: {e.Message}");
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
				Debug.LogWarning($"{LogTag} Failed to search '{fileName}' in '{packPath}': {e.Message}");
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
