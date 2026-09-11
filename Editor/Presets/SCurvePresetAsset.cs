using UnityEngine;

namespace Sperlich.Easing.Editor {

	/// <summary>A user-saved custom curve, stored as a project asset (found project-wide via
	/// <see cref="UnityEditor.AssetDatabase.FindAssets"/>, not tied to any one scene/field) so it can be reused
	/// across the project. The asset's own file name IS its display name -- renaming the asset in the Project
	/// window renames the preset, no separate name field to keep in sync.</summary>
	[CreateAssetMenu(menuName = "Sperlich/SCurve Preset", fileName = "New SCurve Preset")]
	public class SCurvePresetAsset : ScriptableObject {
		public SCurve curve = new();
	}
}
