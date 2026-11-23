using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace com.ktgame.assetdelivery.editor
{
	public enum AssetDeliveryBuildMode
	{
		Aab,

		Apk,
	}

	public enum IssueSeverity
	{
		Info,
		Warning,
		Error,
	}

	public sealed class ValidationIssue
	{
		public IssueSeverity Severity;
		public string Message;

		public override string ToString() => $"[{Severity}] {Message}";
	}

	public sealed class ValidationContext
	{
		public AssetDeliverySettings Settings;
		public AddressableAssetSettings Addressables;
		public AssetDeliveryBuildMode Mode;

		public AssetDeliveryManifest Manifest;

		public readonly List<ValidationIssue> Issues = new List<ValidationIssue>();

		public void Error(string message) => Issues.Add(new ValidationIssue { Severity = IssueSeverity.Error, Message = message });
		public void Warning(string message) => Issues.Add(new ValidationIssue { Severity = IssueSeverity.Warning, Message = message });
		public void Info(string message) => Issues.Add(new ValidationIssue { Severity = IssueSeverity.Info, Message = message });
	}

	public interface IAssetDeliveryCheck
	{
		void Run(ValidationContext context);
	}

	public static class AssetDeliveryValidator
	{
		private const string FixHint = "Run Build AAB (or 'Build Content Only') in the Asset Delivery window.";

		public static List<ValidationIssue> Validate(AssetDeliverySettings settings, AssetDeliveryBuildMode mode)
		{
			var context = new ValidationContext
			{
				Settings = settings,
				Addressables = AddressablesContent.Settings,
				Mode = mode,
				Manifest = AssetPackExporter.ReadManifest(settings),
			};

			if (settings == null)
			{
				context.Error("No AssetDeliverySettings found.");
				return context.Issues;
			}

			if (context.Addressables == null)
			{
				context.Error("AddressableAssetSettings not found.");
				return context.Issues;
			}

			foreach (var error in settings.ValidateSettings()) context.Error(error);
			CheckGroups(context);
			CheckDependencies(context);

			if (mode == AssetDeliveryBuildMode.Aab) CheckAabOutput(context);
			else CheckApk(context);

			RunCustomChecks(context);
			return context.Issues;
		}

		public static bool HasErrors(IEnumerable<ValidationIssue> issues) => issues.Any(i => i.Severity == IssueSeverity.Error);

		public static string Format(IEnumerable<ValidationIssue> issues)
		{
			return string.Join("\n", issues.Select(i => (i.Severity == IssueSeverity.Error ? "[x] " : i.Severity == IssueSeverity.Warning ? "[!] " : "- ") + i.Message));
		}

		private static void CheckGroups(ValidationContext context)
		{
			var aa = context.Addressables;
			foreach (var rule in context.Settings.ActiveRules)
			{
				var misplaced = rule.FindMisplaced(aa);
				if (misplaced.Count > 0)
				{
					string sample = string.Join(", ", misplaced.Take(8)) + (misplaced.Count > 8 ? ", ..." : string.Empty);
					string message = $"Rule '{rule.name}': {misplaced.Count} entries are in the wrong group: {sample}. {FixHint}";
					if (context.Mode == AssetDeliveryBuildMode.Aab) context.Error(message);
					else context.Warning(message);
				}

				foreach (var plan in rule.CollectPlans(aa))
				{
					if (plan.IsBase) continue;
					foreach (var groupName in plan.GroupNames)
					{
						var schema = aa.FindGroup(groupName)?.GetSchema<UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema>();
						if (schema == null) continue;
						string loadPath = schema.LoadPath.GetValue(aa) ?? string.Empty;
						if (loadPath.Contains("://") && !loadPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
						{
							context.Warning($"Group '{groupName}' (pack '{plan.PackName}') uses a remote Load Path ({loadPath}). Asset packs are local content: switch the group to Local.");
						}
					}
				}

				if (rule is GroupPackRule groupRule)
				{
					foreach (var missing in groupRule.MissingGroups(aa))
					{
						context.Warning($"Rule '{rule.name}': group '{missing}' does not exist in Addressables.");
					}

					if (aa.DefaultGroup != null && groupRule.groups.Contains(aa.DefaultGroup.Name))
					{
						context.Warning($"Rule '{rule.name}': '{aa.DefaultGroup.Name}' is the Default group. New Addressables assets land in this pack automatically, and loading them at startup fails while the pack is not downloaded.");
					}
				}
			}
		}

		private static void CheckDependencies(ValidationContext context)
		{
			var aa = context.Addressables;
			var plans = GroupSynchronizer.CollectPackPlans(context.Settings, aa);
			if (plans.Count == 0) return;

			foreach (var pair in PackContentAnalyzer.FindBaseGroupsDependingOnPacks(aa, plans))
			{
				context.Warning($"Group '{pair.Key}' is in the base but references assets in pack {string.Join(", ", pair.Value)}. Loading it before the pack is downloaded fails: move shared assets to the base, or download the pack first.");
			}

			var deps = PackContentAnalyzer.ComputePackDependencies(aa, plans);
			var byName = plans.ToDictionary(p => p.PackName);
			foreach (var pair in deps)
			{
				if (pair.Value.Count == 0) continue;
				var self = byName[pair.Key];
				foreach (var dep in pair.Value)
				{
					var other = byName[dep];
					if (self.Delivery == DeliveryMode.InstallTime && other.Delivery != DeliveryMode.InstallTime)
					{
						context.Warning($"Install-time pack '{pair.Key}' references pack '{dep}' ({other.Delivery.ToGradleValue()}): its assets cannot load until '{dep}' is downloaded.");
					}
				}

				context.Info($"Pack '{pair.Key}' depends on {string.Join(", ", pair.Value)}: the runtime downloads them together.");
			}

			foreach (var rule in context.Settings.ActiveRules)
			{
				foreach (var plan in rule.CollectPlans(aa))
				{
					if (plan.IsBase) continue;
					foreach (var groupName in plan.GroupNames)
					{
						var group = aa.FindGroup(groupName);
						if (group != null && !PackContentAnalyzer.IsIncludedInBuild(group))
						{
							context.Warning($"Group '{groupName}' has 'Include in Build' turned off, so it is left out of pack '{plan.PackName}'.");
						}
					}
				}
			}
		}

		private static void CheckAabOutput(ValidationContext context)
		{
			var settings = context.Settings;
			var plans = GroupSynchronizer.CollectPackPlans(settings, context.Addressables);

			if (!EditorUserBuildSettings.buildAppBundle)
			{
				context.Error("Build App Bundle (AAB) is off: asset packs only exist in .aab files.");
			}

			if (context.Addressables.BuildAddressablesWithPlayerBuild != AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer && plans.Count > 0)
			{
				context.Error("Addressables is set to 'Build Addressables content on Player build': the player build would rebuild it and put pack bundles back into the base. " + FixHint);
			}

			var manifest = context.Manifest;
			if (manifest == null)
			{
				context.Error($"Manifest not found ({settings.manifestPath}). {FixHint}");
				return;
			}

			if (!manifest.packsEnabled)
			{
				context.Error($"Manifest is in APK mode (no packs). {FixHint}");
				return;
			}

			if (manifest.layoutSignature != PackPlan.ComputeSignature(plans))
			{
				context.Error($"Group layout changed since the last content build. {FixHint}");
			}

			if (!AddressablesContent.HasLocalBuild())
			{
				context.Error($"Addressables not built ({AddressablesContent.LocalBundlePath} does not exist). {FixHint}");
				return;
			}

			var activePacks = new HashSet<string>(manifest.packs.Select(p => p.name));
			var stale = AssetPackExporter.FindManagedPackFolders(settings)
				.Where(dir => !activePacks.Contains(Path.GetFileName(dir).Replace(".androidpack", string.Empty)))
				.ToList();
			if (stale.Count > 0) context.Error($"Unused pack folders: {string.Join(", ", stale)}. {FixHint}");

			var baseBundles = new HashSet<string>(
				Directory.GetFiles(AddressablesContent.LocalBundlePath, "*.bundle").Select(Path.GetFileName),
				StringComparer.OrdinalIgnoreCase);

			foreach (var pack in manifest.packs)
			{
				string dir = AssetPackExporter.GetBundleDir(settings, pack.name);
				var inPack = Directory.Exists(dir)
					? new HashSet<string>(Directory.GetFiles(dir, "*.bundle").Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase)
					: new HashSet<string>(StringComparer.OrdinalIgnoreCase);

				var missing = pack.bundles.Where(b => !inPack.Contains(b)).ToList();
				if (missing.Count > 0) context.Error($"Pack '{pack.name}' is missing bundles: {string.Join(", ", missing)}. {FixHint}");

				var extra = inPack.Where(b => !pack.bundles.Contains(b, StringComparer.OrdinalIgnoreCase)).ToList();
				if (extra.Count > 0) context.Error($"Pack '{pack.name}' contains stale bundles: {string.Join(", ", extra)}. {FixHint}");

				var leftover = pack.bundles.Where(baseBundles.Contains).ToList();
				if (leftover.Count > 0) context.Error($"Bundles of pack '{pack.name}' are still in the base: {string.Join(", ", leftover)}. {FixHint}");

				if (!File.Exists(Path.Combine(AssetPackExporter.GetPackFolder(settings, pack.name), "build.gradle")))
				{
					context.Error($"Pack '{pack.name}' is missing build.gradle. {FixHint}");
				}
			}
		}

		private static void CheckApk(ValidationContext context)
		{
			var manifest = context.Manifest;
			if (manifest != null && manifest.packsEnabled && manifest.packs.Count > 0)
			{
				context.Error("Building an APK while the manifest still has packs: pack content would be missing from the .apk. Use Build APK in the Asset Delivery window.");
			}
		}

		private static void RunCustomChecks(ValidationContext context)
		{
			foreach (var type in TypeCache.GetTypesDerivedFrom<IAssetDeliveryCheck>())
			{
				if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null) continue;
				try
				{
					((IAssetDeliveryCheck)Activator.CreateInstance(type)).Run(context);
				}
				catch (Exception e)
				{
					context.Warning($"Check {type.Name} failed: {e.Message}");
					Debug.LogException(e);
				}
			}
		}
	}
}
