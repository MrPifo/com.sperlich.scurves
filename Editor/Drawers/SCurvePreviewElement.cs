using System;
using Sperlich.EditorKit;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sperlich.Easing.Editor {

	/// <summary>Compact preview of an <see cref="SCurve"/>: curve line + keyframe points, drawn via
	/// <c>generateVisualContent</c>/<see cref="Painter2D"/> (no IMGUIContainer). Hover shows the matching
	/// built-in preset name if any; click opens the SCurve editor.</summary>
	public class SCurvePreviewElement : VisualElement {
		const float Padding = 6f;
		static readonly Color FieldBg = new(0.078f, 0.082f, 0.102f);

		SCurve curve;
		public Action OnClicked;

		public SCurvePreviewElement() {
			style.height = 20;
			style.flexGrow = 1;
			style.backgroundColor = FieldBg;
			style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = 1;
			SperlichEditorWidgets.SetBorderColor(this, SperlichEditorTheme.BorderSubtle);
			SperlichEditorWidgets.SetRadius(this, 4);
			SperlichEditorWidgets.SetHoverCursor(this, MouseCursor.Link);
			SperlichEditorWidgets.ApplyColorTransition(this, 100, "border-color");
			pickingMode = PickingMode.Position;

			RegisterCallback<MouseEnterEvent>(_ => SperlichEditorWidgets.SetBorderColor(this, SperlichEditorTheme.ButtonAccent));
			RegisterCallback<MouseLeaveEvent>(_ => SperlichEditorWidgets.SetBorderColor(this, SperlichEditorTheme.BorderSubtle));
			RegisterCallback<ClickEvent>(evt => { evt.StopPropagation(); OnClicked?.Invoke(); });

			generateVisualContent += OnGenerateVisualContent;
		}

		public void SetCurve(SCurve value) {
			curve = value;
			tooltip = curve == null || curve.KeyCount == 0 ? null : BuiltInPresetMatcher.FindMatch(curve) ?? "Custom";
			MarkDirtyRepaint();
		}

		void OnGenerateVisualContent(MeshGenerationContext mgc) {
			if (curve == null || curve.KeyCount == 0) return;
			Rect r = contentRect;
			float w = r.width, h = r.height;
			if (w <= 0f || h <= 0f || float.IsNaN(w) || float.IsNaN(h)) return;

			float minTime = curve.MinTime, maxTime = curve.MaxTime;
			float minValue = Mathf.Min(curve.MinValue, 0f);
			float maxValue = Mathf.Max(curve.MaxValue, 1f);
			if (maxTime <= minTime) maxTime = minTime + 1f;
			if (maxValue <= minValue) maxValue = minValue + 1f;

			Vector2 ToLocal(float t, float v) => new(
				Padding + (t - minTime) / (maxTime - minTime) * (w - Padding * 2f),
				Padding + (1f - (v - minValue) / (maxValue - minValue)) * (h - Padding * 2f));

			Painter2D p = mgc.painter2D;
			p.lineWidth = 2f;
			p.strokeColor = SperlichEditorTheme.ButtonAccent;
			p.BeginPath();
			const int samples = 48;
			for (int i = 0; i <= samples; i++) {
				float t = minTime + (maxTime - minTime) * i / samples;
				Vector2 pt = ToLocal(t, curve.Evaluate(t));
				if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
			}
			p.Stroke();

			p.fillColor = SperlichEditorTheme.TextSecondary;
			foreach (SKeyframe key in curve.Keyframes) {
				Vector2 pt = ToLocal(key.time, key.value);
				p.BeginPath();
				p.Arc(pt, 2.5f, 0f, 360f);
				p.Fill();
			}
		}
	}
}
