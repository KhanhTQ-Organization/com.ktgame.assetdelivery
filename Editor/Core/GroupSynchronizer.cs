using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;

namespace com.ktgame.assetdelivery.editor
{
	public sealed class GroupSyncResult
	{
		public bool Success = true;
		public int Moved;
		public readonly List<string> CreatedGroups = new List<string>();
		public readonly List<string> RemovedGroups = new List<string>();
		public readonly List<string> Errors = new List<string>();
		public readonly List<string> Warnings = new List<string>();
		public List<PackPlan> Plans = new List<PackPlan>();

		public string Summary()
		{
			var lines = new List<string> { $"Moved {Moved} entries." };
			if (CreatedGroups.Count > 0) lines.Add("Created groups: " + string.Join(", ", CreatedGroups));
			if (RemovedGroups.Count > 0) lines.Add("Removed empty groups: " + string.Join(", ", RemovedGroups));
			foreach (var plan in Plans)
			{
				lines.Add($"- {plan.GroupName}: {plan.Keys.Count} ({plan.DescribeRange()}) - {plan.DeliveryLabel}");
			}

			lines.AddRange(Warnings.Select(w => "[!] " + w));
			lines.AddRange(Errors.Select(e => "[x] " + e));
			return string.Join("\n", lines);
		}
	}

	public static class GroupSynchronizer
	{
		public static GroupSyncResult Sync(AssetDeliverySettings settings, AddressableAssetSettings aa)
		{
			var result = new GroupSyncResult();
			if (settings == null || aa == null)
			{
				result.Success = false;
				result.Errors.Add("Missing AssetDeliverySettings or AddressableAssetSettings.");
				return result;
			}

			var settingErrors = settings.ValidateSettings().ToList();
			if (settingErrors.Count > 0)
			{
				result.Success = false;
				result.Errors.AddRange(settingErrors);
				return result;
			}

			var touched = new HashSet<AddressableAssetGroup>();
			foreach (var rule in settings.ActiveRules)
			{
				rule.Sync(aa, result, touched);
			}

			foreach (var group in touched)
			{
				if (group != null) EditorUtility.SetDirty(group);
			}

			if (touched.Count > 0 || result.Moved > 0)
			{
				EditorUtility.SetDirty(aa);
				aa.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
				AssetDatabase.SaveAssets();
			}

			result.Plans = CollectPlans(settings, aa);
			return result;
		}

		public static PackEntryInfo ToInfo(AddressableAssetEntry entry)
		{
			return new PackEntryInfo(entry.address, entry.AssetPath, entry.guid, entry.labels.ToList());
		}

		public static List<PackPlan> CollectPlans(AssetDeliverySettings settings, AddressableAssetSettings aa)
		{
			var plans = new List<PackPlan>();
			if (settings == null || aa == null) return plans;
			foreach (var rule in settings.ActiveRules) plans.AddRange(rule.CollectPlans(aa));
			return plans;
		}

		public static List<PackPlan> CollectPackPlans(AssetDeliverySettings settings, AddressableAssetSettings aa)
		{
			var result = new List<PackPlan>();
			foreach (var plan in CollectPlans(settings, aa))
			{
				if (plan.IsBase) continue;
				var included = plan.GroupNames.Where(g => PackContentAnalyzer.IsIncludedInBuild(aa.FindGroup(g))).ToList();
				if (included.Count != plan.GroupNames.Count)
				{
					plan.GroupNames = included;
					plan.Keys = included.SelectMany(g => aa.FindGroup(g).entries.Select(e => e.address)).OrderBy(k => k, NaturalStringComparer.Instance).ToList();
				}

				if (plan.Keys.Count > 0) result.Add(plan);
			}

			return result;
		}
	}
}
