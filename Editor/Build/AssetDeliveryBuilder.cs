using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace com.ktgame.assetdelivery.editor
{
	public static class AssetDeliveryBuilder
	{
		private const string LogTag = "[AssetDelivery]";

		public static bool IsRunning { get; private set; }

		public static bool PrepareContent(AssetDeliveryBuildMode mode, out string message, bool? cleanOverride = null)
		{
			var settings = AssetDeliverySettings.Load();
			var aa = AddressablesContent.Settings;
			var lines = new List<string>();

			if (aa == null)
			{
				message = "AddressableAssetSettings not found.";
				return false;
			}

			bool enabled = settings != null && settings.enabled;
			ApplyAndroidSettings(mode, settings, lines);
			bool clean = cleanOverride ?? (settings != null && settings.cleanAddressablesBeforeBuild);

			if (enabled && mode == AssetDeliveryBuildMode.Aab)
			{
				EditorUtility.DisplayProgressBar("Asset Delivery", "Syncing groups...", 0.1f);
				try
				{
					var sync = GroupSynchronizer.Sync(settings, aa);
					lines.Add(sync.Summary());
					if (!sync.Success)
					{
						message = string.Join("\n", lines);
						return false;
					}

					EditorUtility.DisplayProgressBar("Asset Delivery", "Building Addressables...", 0.3f);
					if (!AddressablesContent.Build(clean, out var buildResult, out var buildError))
					{
						lines.Add(buildError);
						message = string.Join("\n", lines);
						return false;
					}

					if (settings == null) settings = AssetDeliverySettings.Load();
					aa = AddressablesContent.Settings;

					EditorUtility.DisplayProgressBar("Asset Delivery", "Exporting asset packs...", 0.8f);
					var export = AssetPackExporter.ExportForAab(settings, aa, buildResult);
					lines.Add(export.Summary());
					if (!export.Success)
					{
						message = string.Join("\n", lines);
						return false;
					}
				}
				finally
				{
					EditorUtility.ClearProgressBar();
				}
			}
			else
			{
				EditorUtility.DisplayProgressBar("Asset Delivery", "Building Addressables...", 0.3f);
				try
				{
					if (!AddressablesContent.Build(clean, out _, out var buildError))
					{
						lines.Add(buildError);
						message = string.Join("\n", lines);
						return false;
					}
				}
				finally
				{
					EditorUtility.ClearProgressBar();
				}

				if (enabled)
				{
					if (settings == null) settings = AssetDeliverySettings.Load();
					lines.Add(AssetPackExporter.ExportForApk(settings).Summary());
				}
			}

			if (enabled)
			{
				if (settings == null) settings = AssetDeliverySettings.Load();
				var issues = AssetDeliveryValidator.Validate(settings, mode);
				if (issues.Count > 0) lines.Add(AssetDeliveryValidator.Format(issues));
				if (AssetDeliveryValidator.HasErrors(issues))
				{
					message = string.Join("\n", lines);
					return false;
				}
			}

			message = string.Join("\n", lines.Where(l => !string.IsNullOrEmpty(l)));
			return true;
		}

		public static BuildReport Build(AssetDeliveryBuildMode mode, BuildPlayerOptions options)
		{
			if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
			{
				throw new InvalidOperationException("Active build target is not Android.");
			}

			if (options.scenes == null || options.scenes.Length == 0)
			{
				options.scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
			}

			if (options.scenes.Length == 0) throw new InvalidOperationException("No scenes are enabled in Build Settings.");

			options.target = BuildTarget.Android;
			options.targetGroup = BuildTargetGroup.Android;

			var context = new AssetDeliveryBuildContext
			{
				Settings = AssetDeliverySettings.Load(),
				Mode = mode,
				PlayerOptions = options,
				IsBatchMode = Application.isBatchMode,
			};

			var hooks = CreateHooks();
			IsRunning = true;
			try
			{
				foreach (var hook in hooks) hook.OnBeforeContent(context);

				if (!PrepareContent(mode, out var message))
				{
					throw new InvalidOperationException(message);
				}

				Debug.Log($"{LogTag} Content for {mode} is ready:\n{message}");

				foreach (var hook in hooks) hook.OnBeforePlayer(context);

				string output = context.PlayerOptions.locationPathName;
				string dir = string.IsNullOrEmpty(output) ? null : Path.GetDirectoryName(Path.GetFullPath(output));
				if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

				Debug.Log($"{LogTag} Build {mode} -> {output}");
				var report = BuildPipeline.BuildPlayer(context.PlayerOptions);

				foreach (var hook in hooks) hook.OnAfterPlayer(context, report);

				if (report.summary.result != BuildResult.Succeeded)
				{
					throw new InvalidOperationException($"Player build finished with {report.summary.result}, {report.summary.totalErrors} error(s).");
				}

				Debug.Log($"{LogTag} Build {mode} xong: {output} ({AssetPackExporter.FormatSize((long)report.summary.totalSize)}).");
				return report;
			}
			finally
			{
				IsRunning = false;
			}
		}

		public static void BuildInteractive(AssetDeliveryBuildMode mode)
		{
			if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
			{
				EditorUtility.DisplayDialog("Asset Delivery", "Active build target is not Android. Switch to Android in Build Settings.", "OK");
				return;
			}

			ApplyAndroidSettings(mode, AssetDeliverySettings.Load(), new List<string>());

			BuildPlayerOptions options;
			try
			{
				options = BuildPlayerWindow.DefaultBuildMethods.GetBuildPlayerOptions(new BuildPlayerOptions());
			}
			catch (BuildPlayerWindow.BuildMethodException)
			{
				return;
			}

			try
			{
				var report = Build(mode, options);
				string path = options.locationPathName;
				string note = mode == AssetDeliveryBuildMode.Apk ? "\n\nThis APK contains all content and uses no asset packs. Do not upload it to Google Play." : string.Empty;
				string size = File.Exists(path) ? AssetPackExporter.FormatSize(new FileInfo(path).Length) : AssetPackExporter.FormatSize((long)report.summary.totalSize);
				if (EditorUtility.DisplayDialog($"Build {mode} succeeded", $"{path}\n{size}{note}", "Show in Explorer", "OK"))
				{
					EditorUtility.RevealInFinder(path);
				}
			}
			catch (Exception e)
			{
				Debug.LogError($"{LogTag} Build {mode} failed: {e.Message}");
				EditorUtility.DisplayDialog($"Build {mode} failed", e.Message + "\n\nSee the Console for details.", "OK");
			}
		}

		public static void ApplyAndroidSettings(AssetDeliveryBuildMode mode, AssetDeliverySettings settings, List<string> log)
		{
			bool aab = mode == AssetDeliveryBuildMode.Aab;
			var changed = new List<string>();

			if (EditorUserBuildSettings.buildAppBundle != aab)
			{
				EditorUserBuildSettings.buildAppBundle = aab;
				changed.Add("Build App Bundle = " + (aab ? "on" : "off"));
			}

			var symbols = settings == null ? (aab ? AndroidCreateSymbols.Debugging : AndroidCreateSymbols.Disabled) : aab ? settings.aabSymbols : settings.apkSymbols;
			if (EditorUserBuildSettings.androidCreateSymbols != symbols)
			{
				EditorUserBuildSettings.androidCreateSymbols = symbols;
				changed.Add("Create symbols = " + symbols);
			}

			bool split = aab && (settings == null || settings.splitApplicationBinaryForAab);
#if UNITY_2023_1_OR_NEWER
			if (PlayerSettings.Android.splitApplicationBinary != split)
			{
				PlayerSettings.Android.splitApplicationBinary = split;
#else
			if (PlayerSettings.Android.useAPKExpansionFiles != split)
			{
				PlayerSettings.Android.useAPKExpansionFiles = split;
#endif
				changed.Add("Split Application Binary = " + (split ? "on" : "off"));
			}

			if (settings == null || settings.disableGoogleSplitBaseModuleAssets)
			{
				if (GooglePlayPluginBridge.TrySetSplitBaseModuleAssets(false, out _))
				{
					changed.Add("Google plugin: Separate Base APK Asset = off");
				}
			}

			if (AddressablesContent.EnsureNotBuiltWithPlayer(AddressablesContent.Settings))
			{
				changed.Add("Addressables: do not rebuild content on player build");
			}

			if (changed.Count > 0)
			{
				AssetDatabase.SaveAssets();
				log.Add($"{mode} settings: " + string.Join("; ", changed));
			}
		}

		private static List<AssetDeliveryBuildHook> CreateHooks()
		{
			var hooks = new List<AssetDeliveryBuildHook>();
			foreach (var type in TypeCache.GetTypesDerivedFrom<AssetDeliveryBuildHook>())
			{
				if (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null) continue;
				hooks.Add((AssetDeliveryBuildHook)Activator.CreateInstance(type));
			}

			return hooks.OrderBy(h => h.Order).ToList();
		}
	}
}
