using System;

namespace com.ktgame.assetdelivery
{
	public enum AssetPackStatus
	{
		Unknown,
		Pending,
		Downloading,
		Transferring,
		Completed,
		Failed,
		Canceled,
		WaitingForWifi,
		NotInstalled,
	}

	public struct AssetPackProgress
	{
		public string PackName;
		public AssetPackStatus Status;

		public float Progress;

		public long BytesDownloaded;
		public long TotalBytes;

		public string Error;

		public bool IsPackUnavailable;
	}

	public interface IAssetPackService
	{
		bool IsSupported { get; }

		string GetPackPath(string packName);

		void Download(string packName, Action<AssetPackProgress> onUpdate);

		void RequestMobileDataPermission(Action<bool> onResult);

		void Cancel(string packName);
	}

	public interface IDownloadConfirmationHandler
	{
		void RequestCellularDownload(string packName, IAssetPackService service, Action<bool> onResult);
	}

	public interface IPackPriorityProvider
	{
		System.Collections.Generic.IEnumerable<string> GetPackOrder();
	}
}
