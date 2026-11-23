using System.Collections.Generic;

namespace com.ktgame.assetdelivery.editor
{
	public readonly struct PackEntryInfo
	{
		public readonly string Address;
		public readonly string AssetPath;
		public readonly string Guid;
		public readonly IReadOnlyCollection<string> Labels;

		public PackEntryInfo(string address, string assetPath, string guid, IReadOnlyCollection<string> labels)
		{
			Address = address ?? string.Empty;
			AssetPath = assetPath ?? string.Empty;
			Guid = guid ?? string.Empty;
			Labels = labels ?? System.Array.Empty<string>();
		}
	}
}
