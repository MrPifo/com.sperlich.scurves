using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sperlich.Easing {

	/// <summary>Full keyframe curve replacement for <see cref="UnityEngine.AnimationCurve"/>. Plain serializable
	/// fields only (no external containers Unity can't serialize) so it works as a normal serialized field on a
	/// <see cref="MonoBehaviour"/>/<see cref="ScriptableObject"/> in builds, not just in the Editor. Use
	/// <see cref="AnimationCurveInterop"/> to convert to/from <see cref="AnimationCurve"/>.</summary>
	[Serializable]
	public class SCurve {
		[SerializeField] List<SKeyframe> keyframes = new();
		[SerializeField] SWrapMode preWrapMode = SWrapMode.Default;
		[SerializeField] SWrapMode postWrapMode = SWrapMode.Default;

		public IReadOnlyList<SKeyframe> Keyframes => keyframes;
		public int KeyCount => keyframes.Count;
		public SWrapMode PreWrapMode { get => preWrapMode; set => preWrapMode = value; }
		public SWrapMode PostWrapMode { get => postWrapMode; set => postWrapMode = value; }

		public float MinTime => keyframes.Count > 0 ? keyframes[0].time : 0f;
		public float MaxTime => keyframes.Count > 0 ? keyframes[^1].time : 0f;
		public float MinValue {
			get {
				if (keyframes.Count == 0) return 0f;
				float min = keyframes[0].value;
				for (int i = 1; i < keyframes.Count; i++) min = Mathf.Min(min, keyframes[i].value);
				return min;
			}
		}
		public float MaxValue {
			get {
				if (keyframes.Count == 0) return 0f;
				float max = keyframes[0].value;
				for (int i = 1; i < keyframes.Count; i++) max = Mathf.Max(max, keyframes[i].value);
				return max;
			}
		}

		public SCurve() { }
		public SCurve(IEnumerable<SKeyframe> initialKeys) {
			foreach (SKeyframe key in initialKeys) AddKey(key);
		}

		public SKeyframe this[int index] {
			get => keyframes[index];
			set => keyframes[index] = value;
		}

		/// <summary>Inserts the keyframe at the position that keeps <see cref="Keyframes"/> sorted by time and
		/// returns its resulting index.</summary>
		public int AddKey(SKeyframe key) {
			int index = 0;
			while (index < keyframes.Count && keyframes[index].time < key.time) index++;
			keyframes.Insert(index, key);
			return index;
		}

		public void RemoveKey(int index) {
			if (index < 0 || index >= keyframes.Count) return;
			keyframes.RemoveAt(index);
		}

		/// <summary>Overwrites a keyframe's value/tangents in place without touching its position in the sorted
		/// list -- only valid when <paramref name="key"/>.time is unchanged. Use <see cref="MoveKey"/> if the
		/// time itself is changing.</summary>
		public void SetKeyframe(int index, SKeyframe key) => keyframes[index] = key;

		/// <summary>Repositions a keyframe whose time has changed, re-sorting as needed, and returns its new index.</summary>
		public int MoveKey(int index, SKeyframe newValue) {
			keyframes.RemoveAt(index);
			return AddKey(newValue);
		}

		/// <summary>Recomputes an <see cref="HandleType.Auto"/>-style tangent for the keyframe at <paramref name="index"/>
		/// from its neighbors, clamped to avoid overshoot past a local extremum (Blender "Auto Clamped" behavior).
		/// Also used as the default tangent for freshly inserted keyframes.</summary>
		public void SmoothTangents(int index) {
			if (index < 0 || index >= keyframes.Count) return;
			SKeyframe key = keyframes[index];
			float tangent;

			if (keyframes.Count == 1) {
				tangent = 0f;
			} else if (index == 0) {
				tangent = SafeSlope(key, keyframes[index + 1]);
			} else if (index == keyframes.Count - 1) {
				tangent = SafeSlope(keyframes[index - 1], key);
			} else {
				SKeyframe prev = keyframes[index - 1];
				SKeyframe next = keyframes[index + 1];
				tangent = SafeSlope(prev, next);
				bool isExtremum = (key.value >= prev.value && key.value >= next.value)
					|| (key.value <= prev.value && key.value <= next.value);
				if (isExtremum) tangent = 0f;
			}

			key.inTangent = tangent;
			key.outTangent = tangent;
			keyframes[index] = key;
		}

		static float SafeSlope(SKeyframe a, SKeyframe b) {
			float dt = b.time - a.time;
			return Mathf.Approximately(dt, 0f) ? 0f : (b.value - a.value) / dt;
		}

		public float Evaluate(float time) {
			if (keyframes.Count == 0) return 0f;
			if (keyframes.Count == 1) return keyframes[0].value;

			time = RemapTime(time);

			// At/beyond the very last keyframe's time, its value applies directly -- FindSegment always clamps to
			// the second-to-last segment, so without this early-out an isConstant second-to-last keyframe would
			// keep holding ITS value forever and the curve could never reach the final keyframe's value at all.
			if (time >= keyframes[^1].time) return keyframes[^1].value;
			if (time <= keyframes[0].time) return keyframes[0].value;

			int segment = FindSegment(time);
			SKeyframe k0 = keyframes[segment];
			SKeyframe k1 = keyframes[segment + 1];

			if (k0.isConstant) return k0.value;

			float dt = k1.time - k0.time;
			if (dt <= 0f) return k1.value;

			bool weighted = k0.weightedMode is SWeightedMode.Out or SWeightedMode.Both
				|| k1.weightedMode is SWeightedMode.In or SWeightedMode.Both;

			return weighted ? EvaluateWeighted(k0, k1, dt, time) : EvaluateHermite(k0, k1, dt, time);
		}

		float RemapTime(float time) {
			float min = keyframes[0].time, max = keyframes[^1].time;
			if (max <= min) return min;
			if (time < min) return RemapOutOfRange(time, min, max, preWrapMode, true);
			if (time > max) return RemapOutOfRange(time, min, max, postWrapMode, false);
			return time;
		}

		static float RemapOutOfRange(float time, float min, float max, SWrapMode mode, bool before) {
			float range = max - min;
			switch (mode) {
				case SWrapMode.Loop: {
					float t = (time - min) % range;
					if (t < 0f) t += range;
					return min + t;
				}
				case SWrapMode.PingPong: {
					float t = (time - min) % (range * 2f);
					if (t < 0f) t += range * 2f;
					return min + (t <= range ? t : range * 2f - t);
				}
				default: // Default, Once, ClampForever all clamp for value-sampling purposes
					return before ? min : max;
			}
		}

		int FindSegment(float time) {
			int lo = 0, hi = keyframes.Count - 1;
			while (lo < hi) {
				int mid = (lo + hi + 1) / 2;
				if (keyframes[mid].time <= time) lo = mid; else hi = mid - 1;
			}
			return Mathf.Min(lo, keyframes.Count - 2);
		}

		static float EvaluateHermite(SKeyframe k0, SKeyframe k1, float dt, float time) {
			float t = (time - k0.time) / dt;
			float t2 = t * t, t3 = t2 * t;
			float h00 = 2f * t3 - 3f * t2 + 1f;
			float h10 = t3 - 2f * t2 + t;
			float h01 = -2f * t3 + 3f * t2;
			float h11 = t3 - t2;
			return h00 * k0.value + h10 * dt * k0.outTangent + h01 * k1.value + h11 * dt * k1.inTangent;
		}

		/// <summary>Weighted tangents turn the segment into a genuine cubic Bezier in (time,value) space (matching
		/// how Unity's own weighted AnimationCurve behaves) -- since the Bezier's time-axis is no longer linear in
		/// t, the parameter for the requested <paramref name="time"/> is recovered with a few Newton-Raphson steps.</summary>
		static float EvaluateWeighted(SKeyframe k0, SKeyframe k1, float dt, float time) {
			const float defaultWeight = 1f / 3f;
			float w0 = k0.weightedMode is SWeightedMode.Out or SWeightedMode.Both ? k0.outWeight : defaultWeight;
			float w1 = k1.weightedMode is SWeightedMode.In or SWeightedMode.Both ? k1.inWeight : defaultWeight;

			float p0x = k0.time, p0y = k0.value;
			float p3x = k1.time, p3y = k1.value;
			float p1x = k0.time + w0 * dt, p1y = k0.value + w0 * dt * k0.outTangent;
			float p2x = k1.time - w1 * dt, p2y = k1.value - w1 * dt * k1.inTangent;

			float t = Mathf.Clamp01((time - k0.time) / dt);
			for (int i = 0; i < 6; i++) {
				float omt = 1f - t;
				float x = omt * omt * omt * p0x + 3f * omt * omt * t * p1x + 3f * omt * t * t * p2x + t * t * t * p3x;
				float dx = 3f * omt * omt * (p1x - p0x) + 6f * omt * t * (p2x - p1x) + 3f * t * t * (p3x - p2x);
				if (Mathf.Abs(dx) < 1e-6f) break;
				t = Mathf.Clamp01(t - (x - time) / dx);
			}

			float u = 1f - t;
			return u * u * u * p0y + 3f * u * u * t * p1y + 3f * u * t * t * p2y + t * t * t * p3y;
		}
	}
}
