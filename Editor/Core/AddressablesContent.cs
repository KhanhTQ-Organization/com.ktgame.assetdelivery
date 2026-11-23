using System;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace com.ktgame.assetdelivery.editor
{
	public static class AddressablesContent
	{
		public static AddressableAssetSettings Settings => AddressableAssetSettingsDefaultObject.Settings;

		public static string RootBuildPath => Addressables.BuildPath;

		public static string LocalBundlePath => Path.Combine(Addressables.BuildPath, EditorUserBuildSettings.activeBuildTarget.ToString());

		public static bool Build(bool clean, out AddressablesPlayerBuildResult result, out string error)
		{
			result = null;
			error = null;

			var settings = Settings;
			if (settings == null)
			{
				error = "AddressableAssetSettings not found.";
				return false;
			}

			SelectPackedModeBuilder(settings);

			if (clean)
			{
				AddressableAssetSettings.CleanPlayerContent();
			}

			AssetDatabase.SaveAssets();
			AddressableAssetSettings.BuildPlayerContent(out result);
			if (result == null || !string.IsNullOrEmpty(result.Error))
			{
				error = $"Addressables build failed: {result?.Error ?? "no build result"}";
				return false;
			}

			return true;
		}

		private static void SelectPackedModeBuilder(AddressableAssetSettings settings)
		{
			for (int i = 0; i < settings.DataBuilders.Count; i++)
			{
				var builder = settings.DataBuilders[i];
				if (builder != null && builder.name == "BuildScriptPackedMode")
				{
					if (settings.ActivePlayerDataBuilderIndex != i)
					{
						settings.ActivePlayerDataBuilderIndex = i;
						EditorUtility.SetDirty(settings);
					}

					return;
				}
			}

			Debug.LogWarning("[AssetDelivery] BuildScriptPackedMode not found, using the active data builder.");
		}

		public static bool EnsureNotBuiltWithPlayer(AddressableAssetSettings settings)
		{
			if (settings == null) return false;
			const AddressableAssetSettings.PlayerBuildOption wanted = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
			if (settings.BuildAddressablesWithPlayerBuild == wanted) return false;
			settings.BuildAddressablesWithPlayerBuild = wanted;
			EditorUtility.SetDirty(settings);
			return true;
		}

		public static bool HasLocalBuild() => Directory.Exists(LocalBundlePath);
	}

	internal static class GooglePlayPluginBridge
	{
		private const string SerializerTypeName =
			"Google.Android.AppBundle.Editor.Internal.AssetPacks.AssetDeliveryConfigSerializer, Google.Android.AppBundle.Editor";

		public static bool TrySetSplitBaseModuleAssets(bool value, out string note)
		{
			note = null;
			var serializerType = Type.GetType(SerializerTypeName);
			if (serializerType == null)
			{
				note = "Google Android App Bundle plugin not found";
				return false;
			}

			try
			{
				const System.Reflection.BindingFlags flags =
					System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
					System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;

				var load = serializerType.GetMethod("LoadConfig", flags, null, Type.EmptyTypes, null);
				if (load == null) throw new InvalidOperationException("LoadConfig not found");
				object serializer = load.IsStatic ? null : Activator.CreateInstance(serializerType);
				object config = load.Invoke(serializer, null) ?? throw new InvalidOperationException("LoadConfig returned null");

				var field = config.GetType().GetField("SplitBaseModuleAssets", flags) ?? throw new InvalidOperationException("SplitBaseModuleAssets not found");
				if ((bool)field.GetValue(config) == value) return false;

				field.SetValue(config, value);
				var save = serializerType.GetMethod("SaveConfig", flags, null, new[] { config.GetType() }, null) ?? throw new InvalidOperationException("SaveConfig not found");
				save.Invoke(save.IsStatic ? null : serializer, new[] { config });
				return true;
			}
			catch (Exception e)
			{
				note = $"failed to configure the Google plugin: {e.Message}";
				return false;
			}
		}
	}
}
