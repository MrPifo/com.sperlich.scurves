using System;

namespace Sperlich.Easing {

	/// <summary>A single point on an <see cref="SCurve"/>. Field shape mirrors <see cref="UnityEngine.Keyframe"/>
	/// 1:1 (time/value/tangents/weights) so conversion to/from <see cref="UnityEngine.AnimationCurve"/> is lossless
	/// wherever Unity's own model supports it -- see <see cref="AnimationCurveInterop"/>.</summary>
	[Serializable]
	public struct SKeyframe {
		public float time;
		public float value;
		public float inTangent;
		public float outTangent;
		public float inWeight;
		public float outWeight;
		public SWeightedMode weightedMode;

		/// <summary>Editor-only convenience classification, ignored by <see cref="SCurve.Evaluate"/>.</summary>
		public HandleType handleType;
		/// <summary>When true, this segment holds <see cref="value"/> constant until the next keyframe's time
		/// instead of interpolating -- the runtime-safe equivalent of Unity's editor-only "Constant" tangent mode.</summary>
		public bool isConstant;

		public SKeyframe(float time, float value, float inTangent = 0f, float outTangent = 0f, HandleType handleType = HandleType.Aligned) {
			this.time = time;
			this.value = value;
			this.inTangent = inTangent;
			this.outTangent = outTangent;
			inWeight = 0f;
			outWeight = 0f;
			weightedMode = SWeightedMode.None;
			this.handleType = handleType;
			isConstant = false;
		}
	}
}
