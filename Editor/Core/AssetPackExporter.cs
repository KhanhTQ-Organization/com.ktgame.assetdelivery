using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace com.ktgame.assetdelivery.editor
{
	public sealed class ExportResult
	{
		public bool Success = true;
		public AssetDeliveryManifest Manifest;
		public readonly List<string> Errors = new List<string>();
		public readonly List<string> Lines = new List<string>();

		public string Summary() => string.Join("\n", Lines.Concat(Errors.Select(e => "[x] " + e)));
	}

	public static class AssetPackExporter
	{
		private const string AndroidPackSuffix = ".androidpack";

		public static ExportResult ExportForAab(AssetDeliverySettings settings, AddressableAssetSettings aa, AddressablesPlayerBuildResult buildResult)
		{
			var result = new ExportResult();
			if (settings == null || aa == null)
			{
				result.Success = false;
				result.Errors.Add("Missing AssetDeliverySettings or AddressableAssetSettings (it may have been unloaded during the build).");
				return result;
			}

			var plans = GroupSynchronizer.CollectPackPlans(settings, aa);
			var bundlesByGroup = MapBundlesByGroup(buildResult, aa);
			var dependencies = PackContentAnalyzer.ComputePackDependencies(aa, plans);

			var manifest = new AssetDeliveryManifest
			{
				packsEnabled = true,
				bundleSubdirectory = settings.bundleSubdirectory,
				layoutSignature = PackPlan.ComputeSignature(plans),
			};

			RemoveStalePackFolders(settings, new HashSet<string>(plans.Select(p => p.PackName)), result.Lines);

			foreach (var plan in plans)
			{
				var bundleFiles = new List<string>();
				foreach (var groupName in plan.GroupNames)
				{
					var group = aa.FindGroup(groupName);
					if (group != null && bundlesByGroup.TryGetValue(group, out var files)) bundleFiles.AddRange(files);
					else result.Errors.Add($"No freshly built bundles found for group '{groupName}' (pack '{plan.PackName}').");
				}

				if (bundleFiles.Count == 0) continue;

				string packFolder = GetPackFolder(settings, plan.PackName);
				string bundleDir = GetBundleDir(settings, plan.PackName);
				Directory.CreateDirectory(bundleDir);
				WriteGradle(packFolder, plan.PackName, plan.Delivery);

				foreach (var old in Directory.GetFiles(bundleDir, "*.bundle"))
				{
					DeleteFileWithMeta(old);
				}

				var gathered = plan.GroupNames.SelectMany(g => PackContentAnalyzer.GatherEntries(aa.FindGroup(g))).ToList();
				var entry = new AssetPackEntry
				{
					name = plan.PackName,
					delivery = plan.Delivery,
					sourceGroup = plan.GroupName,
					keys = gathered.Select(e => e.address).Where(a => !string.IsNullOrEmpty(a)).Distinct().OrderBy(a => a, NaturalStringComparer.Instance).ToList(),
					labels = gathered.SelectMany(e => e.labels).Distinct().OrderBy(l => l, StringComparer.Ordinal).ToList(),
					dependsOn = dependencies.TryGetValue(plan.PackName, out var deps) ? deps : new List<string>(),
				};

				foreach (var source in bundleFiles)
				{
					string fileName = Path.GetFileName(source);
					string dest = Path.Combine(bundleDir, fileName);
					File.Copy(source, dest, true);
					File.Delete(source);

					long size = new FileInfo(dest).Length;
					entry.bundles.Add(fileName);
					entry.sizeBytes += size;
				}

				manifest.packs.Add(entry);
				string depText = entry.dependsOn.Count > 0 ? $", depends on {string.Join(", ", entry.dependsOn)}" : string.Empty;
				result.Lines.Add($"- {plan.PackName} ({plan.Delivery.ToGradleValue()}): {plan.Keys.Count} entry, {entry.bundles.Count} bundle, {FormatSize(entry.sizeBytes)}{depText}");
			}

			if (plans.Count == 0)
			{
				result.Lines.Add("No packs: all content stays in the base.");
			}

			WriteManifest(settings, manifest);
			result.Manifest = manifest;
			result.Success = result.Errors.Count == 0;
			AssetDatabase.Refresh();
			return result;
		}

		public static ExportResult ExportForApk(AssetDeliverySettings settings)
		{
			var result = new ExportResult();
			RemoveStalePackFolders(settings, new HashSet<string>(), result.Lines);
			var manifest = new AssetDeliveryManifest { packsEnabled = false, bundleSubdirectory = settings.bundleSubdirectory };
			WriteManifest(settings, manifest);
			result.Manifest = manifest;
			result.Lines.Add("APK: no asset packs, all content is inside the .apk.");
			AssetDatabase.Refresh();
			return result;
		}

		public static string GetPackFolder(AssetDeliverySettings settings, string packName)
		{
			return Path.Combine(settings.assetPacksRoot, packName + AndroidPackSuffix).Replace('\\', '/');
		}

		public static string GetBundleDir(AssetDeliverySettings settings, string packName)
		{
			return Path.Combine(GetPackFolder(settings, packName), "src", "main", "assets", settings.bundleSubdirectory).Replace('\\', '/');
		}

		public static List<string> FindManagedPackFolders(AssetDeliverySettings settings)
		{
			var result = new List<string>();
			var previous = ReadManifest(settings);
			if (!Directory.Exists(settings.assetPacksRoot)) return result;

			foreach (var dir in Directory.GetDirectories(settings.assetPacksRoot))
			{
				string folderName = Path.GetFileName(dir);
				if (!folderName.EndsWith(AndroidPackSuffix, StringComparison.OrdinalIgnoreCase)) continue;
				string packName = AssetPackRule.StripAndroidPackSuffix(folderName);
				bool claimed = settings.rules.Any(r => r != null && r.ClaimsPackName(packName));
				bool builtBefore = previous != null && previous.FindPack(packName) != null;
				if (claimed || builtBefore)
				{
					result.Add(dir.Replace('\\', '/'));
				}
			}

			return result;
		}

		public static AssetDeliveryManifest ReadManifest(AssetDeliverySettings settings)
		{
			if (settings == null || !File.Exists(settings.manifestPath)) return null;
			try
			{
				return AssetDeliveryManifest.FromJson(File.ReadAllText(settings.manifestPath));
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[AssetDelivery] Failed to read {settings.manifestPath}: {e.Message}");
				return null;
			}
		}

		public static string FormatSize(long bytes) => $"{bytes / 1024f / 1024f:F1} MB";

		public static bool IsSharedBuiltInBundle(string fileName)
		{
			string name = (fileName ?? string.Empty).ToLowerInvariant();
			return name.Contains("unitybuiltinshaders") || name.Contains("unitybuiltinassets") || name.Contains("_monoscripts");
		}

		private static Dictionary<AddressableAssetGroup, List<string>> MapBundlesByGroup(AddressablesPlayerBuildResult buildResult, AddressableAssetSettings aa)
		{
			var map = new Dictionary<AddressableAssetGroup, List<string>>();

			if (buildResult?.AssetBundleBuildResults != null)
			{
				foreach (var bundle in buildResult.AssetBundleBuildResults)
				{
					if (bundle?.SourceAssetGroup == null || string.IsNullOrEmpty(bundle.FilePath) || !File.Exists(bundle.FilePath)) continue;
					if (IsSharedBuiltInBundle(Path.GetFileName(bundle.FilePath))) continue;
					if (!map.TryGetValue(bundle.SourceAssetGroup, out var list)) map[bundle.SourceAssetGroup] = list = new List<string>();
					list.Add(bundle.FilePath);
				}
			}

			if (map.Count > 0) return map;

			Debug.LogWarning("[AssetDelivery] Build result has no AssetBundleBuildResults, matching bundles by group name.");
			string dir = AddressablesContent.LocalBundlePath;
			if (!Directory.Exists(dir)) return map;
			var files = Directory.GetFiles(dir, "*.bundle");
			foreach (var group in aa.groups)
			{
				if (group == null) continue;
				string prefix = group.Name.ToLowerInvariant().Replace(" ", string.Empty) + "_";
				var matches = files.Where(f => Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !IsSharedBuiltInBundle(Path.GetFileName(f))).ToList();
				if (matches.Count > 0) map[group] = matches;
			}

			return map;
		}

		private static void RemoveStalePackFolders(AssetDeliverySettings settings, HashSet<string> activePacks, List<string> log)
		{
			foreach (var dir in FindManagedPackFolders(settings))
			{
				string packName = Path.GetFileName(dir);
				packName = packName.Substring(0, packName.Length - AndroidPackSuffix.Length);
				if (activePacks.Contains(packName)) continue;

				if (!AssetDatabase.DeleteAsset(dir))
				{
					Directory.Delete(dir, true);
					DeleteFileWithMeta(dir);
				}

				log.Add($"Removed unused pack: {dir}");
			}
		}

		private static void WriteGradle(string packFolder, string packName, DeliveryMode delivery)
		{
			string content =
				"apply plugin: 'com.android.asset-pack'\n\n" +
				"assetPack {\n" +
				$"    packName = \"{packName}\"\n" +
				"    dynamicDelivery {\n" +
				$"        deliveryType = \"{delivery.ToGradleValue()}\"\n" +
				"    }\n" +
				"}\n";
			WriteIfChanged(Path.Combine(packFolder, "build.gradle"), content);
		}

		private static void WriteManifest(AssetDeliverySettings settings, AssetDeliveryManifest manifest)
		{
			if (WriteIfChanged(settings.manifestPath, manifest.ToJson()))
			{
				AssetDatabase.ImportAsset(settings.manifestPath, ImportAssetOptions.ForceUpdate);
			}
		}

		private static bool WriteIfChanged(string path, string content)
		{
			if (File.Exists(path) && File.ReadAllText(path) == content) return false;
			string dir = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
			File.WriteAllText(path, content);
			return true;
		}

		private static void DeleteFileWithMeta(string path)
		{
			if (File.Exists(path)) File.Delete(path);
			if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
		}
	}
}
