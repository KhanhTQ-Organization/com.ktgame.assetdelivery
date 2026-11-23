namespace com.ktgame.assetdelivery
{
	public enum DeliveryMode
	{
		InstallTime = 0,

		FastFollow = 1,

		OnDemand = 2,
	}

	public static class DeliveryModeExtensions
	{
		public static string ToGradleValue(this DeliveryMode mode)
		{
			switch (mode)
			{
				case DeliveryMode.InstallTime: return "install-time";
				case DeliveryMode.FastFollow: return "fast-follow";
				default: return "on-demand";
			}
		}

		public static bool RequiresDownload(this DeliveryMode mode)
		{
			return mode != DeliveryMode.InstallTime;
		}
	}
}
