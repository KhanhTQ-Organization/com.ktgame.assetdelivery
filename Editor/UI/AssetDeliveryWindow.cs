using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace com.ktgame.assetdelivery.editor
{
	public sealed class AssetDeliveryWindow : EditorWindow
	{
		private const string MenuRoot = "Ktgame/Asset Delivery/";
		private static readonly Regex PackNameRegex = new Regex("^[a-z][a-z0-9_]*$");

		private AssetDeliverySettings _settings;
		private SerializedObject _serialized;
		private Vector2 _scroll;
		private bool _showAdvanced;
		private bool _showAllRules;

		private List<ValidationIssue> _issues;
		private List<DuplicateDependency> _duplicates;
		private string _status;
		private MessageType _statusType = MessageType.Info;

		private GUIStyle _header;
		private GUIStyle _muted;
		private GUIStyle _rowBold;

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
			_settings = AssetDeliverySettings.Load();
		}

		private void OnFocus()
		{
			if (_settings == null) _settings = AssetDeliverySettings.Load();
		}

		private void EnsureStyles()
		{
			if (_header != null) return;
			_header = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft };
			_muted = new GUIStyle(EditorStyles.label) { normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.4f, 0.4f, 0.4f) } };
			_rowBold = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
		}

		private void OnGUI()
		{
			EnsureStyles();
			DrawToolbar();

			if (_settings == null)
			{
				DrawEmptyState();
				return;
			}

			var aa = AddressablesContent.Settings;
			if (aa == null)
			{
				EditorGUILayout.HelpBox("This project has no Addressables settings.", MessageType.Warning);
				return;
			}

			_scroll = EditorGUILayout.BeginScrollView(_scroll);
			DrawGroupTable(aa);
			DrawPartitionedRules(aa);
			DrawAdvanced();
			DrawIssues();
			DrawDuplicates();
			EditorGUILayout.EndScrollView();

			DrawFooter();
		}

		#region Toolbar / empty

		private void DrawToolbar()
		{
			using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
			{
				EditorGUI.BeginChangeCheck();
				var picked = (AssetDeliverySettings)EditorGUILayout.ObjectField(_settings, typeof(AssetDeliverySettings), false, GUILayout.MinWidth(180));
				if (EditorGUI.EndChangeCheck())
				{
					_settings = picked;
					OnSettingsChanged();
				}

				GUILayout.FlexibleSpace();

				if (_settings != null)
				{
					bool enabled = GUILayout.Toggle(_settings.enabled, new GUIContent(" On", "Off: no packs are created and builds are not blocked."), EditorStyles.toolbarButton, GUILayout.Width(44));
					if (enabled != _settings.enabled)
					{
						Undo.RecordObject(_settings, "Toggle Asset Delivery");
						_settings.enabled = enabled;
						OnSettingsChanged();
					}

					if (GUILayout.Button("Add", EditorStyles.toolbarDropDown, GUILayout.Width(56))) ShowAddMenu();
				}
			}
		}

		private void DrawEmptyState()
		{
			GUILayout.FlexibleSpace();
			using (new EditorGUILayout.HorizontalScope())
			{
				GUILayout.FlexibleSpace();
				using (new EditorGUILayout.VerticalScope(GUILayout.Width(320)))
				{
					EditorGUILayout.LabelField("This project has no Asset Delivery Settings yet.", EditorStyles.wordWrappedLabel);
					EditorGUILayout.Space(4);
					if (GUILayout.Button("Create Settings", GUILayout.Height(28)))
					{
						_settings = AssetDeliverySettings.GetOrCreate();
						OnSettingsChanged();
					}
				}

				GUILayout.FlexibleSpace();
			}

			GUILayout.FlexibleSpace();
		}

		private void ShowAddMenu()
		{
			var menu = new GenericMenu();
			menu.AddItem(new GUIContent("Numbered Split Rule (Level)"), false, () =>
			{
				_settings.AddLevelRulePreset();
				OnSettingsChanged();
			});
			menu.AddItem(new GUIContent("Multi-Group Pack Rule"), false, () =>
			{
				var rule = _settings.AddGroupRulePreset();
				rule.name = "Merged Groups";
				_showAdvanced = true;
				_showAllRules = true;
				OnSettingsChanged();
			});
			menu.ShowAsContext();
		}

		#endregion

		#region Group table

		private void DrawGroupTable(AddressableAssetSettings aa)
		{
			EditorGUILayout.Space(4);
			EditorGUILayout.LabelField("Tick a group to move it into an asset pack (not installed with the app, downloaded later from Google Play).", EditorStyles.wordWrappedMiniLabel);

			var manifest = AssetPackExporter.ReadManifest(_settings);

			using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
			{
				GUILayout.Space(22);
				GUILayout.Label("Group", _header, GUILayout.MinWidth(120));
				GUILayout.Label("Asset pack", _header, GUILayout.Width(150));
				GUILayout.Label("Giao", _header, GUILayout.Width(96));
				GUILayout.Label("Entry", _header, GUILayout.Width(44));
				GUILayout.Label("Last Build", _header, GUILayout.Width(96));
			}

			foreach (var group in aa.groups)
			{
				if (group == null || group.ReadOnly) continue;
				DrawGroupRow(aa, group, manifest);
			}
		}

		private void DrawGroupRow(AddressableAssetSettings aa, AddressableAssetGroup group, AssetDeliveryManifest manifest)
		{
			var owner = _settings.rules.FirstOrDefault(r => r != null && r.ClaimsGroup(group.Name));
			var simple = owner as GroupPackRule;
			bool editable = owner == null || (simple != null && simple.groups.Count == 1);
			PackPlan plan = owner?.CollectPlans(aa).FirstOrDefault(p => p.GroupNames.Contains(group.Name));
			bool isPack = owner != null && owner.enabled && plan != null && !plan.IsBase;

			using (new EditorGUILayout.HorizontalScope())
			{
				using (new EditorGUI.DisabledScope(!editable))
				{
					bool ticked = EditorGUILayout.Toggle(isPack || (simple != null && editable), GUILayout.Width(18));
					if (editable && ticked != (simple != null)) SetGroupPacked(group.Name, ticked, simple);
				}

				GUILayout.Label(group.Name, isPack ? _rowBold : EditorStyles.label, GUILayout.MinWidth(120));

				if (simple != null && editable)
				{
					DrawPackNameField(simple);
					var delivery = (DeliveryMode)EditorGUILayout.EnumPopup(simple.delivery, GUILayout.Width(96));
					if (delivery != simple.delivery)
					{
						Undo.RecordObject(_settings, "Change delivery");
						simple.delivery = delivery;
						OnSettingsChanged();
					}
				}
				else if (owner != null)
				{
					string text = plan == null ? "-" : plan.IsBase ? "base" : plan.PackName;
					GUILayout.Label(new GUIContent(text, $"Owned by rule '{owner.name}'. Edit it in Advanced > All Rules."), _muted, GUILayout.Width(150));
					GUILayout.Label(plan == null || plan.IsBase ? "installed" : plan.Delivery.ToGradleValue(), _muted, GUILayout.Width(96));
				}
				else
				{
					GUILayout.Label("installed", _muted, GUILayout.Width(150 + 96 + 4));
				}

				GUILayout.Label(group.entries.Count.ToString(), _muted, GUILayout.Width(44));

				var built = isPack ? manifest?.FindPack(plan.PackName) : null;
				GUILayout.Label(built != null ? AssetPackExporter.FormatSize(built.sizeBytes) : "", _muted, GUILayout.Width(96));
			}
		}

		private void DrawPackNameField(GroupPackRule rule)
		{
			var old = GUI.color;
			if (!PackNameRegex.IsMatch(rule.packName ?? string.Empty)) GUI.color = new Color(1f, 0.55f, 0.55f);
			string value = EditorGUILayout.DelayedTextField(rule.packName, GUILayout.Width(150));
			GUI.color = old;
			if (value == rule.packName) return;

			Undo.RecordObject(_settings, "Rename pack");
			rule.packName = value.Trim().ToLowerInvariant();
			OnSettingsChanged();
		}

		private void SetGroupPacked(string groupName, bool packed, GroupPackRule existing)
		{
			Undo.RecordObject(_settings, packed ? "Pack group" : "Unpack group");
			if (packed) _settings.AddGroupRulePreset(groupName);
			else if (existing != null) _settings.rules.Remove(existing);
			OnSettingsChanged();
		}

		#endregion

		#region Numbered split rules

		private void DrawPartitionedRules(AddressableAssetSettings aa)
		{
			var rules = _settings.rules.OfType<PartitionedPackRule>().ToList();
			if (rules.Count == 0) return;

			EditorGUILayout.Space(8);
			EditorGUILayout.LabelField("Numbered Split Rules", EditorStyles.boldLabel);

			foreach (var rule in rules)
			{
				using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
				{
					using (new EditorGUILayout.HorizontalScope())
					{
						bool enabled = EditorGUILayout.ToggleLeft($"{rule.name} - {rule.Describe()}", rule.enabled, EditorStyles.boldLabel);
						if (enabled != rule.enabled)
						{
							Undo.RecordObject(_settings, "Toggle rule");
							rule.enabled = enabled;
							OnSettingsChanged();
						}

						if (GUILayout.Button("Edit", EditorStyles.miniButtonLeft, GUILayout.Width(40)))
						{
							_showAdvanced = true;
							_showAllRules = true;
						}

						if (GUILayout.Button("Remove", EditorStyles.miniButtonRight, GUILayout.Width(40)))
						{
							Undo.RecordObject(_settings, "Remove rule");
							_settings.rules.Remove(rule);
							OnSettingsChanged();
							GUIUtility.ExitGUI();
						}
					}

					if (!rule.enabled) continue;
					var plans = rule.Preview(aa).Where(p => p.Keys.Count > 0).ToList();
					if (plans.Count == 0)
					{
						EditorGUILayout.LabelField($"No entries in '{rule.baseGroup}' or '{rule.packGroupPrefix}<N>'.", _muted);
						continue;
					}

					foreach (var plan in plans)
					{
						string target = plan.IsBase ? plan.GroupName : plan.PackName;
						EditorGUILayout.LabelField($"{target}: {plan.Keys.Count} ({plan.DescribeRange()}) - {plan.DeliveryLabel}", _muted);
					}
				}
			}
		}

		#endregion

		#region Advanced

		private void DrawAdvanced()
		{
			EditorGUILayout.Space(8);
			_showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Advanced", true);
			if (!_showAdvanced) return;

			if (_serialized == null || _serialized.targetObject != _settings) _serialized = new SerializedObject(_settings);
			_serialized.Update();

			using (new EditorGUI.IndentLevelScope())
			{
				EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(AssetDeliverySettings.assetPacksRoot)));
				EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(AssetDeliverySettings.manifestPath)));
				EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(AssetDeliverySettings.bundleSubdirectory)));
				EditorGUILayout.Space(4);
				EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(AssetDeliverySettings.splitApplicationBinaryForAab)), new GUIContent("Split Application Binary (AAB)"));
				EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(AssetDeliverySettings.disableGoogleSplitBaseModuleAssets)), new GUIContent("Disable Separate Base APK Asset"));
				EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(AssetDeliverySettings.aabSymbols)), new GUIContent("Symbols (AAB)"));
				EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(AssetDeliverySettings.apkSymbols)), new GUIContent("Symbols (APK)"));
				EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(AssetDeliverySettings.cleanAddressablesBeforeBuild)), new GUIContent("Clean Addressables Before Build"));
				EditorGUILayout.Space(4);

				_showAllRules = EditorGUILayout.Foldout(_showAllRules, $"All Rules ({_settings.rules.Count})", true);
				if (_showAllRules)
				{
					EditorGUILayout.PropertyField(_serialized.FindProperty(nameof(AssetDeliverySettings.rules)), GUIContent.none, true);
				}
			}

			if (_serialized.ApplyModifiedProperties()) OnSettingsChanged(saveAssets: false);
		}

		#endregion

		#region Results

		private void DrawIssues()
		{
			if (_issues == null) return;
			EditorGUILayout.Space(8);
			if (_issues.Count == 0)
			{
				EditorGUILayout.HelpBox("Validation: no issues.", MessageType.Info);
				return;
			}

			foreach (var issue in _issues)
			{
				var type = issue.Severity == IssueSeverity.Error ? MessageType.Error : issue.Severity == IssueSeverity.Warning ? MessageType.Warning : MessageType.Info;
				EditorGUILayout.HelpBox(issue.Message, type);
			}
		}

		private void DrawDuplicates()
		{
			if (_duplicates == null) return;
			EditorGUILayout.Space(8);
			if (_duplicates.Count == 0)
			{
				EditorGUILayout.HelpBox("No assets are duplicated between the base and packs.", MessageType.Info);
				return;
			}

			EditorGUILayout.HelpBox($"{_duplicates.Count} assets belong to no group but are shared, so they are copied into every bundle that uses them. Move them into a shared group in the base to reduce size.", MessageType.Warning);
			foreach (var dup in _duplicates.Take(50))
			{
				using (new EditorGUILayout.HorizontalScope())
				{
					if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(44)))
					{
						var obj = AssetDatabase.LoadMainAssetAtPath(dup.AssetPath);
						Selection.activeObject = obj;
						EditorGUIUtility.PingObject(obj);
					}

					GUILayout.Label($"{dup.AssetPath}  |  {AssetPackExporter.FormatSize(dup.SourceBytes)} x {dup.Groups.Count}  |  {string.Join(", ", dup.Groups)}", _muted);
				}
			}
		}

		private void DrawFooter()
		{
			if (!string.IsNullOrEmpty(_status))
			{
				EditorGUILayout.HelpBox(_status, _statusType);
			}

			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button("Validate", GUILayout.Height(26)))
				{
					_issues = AssetDeliveryValidator.Validate(_settings, AssetDeliveryBuildMode.Aab);
					_status = null;
				}

				if (GUILayout.Button("Tools", EditorStyles.popup, GUILayout.Height(26), GUILayout.Width(90))) ShowToolsMenu();

				GUILayout.FlexibleSpace();

				if (GUILayout.Button(new GUIContent("Build APK", "A single .apk with all content, for testing. Do not upload it to Google Play."), GUILayout.Height(26), GUILayout.Width(96)))
				{
					EditorApplication.delayCall += () => AssetDeliveryBuilder.BuildInteractive(AssetDeliveryBuildMode.Apk);
					GUIUtility.ExitGUI();
				}

				var old = GUI.backgroundColor;
				GUI.backgroundColor = new Color(0.45f, 0.7f, 1f);
				if (GUILayout.Button("Build AAB", GUILayout.Height(26), GUILayout.Width(110)))
				{
					EditorApplication.delayCall += () => AssetDeliveryBuilder.BuildInteractive(AssetDeliveryBuildMode.Aab);
					GUIUtility.ExitGUI();
				}

				GUI.backgroundColor = old;
			}

			EditorGUILayout.Space(2);
		}

		private void ShowToolsMenu()
		{
			var menu = new GenericMenu();
			menu.AddItem(new GUIContent("Find Duplicated Assets"), false, () =>
			{
				try
				{
					_duplicates = DuplicateDependencyAnalyzer.Analyze(_settings, AddressablesContent.Settings,
						p => EditorUtility.DisplayProgressBar("Asset Delivery", "Analyzing dependencies...", p));
				}
				finally
				{
					EditorUtility.ClearProgressBar();
				}
			});
			menu.AddItem(new GUIContent("Build Content Only (AAB)"), false, () =>
			{
				bool ok = AssetDeliveryBuilder.PrepareContent(AssetDeliveryBuildMode.Aab, out var message);
				SetStatus(message, ok ? MessageType.Info : MessageType.Error);
			});
			bool hasPartitioned = _settings.rules.Any(r => r is PartitionedPackRule && r.enabled);
			if (hasPartitioned)
			{
				menu.AddItem(new GUIContent("Resync Groups (numbered split rules)"), false, () =>
				{
					var result = GroupSynchronizer.Sync(_settings, AddressablesContent.Settings);
					SetStatus(result.Summary(), result.Success ? MessageType.Info : MessageType.Error);
				});
			}
			else
			{
				menu.AddDisabledItem(new GUIContent("Resync Groups (numbered split rules only)"));
			}

			menu.ShowAsContext();
		}

		private void SetStatus(string message, MessageType type)
		{
			_status = message;
			_statusType = type;
			_issues = null;
			if (type == MessageType.Error) Debug.LogError("[AssetDelivery] " + message);
			else Debug.Log("[AssetDelivery] " + message);
			Repaint();
		}

		#endregion

		private void OnSettingsChanged(bool saveAssets = true)
		{
			if (_settings != null)
			{
				EditorUtility.SetDirty(_settings);
				if (saveAssets) AssetDatabase.SaveAssets();
			}

			_serialized = null;
			_issues = null;
			_status = null;
			Repaint();
		}
	}
}
