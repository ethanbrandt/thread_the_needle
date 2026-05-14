using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class RailMover : MonoBehaviour
{
	[SerializeField] bool loop = false;
	[SerializeField] List<RailPoint> railPoints;

	private float waitTimer	= -1f;
	private int currentPoint;
	private bool forwardList = true;

	private Rigidbody2D rb;

	void Awake()
	{
		rb = GetComponent<Rigidbody2D>();
	}
	
	void Start()
	{
		if (railPoints.Count == 0)
		{
			Debug.LogError("RailMover must have at least 1 RailPoint");
			return;
		}

		foreach (var point in railPoints)
			if (point.speedToPoint <= 0f)
				Debug.LogWarning("speedToPoint should be >0");
		
		rb.position = railPoints[0].pointPos;
	}

	void FixedUpdate()
	{
		if (railPoints.Count < 2)
			return;

		if (waitTimer > railPoints[currentPoint].waitAtPointTime)
			waitTimer = -1f;

		if (waitTimer >= 0f)
		{
			waitTimer += Time.fixedDeltaTime;
			return;
		}

		RailPoint nextPoint;
		if (loop)
			nextPoint = railPoints[currentPoint == railPoints.Count - 1 ? 0 : currentPoint + 1];
		else
			nextPoint = railPoints[forwardList ? currentPoint + 1 : currentPoint - 1];

		Vector2 nextPosition = Vector2.MoveTowards(rb.position, nextPoint.pointPos, nextPoint.speedToPoint * Time.fixedDeltaTime);
		rb.MovePosition(nextPosition);

		if (Vector2.Distance(nextPosition, nextPoint.pointPos) <= 0.001f)
		{
			if (loop)
			{
				currentPoint++;
				if (currentPoint == railPoints.Count)
					currentPoint = 0;
			}
			else
			{
				currentPoint = forwardList ? currentPoint + 1 : currentPoint - 1;

				if (currentPoint == 0)
					forwardList = true;
				else if (currentPoint == railPoints.Count - 1)
					forwardList = false;
			}
			
			print("currentPoint: " + currentPoint);
			
			if (railPoints[currentPoint].waitAtPointTime > 0f)
				waitTimer = 0f;
		}
	}

	[Serializable]
	public struct RailPoint
	{
		public Vector2 pointPos;
		public float waitAtPointTime;
		public float speedToPoint;

		public RailPoint(Vector2 _pointPos, float _waitAtPointTime, float _speedToPoint)
		{
			pointPos = _pointPos;
			waitAtPointTime = _waitAtPointTime;
			speedToPoint = _speedToPoint;
			if (speedToPoint == 0)
				speedToPoint = 1f;
		}
	}
}


