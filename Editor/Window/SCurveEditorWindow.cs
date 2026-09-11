using System;
using Sperlich.EditorKit;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sperlich.Easing.Editor {

	/// <summary>SCurve editor -- Design B ("Canvas-First"): no permanent sidebar, the canvas takes the full
	/// window and a floating <see cref="SCurveSelectionPanel"/> only appears over it while something is
	/// selected. Opens as a genuine, movable/resizable utility window positioned near the Inspector preview
	/// that triggered it (<see cref="Open"/>) -- not a <c>ShowAsDropDown</c> popup, which can't be dragged and
	/// auto-closes on the slightest focus loss.</summary>
	class SCurveEditorWindow : EditorWindow {
		SerializedProperty targetProperty;
		Action onExternalRefresh;

		SCurveCanvasView canvas;
		SCurveSelectionPanel selectionPanel;
		SCurvePresetBar presetBar;
		SCurveCustomPresetBar customPresetBar;
		SCurvePlayhead playhead;
		SCurveBallPreviewPanel ballPreviewPanel;
		bool ballPreviewVisible;
		VisualElement snapBtn;
		VisualElement ballBtn;
		Label presetMatchLabel;
		Action refreshDurationField;
		Action refreshAmplitudeField;

		public static void Open(SerializedProperty property, VisualElement anchor, Action onChanged) {
			var win = CreateInstance<SCurveEditorWindow>();
			win.titleContent = new GUIContent("SCurve Editor");
			win.minSize = new Vector2(420, 280);

			Rect worldBound = anchor.worldBound;
			Vector2 screenPos = GUIUtility.GUIToScreenPoint(new Vector2(worldBound.x, worldBound.yMax + 4f));
			win.position = new Rect(screenPos, new Vector2(560, 400));

			win.Initialize(property, onChanged);
			win.ShowUtility();
		}

		void Initialize(SerializedProperty property, Action onChanged) {
			targetProperty = property;
			onExternalRefresh = onChanged;
			BuildLayout();

			SCurve curve = targetProperty.boxedValue as SCurve;
			if (curve == null || curve.KeyCount == 0) {
				curve = new SCurve(new[] {
					new SKeyframe(0f, 0f, 1f, 1f, HandleType.Aligned),
					new SKeyframe(1f, 1f, 1f, 1f, HandleType.Aligned),
				});
				targetProperty.boxedValue = curve;
				targetProperty.serializedObject.ApplyModifiedProperties();
			}
			canvas.SetCurve(curve);
			selectionPanel.Rebuild(canvas.Curve, canvas.Selection);
			RefreshPresetMatchLabel();
			refreshDurationField?.Invoke();
			refreshAmplitudeField?.Invoke();
			RefreshPlayheadRange();

			Undo.undoRedoPerformed += RefreshFromProperty;
		}

		void BuildLayout() {
			VisualElement root = rootVisualElement;
			root.style.backgroundColor = SperlichEditorTheme.BgDark;

			var toolbar = new VisualElement {
				style = {
					flexDirection = UnityEngine.UIElements.FlexDirection.Row, alignItems = Align.Center,
					paddingLeft = 10, paddingRight = 10, paddingTop = 5, paddingBottom = 5,
					backgroundColor = SperlichEditorTheme.BgPanel,
					borderBottomWidth = 1,
				},
			};
			SperlichEditorWidgets.SetBorderColor(toolbar, SperlichEditorTheme.BorderSubtle);

			presetBar = new SCurvePresetBar();
			presetBar.OnApply += generated => canvas.ApplyGeneratedCurve(generated);
			toolbar.Add(presetBar);

			customPresetBar = new SCurveCustomPresetBar(() => canvas.Curve);
			customPresetBar.OnApply += generated => canvas.ApplyGeneratedCurve(generated);
			customPresetBar.style.marginLeft = 8;
			toolbar.Add(customPresetBar);

			(VisualElement durationField, Action syncDuration) = SperlichEditorWidgets.MakeVirtualDragNumber(
				"Duration", () => Round3(canvas?.CurrentDuration ?? 1f), v => canvas?.RescaleDuration(Round3(v)), 0.01f, "s");
			refreshDurationField = syncDuration;
			durationField.tooltip = "Total curve duration -- stretches/compresses every keyframe's time (and tangents) so the whole curve fits this span instead of just 0-1s.";
			SetFixedFieldWidth(durationField, 42);
			durationField.style.marginLeft = 8;
			toolbar.Add(durationField);

			(VisualElement amplitudeField, Action syncAmplitude) = SperlichEditorWidgets.MakeVirtualDragNumber(
				"Amplitude", () => Round3(canvas?.CurrentAmplitude ?? 1f), v => canvas?.RescaleAmplitude(Round3(v)), 0.01f, null);
			refreshAmplitudeField = syncAmplitude;
			amplitudeField.tooltip = "Total curve value span -- scales every keyframe's value (and tangents) so the whole curve's height fits this instead of just 0-1.";
			SetFixedFieldWidth(amplitudeField, 42);
			amplitudeField.style.marginLeft = 8;
			toolbar.Add(amplitudeField);

			toolbar.Add(new VisualElement { style = { flexGrow = 1 } });

			// Read-only readout of what the CURRENT curve actually matches -- separate from the preset dropdowns
			// above, which only pick a NEW preset to apply. Without this there was no feedback that hand-editing
			// a preset's keyframes silently detaches it from that preset; this makes "Custom" visible the moment
			// it happens.
			presetMatchLabel = new Label {
				style = { fontSize = 10, color = SperlichEditorTheme.TextFaint, marginRight = 10, unityTextAlign = TextAnchor.MiddleRight },
			};
			presetMatchLabel.tooltip = "Which built-in preset the current curve matches -- \"Custom\" once you edit it away from that shape.";
			toolbar.Add(presetMatchLabel);

			ballBtn = MakeToolbarIcon("●", ToggleBallPreview);
			ballBtn.tooltip = "Toggle bouncing-ball preview";
			toolbar.Add(ballBtn);

			VisualElement frameAllBtn = MakeToolbarIcon("⌂", () => canvas.FrameSelectionOrAll());
			frameAllBtn.tooltip = "Frame All / Selection (Home)";
			toolbar.Add(frameAllBtn);

			snapBtn = MakeToolbarIcon("▦", () => { canvas.SnapEnabled = !canvas.SnapEnabled; RefreshSnapButton(); });
			snapBtn.tooltip = "Snap to grid while dragging keyframes";
			toolbar.Add(snapBtn);

			root.Add(toolbar);

			var canvasHost = new VisualElement { style = { flexGrow = 1, position = Position.Relative } };
			canvas = new SCurveCanvasView();
			canvas.OnBeginEdit += HandleBeginEdit;
			canvas.OnCurveMutated += HandleCurveMutated;
			canvas.OnSelectionChanged += () => selectionPanel.Rebuild(canvas.Curve, canvas.Selection);
			canvasHost.Add(canvas);

			selectionPanel = new SCurveSelectionPanel();
			selectionPanel.OnKeyframeEdited += (index, newVal) => canvas.SetKeyframeFull(index, newVal);
			selectionPanel.OnDeltaApplied += (dt, dv) => canvas.ApplySelectionDelta(dt, dv);
			selectionPanel.OnHandleTypeChanged += (index, type) => canvas.SetHandleType(index, type);
			canvas.Add(selectionPanel);

			ballPreviewPanel = new SCurveBallPreviewPanel { style = { display = DisplayStyle.None } };
			canvas.Add(ballPreviewPanel);

			root.Add(canvasHost);

			playhead = new SCurvePlayhead();
			playhead.OnScrub += t => {
				canvas.PlayheadTime = t;
				canvas.ShowPlayhead = true;
				canvas.MarkDirtyRepaint();
				if (ballPreviewVisible) ballPreviewPanel.SetState(canvas.Curve, t);
			};
			root.Add(playhead);

			RefreshSnapButton();
		}

		static float Round3(float v) => Mathf.Round(v * 1000f) / 1000f;

		/// <summary>Pins a MakeVirtualDragNumber field's actual number INPUT to a fixed width, so it can't grow
		/// wider as more digits show up (its FloatField defaults to flexGrow:1, minWidth:0). Fixing the width of
		/// the whole returned wrap instead (which also contains the caption label and grip) squeezed the caption
		/// text itself and made it overflow into whatever sat next to it in the toolbar -- this only constrains
		/// the number box, leaving the caption/grip/suffix at their natural size.</summary>
		static void SetFixedFieldWidth(VisualElement wrap, float inputWidth) {
			wrap.style.flexShrink = 0;
			var floatField = wrap.Q<FloatField>();
			if (floatField == null) return;
			floatField.style.width = inputWidth;
			floatField.style.minWidth = inputWidth;
			floatField.style.flexGrow = 0;
			floatField.style.flexShrink = 0;
		}

		VisualElement MakeToolbarIcon(string glyph, Action onClick) {
			var btn = new VisualElement {
				pickingMode = PickingMode.Position,
				style = {
					width = 24, height = 24, marginLeft = 4, alignItems = Align.Center, justifyContent = Justify.Center,
					backgroundColor = SperlichEditorTheme.ButtonBg,
					borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
				},
			};
			SperlichEditorWidgets.SetBorderColor(btn, SperlichEditorTheme.ButtonBorder);
			SperlichEditorWidgets.SetRadius(btn, 4);
			SperlichEditorWidgets.SetHoverCursor(btn, MouseCursor.Link);
			btn.Add(new Label(glyph) { pickingMode = PickingMode.Ignore, style = { fontSize = 11, color = SperlichEditorTheme.TextSecondary } });
			btn.RegisterCallback<ClickEvent>(_ => onClick());
			return btn;
		}

		void RefreshSnapButton() {
			bool on = canvas.SnapEnabled;
			SperlichEditorWidgets.SetBorderColor(snapBtn, on ? SperlichEditorTheme.ButtonAccent : SperlichEditorTheme.ButtonBorder);
			// Solid accent fill when on (not just a faint tint) so the toggle reads as a clear on/off state.
			snapBtn.style.backgroundColor = on ? SperlichEditorTheme.ButtonAccent : SperlichEditorTheme.ButtonBg;
			var glyph = snapBtn.Q<Label>();
			if (glyph != null) glyph.style.color = on ? SperlichEditorTheme.BgDark : SperlichEditorTheme.TextSecondary;
		}

		void HandleBeginEdit(string label) {
			if (targetProperty?.serializedObject?.targetObject != null) {
				Undo.RecordObject(targetProperty.serializedObject.targetObject, label);
			}
		}

		void HandleCurveMutated() {
			PushCurveToProperty();
			selectionPanel.SyncAll();
			RefreshPresetMatchLabel();
			refreshDurationField?.Invoke();
			refreshAmplitudeField?.Invoke();
			RefreshPlayheadRange();
			if (ballPreviewVisible) ballPreviewPanel.SetState(canvas.Curve, canvas.PlayheadTime);
		}

		/// <summary>Keeps the playhead scrubber/play range matched to the curve's actual time span instead of a
		/// fixed 0-1 -- otherwise scrubbing/playing silently stopped at t=1 even once the curve was stretched
		/// past 1s (Duration field) or simply had a last keyframe beyond t=1.</summary>
		void RefreshPlayheadRange() {
			if (playhead == null) return;
			bool hasRange = canvas.Curve != null && canvas.Curve.KeyCount >= 2;
			playhead.MinTime = hasRange ? canvas.Curve.MinTime : 0f;
			playhead.MaxTime = hasRange ? canvas.Curve.MaxTime : 1f;
			playhead.SetTime(canvas.PlayheadTime);
		}

		void RefreshPresetMatchLabel() {
			if (presetMatchLabel == null) return;
			string match = BuiltInPresetMatcher.FindMatch(canvas.Curve);
			presetMatchLabel.text = match ?? "Custom";
		}

		void ToggleBallPreview() {
			ballPreviewVisible = !ballPreviewVisible;
			ballPreviewPanel.style.display = ballPreviewVisible ? DisplayStyle.Flex : DisplayStyle.None;
			if (ballPreviewVisible) ballPreviewPanel.SetState(canvas.Curve, canvas.PlayheadTime);
			RefreshBallButton();
		}

		void RefreshBallButton() {
			SperlichEditorWidgets.SetBorderColor(ballBtn, ballPreviewVisible ? SperlichEditorTheme.ButtonAccent : SperlichEditorTheme.ButtonBorder);
			ballBtn.style.backgroundColor = ballPreviewVisible ? SperlichEditorTheme.ButtonAccent : SperlichEditorTheme.ButtonBg;
		}

		void PushCurveToProperty() {
			if (targetProperty?.serializedObject == null) return;
			targetProperty.boxedValue = canvas.Curve;
			targetProperty.serializedObject.ApplyModifiedProperties();
			onExternalRefresh?.Invoke();
		}

		void RefreshFromProperty() {
			if (targetProperty?.serializedObject == null) return;
			targetProperty.serializedObject.UpdateIfRequiredOrScript();
			canvas.SetCurve(targetProperty.boxedValue as SCurve ?? new SCurve());
			selectionPanel.Rebuild(canvas.Curve, canvas.Selection);
			RefreshPresetMatchLabel();
			refreshDurationField?.Invoke();
			refreshAmplitudeField?.Invoke();
			RefreshPlayheadRange();
			if (ballPreviewVisible) ballPreviewPanel.SetState(canvas.Curve, canvas.PlayheadTime);
		}

		void OnDisable() {
			Undo.undoRedoPerformed -= RefreshFromProperty;
			playhead?.Stop();
		}
	}
}
