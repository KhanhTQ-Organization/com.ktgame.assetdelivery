using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace com.ktgame.assetdelivery.editor
{
	public sealed class AssetDeliverySettings : ScriptableObject
	{
		public const string DefaultAssetPath = "Assets/AssetDelivery/AssetDeliverySettings.asset";

		[Tooltip("Off: no packs are created and builds are not blocked.")]
		public bool enabled = true;

		[Tooltip("Each element picks a rule type: Group Pack Rule (any group -> one pack) or Partitioned Pack Rule (splits entries into several packs).")]
		[SerializeReference, SubclassSelector]
		public List<AssetPackRule> rules = new List<AssetPackRule>();

		public IEnumerable<AssetPackRule> ActiveRules => rules.Where(r => r != null && r.enabled);

		[Header("Paths")]
		[Tooltip("Where {pack}.androidpack folders are created (must be under Assets so Unity packs them into the AAB).")]
		public string assetPacksRoot = "Assets/CustomAssetPacks";

		[Tooltip("Runtime manifest file. Must be inside a Resources folder and named " + AssetDeliveryManifest.ResourceName + ".json")]
		public string manifestPath = "Assets/AssetDelivery/Resources/" + AssetDeliveryManifest.ResourceName + ".json";

		[Tooltip("Subfolder that holds the bundles inside each pack.")]
		public string bundleSubdirectory = "aa/Android";

		[Header("Android Build")]
		[Tooltip("Enable Split Application Binary when building an AAB.")]
		public bool splitApplicationBinaryForAab = true;

		[Tooltip("Turn off 'Separate Base APK Asset' of the Google Android App Bundle plugin, if present.")]
		public bool disableGoogleSplitBaseModuleAssets = true;

		public AndroidCreateSymbols aabSymbols = AndroidCreateSymbols.Debugging;
		public AndroidCreateSymbols apkSymbols = AndroidCreateSymbols.Disabled;

		[Tooltip("Clean the previous Addressables build before building content.")]
		public bool cleanAddressablesBeforeBuild;

		private static AssetDeliverySettings _cached;

		public static AssetDeliverySettings Load()
		{
			if (_cached != null) return _cached;

			var guids = AssetDatabase.FindAssets("t:" + nameof(AssetDeliverySettings));
			if (guids.Length > 1)
			{
				Debug.LogWarning($"[AssetDelivery] Found {guids.Length} AssetDeliverySettings assets, using the first one: {AssetDatabase.GUIDToAssetPath(guids[0])}");
			}

			_cached = guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<AssetDeliverySettings>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
			return _cached;
		}

		public static AssetDeliverySettings GetOrCreate()
		{
			var settings = Load();
			if (settings != null) return settings;

			Directory.CreateDirectory(Path.GetDirectoryName(DefaultAssetPath) ?? "Assets");
			settings = CreateInstance<AssetDeliverySettings>();
			AssetDatabase.CreateAsset(settings, DefaultAssetPath);
			AssetDatabase.SaveAssets();
			_cached = settings;
			Debug.Log($"[AssetDelivery] Created {DefaultAssetPath}.");
			return settings;
		}

		public PartitionedPackRule AddLevelRulePreset()
		{
			int baseCount = 100, packSize = 50;
			const string legacyConfig = "Assets/Resources/level_pack_config.txt";
			if (File.Exists(legacyConfig))
			{
				foreach (var raw in File.ReadAllLines(legacyConfig))
				{
					var parts = raw.Split('=');
					if (parts.Length != 2 || !int.TryParse(parts[1].Trim(), out int value)) continue;
					if (parts[0].Trim() == "base") baseCount = Mathf.Max(0, value);
					else if (parts[0].Trim() == "size") packSize = Mathf.Max(1, value);
				}
			}

			var rule = new PartitionedPackRule
			{
				partitioner = new NumberedKeyPartitioner { baseCount = baseCount, packSize = packSize },
				delivery = new FastFollowFirstPolicy { fastFollowCount = 1 },
			};
			AddRule(rule, "Add Level rule");
			return rule;
		}

		public GroupPackRule AddGroupRulePreset(string groupName = null)
		{
			var rule = new GroupPackRule();
			if (!string.IsNullOrEmpty(groupName))
			{
				rule.name = groupName;
				rule.groups.Add(groupName);
				rule.packName = ToPackName(groupName);
			}

			AddRule(rule, "Add group rule");
			return rule;
		}

		public static string ToPackName(string value)
		{
			var chars = (value ?? string.Empty).ToLowerInvariant().Select(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ? c : '_').ToArray();
			string result = new string(chars).Trim('_');
			while (result.Contains("__")) result = result.Replace("__", "_");
			if (result.Length == 0 || !(result[0] >= 'a' && result[0] <= 'z')) result = "pack_" + result;
			return result;
		}

		private void AddRule(AssetPackRule rule, string undoName)
		{
			Undo.RecordObject(this, undoName);
			rules.Add(rule);
			EditorUtility.SetDirty(this);
		}

		public IEnumerable<string> ValidateSettings()
		{
			var active = ActiveRules.ToList();
			foreach (var rule in active)
			{
				foreach (var error in rule.Validate()) yield return error;
			}

			var reported = new HashSet<string>();
			for (int i = 0; i < active.Count; i++)
			{
				for (int j = 0; j < active.Count; j++)
				{
					if (i == j) continue;
					var a = active[i];
					var b = active[j];
					foreach (var group in a.SampleGroups().Where(g => !string.IsNullOrEmpty(g)))
					{
						if (b.ClaimsGroup(group) && reported.Add($"g|{System.Math.Min(i, j)}|{System.Math.Max(i, j)}")) yield return $"Rules '{a.name}' and '{b.name}' both use group '{group}'.";
					}

					foreach (var pack in a.SamplePackNames().Where(p => !string.IsNullOrEmpty(p)))
					{
						if (b.ClaimsPackName(pack) && reported.Add($"p|{System.Math.Min(i, j)}|{System.Math.Max(i, j)}")) yield return $"Rules '{a.name}' and '{b.name}' both create pack '{pack}'.";
					}
				}
			}

			string normalized = (manifestPath ?? string.Empty).Replace('\\', '/');
			if (!normalized.Contains("/Resources/") || Path.GetFileNameWithoutExtension(normalized) != AssetDeliveryManifest.ResourceName)
			{
				yield return $"manifestPath '{manifestPath}' must be inside a Resources folder and named {AssetDeliveryManifest.ResourceName}.json.";
			}

			if (string.IsNullOrEmpty(assetPacksRoot) || !assetPacksRoot.Replace('\\', '/').StartsWith("Assets/"))
			{
				yield return $"assetPacksRoot '{assetPacksRoot}' must be under Assets/.";
			}
		}

		private void OnValidate()
		{
			_cached = null;
		}

		private void OnDestroy()
		{
			if (_cached == this) _cached = null;
		}
	}
}
