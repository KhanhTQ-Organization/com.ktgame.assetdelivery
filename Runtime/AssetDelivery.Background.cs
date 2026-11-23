using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace com.ktgame.assetdelivery
{
	[Serializable]
	public sealed class BackgroundDownloadOptions
	{
		public float retryStepSeconds = 15f;

		public float retryMaxSeconds = 300f;

		public float offlinePollSeconds = 2f;
	}

	public static partial class AssetDelivery
	{
		private static Coroutine _backgroundRoutine;

		public static bool IsBackgroundDownloading { get; private set; }

		public static string CurrentBackgroundPack { get; private set; }

		public static void StartBackgroundDownloads(IPackPriorityProvider priority = null, BackgroundDownloadOptions options = null)
		{
			EnsureInitialized();
			if (_backgroundRoutine != null) return;

			if (!PacksEnabled)
			{
				Log("Build has no packs to download, skipping background downloads.");
				return;
			}

			_backgroundRoutine = AssetDeliveryRunner.Run(BackgroundRoutine(priority ?? new ManifestOrderPriority(), options ?? new BackgroundDownloadOptions()));
		}

		public static void StopBackgroundDownloads()
		{
			AssetDeliveryRunner.Stop(_backgroundRoutine);
			ResetBackgroundState();
		}

		private static void ResetBackgroundState()
		{
			_backgroundRoutine = null;
			IsBackgroundDownloading = false;
			CurrentBackgroundPack = null;
		}

		private static bool IsOffline()
		{
			return Application.internetReachability == NetworkReachability.NotReachable;
		}

		private static string PickNextPack(IPackPriorityProvider priority)
		{
			IEnumerable<string> order;
			try
			{
				order = priority.GetPackOrder();
			}
			catch (Exception e)
			{
				Debug.LogWarning($"{LogTag} IPackPriorityProvider failed: {e.Message}. Falling back to manifest order.");
				order = new ManifestOrderPriority().GetPackOrder();
			}

			foreach (var pack in order)
			{
				if (string.IsNullOrEmpty(pack) || PacksNotInBuild.Contains(pack)) continue;
				if (!IsPackAvailable(pack)) return pack;
			}

			return null;
		}

		private static IEnumerator BackgroundRoutine(IPackPriorityProvider priority, BackgroundDownloadOptions options)
		{
			IsBackgroundDownloading = true;
			var attempts = new Dictionary<string, int>();
			var oneSecond = new WaitForSecondsRealtime(1f);
			var offlineWait = new WaitForSecondsRealtime(Mathf.Max(0.1f, options.offlinePollSeconds));

			while (true)
			{
				string pack = PickNextPack(priority);
				if (pack == null) break;

				if (IsOffline())
				{
					CurrentBackgroundPack = null;
					Log($"Offline, pausing background download of '{pack}'.");
					while (IsOffline()) yield return offlineWait;
					continue;
				}

				CurrentBackgroundPack = pack;
				var op = DownloadPack(pack, false);
				yield return op;

				if (op.Succeeded || op.Result == PackDownloadResult.NotInBuild) continue;

				attempts.TryGetValue(pack, out int attempt);
				attempts[pack] = ++attempt;
				float wait = Mathf.Min(options.retryMaxSeconds, options.retryStepSeconds * attempt);
				Log($"Background download of '{pack}' failed ({op.Result}), retrying in {wait:0}s (attempt {attempt}).");

				bool wasOffline = IsOffline();
				for (float waited = 0f; waited < wait; waited += 1f)
				{
					yield return oneSecond;
					bool offlineNow = IsOffline();
					if (wasOffline && !offlineNow) break;
					wasOffline = offlineNow;
				}
			}

			Log("Background downloads finished for all packs.");
			ResetBackgroundState();
		}
	}

	public sealed class ManifestOrderPriority : IPackPriorityProvider
	{
		public IEnumerable<string> GetPackOrder()
		{
			var packs = AssetDelivery.Manifest.packs;
			foreach (var pack in packs)
			{
				if (pack != null && pack.delivery == DeliveryMode.FastFollow) yield return pack.name;
			}

			foreach (var pack in packs)
			{
				if (pack != null && pack.delivery == DeliveryMode.OnDemand) yield return pack.name;
			}
		}
	}

	public sealed class KeyOrderPriority : IPackPriorityProvider
	{
		private readonly Func<IEnumerable<string>> _keys;

		public KeyOrderPriority(Func<IEnumerable<string>> keys)
		{
			_keys = keys ?? throw new ArgumentNullException(nameof(keys));
		}

		public IEnumerable<string> GetPackOrder()
		{
			var seen = new HashSet<string>();
			foreach (var key in _keys())
			{
				var pack = AssetDelivery.GetPackNameForKey(key);
				if (!string.IsNullOrEmpty(pack) && seen.Add(pack)) yield return pack;
			}

			foreach (var pack in new ManifestOrderPriority().GetPackOrder())
			{
				if (seen.Add(pack)) yield return pack;
			}
		}
	}
}
