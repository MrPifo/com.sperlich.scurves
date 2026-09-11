using System.Collections.Generic;

namespace Sperlich.Easing.Editor {

	/// <summary>Process-lifetime keyframe clipboard, shared by every open <see cref="SCurveEditorWindow"/> -- a
	/// plain static buffer, so pasting into a second window instance just works with no extra plumbing.</summary>
	static class SCurveClipboard {
		static readonly List<SKeyframe> Buffer = new();
		public static bool HasContent => Buffer.Count > 0;

		/// <summary>Snapshots the given keyframes with time relative to the earliest one, so paste can re-anchor
		/// them at the playhead or a mouse position.</summary>
		public static void Copy(IEnumerable<SKeyframe> keys) {
			Buffer.Clear();
			float minTime = float.MaxValue;
			var snapshot = new List<SKeyframe>();
			foreach (SKeyframe k in keys) { snapshot.Add(k); if (k.time < minTime) minTime = k.time; }
			if (snapshot.Count == 0) return;
			foreach (SKeyframe k in snapshot) {
				SKeyframe rel = k;
				rel.time -= minTime;
				Buffer.Add(rel);
			}
		}

		/// <summary>Returns the clipboard contents offset so the earliest keyframe lands at <paramref name="anchorTime"/>.</summary>
		public static List<SKeyframe> PasteAt(float anchorTime) {
			var result = new List<SKeyframe>(Buffer.Count);
			foreach (SKeyframe k in Buffer) {
				SKeyframe placed = k;
				placed.time += anchorTime;
				result.Add(placed);
			}
			return result;
		}
	}
}
