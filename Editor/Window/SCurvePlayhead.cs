using System;
using Sperlich.EditorKit;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sperlich.Easing.Editor {

	/// <summary>Slim playhead strip: play/pause + a draggable scrub bar (same pointer-capture pattern as
	/// <see cref="SperlichEditorWidgets.CreateDraggableBar"/>, just get/set-based instead of SerializedProperty-based)
	/// + a mono time readout. Driven by <see cref="EditorApplication.update"/> while playing --
	/// <see cref="Stop"/> MUST be called when the owning window closes, or the callback leaks past the popup's
	/// lifetime.</summary>
	class SCurvePlayhead : VisualElement {
		public Action<float> OnScrub;
		public float MinTime = 0f, MaxTime = 1f;

		readonly VisualElement fill;
		readonly Label timeLabel;
		readonly Label playGlyph;
		bool isPlaying;
		double lastEditorTime;
		float time;
		bool stopped;

		public SCurvePlayhead() {
			style.flexDirection = UnityEngine.UIElements.FlexDirection.Row;
			style.alignItems = Align.Center;
			style.height = 22;
			style.paddingLeft = 8; style.paddingRight = 8;
			style.paddingTop = 3; style.paddingBottom = 3;

			(VisualElement playBtn, Label glyph) = MakeIconButton("▶", TogglePlay);
			playGlyph = glyph;
			Add(playBtn);

			var track = new VisualElement {
				pickingMode = PickingMode.Position,
				style = { height = 4, flexGrow = 1, marginLeft = 8, marginRight = 8, backgroundColor = new Color(0f, 0f, 0f, 0.35f), position = Position.Relative },
			};
			SperlichEditorWidgets.SetRadius(track, 2);
			fill = new VisualElement { style = { height = 4, backgroundColor = SperlichEditorTheme.ButtonAccent, position = Position.Absolute, left = 0, top = 0 } };
			SperlichEditorWidgets.SetRadius(fill, 2);
			track.Add(fill);
			Add(track);

			timeLabel = new Label("t = 0.00") { style = { fontSize = 10, color = SperlichEditorTheme.TextSecondary, width = 60, unityTextAlign = TextAnchor.MiddleRight } };
			Add(timeLabel);

			void SetFromLocalX(float localX) {
				float w = track.resolvedStyle.width;
				if (w <= 0f) return;
				isPlaying = false;
				RefreshPlayIcon();
				SetTime(Mathf.Lerp(MinTime, MaxTime, Mathf.Clamp01(localX / w)));
				OnScrub?.Invoke(time);
			}

			bool dragging = false;
			track.RegisterCallback<PointerDownEvent>(evt => { dragging = true; track.CapturePointer(evt.pointerId); SetFromLocalX(evt.localPosition.x); });
			track.RegisterCallback<PointerMoveEvent>(evt => { if (dragging) SetFromLocalX(evt.localPosition.x); });
			track.RegisterCallback<PointerUpEvent>(evt => { if (dragging) { dragging = false; track.ReleasePointer(evt.pointerId); } });

			EditorApplication.update += Tick;
		}

		public void Stop() {
			if (stopped) return;
			stopped = true;
			EditorApplication.update -= Tick;
		}

		public void SetTime(float t) {
			time = Mathf.Clamp(t, MinTime, MaxTime);
			float t01 = MaxTime > MinTime ? Mathf.InverseLerp(MinTime, MaxTime, time) : 0f;
			fill.style.width = Length.Percent(t01 * 100f);
			timeLabel.text = $"t = {time:0.00}";
		}

		void TogglePlay() {
			isPlaying = !isPlaying;
			lastEditorTime = EditorApplication.timeSinceStartup;
			RefreshPlayIcon();
		}

		void RefreshPlayIcon() => playGlyph.text = isPlaying ? "⏸" : "▶";

		void Tick() {
			if (!isPlaying) return;
			double now = EditorApplication.timeSinceStartup;
			float dt = (float)(now - lastEditorTime);
			lastEditorTime = now;
			float span = Mathf.Max(1e-3f, MaxTime - MinTime);
			float next = time + dt;
			if (next > MaxTime) next = MinTime + (next - MaxTime) % span;
			SetTime(next);
			OnScrub?.Invoke(time);
		}

		static (VisualElement, Label) MakeIconButton(string glyph, Action onClick) {
			var btn = new VisualElement {
				pickingMode = PickingMode.Position,
				style = {
					width = 20, height = 20, alignItems = Align.Center, justifyContent = Justify.Center,
					backgroundColor = SperlichEditorTheme.ButtonBg,
					borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
				},
			};
			SperlichEditorWidgets.SetBorderColor(btn, SperlichEditorTheme.ButtonBorder);
			SperlichEditorWidgets.SetRadius(btn, 3);
			SperlichEditorWidgets.SetHoverCursor(btn, MouseCursor.Link);
			var label = new Label(glyph) { pickingMode = PickingMode.Ignore, style = { fontSize = 10, color = SperlichEditorTheme.TextSecondary } };
			btn.Add(label);
			btn.RegisterCallback<ClickEvent>(_ => onClick());
			return (btn, label);
		}
	}
}
