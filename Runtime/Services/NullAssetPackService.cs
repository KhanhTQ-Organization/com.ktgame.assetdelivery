using System;

namespace com.ktgame.assetdelivery
{
	public sealed class NullAssetPackService : IAssetPackService
	{
		public bool IsSupported => false;

		public string GetPackPath(string packName) => null;

		public void Download(string packName, Action<AssetPackProgress> onUpdate)
		{
			onUpdate?.Invoke(new AssetPackProgress
			{
				PackName = packName,
				Status = AssetPackStatus.Completed,
				Progress = 1f,
			});
		}

		public void RequestMobileDataPermission(Action<bool> onResult) => onResult?.Invoke(true);

		public void Cancel(string packName)
		{
		}
	}
}
