using com.ktgame.core.editor;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;

namespace com.ktgame.assetdelivery.editor.ktgame
{
	[InitializeOnLoad]
	public sealed class AssetDeliveryEditorModule : IMenuTreeExtension, IEditorDirtyHandler
	{
		private const string MenuName = "Asset Delivery";
		private AssetDeliveryPage _page;

		static AssetDeliveryEditorModule()
		{
			var module = new AssetDeliveryEditorModule();
			EditorDirtyRegistry.Register(module);
			MenuTreeExtensionRegistry.Register(module);
		}

		public void BuildMenu(OdinMenuTree tree)
		{
			_page = new AssetDeliveryPage();
			tree.Add(MenuName, _page, SdfIconType.BoxSeam);
		}

		public void SetDirty()
		{
			var settings = _page?.Settings;
			if (settings != null) EditorUtility.SetDirty(settings);
		}
	}
}
