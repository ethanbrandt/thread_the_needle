using System;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Serialization;

public class ThreadPin : MonoBehaviour
{
	[FormerlySerializedAs("needle")]
	[SerializeField] Transform pinTarget;
	[SerializeField] float needleEyeYOffest;
	
	[Header("Rope")]
	[SerializeField] int numOfRopeSegments = 50;
	[SerializeField] float ropeSegmentLength = 0.225f;
	[SerializeField] float addSegmentDistanceMultiplier = 1.5f;
	[SerializeField] int maxRopeSegments = 200;

	[Header("Physics")]
	[SerializeField] Vector2 gravityForce = new Vector2(0f, -2f);
	[SerializeField] float dampingFactor = 0.95f;
	[SerializeField] LayerMask collisionMask;
	[SerializeField] float collisionRadius = 0.1f;
	[SerializeField] float bounceFactor = 0.1f;
	
	[Header("Constraints")]
	[SerializeField] int numOfConstraintRunsNeedle = 50;
	[SerializeField] int numOfConstraintRunsPinned = 20;

	[Header("Optimizations")]
	[SerializeField] int collisionSegmentIntervalNeedle = 1;
	[SerializeField] int collisionSegmentIntervalPinned = 3;
	
	public bool pinned;
	
	private LineRenderer lineRenderer;
	private List<RopeSegment> ropeSegments = new List<RopeSegment>();
	
	private readonly Collider2D[] collisionResults = new Collider2D[8];
	private ContactFilter2D collisionFilter;

	private readonly RaycastHit2D[] castResults = new RaycastHit2D[4];
	
	private Vector2 previousStartAnchor;
	
	void Awake()
	{
		lineRenderer = GetComponent<LineRenderer>();
		lineRenderer.positionCount = numOfRopeSegments;

		collisionFilter = new ContactFilter2D();
		collisionFilter.SetLayerMask(collisionMask);
		collisionFilter.useTriggers = false;
		
		for (int i = 0; i < numOfRopeSegments; i++)
			ropeSegments.Add(new RopeSegment((Vector2)transform.position + (Vector2.down * ropeSegmentLength * i)));
	}

	void Update()
	{
		Vector3[] ropePositions = new Vector3[ropeSegments.Count];
		ropePositions[0] = pinTarget.position + pinTarget.transform.up * needleEyeYOffest;
		for (int i = 1; i < ropeSegments.Count; i++)
			ropePositions[i] = ropeSegments[i].currentPos;
		
		lineRenderer.SetPositions(ropePositions);
	}

	private void FixedUpdate()
	{
		Vector2 startAnchor = pinTarget.position + pinTarget.transform.up * needleEyeYOffest;
		Vector2 anchorDelta = startAnchor - previousStartAnchor;
		previousStartAnchor = startAnchor;

		FollowNeedleMotion(anchorDelta);
		
		SimulateRope();
		TryAddEndSegment();
		
		int numOfConstraintRuns = pinned ? numOfConstraintRunsPinned : numOfConstraintRunsNeedle;
		int collisionSegmentInterval = pinned ? collisionSegmentIntervalPinned : collisionSegmentIntervalNeedle;
		
		for (int i = 0; i < numOfConstraintRuns; i++)
		{
			ApplyConstraints();
			if (i % collisionSegmentInterval == 0)
				HandleCollisions();
		}
	}
	
	[SerializeField] int needleFollowSegments = 6;
	[SerializeField] float needleFollowStrength = 0.65f;

	private void FollowNeedleMotion(Vector2 anchorDelta)
	{
		int count = Mathf.Min(needleFollowSegments, ropeSegments.Count);

		for (int i = 1; i < count; i++)
		{
			float t = 1f - (i / (float)count);
			RopeSegment segment = ropeSegments[i];

			Vector2 offset = anchorDelta * (needleFollowStrength * t);
			segment.currentPos += offset;
			segment.oldPos += offset;

			ropeSegments[i] = segment;
		}
	}

	private void SimulateRope()
	{
		for (int i = 1; i < ropeSegments.Count; i++)
		{
			RopeSegment segment = ropeSegments[i];
			Vector2 vel = (segment.currentPos - segment.oldPos) * dampingFactor;

			segment.oldPos = segment.currentPos;
			segment.currentPos += vel;
			segment.currentPos += gravityForce * Time.fixedDeltaTime;
			ropeSegments[i] = segment;
		}
	}

	private void TryAddEndSegment()
	{
		if (ropeSegments.Count < 2 || ropeSegments.Count >= maxRopeSegments)
			return;

		Vector2 startAnchor = pinTarget.position + pinTarget.transform.up * needleEyeYOffest;
		Vector2 endAnchor = transform.position;
		float anchorDistance = Vector2.Distance(startAnchor, endAnchor);
		float currentRopeLength = ropeSegmentLength * (ropeSegments.Count - 1);

		if (anchorDistance <= currentRopeLength * addSegmentDistanceMultiplier)
			return;

		int lastIndex = ropeSegments.Count - 1;
		int previousIndex = lastIndex - 1;

		RopeSegment previousSegment = ropeSegments[previousIndex];
		RopeSegment lastSegment = ropeSegments[lastIndex];

		Vector2 direction = (previousSegment.currentPos - lastSegment.currentPos).normalized;
		if (direction == Vector2.zero)
			direction = Vector2.up;

		Vector2 newSegmentPosition = lastSegment.currentPos + direction * ropeSegmentLength;
		ropeSegments.Insert(lastIndex, new RopeSegment(newSegmentPosition));

		numOfRopeSegments = ropeSegments.Count;
		lineRenderer.positionCount = ropeSegments.Count;
		print(ropeSegments.Count);
	}

