using Sirenix.OdinInspector;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace com.ktgame.assetdelivery.editor.ktgame
{
	public sealed class AssetDeliveryPage
	{
		private readonly AssetDeliveryPanel _panel;

		public AssetDeliveryPage()
		{
			_panel = new AssetDeliveryPanel(GUIHelper.RequestRepaint) { AdvancedFoldout = false };
		}

		public AssetDeliverySettings Settings => _panel.Settings;

		private bool HasContent => _panel.HasContent;
		private bool HasPartitionedRules => HasContent && _panel.HasPartitionedRules;
		private bool HasResults => HasContent && _panel.HasResults;

		[Title("Asset Delivery", "Move Addressables groups into Google Play asset packs to reduce the install size")]
		[BoxGroup("Settings")]
		[OnInspectorGUI, PropertyOrder(0)]
		private void DrawSettings()
		{
			_panel.Refresh();
			using (new EditorGUILayout.HorizontalScope())
			{
				_panel.DrawSettingsBar(false);
			}

			if (_panel.Settings == null)
			{
				EditorGUILayout.Space(4);
				_panel.DrawEmptyState();
				return;
			}

			if (AddressablesContent.Settings == null)
			{
				EditorGUILayout.HelpBox("This project has no Addressables settings.", MessageType.Warning);
			}
		}

		[BoxGroup("Groups")]
		[OnInspectorGUI, PropertyOrder(10), ShowIf(nameof(HasContent))]
		private void DrawGroups()
		{
			_panel.DrawGroupTable();
		}

		[BoxGroup("Numbered Split Rules")]
		[OnInspectorGUI, PropertyOrder(20), ShowIf(nameof(HasPartitionedRules))]
		private void DrawRules()
		{
			_panel.DrawPartitionedRules(false);
		}

		[FoldoutGroup("Advanced")]
		[OnInspectorGUI, PropertyOrder(30), ShowIf(nameof(HasContent))]
		private void DrawAdvanced()
		{
			_panel.DrawAdvanced();
		}

		[BoxGroup("Results")]
		[OnInspectorGUI, PropertyOrder(40), ShowIf(nameof(HasResults))]
		private void DrawResults()
		{
			_panel.DrawResults();
		}

		[BoxGroup("Build")]
		[OnInspectorGUI, PropertyOrder(50), ShowIf(nameof(HasContent))]
		private void DrawBuild()
		{
			_panel.DrawStatus();
			GUILayout.Space(2);
			_panel.DrawActions();
		}
	}
}
