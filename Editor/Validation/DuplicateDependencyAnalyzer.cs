using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;

namespace com.ktgame.assetdelivery.editor
{
	public sealed class DuplicateDependency
	{
		public string AssetPath;
		public long SourceBytes;

		public List<string> Groups = new List<string>();
	}

	public static class DuplicateDependencyAnalyzer
	{
		private static readonly HashSet<string> IgnoredExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs", ".dll", ".asmdef", ".shader", ".cginc", ".hlsl" };

		public static List<DuplicateDependency> Analyze(AssetDeliverySettings settings, AddressableAssetSettings aa, Action<float> progress = null)
		{
			var explicitAssets = new HashSet<string>();
			foreach (var group in aa.groups)
			{
				if (group == null) continue;
				foreach (var entry in PackContentAnalyzer.GatherEntries(group))
				{
					if (!string.IsNullOrEmpty(entry.AssetPath)) explicitAssets.Add(entry.AssetPath);
				}
			}

			var owners = new List<(string name, List<AddressableAssetGroup> groups)>();
			var inPacks = new HashSet<string>();
			foreach (var plan in GroupSynchronizer.CollectPackPlans(settings, aa))
			{
				var groups = plan.GroupNames.Select(aa.FindGroup).Where(g => g != null).ToList();
				foreach (var g in groups) inPacks.Add(g.Name);
				if (groups.Count > 0) owners.Add((plan.PackName, groups));
			}

			foreach (var group in aa.groups)
			{
				if (group != null && !inPacks.Contains(group.Name) && group.entries.Count > 0)
				{
					owners.Add(("base: " + group.Name, new List<AddressableAssetGroup> { group }));
				}
			}

			var usage = new Dictionary<string, DuplicateDependency>();
			for (int i = 0; i < owners.Count; i++)
			{
				progress?.Invoke((float)i / Math.Max(1, owners.Count));
				var roots = owners[i].groups.SelectMany(PackContentAnalyzer.GatherEntries)
					.Select(e => e.AssetPath)
					.Where(IsProjectAsset)
					.ToArray();
				if (roots.Length == 0) continue;

				foreach (var dep in AssetDatabase.GetDependencies(roots, true))
				{
					if (explicitAssets.Contains(dep) || !IsProjectAsset(dep) || IgnoredExtensions.Contains(Path.GetExtension(dep))) continue;

					if (!usage.TryGetValue(dep, out var info))
					{
						info = new DuplicateDependency { AssetPath = dep, SourceBytes = File.Exists(dep) ? new FileInfo(dep).Length : 0 };
						usage[dep] = info;
					}

					if (!info.Groups.Contains(owners[i].name)) info.Groups.Add(owners[i].name);
				}
			}

			progress?.Invoke(1f);

			return usage.Values
				.Where(u => u.Groups.Count > 1 && u.Groups.Any(g => !g.StartsWith("base: ", StringComparison.Ordinal)))
				.OrderByDescending(u => u.SourceBytes * (u.Groups.Count - 1))
				.ToList();
		}

		private static bool IsProjectAsset(string path)
		{
			return !string.IsNullOrEmpty(path) && (path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal));
		}
	}
}
