using UnityEngine;

namespace Sperlich.Easing {

	/// <summary>Bidirectional conversion between <see cref="SCurve"/> and <see cref="UnityEngine.AnimationCurve"/>,
	/// for interop with internal Unity APIs and 3rd-party assets that expect the latter. Time/value/tangents/weights/
	/// wrap-modes round-trip losslessly; <see cref="HandleType"/> has no runtime equivalent on <see cref="Keyframe"/>
	/// (Unity only exposes tangent-mode classification via the Editor-only AnimationUtility), so it defaults to
	/// <see cref="HandleType.Free"/> when importing -- an Editor-only best-effort classifier can do better, see
	/// the Editor assembly.</summary>
	public static class AnimationCurveInterop {

		public static AnimationCurve ToAnimationCurve(this SCurve curve) {
			var keys = new Keyframe[curve.KeyCount];
			for (int i = 0; i < curve.KeyCount; i++) {
				SKeyframe k = curve[i];
				var uk = new Keyframe(k.time, k.value, k.inTangent, k.outTangent, k.inWeight, k.outWeight) {
					weightedMode = (WeightedMode)k.weightedMode,
				};
				keys[i] = uk;
			}
			return new AnimationCurve(keys) {
				preWrapMode = (WrapMode)curve.PreWrapMode,
				postWrapMode = (WrapMode)curve.PostWrapMode,
			};
			// Note: SKeyframe.isConstant has no runtime-settable Keyframe equivalent -- Unity's "Constant" tangent
			// mode is only settable via the Editor-only AnimationUtility.SetKeyLeftTangentMode.
		}

		public static SCurve ToSCurve(this AnimationCurve curve) {
			var result = new SCurve();
			foreach (Keyframe k in curve.keys) {
				result.AddKey(new SKeyframe {
					time = k.time,
					value = k.value,
					inTangent = k.inTangent,
					outTangent = k.outTangent,
					inWeight = k.inWeight,
					outWeight = k.outWeight,
					weightedMode = (SWeightedMode)k.weightedMode,
					handleType = HandleType.Free,
				});
			}
			result.PreWrapMode = (SWrapMode)curve.preWrapMode;
			result.PostWrapMode = (SWrapMode)curve.postWrapMode;
			return result;
		}
	}
}
