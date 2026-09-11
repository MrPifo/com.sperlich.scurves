using System;
using System.Collections.Generic;
using System.Linq;
using Sperlich.EditorKit;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sperlich.Easing.Editor {

	/// <summary>Interactive curve canvas (Design B, canvas-first): grid/curve/handle rendering via
	/// <c>generateVisualContent</c>/<see cref="Painter2D"/>, zoom/pan, click/box-select, keyframe + tangent-handle
	/// dragging. Rendering and hit-testing both go through <see cref="CurveToScreen"/>/<see cref="ScreenToCurve"/>
	/// so they never drift apart. Doesn't know about SerializedProperty/Undo at all -- <see cref="SCurveEditorWindow"/>
	/// wires both via <see cref="OnBeginEdit"/>/<see cref="OnCurveMutated"/>.</summary>
	class SCurveCanvasView : VisualElement {
		public SCurve Curve { get; private set; }
		public HashSet<int> Selection { get; } = new();
		public bool SnapEnabled;
		public float PlayheadTime;
		public bool ShowPlayhead;

		public event Action<string> OnBeginEdit;
		public event Action OnCurveMutated;
		public event Action OnSelectionChanged;

		Rect viewRect = new(0f, 0f, 1f, 1f);
		readonly Label rangeLabel;

		enum DragMode { None, Keyframe, InTangent, OutTangent, Pan, BoxSelect }
		DragMode dragMode = DragMode.None;
		Vector2 dragStartScreen;
		Vector2 dragStartCurve;
		Rect panStartViewRect;
		List<SKeyframe> dragSnapshotValues;
		List<int> currentDragIndices;
		int dragHandleIndex = -1;
		Vector2 boxSelectCurrent;
		bool boxSelectAdd, boxSelectSubtract;

		const float HandleHitRadius = 9f;
		const float KeyHitRadius = 7f;
		const float DoubleClickSeconds = 0.3f;
		double lastClickTime = -1;

		public SCurveCanvasView() {
			style.flexGrow = 1;
			// Without this, a curve sample that overshoots the currently framed value range draws in NEGATIVE
			// local Y (above the canvas) and, since UI Toolkit doesn't clip content to an element's bounds by
			// default, bleeds straight through into whatever sits above the canvas in the layout -- the toolbar.
			style.overflow = Overflow.Hidden;
			focusable = true;
			pickingMode = PickingMode.Position;
			generateVisualContent += OnGenerateVisualContent;

			RegisterCallback<PointerDownEvent>(OnPointerDown);
			RegisterCallback<PointerMoveEvent>(OnPointerMove);
			RegisterCallback<PointerUpEvent>(OnPointerUp);
			RegisterCallback<WheelEvent>(OnWheel);
			RegisterCallback<KeyDownEvent>(OnKeyDown);
			RegisterCallback<ContextClickEvent>(OnContextClick);
			RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());

			rangeLabel = new Label {
				pickingMode = PickingMode.Ignore,
				style = {
					position = Position.Absolute, right = 6, bottom = 4, fontSize = 9,
					color = SperlichEditorTheme.TextFaint, unityTextAlign = TextAnchor.MiddleRight,
				},
			};
			Add(rangeLabel);
		}

		public void SetCurve(SCurve curve) {
			Curve = curve;
			Selection.Clear();
			FrameAll();
			OnSelectionChanged?.Invoke();
		}

		/// <summary>The curve's current total time span -- what the toolbar's Duration field shows/edits.</summary>
		public float CurrentDuration => Curve == null || Curve.KeyCount < 2 ? 1f : Mathf.Max(1e-4f, Curve.MaxTime - Curve.MinTime);

		/// <summary>Stretches/compresses every keyframe's time (and tangents, so the VALUE shape is preserved --
		/// only the time axis changes) so the curve spans <paramref name="newDuration"/> instead of its current
		/// span. Weighted tangents don't need touching -- their weight is already stored as a fraction of the
		/// distance to the neighbor keyframe, which scales identically on both sides.</summary>
		public void RescaleDuration(float newDuration) {
			if (Curve == null || Curve.KeyCount < 2) return;
			newDuration = Mathf.Max(1e-3f, newDuration);
			RaiseBeginEdit("Rescale Duration");
			ApplyTimeScale(newDuration);
			RaiseMutated();
		}

		void ApplyTimeScale(float newDuration) {
			float oldMin = Curve.MinTime, oldMax = Curve.MaxTime;
			float oldSpan = oldMax - oldMin;
			if (oldSpan <= 1e-6f) return;
			float scale = newDuration / oldSpan;
			if (Mathf.Approximately(scale, 1f)) return;

			var rescaled = new List<SKeyframe>(Curve.KeyCount);
			foreach (SKeyframe k in Curve.Keyframes) {
				SKeyframe nk = k;
				nk.time = oldMin + (k.time - oldMin) * scale;
				nk.inTangent = k.inTangent / scale;
				nk.outTangent = k.outTangent / scale;
				rescaled.Add(nk);
			}
			Curve = new SCurve(rescaled) { PreWrapMode = Curve.PreWrapMode, PostWrapMode = Curve.PostWrapMode };
		}

		/// <summary>The curve's current total value span -- what the toolbar's Amplitude field shows/edits. Same
		/// idea as <see cref="CurrentDuration"/>, just along the value axis instead of the time axis.</summary>
		public float CurrentAmplitude => Curve == null || Curve.KeyCount < 2 ? 1f : Mathf.Max(1e-4f, Curve.MaxValue - Curve.MinValue);

		/// <summary>Scales every keyframe's value (and tangents, inversely to <see cref="ApplyTimeScale"/> since
		/// the time axis is untouched here) so the curve's value span becomes <paramref name="newAmplitude"/>
		/// instead of its current span. Anchored at the curve's current minimum value, mirroring how
		/// <see cref="ApplyTimeScale"/> anchors at the curve's minimum time.</summary>
		public void RescaleAmplitude(float newAmplitude) {
			if (Curve == null || Curve.KeyCount < 2) return;
			newAmplitude = Mathf.Max(1e-3f, newAmplitude);
			RaiseBeginEdit("Rescale Amplitude");
			ApplyValueScale(newAmplitude);
			RaiseMutated();
		}

		void ApplyValueScale(float newAmplitude) {
			float oldMin = Curve.MinValue, oldMax = Curve.MaxValue;
			float oldSpan = oldMax - oldMin;
			if (oldSpan <= 1e-6f) return;
			float scale = newAmplitude / oldSpan;
			if (Mathf.Approximately(scale, 1f)) return;

			var rescaled = new List<SKeyframe>(Curve.KeyCount);
			foreach (SKeyframe k in Curve.Keyframes) {
				SKeyframe nk = k;
				nk.value = oldMin + (k.value - oldMin) * scale;
				nk.inTangent = k.inTangent * scale;
				nk.outTangent = k.outTangent * scale;
				rescaled.Add(nk);
			}
			Curve = new SCurve(rescaled) { PreWrapMode = Curve.PreWrapMode, PostWrapMode = Curve.PostWrapMode };
		}

		// ---------------- coordinate transform ----------------

		Vector2 CurveToScreen(float t, float v) {
			Rect r = contentRect;
			float x = (t - viewRect.xMin) / Mathf.Max(1e-6f, viewRect.width) * r.width;
			float y = (1f - (v - viewRect.yMin) / Mathf.Max(1e-6f, viewRect.height)) * r.height;
			return new Vector2(x, y);
		}

		Vector2 ScreenToCurve(Vector2 screen) {
			Rect r = contentRect;
			float t = viewRect.xMin + screen.x / Mathf.Max(1f, r.width) * viewRect.width;
			float v = viewRect.yMin + (1f - screen.y / Mathf.Max(1f, r.height)) * viewRect.height;
			return new Vector2(t, v);
		}

		const float HandlePixelLength = 42f;

		// ---------------- framing ----------------

		public void FrameAll() {
			if (Curve == null || Curve.KeyCount == 0) { viewRect = new Rect(-0.1f, -0.1f, 1.2f, 1.2f); MarkDirtyRepaint(); return; }
			ApplyFrame(Curve.MinTime, Curve.MaxTime, Curve.MinValue, Curve.MaxValue);
		}

		public void FrameSelectionOrAll() {
			if (Selection.Count == 0 || Curve == null) { FrameAll(); return; }
			float minT = float.MaxValue, maxT = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
			foreach (int i in Selection) {
				SKeyframe k = Curve[i];
				minT = Mathf.Min(minT, k.time); maxT = Mathf.Max(maxT, k.time);
				minV = Mathf.Min(minV, k.value); maxV = Mathf.Max(maxV, k.value);
			}
			// A single selected keyframe has a zero-size bounding box -- framing "just that point" would zoom in
			// to a razor-thin view showing nothing else on the curve. Fall back to the whole curve instead, same
			// as an empty selection.
			bool degenerate = Mathf.Approximately(minT, maxT) && Mathf.Approximately(minV, maxV);
			if (degenerate) { FrameAll(); return; }
			ApplyFrame(minT, maxT, minV, maxV);
		}

		void ApplyFrame(float minT, float maxT, float minV, float maxV) {
			float padT = Mathf.Max(0.05f, (maxT - minT) * 0.15f);
			float padV = Mathf.Max(0.05f, (maxV - minV) * 0.2f);
			viewRect = new Rect(minT - padT, minV - padV, Mathf.Max(1e-3f, maxT - minT) + padT * 2f, Mathf.Max(1e-3f, maxV - minV) + padV * 2f);
			RefreshRangeLabel();
			MarkDirtyRepaint();
		}

		/// <summary>Updates the corner range readout. Deliberately NOT called from OnGenerateVisualContent --
		/// changing a child Label's text during the parent's own generateVisualContent invalidates that child's
		/// layout, which re-fires GeometryChangedEvent -> MarkDirtyRepaint -> another generateVisualContent call,
		/// an infinite repaint loop that showed up as the curve line flickering/vanishing. Only call this from
		/// the handful of places that actually change viewRect (framing, pan, zoom).</summary>
		void RefreshRangeLabel() {
			rangeLabel.text = $"t {viewRect.xMin:0.00}-{viewRect.xMax:0.00}   v {viewRect.yMin:0.00}-{viewRect.yMax:0.00}";
		}

		// ---------------- pointer input ----------------

		void OnPointerDown(PointerDownEvent evt) {
			Focus();
			if (Curve == null) return;
			Vector2 local = evt.localPosition;
			dragStartScreen = local;

			if (evt.button == 2) {
				dragMode = DragMode.Pan;
				panStartViewRect = viewRect;
				dragStartCurve = ScreenToCurve(local);
				this.CapturePointer(evt.pointerId);
				return;
			}
			if (evt.button != 0) return;

			// double-click empty space: insert a keyframe there
			double now = EditorApplicationTime();
			bool isDoubleClick = now - lastClickTime < DoubleClickSeconds;
			lastClickTime = now;

			if (TryHitTangentHandle(local, out int handleIndex, out bool isOut)) {
				dragMode = isOut ? DragMode.OutTangent : DragMode.InTangent;
				dragHandleIndex = handleIndex;
				RaiseBeginEdit("Edit Tangent");
				this.CapturePointer(evt.pointerId);
				return;
			}

			if (TryHitKeyframe(local, out int keyIndex)) {
				bool shift = evt.shiftKey;
				if (!shift && !Selection.Contains(keyIndex)) { Selection.Clear(); Selection.Add(keyIndex); RaiseSelectionChanged(); }
				else if (shift) { if (!Selection.Add(keyIndex)) Selection.Remove(keyIndex); RaiseSelectionChanged(); }
				else if (Selection.Count == 0) { Selection.Add(keyIndex); RaiseSelectionChanged(); }

				if (Selection.Contains(keyIndex)) {
					dragSnapshotValues = Selection.OrderBy(i => i).Select(i => Curve[i]).ToList();
					currentDragIndices = Selection.OrderBy(i => i).ToList();
					dragStartCurve = ScreenToCurve(local);
					dragMode = DragMode.Keyframe;
					RaiseBeginEdit("Move Keyframe");
					this.CapturePointer(evt.pointerId);
				}
				return;
			}

			if (isDoubleClick) {
				Vector2 cp = ScreenToCurve(local);
				RaiseBeginEdit("Add Keyframe");
				var key = new SKeyframe(cp.x, Curve.KeyCount > 0 ? Curve.Evaluate(cp.x) : cp.y, 0f, 0f, HandleType.Aligned);
				int idx = Curve.AddKey(key);
				// Aligned isn't touched by RecomputeNeighborhood (that's Auto/Vector-only), so without this a
				// freshly double-clicked keyframe would start with a flat 0 tangent instead of blending in.
				float neutralSlope = VectorSlope(Curve, idx);
				SKeyframe withTangent = Curve[idx];
				withTangent.inTangent = neutralSlope;
				withTangent.outTangent = neutralSlope;
				Curve.SetKeyframe(idx, withTangent);
				RecomputeNeighborhood(Curve, new[] { idx });
				Selection.Clear();
				Selection.Add(idx);
				RaiseSelectionChanged();
				RaiseMutated();
				return;
			}

			dragMode = DragMode.BoxSelect;
			boxSelectAdd = evt.shiftKey;
			boxSelectSubtract = evt.altKey;
			boxSelectCurrent = local;
			this.CapturePointer(evt.pointerId);
		}

		void OnPointerMove(PointerMoveEvent evt) {
			if (dragMode == DragMode.None || Curve == null) return;
			Vector2 local = evt.localPosition;

			switch (dragMode) {
				case DragMode.Pan: {
					// Keep the curve-space point that was under the cursor at pan-start (dragStartCurve, read
					// against panStartViewRect) under the CURRENT cursor position -- solved directly against the
					// snapshotted start state each move, so panning can't drift from rounding across many ticks.
					float width = Mathf.Max(1f, contentRect.width);
					float height = Mathf.Max(1f, contentRect.height);
					float newX = dragStartCurve.x - local.x / width * panStartViewRect.width;
					float newY = dragStartCurve.y - (1f - local.y / height) * panStartViewRect.height;
					viewRect = new Rect(newX, newY, panStartViewRect.width, panStartViewRect.height);
					RefreshRangeLabel();
					MarkDirtyRepaint();
					break;
				}
				case DragMode.Keyframe: {
					Vector2 cur = ScreenToCurve(local);
					float dt = cur.x - dragStartCurve.x;
					float dv = cur.y - dragStartCurve.y;

					// Snap the ABSOLUTE resulting position to whatever grid is currently drawn (see DrawGrid),
					// not a fixed increment from wherever the drag happened to start -- so keyframes actually land
					// exactly on the visible gridlines. Ctrl temporarily flips the persistent Snap toggle either
					// way. Shift is independent of the toggle entirely -- it always forces snapping on (at a 5x
					// finer step) regardless of whether the toolbar toggle or Ctrl already would have, so it works
					// as a standalone "fine snap" shortcut even with the toggle off and Ctrl not held.
					bool snapActive = (SnapEnabled ^ evt.ctrlKey) || evt.shiftKey;
					float stepT = 0f, stepV = 0f;
					if (snapActive) {
						float divisor = evt.shiftKey ? 5f : 1f;
						stepT = NiceStep(viewRect.width, 10) / divisor;
						stepV = NiceStep(viewRect.height, 6) / divisor;
					}

					foreach (int idx in currentDragIndices.OrderByDescending(i => i)) Curve.RemoveKey(idx);
					currentDragIndices.Clear();
					var newSelection = new HashSet<int>();
					foreach (SKeyframe snap in dragSnapshotValues) {
						SKeyframe moved = snap;
						float newTime = snap.time + dt;
						float newValue = snap.value + dv;
						if (snapActive) { newTime = Snap(newTime, stepT); newValue = Snap(newValue, stepV); }
						moved.time = newTime;
						moved.value = newValue;
						int newIndex = Curve.AddKey(moved);
						currentDragIndices.Add(newIndex);
						newSelection.Add(newIndex);
					}
					RecomputeNeighborhood(Curve, currentDragIndices);
					bool changed = !Selection.SetEquals(newSelection);
					Selection.Clear();
					foreach (int i in newSelection) Selection.Add(i);
					if (changed) RaiseSelectionChanged();
					RaiseMutated();
					break;
				}
				case DragMode.InTangent:
				case DragMode.OutTangent: {
					if (dragHandleIndex < 0 || dragHandleIndex >= Curve.KeyCount) break;
					Vector2 cur = ScreenToCurve(local);
					SKeyframe k = Curve[dragHandleIndex];
					bool isOut = dragMode == DragMode.OutTangent;
					float dt = isOut ? cur.x - k.time : k.time - cur.x;
					if (dt < 1e-4f) dt = 1e-4f;
					float slope = isOut ? (cur.y - k.value) / dt : (k.value - cur.y) / dt;

					if (k.handleType == HandleType.Aligned) { k.inTangent = slope; k.outTangent = slope; } else {
						k.handleType = HandleType.Free;
						if (isOut) k.outTangent = slope; else k.inTangent = slope;
					}

					// Handle scaling (weight) is opt-in via Shift -- a plain drag only rotates the handle and never
					// touches weight/weightedMode, so there's a mode where "just adjust the angle" is guaranteed
					// to not also resize the handle. Shift+drag: how far the handle was dragged from the keyframe,
					// as a fraction of the distance to that side's neighbor, becomes the weight -- "same as
					// Aligned, but you can also pull the handle out to weight one side more" (Unity's own
					// weighted-tangent model, already supported by SCurve.Evaluate -- this just exposes it).
					if (evt.shiftKey) {
						int neighborIndex = isOut ? dragHandleIndex + 1 : dragHandleIndex - 1;
						if (neighborIndex >= 0 && neighborIndex < Curve.KeyCount) {
							float dtRef = Mathf.Abs(Curve[neighborIndex].time - k.time);
							float weight = dtRef > 1e-6f ? Mathf.Clamp(dt / dtRef, 0.02f, 1f) : 1f / 3f;
							if (isOut) { k.outWeight = weight; k.weightedMode = WithWeightedSide(k.weightedMode, true, true); }
							else { k.inWeight = weight; k.weightedMode = WithWeightedSide(k.weightedMode, false, true); }
						}
					}

					Curve.SetKeyframe(dragHandleIndex, k);
					RaiseMutated();
					break;
				}
				case DragMode.BoxSelect: {
					boxSelectCurrent = local;
					MarkDirtyRepaint();
					break;
				}
			}
		}

		void OnPointerUp(PointerUpEvent evt) {
			if (dragMode == DragMode.BoxSelect) {
				Rect r = Rect.MinMaxRect(Mathf.Min(dragStartScreen.x, boxSelectCurrent.x), Mathf.Min(dragStartScreen.y, boxSelectCurrent.y),
					Mathf.Max(dragStartScreen.x, boxSelectCurrent.x), Mathf.Max(dragStartScreen.y, boxSelectCurrent.y));
				if (r.width > 2f || r.height > 2f) {
					var hits = new HashSet<int>();
					if (Curve != null) {
						for (int i = 0; i < Curve.KeyCount; i++) {
							if (r.Contains(CurveToScreen(Curve[i].time, Curve[i].value))) hits.Add(i);
						}
					}
					var result = new HashSet<int>(boxSelectAdd || boxSelectSubtract ? Selection : hits);
					if (boxSelectAdd) foreach (int i in hits) result.Add(i);
					else if (boxSelectSubtract) foreach (int i in hits) result.Remove(i);
					else result = hits;
					bool changed = !Selection.SetEquals(result);
					Selection.Clear();
					foreach (int i in result) Selection.Add(i);
					if (changed) RaiseSelectionChanged();
				}
			}
			dragMode = DragMode.None;
			dragHandleIndex = -1;
			if (this.HasPointerCapture(evt.pointerId)) this.ReleasePointer(evt.pointerId);
			MarkDirtyRepaint();
		}

		void OnWheel(WheelEvent evt) {
			Vector2 local = evt.localMousePosition;
			Vector2 before = ScreenToCurve(local);
			float zoom = Mathf.Pow(1.12f, evt.delta.y > 0 ? 1f : -1f);
			float newWidth = Mathf.Max(1e-3f, viewRect.width * zoom);
			float newHeight = Mathf.Max(1e-3f, viewRect.height * zoom);
			float relX = (before.x - viewRect.xMin) / viewRect.width;
			float relY = (before.y - viewRect.yMin) / viewRect.height;
			viewRect = new Rect(before.x - relX * newWidth, before.y - relY * newHeight, newWidth, newHeight);
			RefreshRangeLabel();
			MarkDirtyRepaint();
			evt.StopPropagation();
		}

		void OnKeyDown(KeyDownEvent evt) {
			switch (evt.keyCode) {
				case KeyCode.Home:
					FrameSelectionOrAll();
					evt.StopPropagation();
					break;
				case KeyCode.Delete:
				case KeyCode.Backspace:
					DeleteSelected();
					evt.StopPropagation();
					break;
				case KeyCode.C when evt.ctrlKey || evt.commandKey:
					CopySelected();
					evt.StopPropagation();
					break;
				case KeyCode.V when evt.ctrlKey || evt.commandKey:
					PasteAt(PlayheadTime);
					evt.StopPropagation();
					break;
			}
		}

		void OnContextClick(ContextClickEvent evt) {
			if (Curve == null || !TryHitKeyframe(evt.localMousePosition, out int index)) return;

			HandleType current = Curve[index].handleType;
			var menu = new GenericMenu();
			menu.AddItem(new GUIContent("Reset Handles"), false, () => ResetHandles(index));
			menu.AddSeparator("");
			foreach (HandleType type in new[] { HandleType.Free, HandleType.Vector, HandleType.Aligned, HandleType.Auto }) {
				HandleType captured = type;
				menu.AddItem(new GUIContent($"Handle Type/{type}"), current == captured, () => SetHandleType(index, captured));
			}
			menu.AddSeparator("");
			menu.AddItem(new GUIContent("Delete Keyframe"), false, () => DeleteKeyframeAt(index));
			menu.ShowAsContext();
			evt.StopPropagation();
		}

		// ---------------- editing operations (also used by the selection panel / toolbar) ----------------

		public void SetKeyframeFull(int index, SKeyframe newValue) {
			if (Curve == null || index < 0 || index >= Curve.KeyCount) return;
			RaiseBeginEdit("Edit Keyframe");
			int newIndex;
			if (Mathf.Approximately(Curve[index].time, newValue.time)) {
				Curve.SetKeyframe(index, newValue);
				newIndex = index;
			} else {
				newIndex = Curve.MoveKey(index, newValue);
			}
			RecomputeNeighborhood(Curve, new[] { newIndex });
			bool changed = !(Selection.Count == 1 && Selection.Contains(newIndex));
			Selection.Clear();
			Selection.Add(newIndex);
			if (changed) RaiseSelectionChanged();
			RaiseMutated();
		}

		public void SetHandleType(int index, HandleType type) {
			if (Curve == null || index < 0 || index >= Curve.KeyCount) return;
			RaiseBeginEdit("Set Handle Type");
			SKeyframe k = Curve[index];
			k.handleType = type;
			Curve.SetKeyframe(index, k);
			RecomputeNeighborhood(Curve, new[] { index });
			RaiseMutated();
		}

		public void ApplySelectionDelta(float dt, float dv) {
			if (Curve == null || Selection.Count == 0) return;
			RaiseBeginEdit("Nudge Keyframes");
			var moved = Selection.OrderBy(i => i).Select(i => Curve[i]).ToList();
			foreach (int idx in Selection.OrderByDescending(i => i)) Curve.RemoveKey(idx);
			var result = new HashSet<int>();
			foreach (SKeyframe snap in moved) {
				SKeyframe k = snap;
				k.time += dt; k.value += dv;
				result.Add(Curve.AddKey(k));
			}
			RecomputeNeighborhood(Curve, result);
			bool changed = !Selection.SetEquals(result);
			Selection.Clear();
			foreach (int i in result) Selection.Add(i);
			if (changed) RaiseSelectionChanged();
			RaiseMutated();
		}

		public void DeleteSelected() {
			if (Curve == null || Selection.Count == 0) return;
			RaiseBeginEdit("Delete Keyframes");
			foreach (int idx in Selection.OrderByDescending(i => i)) Curve.RemoveKey(idx);
			Selection.Clear();
			RecomputeAllDerivedTangents(Curve);
			RaiseSelectionChanged();
			RaiseMutated();
		}

		/// <summary>Deletes one specific keyframe regardless of the current selection (right-click context menu) --
		/// fixes up any remaining selected indices above it, which shift down by one.</summary>
		public void DeleteKeyframeAt(int index) {
			if (Curve == null || index < 0 || index >= Curve.KeyCount) return;
			RaiseBeginEdit("Delete Keyframe");
			Curve.RemoveKey(index);
			var shifted = new HashSet<int>();
			foreach (int i in Selection) { if (i == index) continue; shifted.Add(i > index ? i - 1 : i); }
			bool changed = !Selection.SetEquals(shifted) || Selection.Contains(index);
			Selection.Clear();
			foreach (int i in shifted) Selection.Add(i);
			RecomputeAllDerivedTangents(Curve);
			if (changed) RaiseSelectionChanged();
			RaiseMutated();
		}

		/// <summary>Clears any weight/scaling and recomputes a neutral secant-to-neighbors tangent -- keeps the
		/// keyframe's current HandleType (Free/Aligned/Vector/Auto) as-is, this only undoes manual angle/scale
		/// fiddling, it doesn't reclassify the handle.</summary>
		public void ResetHandles(int index) {
			if (Curve == null || index < 0 || index >= Curve.KeyCount) return;
			RaiseBeginEdit("Reset Handles");
			SKeyframe k = Curve[index];
			float neutral = VectorSlope(Curve, index);
			k.inTangent = neutral;
			k.outTangent = neutral;
			k.inWeight = 0f;
			k.outWeight = 0f;
			k.weightedMode = SWeightedMode.None;
			Curve.SetKeyframe(index, k);
			RaiseMutated();
		}

		public void CopySelected() {
			if (Curve == null || Selection.Count == 0) return;
			SCurveClipboard.Copy(Selection.OrderBy(i => i).Select(i => Curve[i]));
		}

		public void PasteAt(float anchorTime) {
			if (Curve == null || !SCurveClipboard.HasContent) return;
			RaiseBeginEdit("Paste Keyframes");
			var pasted = SCurveClipboard.PasteAt(anchorTime);
			var result = new HashSet<int>();
			foreach (SKeyframe k in pasted) result.Add(Curve.AddKey(k));
			RecomputeNeighborhood(Curve, result);
			Selection.Clear();
			foreach (int i in result) Selection.Add(i);
			RaiseSelectionChanged();
			RaiseMutated();
		}

		public void ApplyGeneratedCurve(SCurve generated) {
			// Presets always generate over [0,1] with a [0,1] value span -- if the curve had been stretched
			// (RescaleDuration/RescaleAmplitude), applying a (built-in or custom) preset on top of that should
			// keep that duration/amplitude rather than silently snapping back to the 0-1 defaults.
			float targetDuration = CurrentDuration;
			float targetAmplitude = CurrentAmplitude;
			RaiseBeginEdit("Apply Preset");
			Curve = generated;
			ApplyTimeScale(targetDuration);
			ApplyValueScale(targetAmplitude);
			Selection.Clear();
			FrameAll();
			RaiseSelectionChanged();
			RaiseMutated();
		}

		/// <summary>Full recompute -- use after structural changes that touch many keyframes at once (delete).</summary>
		static void RecomputeAllDerivedTangents(SCurve curve) {
			for (int i = 0; i < curve.KeyCount; i++) RecomputeDerivedTangent(curve, i);
		}

		/// <summary>Targeted recompute -- only the given indices and their immediate neighbors. Used for ordinary
		/// hand-edits (drag, single field edit) so an untouched Auto/Vector keyframe elsewhere on the curve --
		/// e.g. one of a Back/Elastic preset's finite-difference-fitted tangents -- isn't silently overwritten by
		/// the cruder neighbor-secant Auto formula just because some other keyframe moved.</summary>
		static void RecomputeNeighborhood(SCurve curve, IEnumerable<int> touchedIndices) {
			var toRecompute = new HashSet<int>();
			foreach (int i in touchedIndices) {
				if (i < 0 || i >= curve.KeyCount) continue;
				toRecompute.Add(i);
				if (i > 0) toRecompute.Add(i - 1);
				if (i < curve.KeyCount - 1) toRecompute.Add(i + 1);
			}
			foreach (int i in toRecompute) RecomputeDerivedTangent(curve, i);
		}

		static void RecomputeDerivedTangent(SCurve curve, int i) {
			SKeyframe k = curve[i];
			if (k.handleType == HandleType.Auto) {
				curve.SmoothTangents(i);
			} else if (k.handleType == HandleType.Vector) {
				float slope = VectorSlope(curve, i);
				k.inTangent = slope; k.outTangent = slope;
				curve.SetKeyframe(i, k);
			}
		}

		static float VectorSlope(SCurve curve, int i) {
			if (curve.KeyCount < 2) return 0f;
			if (i == 0) return Slope(curve[i], curve[i + 1]);
			if (i == curve.KeyCount - 1) return Slope(curve[i - 1], curve[i]);
			return Slope(curve[i - 1], curve[i + 1]);
		}

		static float Slope(SKeyframe a, SKeyframe b) {
			float dt = b.time - a.time;
			return Mathf.Approximately(dt, 0f) ? 0f : (b.value - a.value) / dt;
		}

		static float Snap(float value, float step) => step <= 0f ? value : Mathf.Round(value / step) * step;
		static double EditorApplicationTime() => UnityEditor.EditorApplication.timeSinceStartup;

		void RaiseBeginEdit(string label) => OnBeginEdit?.Invoke(label);
		void RaiseMutated() { MarkDirtyRepaint(); OnCurveMutated?.Invoke(); }
		void RaiseSelectionChanged() { MarkDirtyRepaint(); OnSelectionChanged?.Invoke(); }

		// ---------------- hit testing ----------------

		bool TryHitKeyframe(Vector2 local, out int index) {
			index = -1;
			if (Curve == null) return false;
			float best = KeyHitRadius;
			for (int i = 0; i < Curve.KeyCount; i++) {
				float d = Vector2.Distance(local, CurveToScreen(Curve[i].time, Curve[i].value));
				if (d <= best) { best = d; index = i; }
			}
			return index >= 0;
		}

		// Handles are hit-testable regardless of HandleType (Auto/Vector included) -- grabbing one auto-promotes
		// that side to Free, same convention as Unity's own AnimationCurve editor. Requiring a manual switch to
		// "Free" first (via the selection panel) before a handle could even be grabbed was confusing and made
		// handles feel broken/unresponsive.
		bool TryHitTangentHandle(Vector2 local, out int index, out bool isOut) {
			index = -1; isOut = false;
			if (Curve == null) return false;
			float best = HandleHitRadius;
			foreach (int i in Selection) {
				if (i < 0 || i >= Curve.KeyCount) continue;
				SKeyframe k = Curve[i];
				float dOut = Vector2.Distance(local, OutHandlePos(k, i));
				if (dOut <= best) { best = dOut; index = i; isOut = true; }
				float dIn = Vector2.Distance(local, InHandlePos(k, i));
				if (dIn <= best) { best = dIn; index = i; isOut = false; }
			}
			return index >= 0;
		}

		// Unweighted handle length is a FIXED SCREEN-PIXEL distance, not a fraction of the visible time range --
		// using view-relative length made handles balloon out far past the neighboring keyframes whenever zoomed
		// out (or the curve's keyframes were close together), looking disconnected from the actual curve shape
		// instead of hugging it locally. Direction always comes from the real tangent slope, converted through
		// the current per-axis screen scale so the line still looks visually tangent to the curve. When a side IS
		// weighted, its length instead reflects the true stored weight * neighbor distance -- "handle scaling".
		Vector2 OutHandlePos(SKeyframe k, int index) => HandleScreenPos(k, index, k.outTangent, isOut: true);
		Vector2 InHandlePos(SKeyframe k, int index) => HandleScreenPos(k, index, k.inTangent, isOut: false);

		Vector2 HandleScreenPos(SKeyframe k, int index, float slope, bool isOut) {
			Vector2 keyPos = CurveToScreen(k.time, k.value);
			float pixelsPerTime = contentRect.width / Mathf.Max(1e-6f, viewRect.width);
			float pixelsPerValue = contentRect.height / Mathf.Max(1e-6f, viewRect.height);
			Vector2 raw = new Vector2(pixelsPerTime, -slope * pixelsPerValue);
			float rawMag = raw.magnitude;
			Vector2 dir = rawMag > 1e-6f ? raw / rawMag : Vector2.right;
			if (!isOut) dir = -dir;

			float length = HandlePixelLength;
			bool weighted = isOut
				? k.weightedMode is SWeightedMode.Out or SWeightedMode.Both
				: k.weightedMode is SWeightedMode.In or SWeightedMode.Both;
			if (weighted && Curve != null) {
				int neighborIndex = isOut ? index + 1 : index - 1;
				if (neighborIndex >= 0 && neighborIndex < Curve.KeyCount) {
					float dtRef = Mathf.Abs(Curve[neighborIndex].time - k.time);
					float weight = isOut ? k.outWeight : k.inWeight;
					// keyPos's screen offset along the time axis for a curve-space distance of (weight*dtRef) is
					// weight*dtRef*pixelsPerTime; dir.x is that same offset's share of one unit of `length`, so
					// solving length*dir.x = weight*dtRef*pixelsPerTime directly gives the matching handle length
					// (equivalent to, but cheaper than, re-deriving it through the unnormalized vector's magnitude).
					if (Mathf.Abs(dir.x) > 1e-6f) length = Mathf.Max(6f, weight * dtRef * pixelsPerTime / Mathf.Abs(dir.x));
				}
			}
			return keyPos + dir * length;
		}

		static SWeightedMode WithWeightedSide(SWeightedMode mode, bool outSide, bool enabled) {
			int bit = outSide ? (int)SWeightedMode.Out : (int)SWeightedMode.In;
			int m = (int)mode;
			m = enabled ? m | bit : m & ~bit;
			return (SWeightedMode)m;
		}

		// ---------------- rendering ----------------

		void OnGenerateVisualContent(MeshGenerationContext mgc) {
			Rect r = contentRect;
			if (r.width <= 0f || r.height <= 0f || float.IsNaN(r.width)) return;
			Painter2D p = mgc.painter2D;

			DrawGrid(p, r);

			if (Curve != null && Curve.KeyCount > 0) {
				DrawCurve(p);
				DrawKeyframesAndHandles(p);
			}
			if (ShowPlayhead && Curve != null) DrawPlayhead(p, r);
			if (dragMode == DragMode.BoxSelect) DrawMarquee(p);
		}

		/// <summary>Rounds <paramref name="rawStep"/> up to a "nice" 1/2/5 * 10^n step -- the standard graph-paper
		/// trick so grid lines land on readable values (0.1, 0.2, 0.5, 1, 2, 5, ...) at any zoom level instead of
		/// an arbitrary fraction.</summary>
		static float NiceStep(float range, int targetDivisions) {
			float rawStep = Mathf.Max(1e-6f, range) / Mathf.Max(1, targetDivisions);
			float magnitude = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(rawStep)));
			float normalized = rawStep / magnitude;
			float niceNormalized = normalized < 1.5f ? 1f : normalized < 3f ? 2f : normalized < 7f ? 5f : 10f;
			return niceNormalized * magnitude;
		}

		void DrawGrid(Painter2D p, Rect r) {
			float stepT = NiceStep(viewRect.width, 10);
			float stepV = NiceStep(viewRect.height, 6);

			p.lineWidth = 1f;
			p.strokeColor = new Color(1f, 1f, 1f, 0.05f);
			p.BeginPath();
			float firstT = Mathf.Ceil(viewRect.xMin / stepT) * stepT;
			for (float t = firstT; t <= viewRect.xMax; t += stepT) {
				float x = CurveToScreen(t, 0f).x;
				p.MoveTo(new Vector2(x, 0)); p.LineTo(new Vector2(x, r.height));
			}
			float firstV = Mathf.Ceil(viewRect.yMin / stepV) * stepV;
			for (float v = firstV; v <= viewRect.yMax; v += stepV) {
				float y = CurveToScreen(0f, v).y;
				p.MoveTo(new Vector2(0, y)); p.LineTo(new Vector2(r.width, y));
			}
			p.Stroke();

			// value 0 / 1 baselines, when visible -- emphasized since they're the ease-curve reference points
			p.strokeColor = new Color(1f, 1f, 1f, 0.16f);
			p.BeginPath();
			foreach (float v in new[] { 0f, 1f }) {
				if (v < viewRect.yMin || v > viewRect.yMax) continue;
				float y = CurveToScreen(0f, v).y;
				p.MoveTo(new Vector2(0, y)); p.LineTo(new Vector2(r.width, y));
			}
			p.Stroke();
		}

		void DrawCurve(Painter2D p) {
			p.lineWidth = 2.5f;
			p.strokeColor = SperlichEditorTheme.ButtonAccent;
			p.BeginPath();
			// One sample roughly every 1.5px of actual canvas width -- a fixed sample count spread across the
			// full visible time range looked increasingly polygonal/low-res the further out you zoomed, since a
			// constant point count has to cover more curve detail per pixel-worth of the widening view.
			int samples = Mathf.Clamp(Mathf.RoundToInt(contentRect.width / 1.5f), 64, 800);
			float minT = viewRect.xMin, maxT = viewRect.xMax;
			for (int i = 0; i <= samples; i++) {
				float t = minT + (maxT - minT) * i / samples;
				Vector2 pt = CurveToScreen(t, Curve.Evaluate(t));
				if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
			}
			p.Stroke();
		}

		void DrawKeyframesAndHandles(Painter2D p) {
			for (int i = 0; i < Curve.KeyCount; i++) {
				SKeyframe k = Curve[i];
				bool selected = Selection.Contains(i);
				Vector2 pos = CurveToScreen(k.time, k.value);

				if (selected) {
					// Handles are always shown + grabbable when selected, regardless of HandleType -- grabbing
					// one auto-promotes that side to Free (see TryHitTangentHandle), so there's no separate
					// "make it draggable first" step that could silently fail to register.
					Vector2 outPos = OutHandlePos(k, i), inPos = InHandlePos(k, i);
					p.lineWidth = 1.5f;
					p.strokeColor = SperlichEditorTheme.ButtonAccent;
					p.BeginPath(); p.MoveTo(inPos); p.LineTo(outPos); p.Stroke();

					p.fillColor = SperlichEditorTheme.BgDark;
					foreach (Vector2 hp in new[] { inPos, outPos }) {
						p.BeginPath(); p.Arc(hp, 4f, 0f, 360f); p.Fill();
						p.lineWidth = 2f; p.strokeColor = SperlichEditorTheme.ButtonAccent;
						p.BeginPath(); p.Arc(hp, 4f, 0f, 360f); p.Stroke();
					}
				}

				// Keyframe dot: a filled center plus -- only when selected -- an outer ring, so "selected" reads
				// as a distinct selection RING rather than just "the same dot, bigger and recolored".
				if (selected) {
					p.lineWidth = 2f;
					p.strokeColor = SperlichEditorTheme.ButtonAccent;
					p.BeginPath(); p.Arc(pos, 7.5f, 0f, 360f); p.Stroke();
				}
				p.fillColor = selected ? Color.white : SperlichEditorTheme.TextSecondary;
				p.BeginPath();
				p.Arc(pos, selected ? 4.5f : 4f, 0f, 360f);
				p.Fill();
			}
		}

		void DrawPlayhead(Painter2D p, Rect r) {
			float x = CurveToScreen(PlayheadTime, 0f).x;
			if (x < 0f || x > r.width) return;
			p.lineWidth = 1f;
			p.strokeColor = new Color(1f, 1f, 1f, 0.5f);
			p.BeginPath(); p.MoveTo(new Vector2(x, 0)); p.LineTo(new Vector2(x, r.height)); p.Stroke();

			Vector2 dot = CurveToScreen(PlayheadTime, Curve.Evaluate(PlayheadTime));
			p.fillColor = Color.white;
			p.BeginPath(); p.Arc(dot, 4.5f, 0f, 360f); p.Fill();
		}

		void DrawMarquee(Painter2D p) {
			Rect r = Rect.MinMaxRect(Mathf.Min(dragStartScreen.x, boxSelectCurrent.x), Mathf.Min(dragStartScreen.y, boxSelectCurrent.y),
				Mathf.Max(dragStartScreen.x, boxSelectCurrent.x), Mathf.Max(dragStartScreen.y, boxSelectCurrent.y));
			p.fillColor = new Color(SperlichEditorTheme.ButtonAccent.r, SperlichEditorTheme.ButtonAccent.g, SperlichEditorTheme.ButtonAccent.b, 0.1f);
			p.BeginPath();
			p.MoveTo(new Vector2(r.xMin, r.yMin)); p.LineTo(new Vector2(r.xMax, r.yMin));
			p.LineTo(new Vector2(r.xMax, r.yMax)); p.LineTo(new Vector2(r.xMin, r.yMax));
			p.ClosePath();
			p.Fill();
			p.lineWidth = 1f;
			p.strokeColor = SperlichEditorTheme.ButtonAccent;
			p.Stroke();
		}
	}
}
