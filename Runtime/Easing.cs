using System;
using UnityEngine;

namespace Sperlich.Easing {

	/// <summary>Standalone reimplementation of the standard Penner easing formulas (clean-room -- no PrimeTween
	/// or third-party source involved). Each function maps t in [0,1] to an eased value; <see cref="EaseGenerators"/>
	/// samples these to build the matching <see cref="SCurve"/> preset keyframes.</summary>
	public static class Easing {

		public static float Linear(float t) => t;

		public static float InQuad(float t) => t * t;
		public static float OutQuad(float t) => 1f - (1f - t) * (1f - t);
		public static float InOutQuad(float t) => t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2) / 2f;

		public static float InCubic(float t) => t * t * t;
		public static float OutCubic(float t) => 1f - Mathf.Pow(1f - t, 3);
		public static float InOutCubic(float t) => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3) / 2f;

		public static float InQuart(float t) => t * t * t * t;
		public static float OutQuart(float t) => 1f - Mathf.Pow(1f - t, 4);
		public static float InOutQuart(float t) => t < 0.5f ? 8f * t * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 4) / 2f;

		public static float InQuint(float t) => t * t * t * t * t;
		public static float OutQuint(float t) => 1f - Mathf.Pow(1f - t, 5);
		public static float InOutQuint(float t) => t < 0.5f ? 16f * t * t * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 5) / 2f;

		public static float InSine(float t) => 1f - Mathf.Cos(t * Mathf.PI / 2f);
		public static float OutSine(float t) => Mathf.Sin(t * Mathf.PI / 2f);
		public static float InOutSine(float t) => -(Mathf.Cos(Mathf.PI * t) - 1f) / 2f;

		public static float InExpo(float t) => t <= 0f ? 0f : Mathf.Pow(2f, 10f * t - 10f);
		public static float OutExpo(float t) => t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
		public static float InOutExpo(float t) {
			if (t <= 0f) return 0f;
			if (t >= 1f) return 1f;
			return t < 0.5f ? Mathf.Pow(2f, 20f * t - 10f) / 2f : (2f - Mathf.Pow(2f, -20f * t + 10f)) / 2f;
		}

		public static float InCirc(float t) => 1f - Mathf.Sqrt(1f - t * t);
		public static float OutCirc(float t) => Mathf.Sqrt(1f - (t - 1f) * (t - 1f));
		public static float InOutCirc(float t) => t < 0.5f
			? (1f - Mathf.Sqrt(1f - Mathf.Pow(2f * t, 2))) / 2f
			: (Mathf.Sqrt(1f - Mathf.Pow(-2f * t + 2f, 2)) + 1f) / 2f;

		const float BackC1 = 1.70158f;
		const float BackC3 = BackC1 + 1f;
		const float BackC2 = BackC1 * 1.525f;

		public static float InBack(float t, float overshoot = BackC1) {
			float c3 = overshoot + 1f;
			return c3 * t * t * t - overshoot * t * t;
		}
		public static float OutBack(float t, float overshoot = BackC1) {
			float c3 = overshoot + 1f;
			return 1f + c3 * Mathf.Pow(t - 1f, 3) + overshoot * Mathf.Pow(t - 1f, 2);
		}
		public static float InOutBack(float t, float overshoot = BackC1) {
			float c2 = overshoot * 1.525f;
			return t < 0.5f
				? Mathf.Pow(2f * t, 2) * ((c2 + 1f) * 2f * t - c2) / 2f
				: (Mathf.Pow(2f * t - 2f, 2) * ((c2 + 1f) * (t * 2f - 2f) + c2) + 2f) / 2f;
		}

		// period=3 matches the standard Penner/easings.net elastic constant (c4=2pi/3, ~2-3 visible oscillations)
		// -- an earlier 0.3 default was 10x too small, which multiplied c4 into a ~33-cycle oscillation instead
		// of a normal "boing".
		//
		// Phase shift `s`: the plain Penner formula only lands on exactly 0 at t=0 / 1 at t=1 because it was
		// derived for amplitude=1. Naively multiplying that formula by an arbitrary amplitude breaks that --
		// the curve would jump away from 0/1 right at the boundary (visible as a spurious near-zero keyframe
		// with a wild tangent once EaseGenerators samples it). The fix (same trick GSAP's elastic ease uses) is
		// to re-derive the phase offset from the amplitude so the boundary stays exactly 0/1 for any amplitude
		// >= 1: sin(s * 2pi/period) = 1/amplitude. Amplitude < 1 can't satisfy that (asin domain), so it falls
		// back to the amplitude=1 phase -- the boundary then lands at 1-amplitude instead of exactly 0, a small
		// bounded gap rather than the previous unbounded blowup.
		public static float OutElastic(float t, float amplitude = 1f, float period = 3f) {
			if (t <= 0f) return 0f;
			if (t >= 1f) return 1f;
			float a = Mathf.Max(amplitude, 1e-4f);
			float c = 2f * Mathf.PI / period;
			float s = a >= 1f ? period / (2f * Mathf.PI) * Mathf.Asin(1f / a) : period / 4f;
			return a * Mathf.Pow(2f, -10f * t) * Mathf.Sin((10f * t - s) * c) + 1f;
		}
		public static float InElastic(float t, float amplitude = 1f, float period = 3f) {
			if (t <= 0f) return 0f;
			if (t >= 1f) return 1f;
			return 1f - OutElastic(1f - t, amplitude, period);
		}
		public static float InOutElastic(float t, float amplitude = 1f, float period = 3f) {
			if (t <= 0f) return 0f;
			if (t >= 1f) return 1f;
			return t < 0.5f
				? InElastic(2f * t, amplitude, period) * 0.5f
				: OutElastic(2f * t - 1f, amplitude, period) * 0.5f + 0.5f;
		}

		// Configurable bounce count: the fixed Penner formula above is a hardcoded 4-segment shape with no way to
		// add/remove bounces. This replaces it with an envelope-times-oscillation construction -- (1-t)^2 decays
		// the "how far below the ceiling" amount to exactly 0 at t=1, abs(cos(...)) touches that ceiling (value
		// == 1) at each zero-crossing and dips back down at each peak, giving `bounceCount` visible dips while
		// staying exactly in [0,1] for any bounceCount (no per-segment constants to keep in sync).
		public static float OutBounce(float t, float bounceCount = 4f) {
			if (t <= 0f) return 0f;
			if (t >= 1f) return 1f;
			float n = Mathf.Max(1f, bounceCount);
			float envelope = (1f - t) * (1f - t);
			return 1f - envelope * Mathf.Abs(Mathf.Cos(t * n * Mathf.PI));
		}
		public static float InBounce(float t, float bounceCount = 4f) => 1f - OutBounce(1f - t, bounceCount);
		public static float InOutBounce(float t, float bounceCount = 4f) => t < 0.5f
			? (1f - OutBounce(1f - 2f * t, bounceCount)) / 2f
			: (1f + OutBounce(2f * t - 1f, bounceCount)) / 2f;

		public static float Step(float t) => t < 1f ? 0f : 1f;

		/// <summary>Damped-sine spring, no single canonical formula exists for "Spring" the way it does for the
		/// other families -- <paramref name="amplitude"/>/<paramref name="dampening"/>/<paramref name="frequency"/>
		/// are exposed as live editor parameters rather than fixed.</summary>
		public static float Spring(float t, float amplitude = 1f, float dampening = 6f, float frequency = 3f) {
			if (t <= 0f) return 0f;
			if (t >= 1f) return 1f;
			return 1f - Mathf.Exp(-dampening * t) * Mathf.Cos(frequency * Mathf.PI * t) * amplitude;
		}

		/// <summary>Evaluates the non-parametrized formula matching <paramref name="type"/>. Parametrized presets
		/// (Back/Elastic/Spring) should go through <see cref="EaseGenerators"/> directly so their parameters apply.</summary>
		public static float Evaluate(EaseType type, float t) => type switch {
			EaseType.Linear => Linear(t),
			EaseType.InQuad => InQuad(t), EaseType.OutQuad => OutQuad(t), EaseType.InOutQuad => InOutQuad(t),
			EaseType.InCubic => InCubic(t), EaseType.OutCubic => OutCubic(t), EaseType.InOutCubic => InOutCubic(t),
			EaseType.InQuart => InQuart(t), EaseType.OutQuart => OutQuart(t), EaseType.InOutQuart => InOutQuart(t),
			EaseType.InQuint => InQuint(t), EaseType.OutQuint => OutQuint(t), EaseType.InOutQuint => InOutQuint(t),
			EaseType.InSine => InSine(t), EaseType.OutSine => OutSine(t), EaseType.InOutSine => InOutSine(t),
			EaseType.InExpo => InExpo(t), EaseType.OutExpo => OutExpo(t), EaseType.InOutExpo => InOutExpo(t),
			EaseType.InCirc => InCirc(t), EaseType.OutCirc => OutCirc(t), EaseType.InOutCirc => InOutCirc(t),
			EaseType.InBack => InBack(t), EaseType.OutBack => OutBack(t), EaseType.InOutBack => InOutBack(t),
			EaseType.InElastic => InElastic(t), EaseType.OutElastic => OutElastic(t), EaseType.InOutElastic => InOutElastic(t),
			EaseType.InBounce => InBounce(t), EaseType.OutBounce => OutBounce(t), EaseType.InOutBounce => InOutBounce(t),
			EaseType.Step => Step(t),
			EaseType.Spring => Spring(t),
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
		};
	}
}
