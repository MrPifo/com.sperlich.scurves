using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using System.Runtime.CompilerServices;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Sperlich.Easing {
	public static class SCurves {

		private static Dictionary<EaseType, CurvePreset> Curves { get; set; }
		public const string CurvesFolder = "Curves";
		private const string CurvePresetLibraryName = "CurvePresetLibrary";
		private const string CurvePresetSaveFolder = "Assets/UnitySperlich/EasingCurves/Resources/Curves";

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
		public static void Initialize() {
			//ExtractCurvesFromLibrary();
			LoadCurves();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static AnimationCurve GetCurve(EaseType type) => Curves[type].curve;
		public static void LoadCurves() {
			Curves = new();

			foreach(CurvePreset preset in Resources.LoadAll<CurvePreset>(CurvesFolder)) {
				Curves[preset.type] = preset;
			}
		}
		public static float Sample(float time, EaseType curve) {
			if(curve != EaseType.Linear) {
				return GetCurve(curve).Evaluate(Mathf.Clamp01(time));
			} else {
				return Mathf.Clamp01(time);
			}
		}

		#region Curve Functions
		/// <summary>
		/// Smoothens a list of Points.
		/// <para>Smoothness Value: 1-100</para>
		/// </summary>
		/// <param name="arrayToCurve"></param>
		/// <param name="smoothness"></param>
		/// <returns></returns>
		public static Vector3[] GenerateSmoothCurve(this IList<Vector3> arrayToCurve, int smoothness) {
			List<Vector3> points;
			List<Vector3> curvedPoints;
			int pointsLength;
			int curvedLength;
			float t;

			pointsLength = arrayToCurve.Count;

			curvedLength = (pointsLength * Mathf.Clamp(smoothness, 1, 100)) - 1;
			curvedPoints = new List<Vector3>(curvedLength);

			for (int pointInTimeOnCurve = 0; pointInTimeOnCurve < curvedLength + 1; pointInTimeOnCurve++) {
				t = Mathf.InverseLerp(0, curvedLength, pointInTimeOnCurve);

				points = new List<Vector3>(arrayToCurve);

				for (int j = pointsLength - 1; j > 0; j--) {
					for (int i = 0; i < j; i++) {
						points[i] = (1 - t) * points[i] + t * points[i + 1];
					}
				}

				curvedPoints.Add(points[0]);
			}
			return (curvedPoints.ToArray());
		}
		/// <summary>
		/// Resamples the curve to the desired amount of points.
		/// <para></para>
		/// </summary>
		/// <param name="src"></param>
		/// <param name="desiredPoints"></param>
		/// <returns></returns>
		public static IList<Vector3> ResampleCurve(this IList<Vector3> src, int desiredPoints) {
			List<Vector3> current = new(desiredPoints);
			float sampleDiff = 1f / desiredPoints;

			for (int i = 0; i < desiredPoints; i++) {
				current.Add(src.SampleCurve(sampleDiff * i));
			}

			return current;
		}
		/// <summary>
		/// Adds additional interpolated points inbetween the src points.
		/// <para>Iteraions: Doubles the amount of points iteratively</para>
		/// </summary>
		/// <param name="src"></param>
		/// <param name="iterations"></param>
		/// <returns></returns>
		public static IList<Vector3> SmoothCurve(this IList<Vector3> src, int iterations) {
			List<Vector3> current = new(src);
			for (int i = 0; i < iterations; i++) {
				List<Vector3> tmp = new();
				for (int p = 0; p < current.Count - 1; p++) {
					Vector3 lerpedPoint = Vector3.Lerp(current[p], current[p + 1], 0.5f);
					tmp.Add(current[p]);
					tmp.Add(lerpedPoint);
				}
				tmp.Add(current[current.Count - 1]);
				current = new List<Vector3>(tmp);
			}
			return current;
		}
		public static List<Vector3> CalculateDerivative(this IList<Vector3> points) {
			List<Vector3> derivative = new List<Vector3>();

			int pointsCount = points.Count;

			if (pointsCount < 2) {
				UnityEngine.Debug.LogError("Insufficient points to calculate the derivative.");
				return null;
			}

			for (int i = 0; i < pointsCount - 1; i++) {
				Vector3 currentPoint = points[i];
				Vector3 nextPoint = points[i + 1];

				// Calculate the finite difference (derivative)
				Vector3 derivativePoint = (nextPoint - currentPoint);

				// Add the derivative point to the result
				derivative.Add(derivativePoint);
			}

			// The last point doesn't have a corresponding derivative in this simple approach
			derivative.Add(Vector3.zero);

			return derivative;
		}
		/// <summary>
		/// Samples a curve at given [t].
		/// </summary>
		/// <param name="points"></param>
		/// <param name="t"></param>
		/// <returns></returns>
		public static Vector3 SampleCurve(this IList<Vector3> points, float t) {
			int pointsCount = points.Count;

			if (pointsCount < 2) {
				UnityEngine.Debug.LogError("Insufficient points to sample a curve.");
				return Vector3.zero;
			}

			// Ensure t is in the range [0, 1]
			t = Mathf.Clamp01(t);

			float totalLength = 0f;
			float[] segmentLengths = new float[pointsCount - 1];

			for (int i = 0; i < pointsCount - 1; i++) {
				float segmentLength = (points[i + 1] - points[i]).magnitude;
				segmentLengths[i] = segmentLength;
				totalLength += segmentLength;
			}

			float targetLength = t * totalLength;
			float currentLength = 0f;

			for (int i = 0; i < pointsCount - 1; i++) {
				currentLength += segmentLengths[i];

				if (currentLength >= targetLength) {
					float tWithinSegment = 1.0f - ((currentLength - targetLength) / segmentLengths[i]);
					return Vector3.Lerp(points[i], points[i + 1], tWithinSegment);
				}
			}

			// This should not happen, but just in case
			return points[pointsCount - 1];
		}
		public static Vector3 SampleCurvePointDir(this IList<Vector3> points, float t) {
			int pointsCount = points.Count;

			if (pointsCount < 2) {
				UnityEngine.Debug.LogError("Insufficient points to sample a direction curve.");
				return Vector3.zero;
			}

			// Ensure t is in the range [0, 1]
			t = Mathf.Clamp01(t);

			float totalLength = 0f;
			float[] segmentLengths = new float[pointsCount - 1];

			for (int i = 0; i < pointsCount - 1; i++) {
				Vector3 direction = (points[i + 1] - points[i]).normalized;
				float segmentLength = direction.magnitude;
				segmentLengths[i] = segmentLength;
				totalLength += segmentLength;
			}

			float targetLength = t * totalLength;
			float currentLength = 0f;

			for (int i = 0; i < pointsCount - 1; i++) {
				currentLength += segmentLengths[i];

				if (currentLength >= targetLength) {
					float tWithinSegment = 1.0f - ((currentLength - targetLength) / segmentLengths[i]);
					Vector3 direction = (points[i + 1] - points[i]).normalized;
					return Vector3.Lerp(direction, direction, tWithinSegment);
				}
			}

			// This should not happen, but just in case
			return points[pointsCount - 1].normalized;
		}
		public static float GetTotalDistance(this IList<Vector3> points) {
			float dist = 0;

			if (points.Count >= 2) {
				Vector3 p = points[0];

				for(int i = 1; i < points.Count; i++) {
					dist += Vector3.Distance(p, points[i]);
					p = points[i];
				}
			}

			return dist;
		}
		#endregion

		#region CatmullRom
		public static Vector3 CatmullRomInterpolate(IList<Vector3> list, float t) {
			int lastIndex = list.Count - 1;

			// Calculate the index of the segment based on the parameter 't'
			int startIndex = Mathf.FloorToInt(t);
			startIndex = Mathf.Clamp(startIndex, 0, lastIndex - 1);

			// Calculate the fractional part of 't' within the segment
			float tFraction = t - startIndex;

			// Retrieve control points for the segment
			Vector3 p0 = list[Mathf.Max(startIndex - 1, 0)];
			Vector3 p1 = list[startIndex];
			Vector3 p2 = list[Mathf.Min(startIndex + 1, lastIndex)];
			Vector3 p3 = list[Mathf.Min(startIndex + 2, lastIndex)];

			// Catmull-Rom interpolation formula
			return 0.5f * (
				(-p0 + 3f * p1 - 3f * p2 + p3) * (tFraction * tFraction * tFraction)
				+ (2f * p0 - 5f * p1 + 4f * p2 - p3) * (tFraction * tFraction)
				+ (-p0 + p2) * tFraction
				+ 2f * p1
			);
		}
		public static Vector3 CatmullRomInterpolate(IList<Vector3> list, int startIndex, float t) {
			Vector3 p0 = list[Mathf.Max(startIndex - 1, 0)];
			Vector3 p1 = list[startIndex];
			Vector3 p2 = list[Mathf.Min(startIndex + 1, list.Count - 1)];
			Vector3 p3 = list[Mathf.Min(startIndex + 2, list.Count - 1)];

			return 0.5f * (
				(-p0 + 3f * p1 - 3f * p2 + p3) * (t * t * t)
				+ (2f * p0 - 5f * p1 + 4f * p2 - p3) * (t * t)
				+ (-p0 + p2) * t
				+ 2f * p1
			);
		}
		public static float InverseCatmullRomInterpolate(IList<Vector3> list, Vector3 targetPoint, float epsilon = 0.001f, int maxIterations = 1000) {
			int startIndex = 0; // Assuming a default starting index of 0; adjust based on your use case.

			float minTime = 0f;
			float maxTime = 1f;

			float closestTime = 0f;
			float closestDistance = float.MaxValue;

			for (int i = 0; i < maxIterations; i++) {
				float midTime = (minTime + maxTime) * 0.5f;
				Vector3 midPoint = CatmullRomInterpolate(list, startIndex, midTime);

				float sqrDistance = (midPoint - targetPoint).sqrMagnitude;

				if (sqrDistance < closestDistance) {
					closestDistance = sqrDistance;
					closestTime = midTime;
				}

				if (sqrDistance < epsilon * epsilon) {
					// If the distance is sufficiently small, return the current time parameter.
					return midTime;
				} else if (Vector3.Dot(midPoint - targetPoint, CatmullRomInterpolate(list, startIndex, midTime + epsilon) - targetPoint) > 0) {
					maxTime = midTime;
				} else {
					minTime = midTime;
				}
			}

			// If the maximum number of iterations is reached, return the time parameter of the closest point found.
			return closestTime;
		}
		#endregion

#if UNITY_EDITOR
		public static void ExtractCurvesFromLibrary() {
			//UnityEditor.CurvePresetLibrary
			Object curves = Resources.Load<Object>(CurvePresetLibraryName);
			System.Reflection.FieldInfo presetListInfo = curves.GetType().GetField("m_Presets", BindingFlags.NonPublic | BindingFlags.GetField | BindingFlags.Instance);

			int c = 0;
			foreach (var item in presetListInfo.GetValue(curves) as IEnumerable) {
				System.Reflection.FieldInfo curveInfo = item.GetType().GetField("m_Curve", BindingFlags.NonPublic | BindingFlags.GetField | BindingFlags.Instance);
				AnimationCurve curve = curveInfo.GetValue(item) as AnimationCurve;

				CurvePreset preset = ScriptableObject.CreateInstance<CurvePreset>();
				EaseType type = (EaseType)c;
				preset.name = "preset_" + type;
				preset.type = type;
				preset.curve = curve;
				AssetDatabase.CreateAsset(preset, $"{CurvePresetSaveFolder}/{type}.asset");
				AssetDatabase.SaveAssets();
				c++;
			}
			AssetDatabase.Refresh();
		}
#endif
	}
}