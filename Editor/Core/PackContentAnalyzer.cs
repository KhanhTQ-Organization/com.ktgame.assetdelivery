using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace com.ktgame.assetdelivery.editor
{
	public static class PackContentAnalyzer
	{
		public static List<AddressableAssetEntry> GatherEntries(AddressableAssetGroup group)
		{
			var result = new List<AddressableAssetEntry>();
			if (group == null) return result;
			foreach (var entry in group.entries)
			{
				if (entry == null) continue;
				var gathered = new List<AddressableAssetEntry>();
				entry.GatherAllAssets(gathered, true, true, false);
				result.AddRange(gathered);
			}

			return result;
		}

		public static bool IsIncludedInBuild(AddressableAssetGroup group)
		{
			var schema = group?.GetSchema<BundledAssetGroupSchema>();
			return schema != null && schema.IncludeInBuild;
		}

		public static Dictionary<string, string> MapAssetsToPacks(AddressableAssetSettings aa, IEnumerable<PackPlan> packPlans)
		{
			var map = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var plan in packPlans)
			{
				foreach (var groupName in plan.GroupNames)
				{
					foreach (var entry in GatherEntries(aa.FindGroup(groupName)))
					{
						if (IsAssetPath(entry.AssetPath)) map[entry.AssetPath] = plan.PackName;
					}
				}
			}

			return map;
		}

		public static Dictionary<string, List<string>> ComputePackDependencies(AddressableAssetSettings aa, List<PackPlan> packPlans)
		{
			var assetToPack = MapAssetsToPacks(aa, packPlans);
			var result = new Dictionary<string, List<string>>();

			foreach (var plan in packPlans)
			{
				var roots = plan.GroupNames.SelectMany(g => GatherEntries(aa.FindGroup(g))).Select(e => e.AssetPath).Where(IsAssetPath).Distinct().ToArray();
				var deps = new SortedSet<string>(StringComparer.Ordinal);
				if (roots.Length > 0)
				{
					foreach (var dep in AssetDatabase.GetDependencies(roots, true))
					{
						if (assetToPack.TryGetValue(dep, out var other) && other != plan.PackName) deps.Add(other);
					}
				}

				result[plan.PackName] = deps.ToList();
			}

			return result;
		}

		public static Dictionary<string, List<string>> FindBaseGroupsDependingOnPacks(AddressableAssetSettings aa, List<PackPlan> packPlans)
		{
			var assetToPack = MapAssetsToPacks(aa, packPlans);
			var packGroups = new HashSet<string>(packPlans.SelectMany(p => p.GroupNames));
			var result = new Dictionary<string, List<string>>();
			if (assetToPack.Count == 0) return result;

			foreach (var group in aa.groups)
			{
				if (group == null || packGroups.Contains(group.Name) || !IsIncludedInBuild(group)) continue;
				var roots = GatherEntries(group).Select(e => e.AssetPath).Where(IsAssetPath).Distinct().ToArray();
				if (roots.Length == 0) continue;

				var needed = new SortedSet<string>(StringComparer.Ordinal);
				foreach (var dep in AssetDatabase.GetDependencies(roots, true))
				{
					if (assetToPack.TryGetValue(dep, out var pack)) needed.Add(pack);
				}

				if (needed.Count > 0) result[group.Name] = needed.ToList();
			}

			return result;
		}

		public static bool IsAssetPath(string path)
		{
			return !string.IsNullOrEmpty(path) && (path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal));
		}
	}
}
