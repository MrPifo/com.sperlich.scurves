using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Sperlich.Easing {

	/// <summary>Live-editable parameters for the presets whose shape isn't fully described by the family alone
	/// (Back's overshoot, Elastic's amplitude/period, Spring's damped-sine knobs).</summary>
	[Serializable]
	public struct EaseParams {
		public float overshoot;
		public float amplitude;
		public float period;
		public float dampening;
		public float frequency;
		public float bounceCount;

		public static EaseParams Default => new() {
			overshoot = 1.70158f,
			amplitude = 1f,
			period = 3f,
			dampening = 6f,
			frequency = 3f,
			bounceCount = 4f,
		};
	}

	/// <summary>Turns an <see cref="EaseType"/> (+ optional <see cref="EaseParams"/>) into a normal, hand-editable
	/// <see cref="SCurve"/> -- presets generate keyframes once rather than staying a live formula, so a preset can
	/// be applied and then freely tweaked in the curve editor like any other curve.</summary>
	public static class EaseGenerators {
		static readonly HashSet<EaseType> ComplexTypes = new() {
			EaseType.InBack, EaseType.OutBack, EaseType.InOutBack,
			EaseType.InElastic, EaseType.OutElastic, EaseType.InOutElastic,
			EaseType.InBounce, EaseType.OutBounce, EaseType.InOutBounce,
			EaseType.Spring,
		};

		const int ExtremaScanSamples = 500;
		const float SignificanceFraction = 0.03f; // an extremum must move the value by >=3% of the formula's total range to earn its own keyframe
		const float FiniteDiffEpsilon = 1e-3f;
		const int RefinementScanSamples = 300;
		const float RefinementErrorFraction = 0.03f; // reconstruction is refined until it's within 3% of the formula's value range everywhere -- same threshold as SignificanceFraction, so "worth a keyframe" means one consistent thing throughout
		const int MaxRefinementIterations = 6;
		const int MaxTotalKeyframes = 40;

		public static SCurve Generate(EaseType type, EaseParams? parameters = null) {
			if (type == EaseType.Step) return GenerateStep();

			EaseParams p = parameters ?? EaseParams.Default;
			Func<float, float> formula = t => EvaluateWithParams(type, t, p);

			if (!ComplexTypes.Contains(type)) return BuildCurve(formula, new List<float> { 0f, 1f });

			// Simple monotonic families only need endpoints, but Back/Elastic/Bounce/Spring have a genuine shape
			// (overshoot, oscillation, bounces) that a fixed sample count either over- or under-serves. Placing a
			// keyframe at every VISUALLY SIGNIFICANT local extremum gives roughly as many points as the shape
			// actually needs -- a handful for a single Back overshoot, more for a longer decaying Elastic --
			// without also chasing every microscopic tail-wobble down to numerical noise. That alone still leaves
			// gaps where a single Hermite segment can't hug a strongly curved/cusped stretch between two
			// extrema (an Elastic ramp with a steep non-apex boundary, a Bounce cusp's neighboring arc) --
			// RefineForFidelity tops those up by inserting a keyframe wherever the reconstruction still deviates
			// too far from the real formula, rather than hand-tuning per-family constants for it.
			List<float> times = FindSignificantExtremaTimes(formula);
			SCurve curve = BuildCurve(formula, times);
			return RefineForFidelity(formula, times, curve);
		}

		static SCurve BuildCurve(Func<float, float> formula, List<float> times) {
			var curve = new SCurve();
			for (int i = 0; i < times.Count; i++) {
				float t = times[i];
				// A finite-difference epsilon appropriate for well-spaced samples can, for two keyframes placed
				// close together, sample past the NEIGHBOR's own position -- producing a secant slope that has
				// nothing to do with the local shape and can make the Hermite reconstruction blow up between them.
				// Capping each side's epsilon to a fraction of the gap to ITS neighbor on that side keeps every
				// tangent estimate local to where it's actually valid.
				float gapPrev = i > 0 ? t - times[i - 1] : 1f;
				float gapNext = i < times.Count - 1 ? times[i + 1] - t : 1f;
				// One-SIDED derivatives (not a single symmetric central difference) -- most of these formulas are
				// smooth enough that left/right come out nearly equal anyway, but a family with genuine corners
				// (Bounce touches down and immediately reverses direction) needs the two sides to differ, or a
				// forced-symmetric tangent badly overshoots right at the corner. HandleType stays Aligned either
				// way -- that only governs what happens if the user later drags one side by hand.
				float inTangent = EstimateOneSidedTangent(formula, t, Mathf.Min(FiniteDiffEpsilon, gapPrev * 0.4f), fromLeft: true);
				float outTangent = EstimateOneSidedTangent(formula, t, Mathf.Min(FiniteDiffEpsilon, gapNext * 0.4f), fromLeft: false);
				curve.AddKey(new SKeyframe(t, formula(t), inTangent, outTangent, HandleType.Aligned));
			}
			return curve;
		}

		/// <summary>Iteratively inserts a keyframe at whichever point the current reconstruction deviates most from
		/// the real formula, until the worst deviation is within <see cref="RefinementErrorFraction"/> of the
		/// formula's value range (or the iteration/keyframe-count budget runs out). A single Hermite segment
		/// between two extrema is a cubic -- it can't always hug a formula that isn't itself cubic-shaped over that
		/// stretch, so this closes the gap generically instead of tuning constants per ease family.</summary>
		static SCurve RefineForFidelity(Func<float, float> formula, List<float> times, SCurve curve) {
			float rangeMin = float.MaxValue, rangeMax = float.MinValue;
			for (int i = 0; i <= RefinementScanSamples; i++) {
				float v = formula((float)i / RefinementScanSamples);
				rangeMin = Mathf.Min(rangeMin, v);
				rangeMax = Mathf.Max(rangeMax, v);
			}
			float threshold = Mathf.Max(1e-4f, (rangeMax - rangeMin) * RefinementErrorFraction);

			for (int iter = 0; iter < MaxRefinementIterations && times.Count < MaxTotalKeyframes; iter++) {
				float worstT = -1f, worstErr = 0f;
				for (int i = 0; i <= RefinementScanSamples; i++) {
					float t = (float)i / RefinementScanSamples;
					float err = Mathf.Abs(curve.Evaluate(t) - formula(t));
					if (err > worstErr) { worstErr = err; worstT = t; }
				}
				if (worstErr <= threshold || worstT < 0f) break;
				if (times.Any(existing => Mathf.Abs(existing - worstT) < 1e-3f)) break;

				int insertAt = times.FindIndex(existing => existing > worstT);
				times.Insert(insertAt < 0 ? times.Count : insertAt, worstT);
				curve = BuildCurve(formula, times);
			}
			return curve;
		}

		/// <summary>Endpoints plus every local min/max of <paramref name="formula"/> over [0,1] that's visually
		/// significant, found by densely sampling and looking for a sign change in the discrete derivative, then
		/// greedily dropping any extremum whose value doesn't differ from the last KEPT one by at least
		/// <see cref="SignificanceFraction"/> of the formula's overall value range -- otherwise a slowly-decaying
		/// oscillation (Elastic, Bounce) ends up with a keyframe on every microscopic ripple in its tail.</summary>
		static List<float> FindSignificantExtremaTimes(Func<float, float> formula) {
			var samples = new float[ExtremaScanSamples + 1];
			float rangeMin = float.MaxValue, rangeMax = float.MinValue;
			for (int i = 0; i <= ExtremaScanSamples; i++) {
				samples[i] = formula((float)i / ExtremaScanSamples);
				rangeMin = Mathf.Min(rangeMin, samples[i]);
				rangeMax = Mathf.Max(rangeMax, samples[i]);
			}
			float threshold = Mathf.Max(1e-4f, (rangeMax - rangeMin) * SignificanceFraction);

			var candidates = new List<float>();
			for (int i = 1; i < ExtremaScanSamples; i++) {
				float d1 = samples[i] - samples[i - 1];
				float d2 = samples[i + 1] - samples[i];
				if (d1 != 0f && d2 != 0f && (d1 > 0f) != (d2 > 0f)) candidates.Add((float)i / ExtremaScanSamples);
			}

			var kept = new List<float> { 0f };
			float lastKeptValue = formula(0f);
			foreach (float t in candidates) {
				float v = formula(t);
				if (Mathf.Abs(v - lastKeptValue) >= threshold) {
					kept.Add(t);
					lastKeptValue = v;
				}
			}
			if (kept[^1] < 1f) kept.Add(1f); else kept[^1] = 1f;
			return kept;
		}

		static SCurve GenerateStep() {
			var curve = new SCurve();
			curve.AddKey(new SKeyframe(0f, 0f, 0f, 0f, HandleType.Vector) { isConstant = true });
			curve.AddKey(new SKeyframe(1f, 1f, 0f, 0f, HandleType.Vector));
			return curve;
		}

		static float EvaluateWithParams(EaseType type, float t, EaseParams p) => type switch {
			EaseType.InBack => Easing.InBack(t, p.overshoot),
			EaseType.OutBack => Easing.OutBack(t, p.overshoot),
			EaseType.InOutBack => Easing.InOutBack(t, p.overshoot),
			EaseType.InElastic => Easing.InElastic(t, p.amplitude, p.period),
			EaseType.OutElastic => Easing.OutElastic(t, p.amplitude, p.period),
			EaseType.InOutElastic => Easing.InOutElastic(t, p.amplitude, p.period),
			EaseType.InBounce => Easing.InBounce(t, p.bounceCount),
			EaseType.OutBounce => Easing.OutBounce(t, p.bounceCount),
			EaseType.InOutBounce => Easing.InOutBounce(t, p.bounceCount),
			EaseType.Spring => Easing.Spring(t, p.amplitude, p.dampening, p.frequency),
			_ => Easing.Evaluate(type, t),
		};

		/// <summary>One-sided finite-difference slope estimate, approaching <paramref name="t"/> from the left
		/// (<paramref name="fromLeft"/>=true, for inTangent) or departing to the right (false, for outTangent).
		/// Avoids hand-deriving a closed-form derivative for every formula while still giving Hermite tangents a
		/// close visual match, including right at a sharp corner where the two sides genuinely differ.</summary>
		static float EstimateOneSidedTangent(Func<float, float> formula, float t, float epsilon, bool fromLeft) {
			if (epsilon <= 1e-6f) return 0f;
			if (fromLeft) {
				float t0 = Mathf.Clamp01(t - epsilon);
				float dt = t - t0;
				return dt <= 0f ? 0f : (formula(t) - formula(t0)) / dt;
			} else {
				float t1 = Mathf.Clamp01(t + epsilon);
				float dt = t1 - t;
				return dt <= 0f ? 0f : (formula(t1) - formula(t)) / dt;
			}
		}

		/// <summary>All generatable preset names, e.g. for populating an editor dropdown.</summary>
		public static IEnumerable<EaseType> AllTypes() {
			foreach (EaseType type in Enum.GetValues(typeof(EaseType))) yield return type;
		}
	}
}
