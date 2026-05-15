using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class WrappedThreadPin : MonoBehaviour
{
	[Header("Cutting")]
	[Tooltip("Layers that cut the thread when the logical thread path crosses them.")]
	[SerializeField] LayerMask hazardLayer;
	[Tooltip("Gravity applied to the loose simulated pieces after the thread is cut.")]
	[SerializeField] float cutFallGravity = 6f;
	[Tooltip("Small sideways impulse applied at the cut point so the separated ends visibly split.")]
	[SerializeField] float cutSeparationImpulse = 0.15f;
	[Tooltip("Velocity damping used by cut-piece Verlet simulation. Lower values lose energy faster.")]
	[SerializeField] float cutDamping = 0.96f;
	[Tooltip("Constraint iterations for keeping cut-piece segment lengths stable after a cut.")]
	[SerializeField] int cutConstraintIterations = 3;
	[Tooltip("Visual spacing between the two thread ends at the cut point.")]
	[SerializeField] float cutEndOffset = 0.03f;

	[Header("Wrapping")]
	[Tooltip("Layers that can block, wrap, or support the thread.")]
	[SerializeField] LayerMask wallLayer;
	[Tooltip("Maximum stored wrap points for this thread segment.")]
	[SerializeField] int maxWrapPoints = 16;
	[Tooltip("Maximum wrap points allowed on one non-composite collider.")]
	[SerializeField] int maxWrapPointsPerCollider = 2;
	[Tooltip("Distance wrap points are padded outward from obstacle corners or hit points.")]
	[SerializeField] float wrapPointPadding = 0.05f;
	[Tooltip("Distance near segment endpoints ignored by linecasts to avoid immediate self-hits at anchors.")]
	[SerializeField] float linecastEndpointPadding = 0.03f;
	[Tooltip("Radius around a pin anchor where hits on that anchor's own collider are ignored.")]
	[SerializeField] float anchorColliderPadding = 0.1f;
	[Tooltip("Release-only tolerance for ignoring a shallow hit on the exact corner currently being unwrapped.")]
	[SerializeField] float releaseCornerGrazingPadding = 0.08f;
	[Tooltip("Search radius around a blocking hit for candidate corners on any collider.")]
	[SerializeField] float candidateRadius = 1.25f;
	[Tooltip("Deprecated alias for older prefab data. Candidate Radius is used for new filtering.")]
	[SerializeField] float compositeCandidateRadius = 1.5f;
	[Tooltip("Dot threshold for detecting when a moving collider is pushing a wrap point outward.")]
	[SerializeField] float wrapPushDotThreshold = 0.1f;
	[Tooltip("Frames a clear bridge must remain clear before an unpinned or moving-collider wrap releases.")]
	[SerializeField] int passiveStraightenClearFrames = 2;
	[Tooltip("Frames a clear bridge must remain clear before a pinned wrap on a static collider releases.")]
	[SerializeField] int pinnedPassiveStraightenClearFrames = 4;
	[Tooltip("Frames a moving collider bridge must remain clear before its wrap point can release.")]
	[SerializeField] int movingColliderBridgeClearFrames = 6;

	[Header("Rendering")]
	[Tooltip("Offset from the needle transform to the thread eye while the thread is attached to the active needle.")]
	[SerializeField] float needleEyeYOffset = 0.75f;
	[Tooltip("Approximate spacing between rendered line samples.")]
	[SerializeField] float visualSegmentLength = 0.15f;
	[Tooltip("Whether render spans can sag visually. Sag is cosmetic and does not create wrap points.")]
	[SerializeField] bool useSag = true;
	[Tooltip("Sag amount per unit of span length.")]
	[SerializeField] float sagPerUnit = 0.06f;
	[Tooltip("Maximum visual sag for one span.")]
	[SerializeField] float maxSag = 0.35f;
	[Tooltip("Sag multiplier used while the thread is wrapped and visually under tension.")]
	[SerializeField] float wrappedSagMultiplier = 0.15f;
	[Tooltip("Smoothing time used when visual sag changes.")]
	[SerializeField] float sagSmoothTime = 0.12f;
	[Tooltip("Minimum LineRenderer corner vertices assigned on Awake.")]
	[SerializeField] int lineCornerVertices = 4;

	[Header("Debug")]
	[Tooltip("Draw logical spans, wrap points, candidates, and recent solver events as Gizmos.")]
	[SerializeField] bool debugOverlay;
	[Tooltip("Draw the debug overlay only when this object is selected.")]
	[SerializeField] bool debugOnlyWhenSelected = true;
	[Tooltip("Radius of debug point markers in world units.")]
	[SerializeField] float debugPointRadius = 0.06f;
	[Tooltip("Color for logical control spans.")]
	[SerializeField] Color debugControlSpanColor = Color.cyan;
	[Tooltip("Color for stored wrap points.")]
	[SerializeField] Color debugWrapPointColor = Color.yellow;
	[Tooltip("Color for candidate wrap points considered this frame.")]
	[SerializeField] Color debugCandidateColor = Color.magenta;
	[Tooltip("Color for candidate corners rejected because they are too far from the blocking hit.")]
	[SerializeField] Color debugRejectedCandidateColor = Color.gray;
	[Tooltip("Color for blocking hits found by the logical solver.")]
	[SerializeField] Color debugBlockingHitColor = Color.red;
	[Tooltip("Color for wrap points inserted this frame.")]
	[SerializeField] Color debugInsertedWrapColor = Color.green;
	[Tooltip("Color for wrap points released this frame.")]
	[SerializeField] Color debugReleasedWrapColor = Color.blue;

	private const int MaxWrapSolveIterations = 8;
	private const int MovingColliderBridgeSampleCount = 48;
	private const int SagClampSearchIterations = 6;
	private const float SagClampEpsilon = 0.0005f;

	public static Action<bool> CutEvent;
	
	private Transform pinTarget;
	[Tooltip("Runtime state: true once this pin has become a fixed thread anchor.")]
	public bool pinned;

	private LineRenderer lineRenderer;
	private LineRenderer looseRenderer;
	private ContactFilter2D wallFilter;
	private ContactFilter2D hazardFilter;

	private readonly RaycastHit2D[] lineHits = new RaycastHit2D[8];
	private readonly RaycastHit2D[] hazardHits = new RaycastHit2D[4];
	private readonly List<WrapPoint> wrapPoints = new List<WrapPoint>();
	private readonly List<Vector2> controlPoints = new List<Vector2>();
	private readonly List<Collider2D> controlPointColliders = new List<Collider2D>();
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
	private readonly List<Collider2D> colliderTrackingKeys = new List<Collider2D>();
	private readonly List<Vector2> debugCandidatePoints = new List<Vector2>();
	private readonly List<Vector2> debugRejectedCandidatePoints = new List<Vector2>();
	private readonly List<Vector2> debugBlockingHits = new List<Vector2>();
	private readonly List<Vector2> debugInsertedWrapPoints = new List<Vector2>();
	private readonly List<Vector2> debugReleasedWrapPoints = new List<Vector2>();
	private Vector3[] renderBuffer = new Vector3[0];
	private Vector3[] looseRenderBuffer = new Vector3[0];
	private Vector2[] colliderPathBuffer = new Vector2[0];
	private bool releasedWrapPointThisFrame;
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
		Physics2D.SyncTransforms();
		
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

		releasedWrapPointThisFrame = false;
		ClearDebugFrame();
		UpdateWrapTopology(startAnchor, endAnchor);
		BuildRenderPositions(startAnchor, endAnchor);

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

			CutAtRenderSegment(i, hit.point);
			return true;
		}

		return false;
	}

	private bool IsInLayerMask(Collider2D collider, LayerMask layerMask)
	{
		return ((1 << collider.gameObject.layer) & layerMask.value) != 0;
	}

	private void CutAtRenderSegment(int segmentIndex, Vector2 cutPoint)
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

	private void ClearDebugFrame()
	{
		if (!debugOverlay)
			return;

		debugCandidatePoints.Clear();
		debugRejectedCandidatePoints.Clear();
		debugBlockingHits.Clear();
		debugInsertedWrapPoints.Clear();
		debugReleasedWrapPoints.Clear();
	}

	private void RecordDebugPoint(List<Vector2> points, Vector2 point)
	{
		if (!debugOverlay)
			return;

		points.Add(point);
	}

	private void RecordDebugPoints(List<Vector2> destination, List<Vector2> source)
	{
		if (!debugOverlay)
			return;

		for (int i = 0; i < source.Count; i++)
			destination.Add(source[i]);
	}

	private Vector2 GetStartAnchor()
	{
		if (pinned)
			return pinTarget.position;

		return pinTarget.position + pinTarget.up * needleEyeYOffset;
	}

	private void UpdateWrapTopology(Vector2 startAnchor, Vector2 endAnchor)
	{
		RemoveUnneededWrapPoints(startAnchor, endAnchor);

		int solveLimit = Mathf.Min(MaxWrapSolveIterations, Mathf.Max(0, MaxAllowedWrapPoints - wrapPoints.Count));
		for (int iteration = 0; iteration < solveLimit; iteration++)
		{
			if (wrapPoints.Count >= MaxAllowedWrapPoints)
				return;

			BuildControlPoints(startAnchor, endAnchor);
			if (!TryAddFirstBlockingWrapPoint())
				return;
		}
	}

	private bool TryAddFirstBlockingWrapPoint()
	{
		for (int i = 0; i < controlPoints.Count - 1; i++)
		{
			Vector2 from = controlPoints[i];
			Vector2 to = controlPoints[i + 1];
			Collider2D fromCollider = GetControlPointCollider(i);
			Collider2D toCollider = GetControlPointCollider(i + 1);

			if (!TryGetBlockingHit(from, to, fromCollider, toCollider, out BlockingHit hit))
				continue;

			RecordDebugPoint(debugBlockingHits, hit.point);
			if (!CanStoreWrapPoint(hit.collider))
				return false;

			if (TryCreateWrapPoint(hit, from, to, fromCollider, toCollider, out WrapPoint wrapPoint))
			{
				StoreWrapPoint(i, wrapPoint);
				return true;
			}

			bool blockedByEndpointCollider = hit.collider == fromCollider || hit.collider == toCollider;
			if (!blockedByEndpointCollider &&
				(TryCreateFallbackWrapPoint(hit, from, to, fromCollider, toCollider, out wrapPoint) ||
				TryCreateSurfaceContactWrapPoint(hit, from, to, fromCollider, out wrapPoint)))
			{
				StoreWrapPoint(i, wrapPoint);
				return true;
			}

			return false;
		}

		return false;
	}
	
	private bool TryCreateSurfaceContactWrapPoint(
		BlockingHit hit,
		Vector2 from,
		Vector2 to,
		Collider2D fromCollider,
		out WrapPoint wrapPoint)
	{
		wrapPoint = default;

		Vector2 contactPoint = hit.point + hit.normal * wrapPointPadding;

		if (IsDuplicateWrapPoint(contactPoint))
			return false;

		// Only require the path into the contact point to be clear.
		// The next solve iteration can handle the remaining blocked span.
		if (!HasLineOfSight(from, contactPoint, fromCollider, hit.collider))
			return false;

		wrapPoint = new WrapPoint(contactPoint, hit.collider, GetWrapTurnSign(from, contactPoint, to));
		return true;
	}

	private void RemoveUnneededWrapPoints(Vector2 startAnchor, Vector2 endAnchor)
	{
		for (int i = wrapPoints.Count - 1; i >= 0; i--)
		{
			if (!wrapPoints[i].IsValid)
				RemoveWrapPointAt(i);
		}

		UpdateColliderMotionTracking();
		TrimExcessWrapPoints();

		if (pinned)
			ReleaseFirstMatchingWrapPoint(wrapPoints.Count - 1, -1, -1, startAnchor, endAnchor);
		else
			ReleaseFirstMatchingWrapPoint(0, wrapPoints.Count, 1, startAnchor, endAnchor);
	}

	private void ReleaseFirstMatchingWrapPoint(int startIndex, int endIndex, int step, Vector2 startAnchor, Vector2 endAnchor)
	{
		if (releasedWrapPointThisFrame)
			return;

		for (int i = startIndex; i != endIndex; i += step)
		{
			if (!ShouldReleaseWrapPoint(i, startAnchor, endAnchor))
				continue;

			RecordDebugPoint(debugReleasedWrapPoints, wrapPoints[i].Position);
			RemoveWrapPointAt(i);
			releasedWrapPointThisFrame = true;
			return;
		}
	}

	private void TrimExcessWrapPoints()
	{
		int wrapPointLimit = MaxAllowedWrapPoints;
		while (wrapPoints.Count > wrapPointLimit)
			RemoveWrapPointAt(wrapPoints.Count - 1);
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
		colliderTrackingKeys.Clear();
		foreach (Collider2D collider in previousColliderCenters.Keys)
			colliderTrackingKeys.Add(collider);

		for (int i = 0; i < colliderTrackingKeys.Count; i++)
		{
			Collider2D collider = colliderTrackingKeys[i];
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
		Collider2D previousCollider = index == 0 ? GetStartAnchorCollider() : wrapPoints[index - 1].collider;
		Collider2D nextCollider = index == wrapPoints.Count - 1 ? GetEndAnchorCollider() : wrapPoints[index + 1].collider;
		WrapPoint wrapPoint = wrapPoints[index];
		bool wrapColliderMoved = HasWrapPointColliderMovedThisFrame(wrapPoint);

		if (wrapColliderMoved && HasMovingColliderBridgeCrossing(previous, next, wrapPoint.collider))
		{
			ResetPassiveBridgeClearFrames(index, wrapPoint);
			return false;
		}

		if (!HasProtectedBridgeClearance(index, previous, next, previousCollider, nextCollider, wrapPoint.Position))
		{
			ResetPassiveBridgeClearFrames(index, wrapPoint);
			return false;
		}

		if (IsWrapPointBeingPushed(wrapPoint))
		{
			ResetPassiveBridgeClearFrames(index, wrapPoint);
			return false;
		}

		if (HasWrapPointCrossedStoredSide(wrapPoint, previous, next))
			return CanPassivelyStraighten(index, wrapPoint, wrapColliderMoved);

		if (!CanPassivelyStraighten(index, wrapPoint, wrapColliderMoved))
			return false;

		return !pinned || wrapColliderMoved || wrapPoint.PassiveBridgeClearFrames > 0;
	}

	private bool HasProtectedBridgeClearance(
		int index,
		Vector2 previous,
		Vector2 next,
		Collider2D previousCollider,
		Collider2D nextCollider,
		Vector2 currentWrapPosition)
	{
		Collider2D currentCollider = wrapPoints[index].collider;
		return !TryGetProtectedBridgeHit(previous, next, previousCollider, nextCollider, currentCollider, currentWrapPosition, out _);
	}

	private bool HasWrapPointCrossedStoredSide(WrapPoint wrapPoint, Vector2 previous, Vector2 next)
	{
		if (wrapPoint.WrapSign == 0)
			return false;

		int currentWrapSign = GetWrapTurnSign(previous, wrapPoint.Position, next);
		return currentWrapSign != 0 && currentWrapSign != wrapPoint.WrapSign;
	}

	/*private bool CanPassivelyStraighten(int index, WrapPoint wrapPoint, bool wrapColliderMoved)
	{
		int requiredClearFrames = pinned && !wrapColliderMoved
			? PinnedPassiveStraightenClearFrameLimit
			: PassiveStraightenClearFrameLimit;
		int clearFrames = Mathf.Min(wrapPoint.PassiveBridgeClearFrames + 1, requiredClearFrames);
		wrapPoints[index] = wrapPoint.WithPassiveBridgeClearFrames(clearFrames);
		return clearFrames >= requiredClearFrames;
	}*/
	
	private bool CanPassivelyStraighten(int index, WrapPoint wrapPoint, bool wrapColliderMoved)
	{
		int requiredClearFrames;

		if (wrapColliderMoved)
			requiredClearFrames = MovingColliderBridgeClearFrameLimit;
		else if (pinned)
			requiredClearFrames = PinnedPassiveStraightenClearFrameLimit;
		else
			requiredClearFrames = PassiveStraightenClearFrameLimit;

		int clearFrames = Mathf.Min(wrapPoint.PassiveBridgeClearFrames + 1, requiredClearFrames);
		wrapPoints[index] = wrapPoint.WithPassiveBridgeClearFrames(clearFrames);

		return clearFrames >= requiredClearFrames;
	}

	private void ResetPassiveBridgeClearFrames(int index, WrapPoint wrapPoint)
	{
		if (wrapPoint.PassiveBridgeClearFrames == 0)
			return;

		wrapPoints[index] = wrapPoint.WithPassiveBridgeClearFrames(0);
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

	private bool HasMovingColliderBridgeCrossing(Vector2 from, Vector2 to, Collider2D collider)
	{
		if (collider == null)
			return false;

		Vector2 delta = to - from;
		if (delta.sqrMagnitude <= 0.00000001f)
			return false;

		for (int i = 1; i < MovingColliderBridgeSampleCount; i++)
		{
			float t = i / (float)MovingColliderBridgeSampleCount;
			Vector2 sample = Vector2.Lerp(from, to, t);
			if (collider.OverlapPoint(sample))
				return true;
		}

		return false;
	}

	private bool CanStoreWrapPoint(Collider2D collider)
	{
		if (collider == null || wrapPoints.Count >= MaxAllowedWrapPoints)
			return false;

		if (collider is CompositeCollider2D)
			return true;

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
		int clampedInsertIndex = Mathf.Clamp(insertIndex, 0, wrapPoints.Count);
		wrapPoints.Insert(clampedInsertIndex, wrapPoint);
		InsertSagStateAtSpan(clampedInsertIndex);
		RecordDebugPoint(debugInsertedWrapPoints, wrapPoint.Position);
		if (wrapPoint.collider != null && !previousColliderCenters.ContainsKey(wrapPoint.collider))
			previousColliderCenters[wrapPoint.collider] = wrapPoint.collider.bounds.center;
	}

	private void RemoveWrapPointAt(int index)
	{
		if (index < 0 || index >= wrapPoints.Count)
			return;

		MergeSagStateAroundRemovedWrap(index);
		wrapPoints.RemoveAt(index);
		EnsureSagState(wrapPoints.Count + 1);
	}

	private void BuildControlPoints(Vector2 startAnchor, Vector2 endAnchor)
	{
		controlPoints.Clear();
		controlPointColliders.Clear();
		AddControlPoint(startAnchor, GetStartAnchorCollider());

		for (int i = 0; i < wrapPoints.Count; i++)
			AddControlPoint(wrapPoints[i].Position, wrapPoints[i].collider);

		AddControlPoint(endAnchor, GetEndAnchorCollider());
	}

	private void AddControlPoint(Vector2 position, Collider2D ownerCollider)
	{
		controlPoints.Add(position);
		controlPointColliders.Add(ownerCollider);
	}

	private Vector2 GetControlPoint(int index, Vector2 fallback)
	{
		if (index < 0 || index >= controlPoints.Count)
			return fallback;

		return controlPoints[index];
	}

	private Collider2D GetControlPointCollider(int index)
	{
		if (index < 0 || index >= controlPointColliders.Count)
			return null;

		return controlPointColliders[index];
	}

	private Collider2D GetStartAnchorCollider()
	{
		return GetParentCollider(pinTarget);
	}

	private Collider2D GetEndAnchorCollider()
	{
		return GetParentCollider(transform);
	}

	private Collider2D GetParentCollider(Transform anchor)
	{
		if (anchor == null || anchor.parent == null)
			return null;

		CompositeCollider2D compositeCollider = anchor.parent.GetComponent<CompositeCollider2D>();
		if (compositeCollider != null)
			return compositeCollider;

		return anchor.parent.GetComponentInParent<Collider2D>();
	}

	private bool TryCreateWrapPoint(
		BlockingHit hit,
		Vector2 from,
		Vector2 to,
		Collider2D fromCollider,
		Collider2D toCollider,
		out WrapPoint wrapPoint)
	{
		wrapPoint = default;

		BuildColliderCandidates(hit);
		if (candidatePoints.Count == 0)
			return false;

		RecordDebugPoints(debugCandidatePoints, candidatePoints);

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

			if (!HasLineOfSight(from, candidate, fromCollider, hit.collider))
				continue;

			float score = Vector2.Distance(from, candidate) + Vector2.Distance(candidate, to);

			if (HasLineOfSight(candidate, to, hit.collider, toCollider))
			{
				if (score < bestFullPathScore)
				{
					foundFullPathCandidate = true;
					bestFullPathScore = score;
					bestFullPathPoint = candidate;
				}
			}
			/*
			else if (score < bestPartialScore)
			{
				foundPartialCandidate = true;
				bestPartialScore = score;
				bestPartialPoint = candidate;
			}
			*/
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

	private bool TryCreateFallbackWrapPoint(
		BlockingHit hit,
		Vector2 from,
		Vector2 to,
		Collider2D fromCollider,
		Collider2D toCollider,
		out WrapPoint wrapPoint)
	{
		wrapPoint = default;

		Vector2 fallbackPoint = hit.point + hit.normal * wrapPointPadding;
		if (IsDuplicateWrapPoint(fallbackPoint))
			return false;

		if (!HasLineOfSight(from, fallbackPoint, fromCollider, hit.collider) ||
			!HasLineOfSight(fallbackPoint, to, hit.collider, toCollider))
			return false;

		wrapPoint = new WrapPoint(fallbackPoint, hit.collider, GetWrapTurnSign(from, fallbackPoint, to));
		return true;
	}

	private void BuildColliderCandidates(BlockingHit hit)
	{
		candidatePoints.Clear();
		Collider2D collider = hit.collider;

		if (collider is BoxCollider2D boxCollider)
		{
			AddBoxColliderCorners(boxCollider, hit.point);
			return;
		}

		if (collider is PolygonCollider2D polygonCollider)
		{
			AddPolygonColliderCorners(polygonCollider, hit.point);
			return;
		}

		if (collider is CompositeCollider2D compositeCollider)
		{
			AddCompositeColliderNearbyCorners(compositeCollider, hit.point, hit.normal);
			return;
		}

		AddBoundsCorners(collider.bounds, hit.point);
	}

	private void AddCompositeColliderNearbyCorners(
	    CompositeCollider2D compositeCollider,
	    Vector2 hitPoint,
	    Vector2 hitNormal)
	{
	    float maxDistanceSqr = CandidateRadiusSqr;

	    float bestDistanceSqr = float.PositiveInfinity;
	    int bestPathIndex = -1;
	    int bestA = -1;
	    int bestB = -1;

	    for (int pathIndex = 0; pathIndex < compositeCollider.pathCount; pathIndex++)
	    {
	        int pointCount = compositeCollider.GetPathPointCount(pathIndex);
	        if (pointCount < 2)
	            continue;

	        EnsureColliderPathBufferSize(pointCount);
	        int actualPointCount = compositeCollider.GetPath(pathIndex, colliderPathBuffer);

	        for (int i = 0; i < actualPointCount; i++)
	        {
	            int j = (i + 1) % actualPointCount;

	            Vector2 a = compositeCollider.transform.TransformPoint(colliderPathBuffer[i]);
	            Vector2 b = compositeCollider.transform.TransformPoint(colliderPathBuffer[j]);

	            Vector2 closest = ClosestPointOnSegment(a, b, hitPoint);
	            float distanceSqr = (closest - hitPoint).sqrMagnitude;

	            if (distanceSqr < bestDistanceSqr)
	            {
	                bestDistanceSqr = distanceSqr;
	                bestPathIndex = pathIndex;
	                bestA = i;
	                bestB = j;
	            }
	        }
	    }

	    if (bestPathIndex < 0)
	        return;

	    if (bestDistanceSqr > maxDistanceSqr)
	        return;

	    int count = compositeCollider.GetPathPointCount(bestPathIndex);
	    EnsureColliderPathBufferSize(count);
	    int actual = compositeCollider.GetPath(bestPathIndex, colliderPathBuffer);

	    if (bestA >= actual || bestB >= actual)
	        return;

	    Vector2 worldA = compositeCollider.transform.TransformPoint(colliderPathBuffer[bestA]);
	    Vector2 worldB = compositeCollider.transform.TransformPoint(colliderPathBuffer[bestB]);

	    AddPaddedCompositePoint(hitPoint, hitNormal, worldA);
	    AddPaddedCompositePoint(hitPoint, hitNormal, worldB);
	}

	private static Vector2 ClosestPointOnSegment(Vector2 a, Vector2 b, Vector2 p)
	{
	    Vector2 ab = b - a;
	    float denom = Vector2.Dot(ab, ab);

	    if (denom <= 0.000001f)
	        return a;

	    float t = Vector2.Dot(p - a, ab) / denom;
	    t = Mathf.Clamp01(t);

	    return a + ab * t;
	}

	private void AddCompositePathPoint(CompositeCollider2D compositeCollider, int pathIndex, int pointIndex, Vector2 hitPoint, Vector2 hitNormal)
	{
		int pointCount = compositeCollider.GetPathPointCount(pathIndex);
		if (pointCount <= 0)
			return;

		int wrappedIndex = pointIndex;
		while (wrappedIndex < 0)
			wrappedIndex += pointCount;
		while (wrappedIndex >= pointCount)
			wrappedIndex -= pointCount;

		EnsureColliderPathBufferSize(pointCount);
		int actualPointCount = compositeCollider.GetPath(pathIndex, colliderPathBuffer);
		if (wrappedIndex >= actualPointCount)
			return;

		Vector2 worldPoint = compositeCollider.transform.TransformPoint(colliderPathBuffer[wrappedIndex]);
		AddPaddedCompositePoint(hitPoint, hitNormal, worldPoint);
	}

	private void EnsureColliderPathBufferSize(int size)
	{
		if (colliderPathBuffer.Length < size)
			colliderPathBuffer = new Vector2[size];
	}

	private void AddPaddedCompositePoint(Vector2 hitPoint, Vector2 hitNormal, Vector2 worldPoint)
	{
		Vector2 paddingDirection = worldPoint - hitPoint;
		if (paddingDirection.sqrMagnitude <= 0.0001f)
			paddingDirection = hitNormal;
		if (paddingDirection.sqrMagnitude <= 0.0001f)
			paddingDirection = Vector2.up;

		candidatePoints.Add(worldPoint + paddingDirection.normalized * wrapPointPadding);
	}

	private void AddBoxColliderCorners(BoxCollider2D boxCollider, Vector2 hitPoint)
	{
		Vector2 center = boxCollider.offset;
		Vector2 halfSize = boxCollider.size * 0.5f;

		AddLocalCandidateIfNearHit(boxCollider, center + new Vector2(-halfSize.x, -halfSize.y), hitPoint);
		AddLocalCandidateIfNearHit(boxCollider, center + new Vector2(-halfSize.x, halfSize.y), hitPoint);
		AddLocalCandidateIfNearHit(boxCollider, center + new Vector2(halfSize.x, halfSize.y), hitPoint);
		AddLocalCandidateIfNearHit(boxCollider, center + new Vector2(halfSize.x, -halfSize.y), hitPoint);
	}
	
	private void AddPolygonColliderCorners(PolygonCollider2D polygonCollider, Vector2 hitPoint)
	{
		for (int pathIndex = 0; pathIndex < polygonCollider.pathCount; pathIndex++)
		{
			Vector2[] path = polygonCollider.GetPath(pathIndex);
			for (int i = 0; i < path.Length; i++)
				AddLocalCandidateIfNearHit(polygonCollider, polygonCollider.offset + path[i], hitPoint);
		}
	}

	private void AddBoundsCorners(Bounds bounds, Vector2 hitPoint)
	{
		Vector2 min = bounds.min;
		Vector2 max = bounds.max;

		AddWorldCandidateIfNearHit(bounds.center, new Vector2(min.x, min.y), hitPoint);
		AddWorldCandidateIfNearHit(bounds.center, new Vector2(min.x, max.y), hitPoint);
		AddWorldCandidateIfNearHit(bounds.center, new Vector2(max.x, max.y), hitPoint);
		AddWorldCandidateIfNearHit(bounds.center, new Vector2(max.x, min.y), hitPoint);
	}

	private void AddLocalCandidateIfNearHit(Collider2D collider, Vector2 localPoint, Vector2 hitPoint)
	{
		Vector2 worldPoint = collider.transform.TransformPoint(localPoint);
		AddWorldCandidateIfNearHit(collider.bounds.center, worldPoint, hitPoint);
	}

	private void AddWorldCandidateIfNearHit(Vector2 center, Vector2 worldPoint, Vector2 hitPoint)
	{
		if ((worldPoint - hitPoint).sqrMagnitude > CandidateRadiusSqr)
		{
			RecordDebugPoint(debugRejectedCandidatePoints, worldPoint);
			return;
		}

		AddPaddedWorldPoint(center, worldPoint);
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
	private int PassiveStraightenClearFrameLimit => Mathf.Max(1, passiveStraightenClearFrames);
	private int PinnedPassiveStraightenClearFrameLimit => Mathf.Max(1, pinnedPassiveStraightenClearFrames);
	private int MovingColliderBridgeClearFrameLimit => Mathf.Max(1, movingColliderBridgeClearFrames);
	private float AnchorColliderPaddingSqr => anchorColliderPadding * anchorColliderPadding;
	private float ReleaseCornerGrazingPaddingSqr => releaseCornerGrazingPadding * releaseCornerGrazingPadding;
	private float CandidateRadius => Mathf.Max(wrapPointPadding * 4f, candidateRadius);
	private float CandidateRadiusSqr => CandidateRadius * CandidateRadius;

	private bool HasLineOfSight(Vector2 from, Vector2 to)
	{
		return !TryGetBlockingHit(from, to, out _);
	}

	private bool HasLineOfSight(Vector2 from, Vector2 to, Collider2D fromCollider, Collider2D toCollider)
	{
		return !TryGetBlockingHit(from, to, fromCollider, toCollider, out _);
	}

	private bool TryGetBlockingHit(Vector2 from, Vector2 to, out BlockingHit blockingHit)
	{
		return TryGetBlockingHit(from, to, null, from, null, to, out blockingHit);
	}

	private bool TryGetBlockingHit(
		Vector2 from,
		Vector2 to,
		Collider2D fromCollider,
		Collider2D toCollider,
		out BlockingHit blockingHit)
	{
		return TryGetBlockingHit(from, to, fromCollider, from, toCollider, to, out blockingHit);
	}

	private bool TryGetBlockingHit(
		Vector2 from,
		Vector2 to,
		Collider2D fromCollider,
		Vector2 fromAnchor,
		Collider2D toCollider,
		Vector2 toAnchor,
		out BlockingHit blockingHit)
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

			if (IsAnchorColliderHit(hit, fromCollider, fromAnchor) ||
				IsAnchorColliderHit(hit, toCollider, toAnchor))
				continue;

			float endpointPadding = Mathf.Min(Mathf.Max(0f, linecastEndpointPadding), distance * 0.25f);
			float hitDistance = hit.fraction * distance;
			if (hitDistance <= endpointPadding || hitDistance >= distance - endpointPadding)
				continue;

			if (hit.fraction < bestFraction)
			{
				bestFraction = hit.fraction;
				blockingHit = new BlockingHit(hit.collider, hit.point, hit.normal, hit.fraction);
			}
		}

		if (TryGetEndpointInteriorBlockingHit(from, to, fromCollider, fromAnchor, out BlockingHit fromInteriorHit) &&
			fromInteriorHit.fraction < bestFraction)
		{
			bestFraction = fromInteriorHit.fraction;
			blockingHit = fromInteriorHit;
		}

		if (TryGetEndpointInteriorBlockingHit(from, to, toCollider, toAnchor, out BlockingHit toInteriorHit) &&
			toInteriorHit.fraction < bestFraction)
		{
			bestFraction = toInteriorHit.fraction;
			blockingHit = toInteriorHit;
		}

		return blockingHit.collider != null;
	}

	private bool TryGetEndpointInteriorBlockingHit(
		Vector2 from,
		Vector2 to,
		Collider2D endpointCollider,
		Vector2 endpointAnchor,
		out BlockingHit blockingHit)
	{
		blockingHit = default;
		if (endpointCollider == null)
			return false;

		const int sampleCount = 16;
		for (int i = 1; i < sampleCount; i++)
		{
			float t = i / (float)sampleCount;
			Vector2 sample = Vector2.Lerp(from, to, t);
			if ((sample - endpointAnchor).sqrMagnitude <= AnchorColliderPaddingSqr)
				continue;

			if (!endpointCollider.OverlapPoint(sample))
				continue;

			Vector2 normal = sample - (Vector2)endpointCollider.bounds.center;
			if (normal.sqrMagnitude <= 0.0001f)
				normal = (to - from).sqrMagnitude > 0.0001f ? Vector2.Perpendicular(to - from).normalized : Vector2.up;

			blockingHit = new BlockingHit(endpointCollider, sample, normal.normalized, t);
			return true;
		}

		return false;
	}

	private bool IsAnchorColliderHit(RaycastHit2D hit, Collider2D anchorCollider, Vector2 anchorPosition)
	{
		if (anchorCollider == null || hit.collider != anchorCollider)
			return false;

		return ((Vector2)hit.point - anchorPosition).sqrMagnitude <= AnchorColliderPaddingSqr;
	}

	private bool HasSameColliderInteriorCrossing(Vector2 from, Vector2 to, Collider2D fromCollider, Collider2D toCollider)
	{
		if (fromCollider == null || fromCollider != toCollider)
			return false;

		Vector2 delta = to - from;
		if (delta.sqrMagnitude <= 0.00000001f)
			return false;

		const int sampleCount = 5;
		for (int i = 1; i < sampleCount; i++)
		{
			float t = i / (float)sampleCount;
			Vector2 sample = Vector2.Lerp(from, to, t);
			if (fromCollider.OverlapPoint(sample))
				return true;
		}

		return false;
	}

	private bool TryGetProtectedBridgeHit(
		Vector2 from,
		Vector2 to,
		Collider2D fromCollider,
		Collider2D toCollider,
		Collider2D currentCollider,
		Vector2 currentWrapPosition,
		out RaycastHit2D blockingHit)
	{
		blockingHit = default;

		Vector2 delta = to - from;
		if (delta.sqrMagnitude <= 0.00000001f)
			return false;

		if (HasSameColliderInteriorCrossing(from, to, fromCollider, toCollider) ||
			HasSameColliderInteriorCrossing(from, to, fromCollider, currentCollider) ||
			HasSameColliderInteriorCrossing(from, to, toCollider, currentCollider))
		{
			blockingHit = default;
			return true;
		}

		float bestFraction = float.PositiveInfinity;
		CollectProtectedBridgeHit(
			Physics2D.Linecast(from, to, wallFilter, lineHits),
			lineHits,
			from,
			to,
			fromCollider,
			toCollider,
			currentCollider,
			currentWrapPosition,
			ref blockingHit,
			ref bestFraction);
		CollectProtectedBridgeHit(
			Physics2D.Linecast(from, to, hazardFilter, hazardHits),
			hazardHits,
			from,
			to,
			fromCollider,
			toCollider,
			currentCollider,
			currentWrapPosition,
			ref blockingHit,
			ref bestFraction);

		return blockingHit.collider != null;
	}

	private void CollectProtectedBridgeHit(
		int hitCount,
		RaycastHit2D[] hits,
		Vector2 from,
		Vector2 to,
		Collider2D fromCollider,
		Collider2D toCollider,
		Collider2D currentCollider,
		Vector2 currentWrapPosition,
		ref RaycastHit2D blockingHit,
		ref float bestFraction)
	{
		for (int i = 0; i < hitCount; i++)
		{
			RaycastHit2D hit = hits[i];
			if (hit.collider == null)
				continue;

			if (IsAnchorColliderHit(hit, fromCollider, from) ||
				IsAnchorColliderHit(hit, toCollider, to))
				continue;

			if (hit.collider == currentCollider)
			{
				if (((Vector2)hit.point - currentWrapPosition).sqrMagnitude <= ReleaseCornerGrazingPaddingSqr &&
					!HasColliderInteriorCrossingExceptNearPoint(from, to, currentCollider, currentWrapPosition, ReleaseCornerGrazingPaddingSqr))
					continue;

				blockingHit = hit;
				bestFraction = hit.fraction;
				continue;
			}

			if (hit.fraction < bestFraction)
			{
				bestFraction = hit.fraction;
				blockingHit = hit;
			}
		}
	}

	private bool HasColliderInteriorCrossingExceptNearPoint(
		Vector2 from,
		Vector2 to,
		Collider2D collider,
		Vector2 ignoredPoint,
		float ignoredRadiusSqr)
	{
		if (collider == null)
			return false;

		const int sampleCount = 24;
		for (int i = 1; i < sampleCount; i++)
		{
			float t = i / (float)sampleCount;
			Vector2 sample = Vector2.Lerp(from, to, t);
			if ((sample - ignoredPoint).sqrMagnitude <= ignoredRadiusSqr)
				continue;

			if (collider.OverlapPoint(sample))
				return true;
		}

		return false;
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
		spanCount = Mathf.Max(0, spanCount);

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

	private void InsertSagStateAtSpan(int splitSpanIndex)
	{
		if (currentSpanSags.Count == 0)
			return;

		int sourceIndex = Mathf.Clamp(splitSpanIndex, 0, currentSpanSags.Count - 1);
		int insertIndex = Mathf.Clamp(sourceIndex + 1, 0, currentSpanSags.Count);

		currentSpanSags.Insert(insertIndex, currentSpanSags[sourceIndex]);
		spanSagVelocities.Insert(insertIndex, spanSagVelocities[sourceIndex]);
	}

	private void MergeSagStateAroundRemovedWrap(int removedWrapIndex)
	{
		int leftSpanIndex = removedWrapIndex;
		int rightSpanIndex = removedWrapIndex + 1;

		if (leftSpanIndex < 0 || rightSpanIndex >= currentSpanSags.Count)
			return;

		currentSpanSags[leftSpanIndex] = (currentSpanSags[leftSpanIndex] + currentSpanSags[rightSpanIndex]) * 0.5f;
		spanSagVelocities[leftSpanIndex] = (spanSagVelocities[leftSpanIndex] + spanSagVelocities[rightSpanIndex]) * 0.5f;

		currentSpanSags.RemoveAt(rightSpanIndex);
		spanSagVelocities.RemoveAt(rightSpanIndex);
	}

	private void AddRenderSpan(Vector2 from, Vector2 to, bool includeStart, int spanIndex)
	{
		float distance = Vector2.Distance(from, to);
		int sampleCount = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Max(visualSegmentLength, 0.01f)));
		float sagMultiplier = GetSagMultiplier(spanIndex);
		float sagAmount = useSag ? GetSmoothedSagAmount(distance, spanIndex, sagMultiplier) : 0f;
		bool canSag = useSag && sagAmount > SagClampEpsilon;

		if (includeStart)
			renderPositions.Add(from);

		if (canSag)
		{
			float validSagAmount = GetLargestValidSag(from, to, sampleCount, spanIndex, sagAmount);
			ClampStoredSag(spanIndex, sagAmount, validSagAmount);

			if (TryBuildSpan(from, to, sampleCount, spanIndex, validSagAmount))
			{
				for (int i = 0; i < spanSamples.Count; i++)
					AddRenderPosition(spanSamples[i], spanIndex);

				return;
			}
		}

		AddStraightSpan(from, to, sampleCount, spanIndex);
	}

	private float GetSagMultiplier(int spanIndex)
	{
		if (wrapPoints.Count <= 0)
			return 1f;

		return Mathf.Clamp01(wrappedSagMultiplier);
	}

	private float GetLargestValidSag(
		Vector2 from,
		Vector2 to,
		int sampleCount,
		int spanIndex,
		float requestedSag)
	{
		requestedSag = Mathf.Max(0f, requestedSag);
		if (requestedSag <= SagClampEpsilon)
			return 0f;

		if (TryBuildSpan(from, to, sampleCount, spanIndex, requestedSag))
			return requestedSag;

		if (!TryBuildSpan(from, to, sampleCount, spanIndex, 0f))
			return 0f;

		float low = 0f;
		float high = requestedSag;
		for (int i = 0; i < SagClampSearchIterations; i++)
		{
			float mid = (low + high) * 0.5f;
			if (TryBuildSpan(from, to, sampleCount, spanIndex, mid))
				low = mid;
			else
				high = mid;
		}

		return low;
	}

	private void ClampStoredSag(int spanIndex, float requestedSag, float validSag)
	{
		if (requestedSag - validSag <= SagClampEpsilon ||
			spanIndex < 0 ||
			spanIndex >= currentSpanSags.Count ||
			spanIndex >= spanSagVelocities.Count)
			return;

		currentSpanSags[spanIndex] = validSag;
		spanSagVelocities[spanIndex] = 0f;
	}

	private void AddRenderPosition(Vector2 position, int insertIndex)
	{
		if (renderPositions.Count > 0)
			renderSegmentInsertIndices.Add(insertIndex);

		renderPositions.Add(position);
	}

	private float GetSmoothedSagAmount(float spanDistance, int spanIndex, float sagMultiplier)
	{
		float targetSag = Mathf.Min(maxSag, spanDistance * sagPerUnit) * Mathf.Clamp01(sagMultiplier);
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

	private bool TryBuildSpan(Vector2 from, Vector2 to, int sampleCount, int spanIndex, float sagAmount)
	{
		spanSamples.Clear();

		Vector2 previous = from;
		Collider2D fromCollider = GetControlPointCollider(spanIndex);
		Collider2D toCollider = GetControlPointCollider(spanIndex + 1);

		for (int i = 1; i <= sampleCount; i++)
		{
			float t = i / (float)sampleCount;
			Vector2 sample = Vector2.Lerp(from, to, t);
			sample += Vector2.down * (Mathf.Sin(t * Mathf.PI) * sagAmount);

			if (TryGetBlockingHit(previous, sample, fromCollider, from, toCollider, to, out _))
				return false;

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

	public void Pin(Transform newPinTarget)
	{
		pinTarget = newPinTarget;
		pinned = true;
	}

	public void Needle(Transform needle)
	{
		pinTarget = needle;
		pinned = false;
		ResetThreadState();
	}

	private void ResetThreadState()
	{
		cut = false;
		startPiece = null;
		endPiece = null;
		wrapPoints.Clear();
		controlPoints.Clear();
		controlPointColliders.Clear();
		renderPositions.Clear();
		renderSegmentInsertIndices.Clear();
		spanSamples.Clear();
		currentSpanSags.Clear();
		spanSagVelocities.Clear();
		previousColliderCenters.Clear();
		colliderFrameDeltas.Clear();
		activeWrappedColliders.Clear();
		staleWrappedColliders.Clear();
		colliderTrackingKeys.Clear();

		if (lineRenderer != null)
			lineRenderer.positionCount = 0;

		if (looseRenderer != null)
		{
			looseRenderer.enabled = false;
			looseRenderer.positionCount = 0;
		}
	}

	private void OnDrawGizmos()
	{
		if (!debugOverlay || debugOnlyWhenSelected)
			return;

		DrawDebugOverlay();
	}

	private void OnDrawGizmosSelected()
	{
		if (!debugOverlay || !debugOnlyWhenSelected)
			return;

		DrawDebugOverlay();
	}

	private void DrawDebugOverlay()
	{
		DrawDebugPolyline(controlPoints, debugControlSpanColor);
		DrawDebugPointsFromWraps();
		DrawDebugPoints(debugCandidatePoints, debugCandidateColor, debugPointRadius * 0.65f);
		DrawDebugCrosses(debugBlockingHits, debugBlockingHitColor, debugPointRadius * 1.5f);
		DrawDebugPoints(debugInsertedWrapPoints, debugInsertedWrapColor, debugPointRadius * 1.2f);
		DrawDebugCrosses(debugReleasedWrapPoints, debugReleasedWrapColor, debugPointRadius * 1.2f);
	}

	private void DrawDebugPolyline(List<Vector2> points, Color color)
	{
		Gizmos.color = color;
		for (int i = 0; i < points.Count - 1; i++)
			Gizmos.DrawLine(points[i], points[i + 1]);
	}

	private void DrawDebugPointsFromWraps()
	{
		Gizmos.color = debugWrapPointColor;
		for (int i = 0; i < wrapPoints.Count; i++)
			Gizmos.DrawSphere(wrapPoints[i].Position, debugPointRadius);
	}

	private void DrawDebugPoints(List<Vector2> points, Color color, float radius)
	{
		Gizmos.color = color;
		for (int i = 0; i < points.Count; i++)
			Gizmos.DrawSphere(points[i], radius);
	}

	private void DrawDebugCrosses(List<Vector2> points, Color color, float size)
	{
		Gizmos.color = color;
		Vector3 horizontal = Vector3.right * size;
		Vector3 vertical = Vector3.up * size;
		for (int i = 0; i < points.Count; i++)
		{
			Vector3 point = points[i];
			Gizmos.DrawLine(point - horizontal, point + horizontal);
			Gizmos.DrawLine(point - vertical, point + vertical);
		}
	}

	private readonly struct BlockingHit
	{
		public readonly Collider2D collider;
		public readonly Vector2 point;
		public readonly Vector2 normal;
		public readonly float fraction;

		public BlockingHit(Collider2D collider, Vector2 point, Vector2 normal, float fraction)
		{
			this.collider = collider;
			this.point = point;
			this.normal = normal;
			this.fraction = fraction;
		}
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
		private readonly int passiveBridgeClearFrames;
		public readonly Collider2D collider;

		public bool IsValid => collider != null;
		public int WrapSign => wrapSign;
		public int PassiveBridgeClearFrames => passiveBridgeClearFrames;

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
			passiveBridgeClearFrames = 0;
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

		private WrapPoint(
			Collider2D collider,
			Vector2 localPosition,
			Vector2 fallbackWorldPosition,
			Vector2 localOutwardDirection,
			Vector2 fallbackWorldOutwardDirection,
			int wrapSign,
			int passiveBridgeClearFrames)
		{
			this.collider = collider;
			this.localPosition = localPosition;
			this.fallbackWorldPosition = fallbackWorldPosition;
			this.localOutwardDirection = localOutwardDirection;
			this.fallbackWorldOutwardDirection = fallbackWorldOutwardDirection;
			this.wrapSign = wrapSign;
			this.passiveBridgeClearFrames = passiveBridgeClearFrames;
		}

		public WrapPoint WithPassiveBridgeClearFrames(int clearFrames)
		{
			return new WrapPoint(
				collider,
				localPosition,
				fallbackWorldPosition,
				localOutwardDirection,
				fallbackWorldOutwardDirection,
				wrapSign,
				Mathf.Max(0, clearFrames));
		}
	}
}
