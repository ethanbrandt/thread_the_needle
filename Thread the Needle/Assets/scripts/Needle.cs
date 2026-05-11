using UnityEngine;
using System;

public class Needle : MonoBehaviour
{
	[SerializeField] float minStickDepth;
	[SerializeField] float maxStickDepth;
	[SerializeField] float speedForMaxStick;
	[SerializeField] float tipLocalY = -0.92f;
	[SerializeField] float tipHitTolerance = 0.15f;
	[SerializeField] LayerMask wallLayer;

	public Action stickEvent;
	
	private Vector2 currentVelocity;

	private Rigidbody2D rb;

	private bool hasHit;

	private void Start()
	{
		rb = GetComponent<Rigidbody2D>();
	}

	private void FixedUpdate()
	{
		currentVelocity = rb.linearVelocity;

		if (hasHit || currentVelocity.sqrMagnitude < 0.001f)
			return;

		float angle = Mathf.Atan2(currentVelocity.y, currentVelocity.x) * Mathf.Rad2Deg;
		rb.MoveRotation(angle + 90f);
	}

	private void OnCollisionEnter2D(Collision2D col)
	{
		if ((1 << col.gameObject.layer & wallLayer) == 0)
			return;

		Vector2 tipPos = transform.TransformPoint(new Vector2(0f, tipLocalY));

		for (int i = 0; i < col.contactCount; i++)
		{
			ContactPoint2D contact = col.GetContact(i);

			if (Vector2.Distance(contact.point, tipPos) <= tipHitTolerance)
			{
				Vector2 wallNormal = contact.normal;
				Vector2 intoWallDir = -wallNormal;
				
				Debug.DrawRay(contact.point, wallNormal, Color.green, 1f);
				
				StickIntoWall(intoWallDir, col.transform);
			}
		}
	}

	private void StickIntoWall(Vector2 _intoWallDir, Transform wall)
	{
		rb.linearVelocity = Vector2.zero;
		rb.angularVelocity = 0f;
		rb.bodyType = RigidbodyType2D.Kinematic;
		
		float stickDepth = Mathf.Lerp(minStickDepth, maxStickDepth, Mathf.Clamp01(currentVelocity.magnitude / speedForMaxStick));
		transform.position += (Vector3)_intoWallDir * stickDepth;
		
		float angle = Mathf.Atan2(_intoWallDir.y, _intoWallDir.x) * Mathf.Rad2Deg;
		transform.eulerAngles = new Vector3(0f, 0f, angle + 90f);
	
		transform.SetParent(wall);
		stickEvent?.Invoke();
	}
}
