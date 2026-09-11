using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sperlich.EditorKit;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sperlich.Easing.Editor {

	/// <summary>Save/apply/rename/delete UI for <see cref="SCurvePresetAsset"/> -- project-wide custom presets,
	/// found via <see cref="AssetDatabase.FindAssets"/> rather than tied to one field or folder. A dropdown lists
	/// every preset asset in the project; right-clicking it opens Overwrite/Rename/Delete/Reveal. Saving prompts
	/// for a location via the standard project save panel, so the user picks where it lives.</summary>
	class SCurveCustomPresetBar : VisualElement {
		public Action<SCurve> OnApply;
		readonly Func<SCurve> getCurrentCurve;

		readonly VisualElement dropdownSlot;
		List<string> guids = new();
		int selectedIndex = -1;

		public SCurveCustomPresetBar(Func<SCurve> getCurrentCurve) {
			this.getCurrentCurve = getCurrentCurve;
			style.flexDirection = FlexDirection.Row;
			style.alignItems = Align.Center;

			dropdownSlot = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, width = 140 } };
			Add(dropdownSlot);
			RebuildDropdown();

			VisualElement saveBtn = MakeIconButton("+", SaveAsNew);
			saveBtn.tooltip = "Save current curve as a new custom preset";
			saveBtn.style.marginLeft = 4;
			Add(saveBtn);
		}

		void RefreshList() => guids = AssetDatabase.FindAssets("t:" + nameof(SCurvePresetAsset)).ToList();

		void RebuildDropdown() {
			RefreshList();
			dropdownSlot.Clear();

			VisualElement dd = SperlichEditorWidgets.BuildDropdown(
				() => guids.Count,
				i => NameOf(guids[i]),
				() => selectedIndex,
				i => { selectedIndex = i; ApplyIndex(i); },
				SperlichEditorTheme.ButtonAccent);
			dd.style.flexGrow = 1;
			dd.tooltip = guids.Count == 0 ? "No custom presets saved yet" : "Custom Presets (project-wide) -- right-click for more options";
			dd.RegisterCallback<ContextClickEvent>(evt => { ShowContextMenu(); evt.StopPropagation(); });
			dropdownSlot.Add(dd);
		}

		string NameOf(string guid) => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));

		void ApplyIndex(int index) {
			SCurvePresetAsset asset = LoadAt(index);
			if (asset?.curve == null || asset.curve.KeyCount == 0) return;
			OnApply?.Invoke(CloneCurve(asset.curve));
		}

		SCurvePresetAsset LoadAt(int index) {
			if (index < 0 || index >= guids.Count) return null;
			return AssetDatabase.LoadAssetAtPath<SCurvePresetAsset>(AssetDatabase.GUIDToAssetPath(guids[index]));
		}

		void SaveAsNew() {
			SCurve current = getCurrentCurve?.Invoke();
			if (current == null || current.KeyCount == 0) return;

			string path = EditorUtility.SaveFilePanelInProject("Save SCurve Preset", "New SCurve Preset", "asset",
				"Choose where to save this curve as a reusable, project-wide preset.");
			if (string.IsNullOrEmpty(path)) return;

			var asset = ScriptableObject.CreateInstance<SCurvePresetAsset>();
			asset.curve = CloneCurve(current);
			AssetDatabase.CreateAsset(asset, path);
			AssetDatabase.SaveAssets();

			RebuildDropdown();
			selectedIndex = guids.FindIndex(g => AssetDatabase.GUIDToAssetPath(g) == path);
			RebuildDropdown();
		}

		void ShowContextMenu() {
			var menu = new GenericMenu();
			bool hasSelection = selectedIndex >= 0 && selectedIndex < guids.Count;

			if (hasSelection) {
				menu.AddItem(new GUIContent("Overwrite With Current Curve"), false, OverwriteSelected);
				menu.AddItem(new GUIContent("Rename..."), false, RenameSelected);
				menu.AddItem(new GUIContent("Reveal In Project"), false, RevealSelected);
				menu.AddSeparator("");
				menu.AddItem(new GUIContent("Delete"), false, DeleteSelected);
			} else {
				menu.AddDisabledItem(new GUIContent("Select a preset first"));
			}
			menu.ShowAsContext();
		}

		void OverwriteSelected() {
			SCurvePresetAsset asset = LoadAt(selectedIndex);
			SCurve current = getCurrentCurve?.Invoke();
			if (asset == null || current == null || current.KeyCount == 0) return;
			Undo.RecordObject(asset, "Overwrite SCurve Preset");
			asset.curve = CloneCurve(current);
			EditorUtility.SetDirty(asset);
			AssetDatabase.SaveAssets();
		}

		void RenameSelected() {
			SCurvePresetAsset asset = LoadAt(selectedIndex);
			if (asset == null) return;
			string path = AssetDatabase.GetAssetPath(asset);
			SCurveRenamePresetPopup.Open(NameOf(guids[selectedIndex]), newName => {
				AssetDatabase.RenameAsset(path, newName);
				AssetDatabase.SaveAssets();
				RebuildDropdown();
			});
		}

		void RevealSelected() {
			SCurvePresetAsset asset = LoadAt(selectedIndex);
			if (asset == null) return;
			Selection.activeObject = asset;
			EditorGUIUtility.PingObject(asset);
		}

		void DeleteSelected() {
			SCurvePresetAsset asset = LoadAt(selectedIndex);
			if (asset == null) return;
			string path = AssetDatabase.GetAssetPath(asset);
			if (!EditorUtility.DisplayDialog("Delete SCurve Preset", $"Delete the preset \"{NameOf(guids[selectedIndex])}\"? This can't be undone.", "Delete", "Cancel")) return;
			AssetDatabase.DeleteAsset(path);
			selectedIndex = -1;
			RebuildDropdown();
		}

		static SCurve CloneCurve(SCurve source) => new(source.Keyframes) { PreWrapMode = source.PreWrapMode, PostWrapMode = source.PostWrapMode };

		static VisualElement MakeIconButton(string glyph, Action onClick) {
			var btn = new VisualElement {
				pickingMode = PickingMode.Position,
				style = {
					width = 24, height = 20, alignItems = Align.Center, justifyContent = Justify.Center,
					backgroundColor = SperlichEditorTheme.ButtonBg,
					borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
				},
			};
			SperlichEditorWidgets.SetBorderColor(btn, SperlichEditorTheme.ButtonBorder);
			SperlichEditorWidgets.SetRadius(btn, 3);
			SperlichEditorWidgets.SetHoverCursor(btn, MouseCursor.Link);
			btn.Add(new Label(glyph) { pickingMode = PickingMode.Ignore, style = { fontSize = 10 } });
			btn.RegisterCallback<ClickEvent>(_ => onClick());
			return btn;
		}
	}
}
