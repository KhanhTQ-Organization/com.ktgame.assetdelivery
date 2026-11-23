using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace com.ktgame.assetdelivery.editor
{
	[Serializable]
	public abstract class AssetPackRule
	{
		protected static readonly Regex PackNameRegex = new Regex("^[a-z][a-z0-9_]*$");

		public string name = "Rule";
		public bool enabled = true;

		public abstract IEnumerable<string> Validate();

		public virtual void Sync(AddressableAssetSettings aa, GroupSyncResult result, HashSet<AddressableAssetGroup> touched)
		{
		}

		public abstract List<PackPlan> CollectPlans(AddressableAssetSettings aa);

		public virtual List<PackPlan> Preview(AddressableAssetSettings aa) => CollectPlans(aa);

		public virtual List<string> FindMisplaced(AddressableAssetSettings aa) => new List<string>();

		public abstract bool ClaimsGroup(string groupName);

		public abstract bool ClaimsPackName(string packName);

		public abstract IEnumerable<string> SampleGroups();

		public abstract IEnumerable<string> SamplePackNames();

		public virtual string Describe() => GetType().Name;

		protected string Label => string.IsNullOrEmpty(name) ? "(unnamed)" : name;

		protected static List<string> KeysOf(AddressableAssetGroup group)
		{
			return group.entries.Select(e => e.address).OrderBy(k => k, NaturalStringComparer.Instance).ToList();
		}

		internal static string StripAndroidPackSuffix(string value)
		{
			const string suffix = ".androidpack";
			value ??= string.Empty;
			return value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? value.Substring(0, value.Length - suffix.Length) : value;
		}
	}

	[Serializable]
	public sealed class GroupPackRule : AssetPackRule
	{
		[Tooltip("Asset pack name on Google Play. Lowercase letters, digits and underscores only, starting with a letter.")]
		public string packName = "extra_content";

		public DeliveryMode delivery = DeliveryMode.OnDemand;

		[Tooltip("Addressables groups that go into this pack (one or more).")]
		public List<string> groups = new List<string>();

		public GroupPackRule()
		{
			name = "Group";
		}

		public override IEnumerable<string> Validate()
		{
			if (string.IsNullOrEmpty(packName) || !PackNameRegex.IsMatch(packName))
				yield return $"Rule '{Label}': packName '{packName}' is invalid (a-z, 0-9, _ only, must start with a letter).";
			if (groups == null || groups.All(string.IsNullOrEmpty))
				yield return $"Rule '{Label}': no group selected.";
			if (groups != null && groups.Distinct().Count() != groups.Count)
				yield return $"Rule '{Label}': duplicate groups in the list.";
		}

		public override List<PackPlan> CollectPlans(AddressableAssetSettings aa)
		{
			var plan = new PackPlan
			{
				Rule = this,
				Index = 1,
				PackName = packName,
				Delivery = delivery,
			};

			foreach (var groupName in groups.Where(g => !string.IsNullOrEmpty(g)))
			{
				var group = aa.FindGroup(groupName);
				if (group == null) continue;
				plan.GroupNames.Add(group.Name);
				plan.Keys.AddRange(group.entries.Select(e => e.address));
			}

			plan.Keys.Sort(NaturalStringComparer.Instance);
			return new List<PackPlan> { plan };
		}

		public override List<string> FindMisplaced(AddressableAssetSettings aa)
		{
			return new List<string>();
		}

		public override bool ClaimsGroup(string groupName) => groups.Contains(groupName);

		public override bool ClaimsPackName(string value) => string.Equals(StripAndroidPackSuffix(value), packName, StringComparison.OrdinalIgnoreCase);

		public override IEnumerable<string> SampleGroups() => groups.Where(g => !string.IsNullOrEmpty(g));

		public override IEnumerable<string> SamplePackNames()
		{
			yield return packName;
		}

		public override string Describe() => $"{string.Join(", ", groups)} -> {packName} ({delivery.ToGradleValue()})";

		public IEnumerable<string> MissingGroups(AddressableAssetSettings aa) => groups.Where(g => !string.IsNullOrEmpty(g) && aa.FindGroup(g) == null);
	}

	[Serializable]
	public sealed class PartitionedPackRule : AssetPackRule
	{
		[Tooltip("Group that ships with the app (index 0).")]
		public string baseGroup = "Level_Base";

		[Tooltip("Group of pack N is named prefix + N, e.g. Level_Pack1.")]
		public string packGroupPrefix = "Level_Pack";

		[Tooltip("Asset pack name on Google Play = prefix + N. Lowercase letters, digits and underscores only, starting with a letter.")]
		public string packNamePrefix = "level_pack";

		[Tooltip("Other groups scanned for entries (e.g. where new levels are dropped). Matching entries are moved into this rule's groups.")]
		public List<string> extraSourceGroups = new List<string>();

		[SerializeReference, SubclassSelector]
		public PackPartitioner partitioner = new NumberedKeyPartitioner();

		[SerializeReference, SubclassSelector]
		public DeliveryPolicy delivery = new FastFollowFirstPolicy();

		public PartitionedPackRule()
		{
			name = "Level";
		}

		public string GetGroupName(int packIndex) => packIndex <= 0 ? baseGroup : packGroupPrefix + packIndex;

		public string GetPackName(int packIndex) => packIndex <= 0 ? null : packNamePrefix + packIndex;

		public DeliveryMode GetDelivery(int packIndex) => (delivery ?? new FastFollowFirstPolicy()).GetDelivery(packIndex);

		public bool OwnsGroup(string groupName) => groupName == baseGroup || TryParsePackGroup(groupName, out _);

		public bool TryParsePackGroup(string groupName, out int index) => TryParseIndexedName(groupName, packGroupPrefix, StringComparison.Ordinal, out index);

		public override bool ClaimsGroup(string groupName) => OwnsGroup(groupName);

		public override bool ClaimsPackName(string value) => TryParseIndexedName(StripAndroidPackSuffix(value), packNamePrefix, StringComparison.OrdinalIgnoreCase, out _);

		public override IEnumerable<string> SampleGroups()
		{
			yield return baseGroup;
			yield return packGroupPrefix + 1;
		}

		public override IEnumerable<string> SamplePackNames()
		{
			yield return packNamePrefix + 1;
		}

		public override string Describe() => partitioner?.Describe() ?? "(no partitioner)";

		public override IEnumerable<string> Validate()
		{
			if (string.IsNullOrEmpty(baseGroup)) yield return $"Rule '{Label}': baseGroup is missing.";
			if (string.IsNullOrEmpty(packGroupPrefix)) yield return $"Rule '{Label}': packGroupPrefix is missing.";
			if (string.IsNullOrEmpty(packNamePrefix) || !PackNameRegex.IsMatch(packNamePrefix))
				yield return $"Rule '{Label}': packNamePrefix '{packNamePrefix}' is invalid (a-z, 0-9, _ only, must start with a letter).";
			if (!string.IsNullOrEmpty(baseGroup) && TryParsePackGroup(baseGroup, out _))
				yield return $"Rule '{Label}': baseGroup '{baseGroup}' matches the pack group pattern '{packGroupPrefix}<N>'.";
			if (partitioner == null) yield return $"Rule '{Label}': no partitioner selected.";
			if (delivery == null) yield return $"Rule '{Label}': no delivery policy selected.";
		}

		public override void Sync(AddressableAssetSettings aa, GroupSyncResult result, HashSet<AddressableAssetGroup> touched)
		{
			var ownedGroups = aa.groups.Where(g => g != null && OwnsGroup(g.Name)).ToList();
			var template = ownedGroups.FirstOrDefault(g => TryParsePackGroup(g.Name, out _))
			               ?? ownedGroups.FirstOrDefault()
			               ?? extraSourceGroups.Select(aa.FindGroup).FirstOrDefault(g => g != null)
			               ?? aa.DefaultGroup;

			if (template == null)
			{
				result.Errors.Add($"Rule '{Label}': no group available to copy schemas from.");
				result.Success = false;
				return;
			}

			AddressableAssetGroup GetOrCreate(string groupName)
			{
				var group = aa.FindGroup(groupName);
				if (group == null)
				{
					group = aa.CreateGroup(groupName, false, false, true, template.Schemas);
					result.CreatedGroups.Add(groupName);
				}

				touched.Add(group);
				return group;
			}

			GetOrCreate(baseGroup);

			var entries = CollectEntries(aa);
			var infos = entries.Select(GroupSynchronizer.ToInfo).ToList();
			partitioner.Prepare(infos);

			for (int i = 0; i < entries.Count; i++)
			{
				int index = partitioner.GetPackIndex(infos[i]);
				if (index < 0) continue;

				var entry = entries[i];
				var target = GetOrCreate(GetGroupName(index));
				if (entry.parentGroup == target) continue;

				touched.Add(entry.parentGroup);
				aa.MoveEntry(entry, target, false, false);
				result.Moved++;
			}

			foreach (var group in aa.groups.Where(g => g != null && TryParsePackGroup(g.Name, out _)).ToList())
			{
				if (group.entries.Count > 0) continue;
				string groupName = group.Name;
				touched.Remove(group);
				aa.RemoveGroup(group);
				result.RemovedGroups.Add(groupName);
			}
		}

		public override List<PackPlan> CollectPlans(AddressableAssetSettings aa)
		{
			var plans = new List<PackPlan>();
			foreach (var group in aa.groups)
			{
				if (group == null) continue;
				int index;
				if (group.Name == baseGroup) index = 0;
				else if (!TryParsePackGroup(group.Name, out index)) continue;

				var plan = new PackPlan
				{
					Rule = this,
					Index = index,
					PackName = GetPackName(index),
					Delivery = index > 0 ? GetDelivery(index) : DeliveryMode.InstallTime,
					Keys = KeysOf(group),
				};
				plan.GroupNames.Add(group.Name);
				plans.Add(plan);
			}

			return plans.OrderBy(p => p.Index).ToList();
		}

		public override List<PackPlan> Preview(AddressableAssetSettings aa)
		{
			var plans = new Dictionary<int, PackPlan>();
			if (partitioner == null) return new List<PackPlan>();

			var entries = CollectEntries(aa);
			var infos = entries.Select(GroupSynchronizer.ToInfo).ToList();
			partitioner.Prepare(infos);

			for (int i = 0; i < entries.Count; i++)
			{
				int index = Math.Max(-1, partitioner.GetPackIndex(infos[i]));
				if (!plans.TryGetValue(index, out var plan))
				{
					plan = new PackPlan
					{
						Rule = this,
						Index = index,
						PackName = index > 0 ? GetPackName(index) : null,
						Delivery = index > 0 ? GetDelivery(index) : DeliveryMode.InstallTime,
					};
					plan.GroupNames.Add(index < 0 ? "(unchanged)" : GetGroupName(index));
					plans[index] = plan;
				}

				plan.Keys.Add(entries[i].address);
			}

			foreach (var plan in plans.Values) plan.Keys.Sort(NaturalStringComparer.Instance);
			return plans.Values.OrderBy(p => p.Index).ToList();
		}

		public override List<string> FindMisplaced(AddressableAssetSettings aa)
		{
			var result = new List<string>();
			if (partitioner == null) return result;

			var entries = CollectEntries(aa);
			var infos = entries.Select(GroupSynchronizer.ToInfo).ToList();
			partitioner.Prepare(infos);

			for (int i = 0; i < entries.Count; i++)
			{
				int index = partitioner.GetPackIndex(infos[i]);
				if (index < 0) continue;
				string expected = GetGroupName(index);
				if (entries[i].parentGroup.Name != expected)
				{
					result.Add($"{entries[i].address} (in {entries[i].parentGroup.Name}, expected {expected})");
				}
			}

			return result;
		}

		public List<AddressableAssetEntry> CollectEntries(AddressableAssetSettings aa)
		{
			var result = new List<AddressableAssetEntry>();
			var seen = new HashSet<string>();
			foreach (var group in aa.groups)
			{
				if (group == null || !(OwnsGroup(group.Name) || extraSourceGroups.Contains(group.Name))) continue;
				foreach (var entry in group.entries)
				{
					if (entry != null && seen.Add(entry.guid)) result.Add(entry);
				}
			}

			return result;
		}

		private static bool TryParseIndexedName(string value, string prefix, StringComparison comparison, out int index)
		{
			index = -1;
			if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(prefix) || value.Length <= prefix.Length) return false;
			if (!value.StartsWith(prefix, comparison)) return false;

			string digits = value.Substring(prefix.Length);
			foreach (char c in digits)
			{
				if (c < '0' || c > '9') return false;
			}

			return int.TryParse(digits, out index) && index > 0;
		}
	}
}
