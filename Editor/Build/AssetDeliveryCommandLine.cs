using System;
using UnityEditor;
using UnityEngine;

namespace com.ktgame.assetdelivery.editor
{
	public static class AssetDeliveryCommandLine
	{
		public static void BuildAab() => Run(AssetDeliveryBuildMode.Aab, "Build/Android/build.aab");

		public static void BuildApk() => Run(AssetDeliveryBuildMode.Apk, "Build/Android/build.apk");

		public static void PrepareAabContent() => RunPrepare(AssetDeliveryBuildMode.Aab);

		public static void PrepareApkContent() => RunPrepare(AssetDeliveryBuildMode.Apk);

		public static string GetArg(string name)
		{
			var args = Environment.GetCommandLineArgs();
			for (int i = 0; i < args.Length - 1; i++)
			{
				if (string.Equals(args[i], name, StringComparison.Ordinal)) return args[i + 1];
			}

			return null;
		}

		private static void Run(AssetDeliveryBuildMode mode, string defaultOutput)
		{
			Execute(mode.ToString(), () =>
			{
				var options = new BuildPlayerOptions
				{
					locationPathName = GetArg("-outputPath") ?? defaultOutput,
					target = BuildTarget.Android,
					targetGroup = BuildTargetGroup.Android,
					options = BuildOptions.None,
				};
				AssetDeliveryBuilder.Build(mode, options);
			});
		}

		private static void RunPrepare(AssetDeliveryBuildMode mode)
		{
			Execute("content " + mode, () =>
			{
				if (!AssetDeliveryBuilder.PrepareContent(mode, out var message)) throw new InvalidOperationException(message);
				Debug.Log("[AssetDelivery] " + message);
			});
		}

		private static void Execute(string label, Action action)
		{
			try
			{
				action();
				Debug.Log($"[AssetDelivery CLI] {label} xong.");
			}
			catch (Exception e)
			{
				Debug.LogError($"[AssetDelivery CLI] {label} failed: {e.Message}");
				if (Application.isBatchMode) EditorApplication.Exit(1);
				else throw;
				return;
			}

			if (Application.isBatchMode) EditorApplication.Exit(0);
		}
	}
}
