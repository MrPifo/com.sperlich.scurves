namespace Sperlich.Easing {

	/// <summary>Editor-facing tangent-handle classification for a keyframe. Purely a constraint on how the
	/// two tangents of a keyframe may be edited together -- <see cref="SCurve.Evaluate"/> only ever reads the
	/// raw <see cref="SKeyframe.inTangent"/>/<see cref="SKeyframe.outTangent"/> values and never branches on this.</summary>
	public enum HandleType {
		/// <summary>In/out tangent set independently, arbitrary angle and length each.</summary>
		Free = 0,
		/// <summary>Tangent points straight at the neighboring keyframe; recomputed whenever a neighbor moves.</summary>
		Vector = 1,
		/// <summary>In/out tangent angles are locked collinear (180° apart); length stays independent per side.</summary>
		Aligned = 2,
		/// <summary>Tangent computed automatically from neighboring keyframes, clamped to avoid overshoot.</summary>
		Auto = 3,
	}
}
