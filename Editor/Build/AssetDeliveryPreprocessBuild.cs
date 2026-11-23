using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace com.ktgame.assetdelivery.editor
{
	internal sealed class AssetDeliveryPreprocessBuild : IPreprocessBuildWithReport
	{
		public int callbackOrder => -100;

		public void OnPreprocessBuild(BuildReport report)
		{
			if (report.summary.platform != BuildTarget.Android || AssetDeliveryBuilder.IsRunning) return;

			var settings = AssetDeliverySettings.Load();
			if (settings == null || !settings.enabled) return;

			var mode = EditorUserBuildSettings.buildAppBundle ? AssetDeliveryBuildMode.Aab : AssetDeliveryBuildMode.Apk;
			var issues = AssetDeliveryValidator.Validate(settings, mode);
			if (!issues.Exists(i => i.Severity != IssueSeverity.Info))
			{
				Debug.Log($"[AssetDelivery] Pre-build check ({mode}): OK." + (issues.Count > 0 ? "\n" + AssetDeliveryValidator.Format(issues) : string.Empty));
				return;
			}

			string message = AssetDeliveryValidator.Format(issues);
			if (AssetDeliveryValidator.HasErrors(issues))
			{
				throw new BuildFailedException($"[AssetDelivery] Content is not ready for {mode}:\n{message}");
			}

			Debug.LogWarning($"[AssetDelivery] {message}");
		}
	}
}
