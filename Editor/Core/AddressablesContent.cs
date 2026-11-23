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

		/// <
		public static string LocalBundlePath => Path.Combine(Addressables.BuildPath, EditorUserBuildSettings.activeBuildTarget.ToString());

		public static bool Build(bool clean, out AddressablesPlayerBuildResult result, out string error)
		{
			result = null;
			error = null;

			var settings = Settings;
			if (settings == null)
			{
				error = "Không tìm thấy AddressableAssetSettings.";
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
				error = $"Build Addressables lỗi: {result?.Error ?? "không có kết quả build"}";
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

			Debug.LogWarning("[AssetDelivery] Không tìm thấy BuildScriptPackedMode, dùng data builder hiện tại.");
		}

		/// <summary>Content đã build xong thì player build không được build lại Addressables (sẽ đưa bundle của pack trở lại base).</summary>
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

	/// <summary>Chỉnh plugin Google Android App Bundle (nếu có) bằng reflection. Không có plugin thì bỏ qua.</summary>
	internal static class GooglePlayPluginBridge
	{
		private const string SerializerTypeName =
			"Google.Android.AppBundle.Editor.Internal.AssetPacks.AssetDeliveryConfigSerializer, Google.Android.AppBundle.Editor";

		/// <returns>true nếu đã đổi giá trị.</returns>
		public static bool TrySetSplitBaseModuleAssets(bool value, out string note)
		{
			note = null;
			var serializerType = Type.GetType(SerializerTypeName);
			if (serializerType == null)
			{
				note = "project không có plugin Google Android App Bundle";
				return false;
			}

			try
			{
				const System.Reflection.BindingFlags flags =
					System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
					System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;

				var load = serializerType.GetMethod("LoadConfig", flags, null, Type.EmptyTypes, null);
				if (load == null) throw new InvalidOperationException("không có LoadConfig");
				object serializer = load.IsStatic ? null : Activator.CreateInstance(serializerType);
				object config = load.Invoke(serializer, null) ?? throw new InvalidOperationException("LoadConfig trả về null");

				var field = config.GetType().GetField("SplitBaseModuleAssets", flags) ?? throw new InvalidOperationException("không có SplitBaseModuleAssets");
				if ((bool)field.GetValue(config) == value) return false;

				field.SetValue(config, value);
				var save = serializerType.GetMethod("SaveConfig", flags, null, new[] { config.GetType() }, null) ?? throw new InvalidOperationException("không có SaveConfig");
				save.Invoke(save.IsStatic ? null : serializer, new[] { config });
				return true;
			}
			catch (Exception e)
			{
				note = $"không chỉnh được plugin Google: {e.Message}";
				return false;
			}
		}
	}
}
