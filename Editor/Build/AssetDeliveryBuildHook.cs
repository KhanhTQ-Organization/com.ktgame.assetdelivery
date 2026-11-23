using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace com.ktgame.assetdelivery.editor
{
	public sealed class AssetDeliveryBuildContext
	{
		public AssetDeliverySettings Settings;
		public AssetDeliveryBuildMode Mode;
		public BuildPlayerOptions PlayerOptions;

		public bool IsBatchMode;

		public readonly Dictionary<string, object> UserData = new Dictionary<string, object>();
	}

	public abstract class AssetDeliveryBuildHook
	{
		public virtual int Order => 0;

		public virtual void OnBeforeContent(AssetDeliveryBuildContext context)
		{
		}

		public virtual void OnBeforePlayer(AssetDeliveryBuildContext context)
		{
		}

		public virtual void OnAfterPlayer(AssetDeliveryBuildContext context, BuildReport report)
		{
		}
	}
}
