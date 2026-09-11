using System;
using Sperlich.EditorKit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sperlich.Easing.Editor {

	/// <summary>A preset family that doesn't have In/Out/InOut variants (a single fixed shape).</summary>
	enum EaseFamily { Linear, Quad, Cubic, Quart, Quint, Sine, Expo, Circ, Back, Elastic, Bounce, Step, Spring }
	enum EaseDirection { In, Out, InOut }

	/// <summary>Toolbar-left preset controls: pick a curve family first (Quad/Cubic/.../Back/Elastic/...), then --
	/// only for families that actually have them -- an In/Out/InOut variant next to it. Families with a single
	/// fixed shape (Linear/Step/Spring) hide the variant dropdown entirely instead of showing a meaningless
	/// choice. Applying overwrites the curve's keyframes via <see cref="EaseGenerators"/> -- destructive, but the
	/// result stays freely hand-editable afterward.</summary>
	class SCurvePresetBar : VisualElement {
		public Action<SCurve> OnApply;

		EaseFamily family = EaseFamily.Back;
		EaseDirection direction = EaseDirection.Out;
		EaseParams parameters = EaseParams.Default;

		readonly VisualElement directionDropdown;
		readonly VisualElement paramRow;

		public SCurvePresetBar() {
			style.flexDirection = UnityEngine.UIElements.FlexDirection.Row;
			style.alignItems = Align.Center;

			VisualElement familyDropdown = SperlichEditorWidgets.BuildDropdown(
				() => Enum.GetValues(typeof(EaseFamily)).Length,
				i => ((EaseFamily)i).ToString(),
				() => (int)family,
				i => { family = (EaseFamily)i; RefreshDirectionVisibility(); RefreshParamRow(); Apply(); },
				SperlichEditorTheme.ButtonAccent);
			familyDropdown.style.width = 90;
			Add(familyDropdown);

			directionDropdown = SperlichEditorWidgets.BuildDropdown(
				() => Enum.GetValues(typeof(EaseDirection)).Length,
				i => ((EaseDirection)i).ToString(),
				() => (int)direction,
				i => { direction = (EaseDirection)i; RefreshParamRow(); Apply(); },
				SperlichEditorTheme.ButtonAccent);
			directionDropdown.style.width = 72;
			directionDropdown.style.marginLeft = 6;
			Add(directionDropdown);

			paramRow = new VisualElement { style = { flexDirection = UnityEngine.UIElements.FlexDirection.Row, alignItems = Align.Center, marginLeft = 10 } };
			Add(paramRow);

			RefreshDirectionVisibility();
			RefreshParamRow();
		}

		static bool HasDirectionVariants(EaseFamily f) => f is not (EaseFamily.Linear or EaseFamily.Step or EaseFamily.Spring);

		void RefreshDirectionVisibility() => directionDropdown.style.display = HasDirectionVariants(family) ? DisplayStyle.Flex : DisplayStyle.None;

		EaseType ResolveType() {
			if (family == EaseFamily.Linear) return EaseType.Linear;
			if (family == EaseFamily.Step) return EaseType.Step;
			if (family == EaseFamily.Spring) return EaseType.Spring;
			return family switch {
				EaseFamily.Quad => direction switch { EaseDirection.In => EaseType.InQuad, EaseDirection.Out => EaseType.OutQuad, _ => EaseType.InOutQuad },
				EaseFamily.Cubic => direction switch { EaseDirection.In => EaseType.InCubic, EaseDirection.Out => EaseType.OutCubic, _ => EaseType.InOutCubic },
				EaseFamily.Quart => direction switch { EaseDirection.In => EaseType.InQuart, EaseDirection.Out => EaseType.OutQuart, _ => EaseType.InOutQuart },
				EaseFamily.Quint => direction switch { EaseDirection.In => EaseType.InQuint, EaseDirection.Out => EaseType.OutQuint, _ => EaseType.InOutQuint },
				EaseFamily.Sine => direction switch { EaseDirection.In => EaseType.InSine, EaseDirection.Out => EaseType.OutSine, _ => EaseType.InOutSine },
				EaseFamily.Expo => direction switch { EaseDirection.In => EaseType.InExpo, EaseDirection.Out => EaseType.OutExpo, _ => EaseType.InOutExpo },
				EaseFamily.Circ => direction switch { EaseDirection.In => EaseType.InCirc, EaseDirection.Out => EaseType.OutCirc, _ => EaseType.InOutCirc },
				EaseFamily.Back => direction switch { EaseDirection.In => EaseType.InBack, EaseDirection.Out => EaseType.OutBack, _ => EaseType.InOutBack },
				EaseFamily.Elastic => direction switch { EaseDirection.In => EaseType.InElastic, EaseDirection.Out => EaseType.OutElastic, _ => EaseType.InOutElastic },
				EaseFamily.Bounce => direction switch { EaseDirection.In => EaseType.InBounce, EaseDirection.Out => EaseType.OutBounce, _ => EaseType.InOutBounce },
				_ => EaseType.Linear,
			};
		}

		void RefreshParamRow() {
			paramRow.Clear();
			switch (family) {
				case EaseFamily.Back:
					AddParamField("Overshoot", () => parameters.overshoot, v => parameters.overshoot = v, 0.01f);
					break;
				case EaseFamily.Elastic:
					AddParamField("Amplitude", () => parameters.amplitude, v => parameters.amplitude = Mathf.Max(v, 0.01f), 0.01f);
					AddParamField("Period", () => parameters.period, v => parameters.period = Mathf.Max(v, 0.1f), 0.005f);
					break;
				case EaseFamily.Bounce:
					AddParamField("Bounces", () => parameters.bounceCount, v => parameters.bounceCount = Mathf.Round(Mathf.Max(v, 1f)), 0.5f);
					break;
				case EaseFamily.Spring:
					AddParamField("Amplitude", () => parameters.amplitude, v => parameters.amplitude = v, 0.01f);
					AddParamField("Dampening", () => parameters.dampening, v => parameters.dampening = v, 0.05f);
					AddParamField("Frequency", () => parameters.frequency, v => parameters.frequency = v, 0.02f);
					break;
			}
		}

		void AddParamField(string label, Func<float> get, Action<float> set, float speed) {
			(VisualElement field, Action _) = SperlichEditorWidgets.MakeVirtualDragNumber(label, get, v => { set(v); Apply(); }, speed, null);
			field.style.width = 108;
			field.style.marginRight = 10;
			paramRow.Add(field);
		}

		void Apply() => OnApply?.Invoke(EaseGenerators.Generate(ResolveType(), parameters));
	}
}
