#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Android;

namespace com.ktgame.assetdelivery
{
	public sealed class AndroidAssetPackService : IAssetPackService
	{
		public bool IsSupported => true;

		public string GetPackPath(string packName)
		{
			try
			{
				return AndroidAssetPacks.GetAssetPackPath(packName);
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[AssetDelivery] GetAssetPackPath('{packName}') failed: {e.Message}");
				return null;
			}
		}

		public void Download(string packName, Action<AssetPackProgress> onUpdate)
		{
			AndroidAssetPacks.DownloadAssetPackAsync(new[] { packName }, info => onUpdate?.Invoke(Convert(packName, info)));
		}

		public void RequestMobileDataPermission(Action<bool> onResult)
		{
			AndroidAssetPacks.RequestToUseMobileDataAsync(result => onResult?.Invoke(result != null && result.allowed));
		}

		public void Cancel(string packName)
		{
			try
			{
				AndroidAssetPacks.CancelAssetPackDownload(new[] { packName });
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[AssetDelivery] CancelAssetPackDownload('{packName}') failed: {e.Message}");
			}
		}

		private static AssetPackProgress Convert(string requestedName, AndroidAssetPackInfo info)
		{
			if (info == null)
			{
				return new AssetPackProgress { PackName = requestedName, Status = AssetPackStatus.Unknown };
			}

			var status = Enum.TryParse(info.status.ToString(), out AssetPackStatus mapped) ? mapped : AssetPackStatus.Unknown;
			long total = (long)info.size;
			long done = (long)info.bytesDownloaded;

			float progress;
			switch (status)
			{
				case AssetPackStatus.Completed:
					progress = 1f;
					break;
				case AssetPackStatus.Transferring:
					progress = 0.9f + 0.1f * Mathf.Clamp01(info.transferProgress);
					break;
				default:
					progress = total > 0 ? 0.9f * Mathf.Clamp01((float)done / total) : 0f;
					break;
			}

			bool hasError = info.error != AndroidAssetPackError.NoError;
			return new AssetPackProgress
			{
				PackName = string.IsNullOrEmpty(info.name) ? requestedName : info.name,
				Status = status,
				Progress = progress,
				BytesDownloaded = done,
				TotalBytes = total,
				Error = hasError ? info.error.ToString() : null,
				IsPackUnavailable = info.error == AndroidAssetPackError.PackUnavailable,
			};
		}
	}
}
#endif
