using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace com.ktgame.assetdelivery.editor
{
	[Serializable]
	public abstract class PackPartitioner
	{
		public const int Unmanaged = -1;
		public const int Base = 0;

		public virtual void Prepare(IReadOnlyList<PackEntryInfo> entries)
		{
		}

		public abstract int GetPackIndex(in PackEntryInfo entry);

		public virtual string Describe() => GetType().Name;
	}

	[Serializable]
	public sealed class NumberedKeyPartitioner : PackPartitioner
	{
		[Tooltip("Regex applied to the address (or file name when useAddress is off). Group 1 must be a number.")]
		public string pattern = @"^Level(\d+)$";

		[Tooltip("Untick to use the file name (without extension) instead of the address.")]
		public bool useAddress = true;

		[Min(0)] public int baseCount = 100;
		[Min(1)] public int packSize = 50;

		[NonSerialized] private Regex _regex;
		[NonSerialized] private string _regexPattern;

		public override int GetPackIndex(in PackEntryInfo entry)
		{
			if (!TryGetNumber(entry, out int number)) return Unmanaged;
			return GetPackIndexForNumber(number, baseCount, packSize);
		}

		public bool TryGetNumber(in PackEntryInfo entry, out int number)
		{
			number = -1;
			var regex = GetRegex();
			if (regex == null) return false;

			string source = useAddress ? entry.Address : System.IO.Path.GetFileNameWithoutExtension(entry.AssetPath);
			var match = regex.Match(source ?? string.Empty);
			return match.Success && match.Groups.Count > 1 && int.TryParse(match.Groups[1].Value, out number);
		}

		public static int GetPackIndexForNumber(int number, int baseCount, int packSize)
		{
			if (number <= baseCount) return Base;
			return (number - baseCount - 1) / Mathf.Max(1, packSize) + 1;
		}

		public override string Describe() => $"first {baseCount} in base, {packSize} per pack (regex {pattern})";

		private Regex GetRegex()
		{
			if (_regex != null && _regexPattern == pattern) return _regex;
			_regexPattern = pattern;
			try
			{
				_regex = string.IsNullOrEmpty(pattern) ? null : new Regex(pattern, RegexOptions.CultureInvariant);
			}
			catch (ArgumentException e)
			{
				Debug.LogError($"[AssetDelivery] Invalid regex '{pattern}': {e.Message}");
				_regex = null;
			}

			return _regex;
		}
	}

	[Serializable]
	public sealed class LabelPartitioner : PackPartitioner
	{
		[Serializable]
		public struct Mapping
		{
			public string label;
			[Min(0)] public int packIndex;
		}

		public List<Mapping> mappings = new List<Mapping>();

		[Tooltip("-1: entries with no matching label stay where they are; 0: move them to base.")]
		public int fallbackIndex = Unmanaged;

		public override int GetPackIndex(in PackEntryInfo entry)
		{
			foreach (var mapping in mappings)
			{
				if (string.IsNullOrEmpty(mapping.label)) continue;
				foreach (var label in entry.Labels)
				{
					if (label == mapping.label) return mapping.packIndex;
				}
			}

			return fallbackIndex;
		}

		public override string Describe() => $"By label ({mappings.Count} mappings)";
	}

	[Serializable]
	public sealed class FolderPartitioner : PackPartitioner
	{
		[Serializable]
		public struct Mapping
		{
			[Tooltip("e.g. Assets/_Game/Levels/Chapter2")]
			public string folder;

			[Min(0)] public int packIndex;
		}

		public List<Mapping> mappings = new List<Mapping>();
		public int fallbackIndex = Unmanaged;

		public override int GetPackIndex(in PackEntryInfo entry)
		{
			int best = fallbackIndex;
			int bestLength = -1;
			string path = entry.AssetPath.Replace('\\', '/');

			foreach (var mapping in mappings)
			{
				if (string.IsNullOrEmpty(mapping.folder)) continue;
				string folder = mapping.folder.Replace('\\', '/').TrimEnd('/') + "/";
				if (path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && folder.Length > bestLength)
				{
					best = mapping.packIndex;
					bestLength = folder.Length;
				}
			}

			return best;
		}

		public override string Describe() => $"By folder ({mappings.Count} mappings)";
	}
}
