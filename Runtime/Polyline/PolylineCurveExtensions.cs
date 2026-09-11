using System.Collections.Generic;
using UnityEngine;

namespace Sperlich.Easing.Polyline {

	/// <summary>Utilities for smoothing/resampling/sampling a point-list ("polyline") -- unrelated to
	/// <see cref="SCurve"/>'s keyframe-based easing curves, kept in its own namespace for that reason.
	/// Refactored out of the old <c>SCurves.cs</c> grab-bag: segment-length computation is now shared and
	/// computed once per call instead of once per sample, and the hot allocation paths in
	/// <see cref="GenerateSmoothCurve"/>/<see cref="SmoothCurve"/> were reworked to avoid re-copying the whole
	/// working buffer on every iteration.</summary>
	public static class PolylineCurveExtensions {

		/// <summary>Smoothens a list of points via De Casteljau subdivision. Smoothness: 1-100.</summary>
		public static Vector3[] GenerateSmoothCurve(this IList<Vector3> arrayToCurve, int smoothness) {
			int pointsLength = arrayToCurve.Count;
			if (pointsLength < 2) {
				var copy = new Vector3[pointsLength];
				for (int i = 0; i < pointsLength; i++) copy[i] = arrayToCurve[i];
				return copy;
			}

			int curvedLength = pointsLength * Mathf.Clamp(smoothness, 1, 100) - 1;
			var curvedPoints = new Vector3[curvedLength + 1];
			var work = new Vector3[pointsLength]; // reused across every output point instead of copying arrayToCurve per-point

			for (int pointInTimeOnCurve = 0; pointInTimeOnCurve <= curvedLength; pointInTimeOnCurve++) {
				float t = Mathf.InverseLerp(0, curvedLength, pointInTimeOnCurve);
				for (int i = 0; i < pointsLength; i++) work[i] = arrayToCurve[i];

				for (int j = pointsLength - 1; j > 0; j--) {
					for (int i = 0; i < j; i++) {
						work[i] = (1f - t) * work[i] + t * work[i + 1];
					}
				}
				curvedPoints[pointInTimeOnCurve] = work[0];
			}
			return curvedPoints;
		}

		/// <summary>Resamples the polyline to the desired amount of points, evenly spaced by arc length.</summary>
		public static IList<Vector3> ResampleCurve(this IList<Vector3> src, int desiredPoints) {
			if (src.Count < 2 || desiredPoints <= 0) return new List<Vector3>(src);

			float[] cumulative = ComputeCumulativeLengths(src, out float totalLength);
			var result = new List<Vector3>(desiredPoints);
			for (int i = 0; i < desiredPoints; i++) {
				float t = (float)i / desiredPoints;
				result.Add(totalLength <= 0f ? src[0] : SampleAtLength(src, cumulative, t * totalLength));
			}
			return result;
		}

		/// <summary>Adds additional interpolated points inbetween the src points via iterative midpoint subdivision
		/// -- each iteration doubles the point count.</summary>
		public static IList<Vector3> SmoothCurve(this IList<Vector3> src, int iterations) {
			if (src.Count < 2 || iterations <= 0) return new List<Vector3>(src);

			List<Vector3> current = new(src);
			for (int i = 0; i < iterations; i++) {
				var next = new List<Vector3>(current.Count * 2 - 1);
				for (int p = 0; p < current.Count - 1; p++) {
					next.Add(current[p]);
					next.Add(Vector3.Lerp(current[p], current[p + 1], 0.5f));
				}
				next.Add(current[^1]);
				current = next;
			}
			return current;
		}

		public static List<Vector3> CalculateDerivative(this IList<Vector3> points) {
			int pointsCount = points.Count;
			if (pointsCount < 2) {
				Debug.LogError("Insufficient points to calculate the derivative.");
				return new List<Vector3>();
			}

			var derivative = new List<Vector3>(pointsCount);
			for (int i = 0; i < pointsCount - 1; i++) derivative.Add(points[i + 1] - points[i]);
			derivative.Add(Vector3.zero);
			return derivative;
		}

