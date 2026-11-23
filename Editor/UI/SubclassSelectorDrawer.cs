using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace com.ktgame.assetdelivery.editor
{
	[CustomPropertyDrawer(typeof(SubclassSelectorAttribute))]
	internal sealed class SubclassSelectorDrawer : PropertyDrawer
	{
		private static readonly Dictionary<Type, Type[]> TypeCacheByBase = new Dictionary<Type, Type[]>();

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			return EditorGUI.GetPropertyHeight(property, label, true);
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			if (property.propertyType != SerializedPropertyType.ManagedReference)
			{
				EditorGUI.PropertyField(position, property, label, true);
				return;
			}

			EditorGUI.BeginProperty(position, label, property);

			var baseType = GetBaseType();
			var popupRect = new Rect(
				position.x + EditorGUIUtility.labelWidth + 2f,
				position.y,
				Mathf.Max(60f, position.width - EditorGUIUtility.labelWidth - 2f),
				EditorGUIUtility.singleLineHeight);

			string current = GetShortTypeName(property.managedReferenceFullTypename);
			if (EditorGUI.DropdownButton(popupRect, new GUIContent(string.IsNullOrEmpty(current) ? "(none)" : ObjectNames.NicifyVariableName(current)), FocusType.Keyboard))
			{
				var menu = new GenericMenu();
				menu.AddItem(new GUIContent("(none)"), string.IsNullOrEmpty(current), () => Assign(property, null));
				foreach (var type in GetCandidates(baseType))
				{
					var t = type;
					menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(t.Name)), t.Name == current, () => Assign(property, Activator.CreateInstance(t)));
				}

				menu.DropDown(popupRect);
			}

			EditorGUI.PropertyField(position, property, label, true);
			EditorGUI.EndProperty();
		}

		private Type GetBaseType()
		{
			var type = fieldInfo.FieldType;
			if (type.IsArray) return type.GetElementType();
			if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return type.GetGenericArguments()[0];
			return type;
		}

		private static IEnumerable<Type> GetCandidates(Type baseType)
		{
			if (TypeCacheByBase.TryGetValue(baseType, out var cached)) return cached;
			cached = TypeCache.GetTypesDerivedFrom(baseType)
				.Where(t => !t.IsAbstract && !t.IsGenericType && !typeof(UnityEngine.Object).IsAssignableFrom(t)
				            && t.IsSerializable && t.GetConstructor(Type.EmptyTypes) != null)
				.OrderBy(t => t.Name)
				.ToArray();
			TypeCacheByBase[baseType] = cached;
			return cached;
		}

		private static void Assign(SerializedProperty property, object value)
		{
			property.serializedObject.Update();
			property.managedReferenceValue = value;
			property.serializedObject.ApplyModifiedProperties();
		}

		private static string GetShortTypeName(string fullTypename)
		{
			if (string.IsNullOrEmpty(fullTypename)) return null;
			int space = fullTypename.IndexOf(' ');
			string typeName = space >= 0 ? fullTypename.Substring(space + 1) : fullTypename;
			int dot = typeName.LastIndexOf('.');
			return dot >= 0 ? typeName.Substring(dot + 1) : typeName;
		}
	}
}
