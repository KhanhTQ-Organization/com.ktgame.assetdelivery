using System;
using System.Collections.Generic;
using UnityEngine;

namespace com.ktgame.assetdelivery
{
	[Serializable]
	public sealed class AssetDeliveryManifest
	{
		public const string ResourceName = "ktgame_asset_delivery_manifest";
		public const int CurrentFormat = 1;

		public int format = CurrentFormat;

		public bool packsEnabled;

		public string bundleSubdirectory = "aa/Android";

		public string layoutSignature;

		public List<AssetPackEntry> packs = new List<AssetPackEntry>();

		[NonSerialized] private Dictionary<string, AssetPackEntry> _byName;
		[NonSerialized] private Dictionary<string, AssetPackEntry> _byBundle;
		[NonSerialized] private Dictionary<string, AssetPackEntry> _byKey;
		[NonSerialized] private Dictionary<string, List<AssetPackEntry>> _byLabel;

		public static AssetDeliveryManifest CreateEmpty()
		{
			return new AssetDeliveryManifest { packsEnabled = false };
		}

		public static AssetDeliveryManifest LoadFromResources()
		{
			try
			{
				var asset = Resources.Load<TextAsset>(ResourceName);
				if (asset == null)
				{
					return CreateEmpty();
				}

				var manifest = FromJson(asset.text);
				Resources.UnloadAsset(asset);
				return manifest;
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[AssetDelivery] Failed to read manifest '{ResourceName}': {e.Message}. Treating all content as base.");
				return CreateEmpty();
			}
		}

		public static AssetDeliveryManifest FromJson(string json)
		{
			if (string.IsNullOrEmpty(json))
			{
				return CreateEmpty();
			}

			var manifest = JsonUtility.FromJson<AssetDeliveryManifest>(json) ?? CreateEmpty();
			manifest.packs ??= new List<AssetPackEntry>();
			return manifest;
		}

		public string ToJson(bool pretty = true)
		{
			return JsonUtility.ToJson(this, pretty);
		}

		public AssetPackEntry FindPack(string packName)
		{
			if (string.IsNullOrEmpty(packName)) return null;
			EnsureIndex();
			return _byName.TryGetValue(packName, out var entry) ? entry : null;
		}

		public AssetPackEntry FindPackByBundle(string bundleFileName)
		{
			if (string.IsNullOrEmpty(bundleFileName)) return null;
			EnsureIndex();
			return _byBundle.TryGetValue(bundleFileName, out var entry) ? entry : null;
		}

		public AssetPackEntry FindPackByKey(string key)
		{
			if (string.IsNullOrEmpty(key)) return null;
			EnsureIndex();
			return _byKey.TryGetValue(key, out var entry) ? entry : null;
		}

		public IReadOnlyList<AssetPackEntry> FindPacksByLabel(string label)
		{
			if (string.IsNullOrEmpty(label)) return Array.Empty<AssetPackEntry>();
			EnsureIndex();
			return _byLabel.TryGetValue(label, out var list) ? list : (IReadOnlyList<AssetPackEntry>)Array.Empty<AssetPackEntry>();
		}

		public List<AssetPackEntry> ResolveWithDependencies(string packName)
		{
			var result = new List<AssetPackEntry>();
			var visited = new HashSet<string>(StringComparer.Ordinal);

			void Visit(string name)
			{
				if (string.IsNullOrEmpty(name) || !visited.Add(name)) return;
				var pack = FindPack(name);
				if (pack == null) return;
				if (pack.dependsOn != null)
				{
					foreach (var dep in pack.dependsOn) Visit(dep);
				}

				result.Add(pack);
			}

			Visit(packName);
			return result;
		}

		public void RebuildIndex()
		{
			_byName = null;
			EnsureIndex();
		}

		private void EnsureIndex()
		{
			if (_byName != null) return;

			_byName = new Dictionary<string, AssetPackEntry>(StringComparer.Ordinal);
			_byBundle = new Dictionary<string, AssetPackEntry>(StringComparer.OrdinalIgnoreCase);
			_byKey = new Dictionary<string, AssetPackEntry>(StringComparer.Ordinal);
			_byLabel = new Dictionary<string, List<AssetPackEntry>>(StringComparer.Ordinal);

			if (packs == null) return;
			foreach (var pack in packs)
			{
				if (pack == null || string.IsNullOrEmpty(pack.name)) continue;
				_byName[pack.name] = pack;

				if (pack.bundles != null)
				{
					foreach (var bundle in pack.bundles)
					{
						if (!string.IsNullOrEmpty(bundle)) _byBundle[bundle] = pack;
					}
				}

				if (pack.keys != null)
				{
					foreach (var key in pack.keys)
					{
						if (!string.IsNullOrEmpty(key)) _byKey[key] = pack;
					}
				}

				if (pack.labels != null)
				{
					foreach (var label in pack.labels)
					{
						if (string.IsNullOrEmpty(label)) continue;
						if (!_byLabel.TryGetValue(label, out var list)) _byLabel[label] = list = new List<AssetPackEntry>();
						if (!list.Contains(pack)) list.Add(pack);
					}
				}
			}
		}
	}

	[Serializable]
	public sealed class AssetPackEntry
	{
		public string name;
		public DeliveryMode delivery = DeliveryMode.OnDemand;

		public string sourceGroup;

		public List<string> bundles = new List<string>();

		public List<string> keys = new List<string>();

		public List<string> labels = new List<string>();

		public List<string> dependsOn = new List<string>();

		public long sizeBytes;
	}
}
