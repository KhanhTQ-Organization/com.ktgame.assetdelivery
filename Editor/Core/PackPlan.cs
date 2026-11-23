using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace com.ktgame.assetdelivery.editor
{
	public sealed class PackPlan
	{
		public AssetPackRule Rule;

		public int Index;

		public List<string> GroupNames = new List<string>();

		public string PackName;
		public DeliveryMode Delivery;
		public List<string> Keys = new List<string>();

		public bool IsBase => Index <= 0;

		public string GroupName => string.Join(" + ", GroupNames);

		public string DescribeRange()
		{
			if (Keys.Count == 0) return "(empty)";
			return Keys.Count == 1 ? Keys[0] : $"{Keys[0]} - {Keys[Keys.Count - 1]}";
		}

		public string DeliveryLabel => Index < 0 ? "unchanged" : IsBase ? "install with app" : Delivery.ToGradleValue();

		public static string ComputeSignature(IEnumerable<PackPlan> plans)
		{
			var sb = new StringBuilder();
			foreach (var plan in plans)
			{
				if (plan.IsBase || plan.Keys.Count == 0) continue;
				sb.Append(plan.PackName).Append('|').Append(string.Join("+", plan.GroupNames)).Append('|').Append(plan.Delivery).Append('|');
				sb.Append(string.Join(",", plan.Keys)).Append('\n');
			}

			return Hash128.Compute(sb.ToString()).ToString();
		}
	}

	public sealed class NaturalStringComparer : IComparer<string>
	{
		public static readonly NaturalStringComparer Instance = new NaturalStringComparer();

		public int Compare(string x, string y)
		{
			if (ReferenceEquals(x, y)) return 0;
			if (x == null) return -1;
			if (y == null) return 1;

			int i = 0, j = 0;
			while (i < x.Length && j < y.Length)
			{
				if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
				{
					int si = i, sj = j;
					while (i < x.Length && char.IsDigit(x[i])) i++;
					while (j < y.Length && char.IsDigit(y[j])) j++;
					string a = x.Substring(si, i - si).TrimStart('0');
					string b = y.Substring(sj, j - sj).TrimStart('0');
					if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
					int c = string.CompareOrdinal(a, b);
					if (c != 0) return c;
				}
				else
				{
					int c = x[i].CompareTo(y[j]);
					if (c != 0) return c;
					i++;
					j++;
				}
			}

			return (x.Length - i).CompareTo(y.Length - j);
		}
	}
}
