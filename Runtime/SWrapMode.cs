namespace Sperlich.Easing {

	/// <summary>Mirrors <see cref="UnityEngine.WrapMode"/> value-for-value so casting between the two is lossless
	/// (used by the AnimationCurve interop instead of a lookup/switch).</summary>
	public enum SWrapMode {
		Default = 0,
		Once = 1,
		Loop = 2,
		PingPong = 4,
		ClampForever = 8,
	}
}
