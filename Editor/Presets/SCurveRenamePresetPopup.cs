using System;
using UnityEditor;
using UnityEngine;

namespace Sperlich.Easing.Editor {

	/// <summary>Tiny native (IMGUI) rename prompt -- Unity has no built-in single-line text-input dialog, and a
	/// whole UI-Toolkit window felt like overkill for "type a name, press Enter".</summary>
	class SCurveRenamePresetPopup : EditorWindow {
		string newName;
		Action<string> onConfirm;

		public static void Open(string currentName, Action<string> onConfirm) {
			var win = CreateInstance<SCurveRenamePresetPopup>();
			win.titleContent = new GUIContent("Rename Preset");
			win.newName = currentName;
			win.onConfirm = onConfirm;
			win.minSize = win.maxSize = new Vector2(280, 74);
			win.ShowUtility();
		}

		void OnGUI() {
			GUILayout.Space(6);
			GUI.SetNextControlName("SCurveRenameField");
			newName = EditorGUILayout.TextField(newName);
			EditorGUI.FocusTextInControl("SCurveRenameField");

			bool confirmedByEnter = Event.current.isKey && Event.current.keyCode is KeyCode.Return or KeyCode.KeypadEnter;

			GUILayout.Space(6);
			using (new EditorGUILayout.HorizontalScope()) {
				GUILayout.FlexibleSpace();
				bool confirmedByButton = GUILayout.Button("Rename", GUILayout.Width(70));
				if (GUILayout.Button("Cancel", GUILayout.Width(70))) Close();
				if ((confirmedByButton || confirmedByEnter) && !string.IsNullOrWhiteSpace(newName)) {
					onConfirm?.Invoke(newName.Trim());
					Close();
				}
			}
		}
	}
}
