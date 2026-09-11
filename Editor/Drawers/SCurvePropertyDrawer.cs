using Sperlich.EditorKit;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Sperlich.Easing.Editor {

	[CustomPropertyDrawer(typeof(SCurve))]
	public class SCurvePropertyDrawer : PropertyDrawer {

		public override VisualElement CreatePropertyGUI(SerializedProperty property) {
			SerializedProperty prop = property.Copy();
			var preview = new SCurvePreviewElement();

			void Refresh() => preview.SetCurve(prop.boxedValue as SCurve ?? new SCurve());
			Refresh();

			preview.OnClicked = () => SCurveEditorWindow.Open(prop, preview, Refresh);
			preview.TrackPropertyValue(prop, _ => Refresh());

			return SperlichEditorWidgets.CreateAlignedRow(prop.displayName, preview);
		}
	}
}
