using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class WrappedThreadPin : MonoBehaviour
{
	[Header("Cutting")]
	[SerializeField] LayerMask hazardLayer;
	[SerializeField] float cutFallGravity = 6f;
	[SerializeField] float cutSeparationImpulse = 0.15f;
	[SerializeField] float cutDamping = 0.96f;
	[SerializeField] int cutConstraintIterations = 3;
	[SerializeField] float cutEndOffset = 0.03f;

	[Header("Wrapping")]
	[SerializeField] LayerMask wallLayer;
	[SerializeField] int maxWrapPoints = 16;
	[SerializeField] int maxWrapPointsPerCollider = 2;
	[SerializeField] float wrapPointPadding = 0.05f;
	[SerializeField] float linecastEndpointPadding = 0.03f;
	[SerializeField] float wrapPushDotThreshold = 0.1f;

	[Header("Rendering")]
	[SerializeField] float needleEyeYOffset = 0.75f;
	[SerializeField] float visualSegmentLength = 0.15f;
	[SerializeField] bool useSag = true;
	[SerializeField] float sagPerUnit = 0.06f;
	[SerializeField] float maxSag = 0.35f;
	[SerializeField] float sagSmoothTime = 0.12f;
	[SerializeField] int lineCornerVertices = 4;

	[Header("Needle Arc")]
	[SerializeField] int needleArcSamples = 5;
	[SerializeField] float needleArcStrength = 0.35f;
	[SerializeField] float needleArcReturnSpeed = 12f;
	[SerializeField] float maxNeedleArcOffset = 0.5f;

	public static Action<bool> CutEvent;
	
	private Transform pinTarget;
	public bool pinned;

	private LineRenderer lineRenderer;
	private LineRenderer looseRenderer;
	private ContactFilter2D wallFilter;
	private ContactFilter2D hazardFilter;

	private readonly RaycastHit2D[] lineHits = new RaycastHit2D[8];
	private readonly RaycastHit2D[] hazardHits = new RaycastHit2D[4];
	private readonly List<WrapPoint> wrapPoints = new List<WrapPoint>();
	private readonly List<Vector2> controlPoints = new List<Vector2>();
	private readonly List<Vector2> candidatePoints = new List<Vector2>();
	private readonly List<Vector3> renderPositions = new List<Vector3>();
	private readonly List<int> renderSegmentInsertIndices = new List<int>();
	private readonly List<Vector2> spanSamples = new List<Vector2>();
	private readonly List<float> currentSpanSags = new List<float>();
	private readonly List<float> spanSagVelocities = new List<float>();
	private readonly Dictionary<Collider2D, Vector2> previousColliderCenters = new Dictionary<Collider2D, Vector2>();
	private readonly Dictionary<Collider2D, Vector2> colliderFrameDeltas = new Dictionary<Collider2D, Vector2>();
	private readonly HashSet<Collider2D> activeWrappedColliders = new HashSet<Collider2D>();
	private readonly List<Collider2D> staleWrappedColliders = new List<Collider2D>();
	private Vector3[] renderBuffer = new Vector3[0];
	private Vector3[] looseRenderBuffer = new Vector3[0];
	private Vector2 previousStartAnchor;
	private Vector2 needleArcOffset;
	private bool hasPreviousStartAnchor;
	private bool hasPendingVisualWrapHit;
	private RaycastHit2D pendingVisualWrapHit;
	private Vector2 pendingVisualWrapFrom;
	private Vector2 pendingVisualWrapTo;
	private int pendingVisualWrapInsertIndex;
	private bool addedWrapPointThisFrame;
	private bool cut;
	private CutPiece startPiece;
	private CutPiece endPiece;

	private static int sortingOrder = 10;

	private void Awake()
	{
		lineRenderer = GetComponent<LineRenderer>();
		lineRenderer.numCornerVertices = Mathf.Max(lineRenderer.numCornerVertices, lineCornerVertices);
		lineRenderer.sortingOrder = sortingOrder;
		sortingOrder++;
		looseRenderer = CreateLooseRenderer();
		
		wallFilter = new ContactFilter2D();
		wallFilter.SetLayerMask(wallLayer);
		wallFilter.useTriggers = false;

		hazardFilter = new ContactFilter2D();
		hazardFilter.SetLayerMask(hazardLayer);
		hazardFilter.useTriggers = true;
	}

	private LineRenderer CreateLooseRenderer()
	{
		GameObject looseObject = new GameObject("Cut Rope Piece");
		looseObject.transform.SetParent(transform, false);

		LineRenderer renderer = looseObject.AddComponent<LineRenderer>();
		renderer.useWorldSpace = lineRenderer.useWorldSpace;
		renderer.sharedMaterial = lineRenderer.sharedMaterial;
		renderer.widthMultiplier = lineRenderer.widthMultiplier;
		renderer.widthCurve = lineRenderer.widthCurve;
		renderer.colorGradient = lineRenderer.colorGradient;
		renderer.numCornerVertices = lineRenderer.numCornerVertices;
		renderer.numCapVertices = lineRenderer.numCapVertices;
		renderer.textureMode = lineRenderer.textureMode;
		renderer.sortingLayerID = lineRenderer.sortingLayerID;
		renderer.sortingOrder = lineRenderer.sortingOrder + 1;
		renderer.enabled = false;

		return renderer;
	}

	private void LateUpdate()
	{
		if (pinTarget == null)
		{
			lineRenderer.positionCount = 0;
			looseRenderer.positionCount = 0;
			return;
		}

		if (cut)
		{
			UpdateCutPieces();
			RenderCutPieces();
			return;
		}

		Vector2 startAnchor = GetStartAnchor();
		Vector2 endAnchor = transform.position;

		addedWrapPointThisFrame = false;
		UpdateNeedleArc(startAnchor);
		ClearPendingVisualWrapHit();
		UpdateWrapTopology(startAnchor, endAnchor);
		BuildRenderPositions(startAnchor, endAnchor);

		if (TryAddWrapPointFromVisualRope())
		{
			ClearPendingVisualWrapHit();
			UpdateWrapTopology(startAnchor, endAnchor);
			BuildRenderPositions(startAnchor, endAnchor);
		}

		if (TryCutFromHazard())
		{
			UpdateCutPieces();
			RenderCutPieces();
			return;
		}

		lineRenderer.positionCount = renderPositions.Count;
		EnsureRenderBufferSize(renderPositions.Count);
		for (int i = 0; i < renderPositions.Count; i++)
			renderBuffer[i] = renderPositions[i];
		lineRenderer.SetPositions(renderBuffer);
	}

	private bool TryCutFromHazard()
	{
		for (int i = 0; i < renderPositions.Count - 1; i++)
		{
			Vector2 from = renderPositions[i];
			Vector2 to = renderPositions[i + 1];

			int hitCount = Physics2D.Linecast(from, to, hazardFilter, hazardHits);
			if (hitCount <= 0)
				continue;

			RaycastHit2D hit = hazardHits[0];
			for (int hitIndex = 1; hitIndex < hitCount; hitIndex++)
			{
				if (hazardHits[hitIndex].fraction < hit.fraction)
					hit = hazardHits[hitIndex];
			}

			CutAt(i, hit.point);
			return true;
		}

		return false;
	}

	private bool TryAddWrapPointFromVisualRope()
	{
		if (hasPendingVisualWrapHit && TryInsertWrapPoint(pendingVisualWrapHit, pendingVisualWrapFrom, pendingVisualWrapTo, pendingVisualWrapInsertIndex))
			return true;

		int segmentCount = Mathf.Min(renderPositions.Count - 1, renderSegmentInsertIndices.Count);
		for (int i = 0; i < segmentCount; i++)
		{
			Vector2 from = renderPositions[i];
			Vector2 to = renderPositions[i + 1];

			if (!TryGetBlockingHit(from, to, out RaycastHit2D hit))
				continue;

			if (TryInsertWrapPoint(hit, from, to, renderSegmentInsertIndices[i]))
				return true;
		}

		return false;
	}

	private bool TryInsertWrapPoint(RaycastHit2D hit, Vector2 from, Vector2 to, int insertIndex)
	{
		if (hit.collider == null || IsInLayerMask(hit.collider, hazardLayer))
			return false;

		if (!CanStoreWrapPoint(hit.collider))
			return false;

		if (!TryCreateWrapPoint(hit, from, to, out WrapPoint wrapPoint))
		{
			Vector2 fallbackPoint = hit.point + hit.normal * wrapPointPadding;
			if (IsDuplicateWrapPoint(fallbackPoint))
				return false;

			wrapPoint = new WrapPoint(fallbackPoint, hit.collider, GetWrapTurnSign(from, fallbackPoint, to));
		}

		StoreWrapPoint(insertIndex, wrapPoint);
		return true;
	}

	private void ClearPendingVisualWrapHit()
	{
		hasPendingVisualWrapHit = false;
		pendingVisualWrapHit = default;
		pendingVisualWrapFrom = Vector2.zero;
		pendingVisualWrapTo = Vector2.zero;
		pendingVisualWrapInsertIndex = 0;
	}

	private void RecordPendingVisualWrapHit(RaycastHit2D hit, Vector2 from, Vector2 to, int insertIndex)
	{
		if (hasPendingVisualWrapHit || hit.collider == null || IsInLayerMask(hit.collider, hazardLayer))
			return;

		hasPendingVisualWrapHit = true;
		pendingVisualWrapHit = hit;
		pendingVisualWrapFrom = from;
		pendingVisualWrapTo = to;
		pendingVisualWrapInsertIndex = insertIndex;
	}

	private bool IsInLayerMask(Collider2D collider, LayerMask layerMask)
	{
		return ((1 << collider.gameObject.layer) & layerMask.value) != 0;
	}

	private void CutAt(int segmentIndex, Vector2 cutPoint)
	{
		Vector2 segmentStart = renderPositions[segmentIndex];
		Vector2 segmentEnd = renderPositions[segmentIndex + 1];
		Vector2 tangent = segmentEnd - segmentStart;
		if (tangent.sqrMagnitude < 0.0001f)
			tangent = Vector2.right;
		tangent.Normalize();

		Vector2 normal = Vector2.Perpendicular(tangent);

		startPiece = new CutPiece(true, false);
		for (int i = 0; i <= segmentIndex; i++)
			startPiece.Add(renderPositions[i]);
		startPiece.Add(cutPoint + normal * cutEndOffset);

		endPiece = new CutPiece(false, true);
		endPiece.Add(cutPoint - normal * cutEndOffset);
		for (int i = segmentIndex + 1; i < renderPositions.Count; i++)
			endPiece.Add(renderPositions[i]);

		startPiece.KickEnd(normal * cutSeparationImpulse);
		endPiece.KickStart(-normal * cutSeparationImpulse);

		cut = true;
		looseRenderer.enabled = true;
		looseRenderer.colorGradient = lineRenderer.colorGradient;
		needleArcOffset = Vector2.zero;
		
		CutEvent?.Invoke(!pinned);
	}

	private void UpdateCutPieces()
	{
		float dt = Mathf.Min(Time.deltaTime, 0.033f);
		Vector2 acceleration = Vector2.down * cutFallGravity;

		startPiece.SetStartAnchor(GetStartAnchor());
		endPiece.SetEndAnchor(transform.position);

		startPiece.Simulate(acceleration, cutDamping, dt, cutConstraintIterations);
		endPiece.Simulate(acceleration, cutDamping, dt, cutConstraintIterations);
	}

	private void RenderCutPieces()
	{
		RenderCutPiece(lineRenderer, startPiece, ref renderBuffer);
		RenderCutPiece(looseRenderer, endPiece, ref looseRenderBuffer);
	}

	private void RenderCutPiece(LineRenderer renderer, CutPiece piece, ref Vector3[] buffer)
	{
		if (piece == null || piece.Count == 0)
		{
			renderer.positionCount = 0;
			return;
		}

		if (buffer.Length != piece.Count)
			buffer = new Vector3[piece.Count];

		piece.CopyPositions(buffer);
		renderer.positionCount = piece.Count;
		renderer.SetPositions(buffer);
	}

	private Vector2 GetStartAnchor()
	{
		if (pinned)
			return pinTarget.position;

		return pinTarget.position + pinTarget.up * needleEyeYOffset;
	}

	private void UpdateNeedleArc(Vector2 startAnchor)
	{
		if (!hasPreviousStartAnchor)
		{
			previousStartAnchor = startAnchor;
			hasPreviousStartAnchor = true;
			return;
		}

		Vector2 anchorDelta = startAnchor - previousStartAnchor;
		previousStartAnchor = startAnchor;

		if (!pinned)
			needleArcOffset -= anchorDelta * needleArcStrength;

		float maxOffsetSqr = maxNeedleArcOffset * maxNeedleArcOffset;
		if (needleArcOffset.sqrMagnitude > maxOffsetSqr)
			needleArcOffset = needleArcOffset.normalized * maxNeedleArcOffset;

		float decay = 1f - Mathf.Exp(-needleArcReturnSpeed * Time.deltaTime);
		needleArcOffset = Vector2.Lerp(needleArcOffset, Vector2.zero, decay);
	}

	private void UpdateWrapTopology(Vector2 startAnchor, Vector2 endAnchor)
	{
		RemoveUnneededWrapPoints(startAnchor, endAnchor);

		if (addedWrapPointThisFrame || wrapPoints.Count >= MaxAllowedWrapPoints)
			return;

		BuildControlPoints(startAnchor, endAnchor);

		for (int i = 0; i < controlPoints.Count - 1; i++)
		{
			Vector2 from = controlPoints[i];
			Vector2 to = controlPoints[i + 1];

			if (!TryGetBlockingHit(from, to, out RaycastHit2D hit))
				continue;

			if (!CanStoreWrapPoint(hit.collider))
				return;

			if (!TryCreateWrapPoint(hit, from, to, out WrapPoint wrapPoint))
			{
				Vector2 fallbackPoint = hit.point + hit.normal * wrapPointPadding;
				if (IsDuplicateWrapPoint(fallbackPoint))
					return;

				StoreWrapPoint(i, new WrapPoint(fallbackPoint, hit.collider, GetWrapTurnSign(from, fallbackPoint, to)));
				return;
			}

			StoreWrapPoint(i, wrapPoint);
			return;
		}
	}

	private void RemoveUnneededWrapPoints(Vector2 startAnchor, Vector2 endAnchor)
	{
		for (int i = wrapPoints.Count - 1; i >= 0; i--)
		{
			if (!wrapPoints[i].IsValid)
				wrapPoints.RemoveAt(i);
		}

		UpdateColliderMotionTracking();
		TrimExcessWrapPoints();

		for (int i = wrapPoints.Count - 1; i >= 0; i--)
		{
			if (ShouldReleaseWrapPoint(i, startAnchor, endAnchor))
				wrapPoints.RemoveAt(i);
		}
	}

	private void TrimExcessWrapPoints()
	{
		int wrapPointLimit = MaxAllowedWrapPoints;
		while (wrapPoints.Count > wrapPointLimit)
			wrapPoints.RemoveAt(wrapPoints.Count - 1);
	}

	private void UpdateColliderMotionTracking()
	{
		activeWrappedColliders.Clear();
		colliderFrameDeltas.Clear();

		for (int i = 0; i < wrapPoints.Count; i++)
		{
			Collider2D collider = wrapPoints[i].collider;
			if (collider == null || !activeWrappedColliders.Add(collider))
				continue;

			Vector2 currentCenter = collider.bounds.center;
			if (previousColliderCenters.TryGetValue(collider, out Vector2 previousCenter))
				colliderFrameDeltas[collider] = currentCenter - previousCenter;
			else
				colliderFrameDeltas[collider] = Vector2.zero;

			previousColliderCenters[collider] = currentCenter;
		}

		staleWrappedColliders.Clear();
		foreach (Collider2D collider in previousColliderCenters.Keys)
		{
			if (collider == null || !activeWrappedColliders.Contains(collider))
				staleWrappedColliders.Add(collider);
		}

		for (int i = 0; i < staleWrappedColliders.Count; i++)
		{
			Collider2D collider = staleWrappedColliders[i];
			previousColliderCenters.Remove(collider);
			colliderFrameDeltas.Remove(collider);
		}
	}

	private bool ShouldReleaseWrapPoint(int index, Vector2 startAnchor, Vector2 endAnchor)
	{
		Vector2 previous = index == 0 ? startAnchor : wrapPoints[index - 1].Position;
		Vector2 next = index == wrapPoints.Count - 1 ? endAnchor : wrapPoints[index + 1].Position;
		WrapPoint wrapPoint = wrapPoints[index];

		if (!HasLineOfSight(previous, next))
			return false;

		if (pinned && !HasWrapPointColliderMovedThisFrame(wrapPoint))
			return false;

		return !IsWrapPointBeingPushed(wrapPoint);
	}

	private bool IsWrapPointBeingPushed(WrapPoint wrapPoint)
	{
		if (wrapPoint.collider == null ||
			!colliderFrameDeltas.TryGetValue(wrapPoint.collider, out Vector2 colliderDelta) ||
			colliderDelta.sqrMagnitude <= 0.000001f)
			return false;

		Vector2 outwardDirection = wrapPoint.OutwardDirection;
		if (outwardDirection.sqrMagnitude <= 0.0001f)
			return false;

		return Vector2.Dot(colliderDelta.normalized, outwardDirection.normalized) > wrapPushDotThreshold;
	}

	private bool HasWrapPointColliderMovedThisFrame(WrapPoint wrapPoint)
	{
		return wrapPoint.collider != null &&
			colliderFrameDeltas.TryGetValue(wrapPoint.collider, out Vector2 colliderDelta) &&
			colliderDelta.sqrMagnitude > 0.000001f;
	}

	private bool CanStoreWrapPoint(Collider2D collider)
	{
		if (addedWrapPointThisFrame || collider == null || wrapPoints.Count >= MaxAllowedWrapPoints)
			return false;

		int colliderWrapPointCount = 0;
		for (int i = 0; i < wrapPoints.Count; i++)
		{
			if (wrapPoints[i].collider == collider)
				colliderWrapPointCount++;
		}

		return colliderWrapPointCount < MaxAllowedWrapPointsPerCollider;
	}

	private void StoreWrapPoint(int insertIndex, WrapPoint wrapPoint)
	{
		wrapPoints.Insert(Mathf.Clamp(insertIndex, 0, wrapPoints.Count), wrapPoint);
		if (wrapPoint.collider != null && !previousColliderCenters.ContainsKey(wrapPoint.collider))
			previousColliderCenters[wrapPoint.collider] = wrapPoint.collider.bounds.center;

		addedWrapPointThisFrame = true;
	}

	private void BuildControlPoints(Vector2 startAnchor, Vector2 endAnchor)
	{
		controlPoints.Clear();
		controlPoints.Add(startAnchor);

		for (int i = 0; i < wrapPoints.Count; i++)
			controlPoints.Add(wrapPoints[i].Position);

		controlPoints.Add(endAnchor);
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
			wrapPoint = new WrapPoint(bestFullPathPoint, hit.collider, GetWrapTurnSign(from, bestFullPathPoint, to));
			return true;
		}

		if (foundPartialCandidate)
		{
			wrapPoint = new WrapPoint(bestPartialPoint, hit.collider, GetWrapTurnSign(from, bestPartialPoint, to));
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
			if ((wrapPoints[i].Position - candidate).sqrMagnitude <= minDistanceSqr)
				return true;
		}

		return false;
	}

	private static int GetWrapTurnSign(Vector2 from, Vector2 wrapPoint, Vector2 to)
	{
		float cross = Cross(from - wrapPoint, to - wrapPoint);
		if (Mathf.Abs(cross) <= 0.0001f)
			return 0;

		return cross > 0f ? 1 : -1;
	}

	private static float Cross(Vector2 a, Vector2 b)
	{
		return a.x * b.y - a.y * b.x;
	}

	private int MaxAllowedWrapPoints => Mathf.Max(0, maxWrapPoints);
	private int MaxAllowedWrapPointsPerCollider => Mathf.Max(1, maxWrapPointsPerCollider);

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

	private void BuildRenderPositions(Vector2 startAnchor, Vector2 endAnchor)
	{
		renderPositions.Clear();
		renderSegmentInsertIndices.Clear();
		BuildControlPoints(startAnchor, endAnchor);
		EnsureSagState(controlPoints.Count - 1);

		for (int i = 0; i < controlPoints.Count - 1; i++)
		{
			Vector2 from = controlPoints[i];
			Vector2 to = controlPoints[i + 1];
			AddRenderSpan(from, to, i == 0, i);
		}
	}

	private void EnsureRenderBufferSize(int size)
	{
		if (renderBuffer.Length != size)
			renderBuffer = new Vector3[size];
	}

	private void EnsureSagState(int spanCount)
	{
		while (currentSpanSags.Count < spanCount)
		{
			currentSpanSags.Add(0f);
			spanSagVelocities.Add(0f);
		}

		while (currentSpanSags.Count > spanCount)
		{
			int lastIndex = currentSpanSags.Count - 1;
			currentSpanSags.RemoveAt(lastIndex);
			spanSagVelocities.RemoveAt(lastIndex);
		}
	}

	private void AddRenderSpan(Vector2 from, Vector2 to, bool includeStart, int spanIndex)
	{
		float distance = Vector2.Distance(from, to);
		int sampleCount = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Max(visualSegmentLength, 0.01f)));
		bool canUseNeedleArc = CanUseNeedleArc(spanIndex, distance);
		float sagAmount = useSag ? GetSmoothedSagAmount(distance, spanIndex) : 0f;

		if (includeStart)
			renderPositions.Add(from);

		if (useSag && TryBuildSpan(from, to, sampleCount, spanIndex, sagAmount, canUseNeedleArc))
		{
			for (int i = 0; i < spanSamples.Count; i++)
				AddRenderPosition(spanSamples[i], spanIndex);

			return;
		}

		if (useSag && canUseNeedleArc && TryBuildSpan(from, to, sampleCount, spanIndex, sagAmount, false))
		{
			for (int i = 0; i < spanSamples.Count; i++)
				AddRenderPosition(spanSamples[i], spanIndex);

			return;
		}

		if (!useSag && canUseNeedleArc && TryBuildSpan(from, to, sampleCount, spanIndex, 0f, true))
		{
			for (int i = 0; i < spanSamples.Count; i++)
				AddRenderPosition(spanSamples[i], spanIndex);

			return;
		}

		AddStraightSpan(from, to, sampleCount, spanIndex);
	}

	private void AddRenderPosition(Vector2 position, int insertIndex)
	{
		if (renderPositions.Count > 0)
			renderSegmentInsertIndices.Add(insertIndex);

		renderPositions.Add(position);
	}

	private float GetSmoothedSagAmount(float spanDistance, int spanIndex)
	{
		float targetSag = Mathf.Min(maxSag, spanDistance * sagPerUnit);
		float sagVelocity = spanSagVelocities[spanIndex];
		currentSpanSags[spanIndex] = Mathf.SmoothDamp(
			currentSpanSags[spanIndex],
			targetSag,
			ref sagVelocity,
			sagSmoothTime
		);
		spanSagVelocities[spanIndex] = sagVelocity;

		return currentSpanSags[spanIndex];
	}

	private bool TryBuildSpan(Vector2 from, Vector2 to, int sampleCount, int spanIndex, float sagAmount, bool applyNeedleArc)
	{
		spanSamples.Clear();

		Vector2 previous = from;

		for (int i = 1; i <= sampleCount; i++)
		{
			float t = i / (float)sampleCount;
			Vector2 sample = Vector2.Lerp(from, to, t);
			sample += Vector2.down * (Mathf.Sin(t * Mathf.PI) * sagAmount);
			if (applyNeedleArc)
				sample += needleArcOffset * GetNeedleArcFalloff(t, from, to);

			if (TryGetBlockingHit(previous, sample, out RaycastHit2D hit))
			{
				RecordPendingVisualWrapHit(hit, previous, sample, spanIndex);
				return false;
			}

			spanSamples.Add(sample);
			previous = sample;
		}

		return true;
	}

	private void AddStraightSpan(Vector2 from, Vector2 to, int sampleCount, int spanIndex)
	{
		for (int i = 1; i <= sampleCount; i++)
			AddRenderPosition(Vector2.Lerp(from, to, i / (float)sampleCount), spanIndex);
	}

	private bool CanUseNeedleArc(int spanIndex, float spanDistance)
	{
		return !pinned &&
			spanIndex == 0 &&
			needleArcSamples > 0 &&
			spanDistance > 0.0001f &&
			needleArcOffset.sqrMagnitude >= 0.0001f;
	}

	private float GetNeedleArcFalloff(float t, Vector2 from, Vector2 to)
	{
		float spanDistance = Vector2.Distance(from, to);
		float arcReach = Mathf.Clamp01((needleArcSamples * Mathf.Max(visualSegmentLength, 0.01f)) / spanDistance);
		if (arcReach <= 0.0001f || t > arcReach)
			return 0f;

		float localT = Mathf.Clamp01(t / arcReach);
		float easeIn = Mathf.SmoothStep(0f, 1f, localT);
		float easeOut = 1f - Mathf.SmoothStep(0f, 1f, localT);

		return easeIn * easeOut * 4f;
	}

	public void Pin(Transform newPinTarget)
	{
		pinTarget = newPinTarget;
		pinned = true;
		hasPreviousStartAnchor = false;
	}

	public void Needle(Transform needle)
	{
		pinTarget = needle;
		pinned = false;
		needleArcOffset = Vector2.zero;
		hasPreviousStartAnchor = false;
		wrapPoints.Clear();
	}

	private sealed class CutPiece
	{
		private readonly bool anchorStart;
		private readonly bool anchorEnd;
		private readonly List<CutPoint> points = new List<CutPoint>();
		private readonly List<float> segmentLengths = new List<float>();

		public int Count => points.Count;

		public CutPiece(bool anchorStart, bool anchorEnd)
		{
			this.anchorStart = anchorStart;
			this.anchorEnd = anchorEnd;
		}

		public void Add(Vector2 position)
		{
			if (points.Count > 0)
				segmentLengths.Add(Vector2.Distance(points[points.Count - 1].current, position));

			points.Add(new CutPoint(position));
		}

		public void SetStartAnchor(Vector2 position)
		{
			if (!anchorStart || points.Count == 0)
				return;

			CutPoint point = points[0];
			point.current = position;
			point.previous = position;
			points[0] = point;
		}

		public void SetEndAnchor(Vector2 position)
		{
			if (!anchorEnd || points.Count == 0)
				return;

			int index = points.Count - 1;
			CutPoint point = points[index];
			point.current = position;
			point.previous = position;
			points[index] = point;
		}

		public void KickStart(Vector2 velocity)
		{
			if (points.Count == 0 || IsAnchored(0))
				return;

			CutPoint point = points[0];
			point.previous = point.current - velocity;
			points[0] = point;
		}

		public void KickEnd(Vector2 velocity)
		{
			if (points.Count == 0 || IsAnchored(points.Count - 1))
				return;

			int index = points.Count - 1;
			CutPoint point = points[index];
			point.previous = point.current - velocity;
			points[index] = point;
		}

		public void Simulate(Vector2 acceleration, float damping, float dt, int constraintIterations)
		{
			for (int i = 0; i < points.Count; i++)
			{
				if (IsAnchored(i))
					continue;

				CutPoint point = points[i];
				Vector2 velocity = (point.current - point.previous) * damping;
				point.previous = point.current;
				point.current += velocity;
				point.current += acceleration * (dt * dt);
				points[i] = point;
			}

			int iterations = Mathf.Max(0, constraintIterations);
			for (int i = 0; i < iterations; i++)
			{
				ApplyConstraints();
				PinAnchors();
			}
		}

		public void CopyPositions(Vector3[] buffer)
		{
			for (int i = 0; i < points.Count; i++)
				buffer[i] = points[i].current;
		}

		private void ApplyConstraints()
		{
			for (int i = 0; i < points.Count - 1; i++)
			{
				CutPoint current = points[i];
				CutPoint next = points[i + 1];
				Vector2 delta = next.current - current.current;
				float distance = delta.magnitude;
				if (distance <= 0.0001f)
					continue;

				Vector2 correction = delta.normalized * (distance - segmentLengths[i]);
				bool currentAnchored = IsAnchored(i);
				bool nextAnchored = IsAnchored(i + 1);

				if (currentAnchored && nextAnchored)
					continue;

				if (currentAnchored)
					next.current -= correction;
				else if (nextAnchored)
					current.current += correction;
				else
				{
					current.current += correction * 0.5f;
					next.current -= correction * 0.5f;
				}

				points[i] = current;
				points[i + 1] = next;
			}
		}

		private void PinAnchors()
		{
			if (anchorStart && points.Count > 0)
			{
				CutPoint point = points[0];
				point.previous = point.current;
				points[0] = point;
			}

			if (anchorEnd && points.Count > 0)
			{
				int index = points.Count - 1;
				CutPoint point = points[index];
				point.previous = point.current;
				points[index] = point;
			}
		}

		private bool IsAnchored(int index)
		{
			return (anchorStart && index == 0) || (anchorEnd && index == points.Count - 1);
		}

		private struct CutPoint
		{
			public Vector2 current;
			public Vector2 previous;

			public CutPoint(Vector2 position)
			{
				current = position;
				previous = position;
			}
		}
	}

	private readonly struct WrapPoint
	{
		private readonly Vector2 localPosition;
		private readonly Vector2 fallbackWorldPosition;
		private readonly Vector2 localOutwardDirection;
		private readonly Vector2 fallbackWorldOutwardDirection;
		private readonly int wrapSign;
		public readonly Collider2D collider;

		public bool IsValid => collider != null;
		public int WrapSign => wrapSign;

		public Vector2 Position
		{
			get
			{
				if (collider == null)
					return fallbackWorldPosition;

				return collider.transform.TransformPoint(localPosition);
			}
		}

		public Vector2 OutwardDirection
		{
			get
			{
				if (collider == null)
					return fallbackWorldOutwardDirection;

				Vector2 worldDirection = collider.transform.TransformDirection(localOutwardDirection);
				if (worldDirection.sqrMagnitude <= 0.0001f)
					return fallbackWorldOutwardDirection;

				return worldDirection.normalized;
			}
		}

		public WrapPoint(Vector2 worldPosition, Collider2D collider, int wrapSign)
		{
			this.collider = collider;
			this.wrapSign = wrapSign;
			fallbackWorldPosition = worldPosition;
			localPosition = collider != null ? collider.transform.InverseTransformPoint(worldPosition) : worldPosition;

			Vector2 outwardWorldDirection = collider != null ? worldPosition - (Vector2)collider.bounds.center : Vector2.up;
			if (outwardWorldDirection.sqrMagnitude <= 0.0001f)
				outwardWorldDirection = Vector2.up;

			fallbackWorldOutwardDirection = outwardWorldDirection.normalized;
			localOutwardDirection = collider != null
				? ((Vector2)collider.transform.InverseTransformDirection(fallbackWorldOutwardDirection)).normalized
				: fallbackWorldOutwardDirection;
		}
	}
}
