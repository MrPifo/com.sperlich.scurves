using UnityEngine;

namespace Sperlich.Easing {
	[CreateAssetMenu(fileName = "Asset", menuName = "Presets/Curve", order = 1)]
	public class CurvePreset : ScriptableObject {

		public EaseType type;
		public AnimationCurve curve;

	}
}