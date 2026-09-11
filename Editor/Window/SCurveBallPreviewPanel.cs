using Sperlich.EditorKit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sperlich.Easing.Editor {

	/// <summary>Small overlay card, embedded directly in the canvas (top-left corner) -- shows a ball riding the
	/// curve's value (0 = bottom, 1 = top, same convention as the grid's 0/1 baselines) at the current playhead
	/// time. Gives a felt sense of how a curve moves that's hard to read off the graph alone. Toggled from the
	/// toolbar; lives inside the editor window rather than as a separate OS window so it can't get lost behind it.</summary>
	class SCurveBallPreviewPanel : VisualElement {
		SCurve curve;
		float time;
		readonly VisualElement track;
		readonly VisualElement ball;
		const float TrackPadding = 14f;
		const float BallSize = 14f;

		public SCurveBallPreviewPanel() {
			pickingMode = PickingMode.Position; // swallow pointer events -- sits on top of the canvas, must not fall through into box-select
			style.position = Position.Absolute;
			style.left = 8; style.top = 8;
			style.width = 56; style.height = 140;
			style.backgroundColor = SperlichEditorTheme.BgPanel;
			style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = 1;
			SperlichEditorWidgets.SetBorderColor(this, SperlichEditorTheme.BorderSubtle);
			SperlichEditorWidgets.SetRadius(this, 4);
			RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());

			track = new VisualElement {
				pickingMode = PickingMode.Ignore,
				style = { position = Position.Absolute, top = TrackPadding, bottom = TrackPadding, width = 2, backgroundColor = new Color(1f, 1f, 1f, 0.14f) },
			};
			Add(track);

			ball = new VisualElement {
				pickingMode = PickingMode.Ignore,
				style = { position = Position.Absolute, width = BallSize, height = BallSize, backgroundColor = SperlichEditorTheme.ButtonAccent },
			};
			SperlichEditorWidgets.SetRadius(ball, BallSize / 2f);
			Add(ball);

			RegisterCallback<GeometryChangedEvent>(_ => UpdateBallPosition());
		}

		public void SetState(SCurve newCurve, float newTime) {
			curve = newCurve;
			time = newTime;
			UpdateBallPosition();
		}

		void UpdateBallPosition() {
			if (curve == null || curve.KeyCount == 0) { ball.style.display = DisplayStyle.None; return; }
			ball.style.display = DisplayStyle.Flex;

			float minV = Mathf.Min(curve.MinValue, 0f);
			float maxV = Mathf.Max(curve.MaxValue, 1f);
			if (maxV <= minV) maxV = minV + 1f;

			float v = curve.Evaluate(time);
			float t01 = Mathf.InverseLerp(minV, maxV, v);

			float w = resolvedStyle.width, h = resolvedStyle.height;
			if (w <= 0f || h <= 0f) return;
			float trackHeight = Mathf.Max(1f, h - TrackPadding * 2f);
			// 1 (or above) at the TOP, 0 (or below) at the BOTTOM -- the ball FALLS as the curve value rises
			// towards 1, matching gravity (a "drop" feels more intuitive than a "jump" for reading ease-out).
			float y = TrackPadding + t01 * trackHeight - BallSize / 2f;
			float centerX = w / 2f;
			ball.style.left = centerX - BallSize / 2f;
			ball.style.top = y;
			track.style.left = centerX - 1f;
		}
	}
}
