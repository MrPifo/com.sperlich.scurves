using System;
using System.Collections.Generic;
using System.Linq;
using Sperlich.EditorKit;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sperlich.Easing.Editor {

	/// <summary>Floating overlay panel (Design B) shown over the canvas whenever >=1 keyframe is selected. Single
	/// selection: full Zeit/Wert/Tangenten/Handle-Typ fields. Multi-selection: only relative delta fields, per the
	/// confirmed spec. Numeric fields use <see cref="SperlichEditorWidgets.MakeVirtualDragNumber"/> -- the same
	/// drag-grip look as every other EditorKit number field.</summary>
	class SCurveSelectionPanel : VisualElement {
		readonly Label countLabel;
		readonly VisualElement body;
		readonly List<Action> syncActions = new();

		public Action<int, SKeyframe> OnKeyframeEdited;
		public Action<float, float> OnDeltaApplied;
		public Action<int, HandleType> OnHandleTypeChanged;

		SCurve curve;
		List<int> selectedIndices = new();
		float pendingDeltaTime, pendingDeltaValue;

		public SCurveSelectionPanel() {
			style.position = Position.Absolute;
			style.top = 10; style.right = 10; style.width = 190;
			style.backgroundColor = new Color(SperlichEditorTheme.BgDark.r, SperlichEditorTheme.BgDark.g, SperlichEditorTheme.BgDark.b, 0.94f);
			style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = 1;
			SperlichEditorWidgets.SetBorderColor(this, SperlichEditorTheme.BorderStrong);
			SperlichEditorWidgets.SetRadius(this, 5);
			style.paddingTop = style.paddingBottom = 10;
			style.paddingLeft = style.paddingRight = 10;
			pickingMode = PickingMode.Position;

			// Without this, every click inside the panel (segment buttons, drag-grips, ...) bubbles its raw
			// PointerDownEvent past this overlay and into the canvas underneath -- which then runs its own
			// hit-testing on that same screen position, usually missing every keyframe and starting a box-select
			// drag that steals pointer capture away from whatever was just clicked. That's why handle-type
			// buttons and fields could randomly seem to "not respond".
			RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());

			var titleRow = new VisualElement { style = { flexDirection = UnityEngine.UIElements.FlexDirection.Row, marginBottom = 8 } };
			titleRow.Add(new Label("Ausgewählt") { style = { fontSize = 10, unityFontStyleAndWeight = FontStyle.Bold, color = SperlichEditorTheme.TextFaint } });
			countLabel = new Label { style = { fontSize = 10, unityFontStyleAndWeight = FontStyle.Bold, color = SperlichEditorTheme.ButtonAccent, marginLeft = 4 } };
			titleRow.Add(countLabel);
			Add(titleRow);
			// countLabel doubles as "Key #N" for a single selection and "(N)" for multi-selection -- set in Rebuild.

			body = new VisualElement();
			Add(body);

			style.display = DisplayStyle.None;
		}

		public void Rebuild(SCurve curveRef, IReadOnlyCollection<int> selection) {
			curve = curveRef;
			selectedIndices = selection.OrderBy(i => i).ToList();
			syncActions.Clear();
			body.Clear();
			pendingDeltaTime = 0f;
			pendingDeltaValue = 0f;

			if (selectedIndices.Count == 0) { style.display = DisplayStyle.None; return; }
			style.display = DisplayStyle.Flex;
			countLabel.text = selectedIndices.Count == 1 ? $"Key #{selectedIndices[0]}" : $"({selectedIndices.Count})";

			if (selectedIndices.Count == 1) BuildSingle(selectedIndices[0]);
			else BuildMulti();
		}

		void BuildSingle(int index) {
			SKeyframe Key() => curve[index];

			AddField("Zeit", () => Key().time, v => {
				SKeyframe k = Key(); k.time = v;
				OnKeyframeEdited?.Invoke(index, k);
			});
			AddField("Wert", () => Key().value, v => {
				SKeyframe k = Key(); k.value = v;
				OnKeyframeEdited?.Invoke(index, k);
			});

			body.Add(Divider());

			AddField("In-Tan.", () => Key().inTangent, v => {
				SKeyframe k = Key(); k.inTangent = v; k.handleType = HandleType.Free;
				OnKeyframeEdited?.Invoke(index, k);
			});
			AddField("Out-Tan.", () => Key().outTangent, v => {
				SKeyframe k = Key(); k.outTangent = v; k.handleType = HandleType.Free;
				OnKeyframeEdited?.Invoke(index, k);
			});

			body.Add(Divider());
			body.Add(new Label("Handle-Typ") { style = { fontSize = 10, unityFontStyleAndWeight = FontStyle.Bold, color = SperlichEditorTheme.TextFaint, marginBottom = 5 } });

			var segRow = new VisualElement { style = { flexDirection = UnityEngine.UIElements.FlexDirection.Row, flexWrap = Wrap.Wrap } };
			HandleType[] types = { HandleType.Free, HandleType.Vector, HandleType.Aligned, HandleType.Auto };
			string[] labels = { "Free", "Vector", "Aligned", "Auto" };
			string[] tooltips = {
				"Manuell: In/Out-Tangente unabhängig frei einstellbar.",
				"Automatisch: zeigt strikt Richtung Nachbar-Keyframe, kein Abflachen an Hoch-/Tiefpunkten.",
				"Manuell: In/Out-Tangente teilen sich eine Steigung (eine gerade Linie durchs Handle).",
				"Automatisch: geglättet aus den Nachbarn, flacht an Hoch-/Tiefpunkten ab (Standard für neue Keys).",
			};
			var segments = new List<VisualElement>();

			void RefreshSegments() {
				HandleType current = Key().handleType;
				for (int i = 0; i < segments.Count; i++) {
					bool active = types[i] == current;
					SperlichEditorWidgets.SetBorderColor(segments[i], active ? SperlichEditorTheme.ButtonAccent : SperlichEditorTheme.BorderSubtle);
					segments[i].style.backgroundColor = active
						? new Color(SperlichEditorTheme.ButtonAccent.r, SperlichEditorTheme.ButtonAccent.g, SperlichEditorTheme.ButtonAccent.b, 0.16f)
						: Color.clear;
					((Label)segments[i][0]).style.color = active ? SperlichEditorTheme.ButtonAccent : SperlichEditorTheme.TextMuted;
				}
			}

			for (int i = 0; i < types.Length; i++) {
				int captured = i;
				var seg = new VisualElement {
					pickingMode = PickingMode.Position,
					tooltip = tooltips[captured],
					style = {
						flexGrow = 1, borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
						marginRight = captured % 2 == 0 ? 4 : 0, marginBottom = 4,
						paddingTop = 4, paddingBottom = 4, alignItems = Align.Center, minWidth = 70,
					},
				};
				SperlichEditorWidgets.SetRadius(seg, 3);
				SperlichEditorWidgets.SetHoverCursor(seg, MouseCursor.Link);
				seg.Add(new Label(labels[captured]) { pickingMode = PickingMode.Ignore, style = { fontSize = 9.5f, unityTextAlign = TextAnchor.MiddleCenter } });
				seg.RegisterCallback<ClickEvent>(_ => {
					OnHandleTypeChanged?.Invoke(index, types[captured]);
					RefreshSegments();
				});
				segments.Add(seg);
				segRow.Add(seg);
			}
			RefreshSegments();
			body.Add(segRow);
			syncActions.Add(RefreshSegments);
		}

		void BuildMulti() {
			AddField("Δ Zeit", () => pendingDeltaTime, v => {
				float delta = v - pendingDeltaTime;
				pendingDeltaTime = v;
				OnDeltaApplied?.Invoke(delta, 0f);
			});
			AddField("Δ Wert", () => pendingDeltaValue, v => {
				float delta = v - pendingDeltaValue;
				pendingDeltaValue = v;
				OnDeltaApplied?.Invoke(0f, delta);
			});
		}

		void AddField(string label, Func<float> get, Action<float> set) {
			var row = new VisualElement { style = { flexDirection = UnityEngine.UIElements.FlexDirection.Row, alignItems = Align.Center, marginBottom = 6 } };
			row.Add(new Label(label) { style = { width = 58, flexShrink = 0, fontSize = 10.5f, color = SperlichEditorTheme.TextSecondary } });
			(VisualElement field, Action sync) = SperlichEditorWidgets.MakeVirtualDragNumber(null, get, set, 0.01f, null);
			field.style.flexGrow = 1;
			row.Add(field);
			body.Add(row);
			syncActions.Add(sync);
		}

		static VisualElement Divider() => new() { style = { height = 1, backgroundColor = SperlichEditorTheme.BorderSubtle, marginTop = 2, marginBottom = 8 } };

		/// <summary>Refreshes displayed numbers without rebuilding field elements -- call after every
		/// canvas-driven mutation (drag) so fields don't lose focus/identity mid-gesture.</summary>
		public void SyncAll() { foreach (Action sync in syncActions) sync(); }
	}
}
