using UnityEngine;

namespace Sperlich.Easing.Editor {

	/// <summary>Best-effort lookup of which built-in <see cref="EaseType"/> preset (if any) an <see cref="SCurve"/>'s
	/// keyframes currently match, for the Inspector preview's hover tooltip. Only matches the default
	/// parameterization -- a hand-tweaked Back/Elastic/Spring simply won't match, which is correct.</summary>
	static class BuiltInPresetMatcher {
		const float Epsilon = 0.01f;

		public static string FindMatch(SCurve curve) {
			if (curve == null || curve.KeyCount == 0) return null;

			foreach (EaseType type in EaseGenerators.AllTypes()) {
				if (Matches(curve, EaseGenerators.Generate(type))) return type.ToString();
			}
			return null;
		}

		static bool Matches(SCurve a, SCurve b) {
			if (a.KeyCount != b.KeyCount) return false;
			for (int i = 0; i < a.KeyCount; i++) {
				SKeyframe ka = a[i], kb = b[i];
				if (Mathf.Abs(ka.time - kb.time) > Epsilon) return false;
				if (Mathf.Abs(ka.value - kb.value) > Epsilon) return false;
			}
			return true;
		}
	}
}