	private void ApplyConstraints()
	{
		int lastIndex = ropeSegments.Count - 1;

		RopeSegment firstSegment = ropeSegments[0];
		if (!pinned)
			firstSegment.currentPos = pinTarget.position + pinTarget.transform.up * needleEyeYOffest;
		else
			firstSegment.currentPos = pinTarget.position + pinTarget.transform.up * needleEyeYOffest;
		ropeSegments[0] = firstSegment;

		RopeSegment lastSegment = ropeSegments[lastIndex];
		lastSegment.currentPos = transform.position;
		ropeSegments[lastIndex] = lastSegment;

		for (int i = 0; i < ropeSegments.Count - 1; i++)
		{
			RopeSegment currentSeg = ropeSegments[i];
			RopeSegment nextSeg = ropeSegments[i + 1];

			float distance = (currentSeg.currentPos - nextSeg.currentPos).magnitude;
			float difference = distance - ropeSegmentLength;

			Vector2 changeDir = (currentSeg.currentPos - nextSeg.currentPos).normalized;
			Vector2 changeVector = changeDir * difference;

			if (i == 0)
				nextSeg.currentPos += changeVector;
			else if (i == ropeSegments.Count - 2)
				currentSeg.currentPos -= changeVector;
			else
			{
				nextSeg.currentPos += changeVector * 0.5f;
				currentSeg.currentPos -= changeVector * 0.5f;
			}

			ropeSegments[i] = currentSeg;
			ropeSegments[i + 1] = nextSeg;
		}
	}

	/*private void HandleCollisions()
	{
		for (int i = 1; i < ropeSegments.Count; i++)
		{
			RopeSegment segment = ropeSegments[i];
			Vector2 vel = segment.currentPos - segment.oldPos;
			int colliderCount = Physics2D.OverlapCircle(segment.currentPos, collisionRadius, collisionFilter, collisionResults);

			for (int j = 0; j < colliderCount; j++)
			{
				Collider2D collider = collisionResults[j];
				Vector2 closestPoint = collider.ClosestPoint(segment.currentPos);
				float distance = Vector2.Distance(segment.currentPos, closestPoint);

				if (distance < collisionRadius)
				{
					Vector2 normal = (segment.currentPos - closestPoint).normalized;
					if (normal == Vector2.zero)
						normal = (segment.currentPos - (Vector2)collider.transform.position).normalized;

					float depth = collisionRadius - distance;
					segment.currentPos += normal * depth;

					vel = Vector2.Reflect(vel, normal) * bounceFactor;
				}
			}
			segment.oldPos = segment.currentPos - vel;
			ropeSegments[i] = segment;
		}
	}*/
	
	private void HandleCollisions()
	{
		for (int i = 1; i < ropeSegments.Count; i++)
		{
			RopeSegment segment = ropeSegments[i];
			Vector2 vel = segment.currentPos - segment.oldPos;

			Vector2 movement = segment.currentPos - segment.oldPos;
			float movementDistance = movement.magnitude;

			if (movementDistance > 0.0001f)
			{
				int castCount = Physics2D.CircleCast(
					segment.oldPos,
					collisionRadius,
					movement / movementDistance,
					collisionFilter,
					castResults,
					movementDistance
				);

				if (castCount > 0)
				{
					RaycastHit2D hit = castResults[0];

					segment.currentPos = hit.centroid;

					vel = Vector2.Reflect(vel, hit.normal) * bounceFactor;
					segment.oldPos = segment.currentPos - vel;

					ropeSegments[i] = segment;
					continue;
				}
			}

			int colliderCount = Physics2D.OverlapCircle(
				segment.currentPos,
				collisionRadius,
				collisionFilter,
				collisionResults
			);

			for (int j = 0; j < colliderCount; j++)
			{
				Collider2D collider = collisionResults[j];
				Vector2 closestPoint = collider.ClosestPoint(segment.currentPos);
				float distance = Vector2.Distance(segment.currentPos, closestPoint);

				if (distance < collisionRadius)
				{
					Vector2 normal = (segment.currentPos - closestPoint).normalized;
					if (normal == Vector2.zero)
						normal = (segment.currentPos - (Vector2)collider.transform.position).normalized;

					float depth = collisionRadius - distance;
					segment.currentPos += normal * depth;

					vel = Vector2.Reflect(vel, normal) * bounceFactor;
				}
			}

			segment.oldPos = segment.currentPos - vel;
			ropeSegments[i] = segment;
		}
	}

	public void Pin(Transform _pin)
	{
		pinTarget = _pin;
		pinned = true;
	}

	public void Needle(Transform _needle)
	{
		pinTarget = _needle;
		previousStartAnchor = pinTarget.position + pinTarget.transform.up * needleEyeYOffest;
		pinned = false;
	}

	struct RopeSegment
	{
		public Vector2 currentPos;
		public Vector2 oldPos;

		public RopeSegment(Vector2 _pos)
		{
			currentPos = _pos;
			oldPos = _pos;
		}
	}
}
