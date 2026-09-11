namespace Sperlich.Easing {

	/// <summary>Identifies a built-in easing preset. Applying one to an <see cref="SCurve"/> (see
	/// <see cref="EaseGenerators"/>) regenerates its keyframes from the matching formula in <see cref="Easing"/> --
	/// this is an identifier for preset generation, not a runtime evaluation branch.</summary>
	public enum EaseType {
		Linear = 0,
		InQuad = 1, OutQuad = 2, InOutQuad = 3,
		InCubic = 4, OutCubic = 5, InOutCubic = 6,
		InQuart = 7, OutQuart = 8, InOutQuart = 9,
		InQuint = 10, OutQuint = 11, InOutQuint = 12,
		InSine = 13, OutSine = 14, InOutSine = 15,
		InExpo = 16, OutExpo = 17, InOutExpo = 18,
		InCirc = 19, OutCirc = 20, InOutCirc = 21,
		InBack = 22, OutBack = 23, InOutBack = 24,
		InElastic = 25, OutElastic = 26, InOutElastic = 27,
		InBounce = 28, OutBounce = 29, InOutBounce = 30,
		Step = 31,
		Spring = 32,
	}
}
