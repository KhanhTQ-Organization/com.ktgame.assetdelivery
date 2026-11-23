using UnityEditor;
using UnityEngine;

namespace com.ktgame.assetdelivery.editor
{
	public sealed class AssetDeliveryWindow : EditorWindow
	{
		private const string MenuRoot = "Ktgame/Asset Delivery/";
		private AssetDeliveryPanel _panel;

		[MenuItem(MenuRoot + "Window", false, 5100)]
		public static void Open()
		{
			var window = GetWindow<AssetDeliveryWindow>("Asset Delivery");
			window.minSize = new Vector2(560, 360);
			window.Show();
		}

		[MenuItem(MenuRoot + "Validate (AAB)", false, 5120)]
		public static void ValidateMenu()
		{
			var issues = AssetDeliveryValidator.Validate(AssetDeliverySettings.Load(), AssetDeliveryBuildMode.Aab);
			string message = issues.Count == 0 ? "Asset packs are valid and match the manifest." : AssetDeliveryValidator.Format(issues);
			if (AssetDeliveryValidator.HasErrors(issues)) Debug.LogError("[AssetDelivery] " + message);
			else Debug.Log("[AssetDelivery] " + message);
			EditorUtility.DisplayDialog("Asset Delivery", message, "OK");
		}

		[MenuItem(MenuRoot + "Build AAB", false, 5140)]
		public static void BuildAabMenu() => EditorApplication.delayCall += () => AssetDeliveryBuilder.BuildInteractive(AssetDeliveryBuildMode.Aab);

		[MenuItem(MenuRoot + "Build APK (test)", false, 5141)]
		public static void BuildApkMenu() => EditorApplication.delayCall += () => AssetDeliveryBuilder.BuildInteractive(AssetDeliveryBuildMode.Apk);

		private void OnEnable()
		{
			_panel = new AssetDeliveryPanel(Repaint);
		}

		private void OnFocus()
		{
			_panel?.Refresh();
		}

		private void OnGUI()
		{
			if (_panel == null) _panel = new AssetDeliveryPanel(Repaint);
			_panel.DrawWindow();
		}
	}
}
