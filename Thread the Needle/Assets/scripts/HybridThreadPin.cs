using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class HybridThreadPin : MonoBehaviour
{
	[SerializeField] Transform pinTarget;
	[SerializeField] float needleEyeYOffest = 0.75f;
	[SerializeField] LayerMask wallLayer;

	[Header("Wrapping")]
	[SerializeField] int maxWrapPoints = 16;
	[SerializeField] float wrapPointPadding = 0.06f;
	[SerializeField] float linecastEndpointPadding = 0.025f;

	[Header("Visual Verlet")]
	[SerializeField] float visualPointSpacing = 0.09f;
	[SerializeField] int constraintIterations = 5;
	[SerializeField] float damping = 0.9f;
	[SerializeField] Vector2 gravity = new Vector2(0f, -3f);
	[SerializeField] float baseReturnStrength = 3f;
	[SerializeField] float invalidReturnStrength = 45f;
	[SerializeField] float maxVisualOffset = 0.55f;

	[Header("Rendering")]
	[SerializeField] int lineCornerVertices = 8;

	public bool pinned;

	private LineRenderer lineRenderer;
	private ContactFilter2D wallFilter;

	private readonly RaycastHit2D[] lineHits = new RaycastHit2D[8];
	private readonly List<WrapPoint> wrapPoints = new List<WrapPoint>();
	private readonly List<Vector2> controlPoints = new List<Vector2>();
	private readonly List<Vector2> candidatePoints = new List<Vector2>();
	private readonly List<Vector2> basePoints = new List<Vector2>();
	private readonly List<bool> anchorPoints = new List<bool>();
	private readonly List<SpanRange> spanRanges = new List<SpanRange>();
	private readonly List<VisualPoint> visualPoints = new List<VisualPoint>();
	private readonly List<Vector3> renderPositions = new List<Vector3>();

	private Vector3[] renderBuffer = new Vector3[0];

	private void Awake()
	{
		lineRenderer = GetComponent<LineRenderer>();
		lineRenderer.numCornerVertices = Mathf.Max(lineRenderer.numCornerVertices, lineCornerVertices);

		wallFilter = new ContactFilter2D();
		wallFilter.SetLayerMask(wallLayer);
		wallFilter.useTriggers = false;
	}

	private void FixedUpdate()
	{
		if (pinTarget == null)
			return;

		Vector2 startAnchor = GetStartAnchor();
		Vector2 endAnchor = transform.position;

		UpdateWrapTopology(startAnchor, endAnchor);
		BuildBasePath(startAnchor, endAnchor);
		SyncVisualPointsToBase();
		SimulateVisualRope(Time.fixedDeltaTime);
	}

	private void LateUpdate()
	{
		if (pinTarget == null)
		{
			lineRenderer.positionCount = 0;
			return;
		}

		BuildRenderPositions();

		lineRenderer.positionCount = renderPositions.Count;
		EnsureRenderBufferSize(renderPositions.Count);
		for (int i = 0; i < renderPositions.Count; i++)
			renderBuffer[i] = renderPositions[i];
		lineRenderer.SetPositions(renderBuffer);
	}

	private Vector2 GetStartAnchor()
	{
		if (pinned)
			return pinTarget.position;

		return pinTarget.position + pinTarget.up * needleEyeYOffest;
	}

	private void UpdateWrapTopology(Vector2 startAnchor, Vector2 endAnchor)
	{
		RemoveUnneededWrapPoints(startAnchor, endAnchor);

		for (int iteration = 0; iteration < maxWrapPoints; iteration++)
		{
			BuildControlPoints(startAnchor, endAnchor);

			bool addedWrapPoint = false;
			for (int i = 0; i < controlPoints.Count - 1; i++)
			{
				Vector2 from = controlPoints[i];
				Vector2 to = controlPoints[i + 1];

				if (!TryGetBlockingHit(from, to, out RaycastHit2D hit))
					continue;

				if (!TryCreateWrapPoint(hit, from, to, out WrapPoint wrapPoint))
					wrapPoint = new WrapPoint(hit.point + hit.normal * wrapPointPadding, hit.collider);

				wrapPoints.Insert(i, wrapPoint);
				addedWrapPoint = true;
				break;
			}

			if (!addedWrapPoint)
				return;
		}
	}

	private void RemoveUnneededWrapPoints(Vector2 startAnchor, Vector2 endAnchor)
	{
		for (int i = wrapPoints.Count - 1; i >= 0; i--)
		{
			Vector2 previous = i == 0 ? startAnchor : wrapPoints[i - 1].position;
			Vector2 next = i == wrapPoints.Count - 1 ? endAnchor : wrapPoints[i + 1].position;

			if (HasLineOfSight(previous, next))
				wrapPoints.RemoveAt(i);
		}
	}

	private void BuildControlPoints(Vector2 startAnchor, Vector2 endAnchor)
	{
		controlPoints.Clear();
		controlPoints.Add(startAnchor);

		for (int i = 0; i < wrapPoints.Count; i++)
			controlPoints.Add(wrapPoints[i].position);

		controlPoints.Add(endAnchor);
	}

	private void BuildBasePath(Vector2 startAnchor, Vector2 endAnchor)
	{
		basePoints.Clear();
		anchorPoints.Clear();
		spanRanges.Clear();
		BuildControlPoints(startAnchor, endAnchor);

		basePoints.Add(controlPoints[0]);
		anchorPoints.Add(true);

		for (int i = 0; i < controlPoints.Count - 1; i++)
		{
			int spanStartIndex = basePoints.Count - 1;
			Vector2 from = controlPoints[i];
			Vector2 to = controlPoints[i + 1];
			float distance = Vector2.Distance(from, to);
			int sampleCount = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Max(visualPointSpacing, 0.01f)));

			for (int sampleIndex = 1; sampleIndex <= sampleCount; sampleIndex++)
			{
				float t = sampleIndex / (float)sampleCount;
				basePoints.Add(Vector2.Lerp(from, to, t));
				anchorPoints.Add(sampleIndex == sampleCount);
			}

			spanRanges.Add(new SpanRange(spanStartIndex, basePoints.Count - 1));
		}
	}

	private void SyncVisualPointsToBase()
	{
		while (visualPoints.Count < basePoints.Count)
			visualPoints.Add(new VisualPoint(basePoints[visualPoints.Count]));

		while (visualPoints.Count > basePoints.Count)
			visualPoints.RemoveAt(visualPoints.Count - 1);

		for (int i = 0; i < visualPoints.Count; i++)
		{
			VisualPoint point = visualPoints[i];
			if (anchorPoints[i])
			{
				point.current = basePoints[i];
				point.previous = basePoints[i];
			}
			else
			{
				Vector2 offset = point.current - basePoints[i];
				if (offset.sqrMagnitude > maxVisualOffset * maxVisualOffset)
				{
					offset = offset.normalized * maxVisualOffset;
					point.current = basePoints[i] + offset;
					point.previous = Vector2.Lerp(point.previous, point.current, 0.5f);
				}
			}

			visualPoints[i] = point;
		}
	}

	private void SimulateVisualRope(float dt)
	{
		dt = Mathf.Min(dt, 0.033f);
		float baseReturn = 1f - Mathf.Exp(-baseReturnStrength * dt);

		for (int i = 0; i < visualPoints.Count; i++)
		{
			VisualPoint point = visualPoints[i];
			if (anchorPoints[i])
			{
				point.current = basePoints[i];
				point.previous = basePoints[i];
				visualPoints[i] = point;
				continue;
			}

			Vector2 velocity = (point.current - point.previous) * damping;
			point.previous = point.current;
			point.current += velocity;
			point.current += gravity * (dt * dt);
			point.current = Vector2.Lerp(point.current, basePoints[i], baseReturn);

			visualPoints[i] = point;
		}

		for (int iteration = 0; iteration < constraintIterations; iteration++)
		{
			ApplyVisualConstraints();
			PinAnchorPoints();
		}
	}

	private void ApplyVisualConstraints()
	{
		for (int i = 0; i < visualPoints.Count - 1; i++)
		{
			VisualPoint current = visualPoints[i];
			VisualPoint next = visualPoints[i + 1];
			float targetDistance = Vector2.Distance(basePoints[i], basePoints[i + 1]);
			Vector2 delta = next.current - current.current;
			float distance = delta.magnitude;

			if (distance <= 0.0001f)
				continue;

			Vector2 correction = delta.normalized * (distance - targetDistance);
			bool currentAnchor = anchorPoints[i];
			bool nextAnchor = anchorPoints[i + 1];

			if (currentAnchor && nextAnchor)
				continue;

			if (currentAnchor)
				next.current -= correction;
			else if (nextAnchor)
				current.current += correction;
			else
			{
				current.current += correction * 0.5f;
				next.current -= correction * 0.5f;
			}

			visualPoints[i] = current;
			visualPoints[i + 1] = next;
		}
	}

	private void PinAnchorPoints()
	{
		for (int i = 0; i < visualPoints.Count; i++)
		{
			if (!anchorPoints[i])
				continue;

			VisualPoint point = visualPoints[i];
			point.current = basePoints[i];
			point.previous = basePoints[i];
			visualPoints[i] = point;
		}
	}

	private void BuildRenderPositions()
	{
		renderPositions.Clear();
		if (basePoints.Count == 0)
			return;

		renderPositions.Add(basePoints[0]);

		for (int i = 0; i < spanRanges.Count; i++)
		{
			SpanRange span = spanRanges[i];
			if (IsVisualSpanClear(span.startIndex, span.endIndex))
				AddVisualSpan(span.startIndex, span.endIndex);
			else
			{
				PullSpanBackToBase(span.startIndex, span.endIndex);
				AddBaseSpan(span.startIndex, span.endIndex);
			}
		}
	}

	private bool IsVisualSpanClear(int startIndex, int endIndex)
	{
		for (int i = startIndex; i < endIndex; i++)
		{
			if (!HasLineOfSight(visualPoints[i].current, visualPoints[i + 1].current))
				return false;
		}

		return true;
	}

	private void AddVisualSpan(int startIndex, int endIndex)
	{
		for (int i = startIndex + 1; i <= endIndex; i++)
			renderPositions.Add(visualPoints[i].current);
	}

	private void AddBaseSpan(int startIndex, int endIndex)
	{
		for (int i = startIndex + 1; i <= endIndex; i++)
			renderPositions.Add(basePoints[i]);
	}

	private void PullSpanBackToBase(int startIndex, int endIndex)
	{
		float dt = Mathf.Min(Time.deltaTime, 0.033f);
		float invalidReturn = 1f - Mathf.Exp(-invalidReturnStrength * dt);

		for (int i = startIndex + 1; i < endIndex; i++)
		{
			VisualPoint point = visualPoints[i];
			point.current = Vector2.Lerp(point.current, basePoints[i], invalidReturn);
			point.previous = Vector2.Lerp(point.previous, basePoints[i], invalidReturn);
			visualPoints[i] = point;
		}
	}

	private void EnsureRenderBufferSize(int size)
	{
		if (renderBuffer.Length != size)
			renderBuffer = new Vector3[size];
	}

	private bool TryCreateWrapPoint(RaycastHit2D hit, Vector2 from, Vector2 to, out WrapPoint wrapPoint)
	{
		wrapPoint = default;

		BuildColliderCandidates(hit.collider);
		if (candidatePoints.Count == 0)
			return false;

		bool foundFullPathCandidate = false;
		bool foundPartialCandidate = false;
		float bestFullPathScore = float.PositiveInfinity;
		float bestPartialScore = float.PositiveInfinity;
		Vector2 bestFullPathPoint = Vector2.zero;
		Vector2 bestPartialPoint = Vector2.zero;

		for (int i = 0; i < candidatePoints.Count; i++)
		{
			Vector2 candidate = candidatePoints[i];
			if (IsDuplicateWrapPoint(candidate))
				continue;

			if (!HasLineOfSight(from, candidate))
				continue;

			float score = Vector2.Distance(from, candidate) + Vector2.Distance(candidate, to);

			if (HasLineOfSight(candidate, to))
			{
				if (score < bestFullPathScore)
				{
					foundFullPathCandidate = true;
					bestFullPathScore = score;
					bestFullPathPoint = candidate;
				}
			}
			else if (score < bestPartialScore)
			{
				foundPartialCandidate = true;
				bestPartialScore = score;
				bestPartialPoint = candidate;
			}
		}

		if (foundFullPathCandidate)
		{
			wrapPoint = new WrapPoint(bestFullPathPoint, hit.collider);
			return true;
		}

		if (foundPartialCandidate)
		{
			wrapPoint = new WrapPoint(bestPartialPoint, hit.collider);
			return true;
		}

		return false;
	}

	private void BuildColliderCandidates(Collider2D collider)
	{
		candidatePoints.Clear();

		if (collider is BoxCollider2D boxCollider)
		{
			AddBoxColliderCorners(boxCollider);
			return;
		}

		if (collider is PolygonCollider2D polygonCollider)
		{
			AddPolygonColliderCorners(polygonCollider);
			return;
		}

		AddBoundsCorners(collider.bounds);
	}

	private void AddBoxColliderCorners(BoxCollider2D boxCollider)
	{
		Vector2 center = boxCollider.offset;
		Vector2 halfSize = boxCollider.size * 0.5f;

		AddPaddedColliderPoint(boxCollider, center + new Vector2(-halfSize.x, -halfSize.y));
		AddPaddedColliderPoint(boxCollider, center + new Vector2(-halfSize.x, halfSize.y));
		AddPaddedColliderPoint(boxCollider, center + new Vector2(halfSize.x, halfSize.y));
		AddPaddedColliderPoint(boxCollider, center + new Vector2(halfSize.x, -halfSize.y));
	}

	private void AddPolygonColliderCorners(PolygonCollider2D polygonCollider)
	{
		for (int pathIndex = 0; pathIndex < polygonCollider.pathCount; pathIndex++)
		{
			Vector2[] path = polygonCollider.GetPath(pathIndex);
			for (int i = 0; i < path.Length; i++)
				AddPaddedColliderPoint(polygonCollider, polygonCollider.offset + path[i]);
		}
	}

	private void AddBoundsCorners(Bounds bounds)
	{
		Vector2 min = bounds.min;
		Vector2 max = bounds.max;

		AddPaddedWorldPoint(bounds.center, new Vector2(min.x, min.y));
		AddPaddedWorldPoint(bounds.center, new Vector2(min.x, max.y));
		AddPaddedWorldPoint(bounds.center, new Vector2(max.x, max.y));
		AddPaddedWorldPoint(bounds.center, new Vector2(max.x, min.y));
	}

	private void AddPaddedColliderPoint(Collider2D collider, Vector2 localPoint)
	{
		Vector2 worldPoint = collider.transform.TransformPoint(localPoint);
		AddPaddedWorldPoint(collider.bounds.center, worldPoint);
	}

	private void AddPaddedWorldPoint(Vector2 center, Vector2 worldPoint)
	{
		Vector2 awayFromCollider = worldPoint - center;
		if (awayFromCollider.sqrMagnitude < 0.0001f)
			awayFromCollider = Vector2.up;

		candidatePoints.Add(worldPoint + awayFromCollider.normalized * wrapPointPadding);
	}

	private bool IsDuplicateWrapPoint(Vector2 candidate)
	{
		float minDistance = wrapPointPadding * 2f;
		float minDistanceSqr = minDistance * minDistance;

		for (int i = 0; i < wrapPoints.Count; i++)
		{
			if ((wrapPoints[i].position - candidate).sqrMagnitude <= minDistanceSqr)
				return true;
		}

		return false;
	}

	private bool HasLineOfSight(Vector2 from, Vector2 to)
	{
		return !TryGetBlockingHit(from, to, out _);
	}

	private bool TryGetBlockingHit(Vector2 from, Vector2 to, out RaycastHit2D blockingHit)
	{
		blockingHit = default;

		Vector2 delta = to - from;
		float distance = delta.magnitude;
		if (distance <= 0.0001f)
			return false;

		int hitCount = Physics2D.Linecast(from, to, wallFilter, lineHits);
		float bestFraction = float.PositiveInfinity;

		for (int i = 0; i < hitCount; i++)
		{
			RaycastHit2D hit = lineHits[i];
			if (hit.collider == null)
				continue;

			float endpointPadding = Mathf.Clamp01(linecastEndpointPadding / distance);
			if (hit.fraction <= endpointPadding || hit.fraction >= 1f - endpointPadding)
				continue;

			if (hit.fraction < bestFraction)
			{
				bestFraction = hit.fraction;
				blockingHit = hit;
			}
		}

		return blockingHit.collider != null;
	}

	public void Pin(Transform newPinTarget)
	{
		pinTarget = newPinTarget;
		pinned = true;
	}

	public void Needle(Transform needle)
	{
		pinTarget = needle;
		pinned = false;
		wrapPoints.Clear();
		visualPoints.Clear();
	}

	private struct VisualPoint
	{
		public Vector2 current;
		public Vector2 previous;

		public VisualPoint(Vector2 position)
		{
			current = position;
			previous = position;
		}
	}

	private readonly struct SpanRange
	{
		public readonly int startIndex;
		public readonly int endIndex;

		public SpanRange(int startIndex, int endIndex)
		{
			this.startIndex = startIndex;
			this.endIndex = endIndex;
		}
	}

	private readonly struct WrapPoint
	{
		public readonly Vector2 position;
		public readonly Collider2D collider;

		public WrapPoint(Vector2 position, Collider2D collider)
		{
			this.position = position;
			this.collider = collider;
		}
	}
}