		/// <summary>Samples a point along the polyline at normalized arc-length position <paramref name="t"/> (0-1).</summary>
		public static Vector3 SampleCurve(this IList<Vector3> points, float t) {
			if (points.Count == 0) { Debug.LogError("Insufficient points to sample a curve."); return Vector3.zero; }
			if (points.Count == 1) return points[0];

			float[] cumulative = ComputeCumulativeLengths(points, out float totalLength);
			if (totalLength <= 0f) return points[0];
			return SampleAtLength(points, cumulative, Mathf.Clamp01(t) * totalLength);
		}

		/// <summary>Direction of the polyline segment at normalized arc-length position <paramref name="t"/> (0-1).</summary>
		public static Vector3 SampleCurvePointDir(this IList<Vector3> points, float t) {
			if (points.Count < 2) { Debug.LogError("Insufficient points to sample a direction curve."); return Vector3.zero; }

			float[] cumulative = ComputeCumulativeLengths(points, out float totalLength);
			if (totalLength <= 0f) return Vector3.zero;

			float targetLength = Mathf.Clamp01(t) * totalLength;
			int segment = FindSegment(cumulative, targetLength);
			return (points[segment + 1] - points[segment]).normalized;
		}

		public static float GetTotalDistance(this IList<Vector3> points) {
			float dist = 0f;
			for (int i = 1; i < points.Count; i++) dist += Vector3.Distance(points[i - 1], points[i]);
			return dist;
		}

		static float[] ComputeCumulativeLengths(IList<Vector3> points, out float totalLength) {
			int n = points.Count;
			var cumulative = new float[n];
			float total = 0f;
			for (int i = 1; i < n; i++) {
				total += Vector3.Distance(points[i - 1], points[i]);
				cumulative[i] = total;
			}
			totalLength = total;
			return cumulative;
		}

		static int FindSegment(float[] cumulative, float targetLength) {
			int lo = 0, hi = cumulative.Length - 2;
			while (lo < hi) {
				int mid = (lo + hi + 1) / 2;
				if (cumulative[mid] <= targetLength) lo = mid; else hi = mid - 1;
			}
			return lo;
		}

		static Vector3 SampleAtLength(IList<Vector3> points, float[] cumulative, float targetLength) {
			int n = points.Count;
			if (targetLength <= 0f) return points[0];
			if (targetLength >= cumulative[n - 1]) return points[n - 1];

			int segment = FindSegment(cumulative, targetLength);
			float segmentLength = cumulative[segment + 1] - cumulative[segment];
			float tWithin = segmentLength <= 0f ? 0f : (targetLength - cumulative[segment]) / segmentLength;
			return Vector3.Lerp(points[segment], points[segment + 1], tWithin);
		}

		#region CatmullRom

		public static Vector3 CatmullRomInterpolate(IList<Vector3> list, float t) {
			int lastIndex = list.Count - 1;
			int startIndex = Mathf.Clamp(Mathf.FloorToInt(t), 0, lastIndex - 1);
			float tFraction = t - startIndex;
			return CatmullRomInterpolate(list, startIndex, tFraction);
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
			const int startIndex = 0;
			float minTime = 0f, maxTime = 1f;
			float closestTime = 0f;
			float closestDistance = float.MaxValue;
			float epsilonSqr = epsilon * epsilon;

			for (int i = 0; i < maxIterations; i++) {
				float midTime = (minTime + maxTime) * 0.5f;
				Vector3 midPoint = CatmullRomInterpolate(list, startIndex, midTime);
				float sqrDistance = (midPoint - targetPoint).sqrMagnitude;

				if (sqrDistance < closestDistance) {
					closestDistance = sqrDistance;
					closestTime = midTime;
				}
				if (sqrDistance < epsilonSqr) return midTime;

				if (Vector3.Dot(midPoint - targetPoint, CatmullRomInterpolate(list, startIndex, midTime + epsilon) - targetPoint) > 0) {
					maxTime = midTime;
				} else {
					minTime = midTime;
				}
			}
			return closestTime;
		}

		#endregion
	}
}
