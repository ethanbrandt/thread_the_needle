using UnityEngine;
using System;
using System.Collections;
using Unity.Mathematics;

public class Needle : MonoBehaviour
{
	[SerializeField] float minStickDepth;
	[SerializeField] float maxStickDepth;
	[SerializeField] float speedForMaxStick;
	[SerializeField] float minSpeedForStick;
	[SerializeField] float tipLocalY = -0.92f;
	[SerializeField] float tipHitTolerance = 0.15f;
	[SerializeField] LayerMask wallLayer;

	[SerializeField] GameObject pinPrefab;
	
	public Action<WrappedThreadPin> stickEvent;
	
	private Vector2 currentVelocity;

	private Rigidbody2D rb;
	
	private Collider2D stuckWallCollider;
	private Collider2D stuckNeedleCollider;
	private Vector2 stuckContactPoint;
	private Vector2 stuckWallNormal;
	private bool hasHit;

	public Vector2 StuckContactPoint => stuckContactPoint;
	public Vector2 StuckWallNormal => stuckWallNormal;

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
		print(col.gameObject.name);
		if (hasHit)
			return;
		
		if ((1 << col.gameObject.layer & wallLayer) == 0)
		{
			hasHit = true;
			return;
		}

		Vector2 tipPos = transform.TransformPoint(new Vector2(0f, tipLocalY));

		hasHit = true;
		for (int i = 0; i < col.contactCount; i++)
		{
			ContactPoint2D contact = col.GetContact(i);
			Vector2 tipDir = -transform.up;
			float tipIntoWallAmount = Vector2.Dot(tipDir, -contact.normal);
			if (currentVelocity.magnitude >= minSpeedForStick && Vector2.Distance(contact.point, tipPos) <= tipHitTolerance)
			{
				Vector2 wallNormal = contact.normal;
				Vector2 intoWallDir = -wallNormal;
				
				Debug.DrawRay(contact.point, wallNormal, Color.green, 1f);

				stuckWallCollider = col.collider;
				stuckNeedleCollider = col.otherCollider;
				stuckContactPoint = contact.point;
				stuckWallNormal = contact.normal;
				
				StickIntoWall(intoWallDir, contact.point, col.transform);
				hasHit = false;
			}
		}
	}

	private void StickIntoWall(Vector2 _intoWallDir, Vector2 _contactPos, Transform _wall)
	{
		rb.linearVelocity = Vector2.zero;
		rb.angularVelocity = 0f;
		rb.bodyType = RigidbodyType2D.Kinematic;
		
		float stickDepth = Mathf.Lerp(minStickDepth, maxStickDepth, Mathf.Clamp01((currentVelocity.magnitude - minSpeedForStick) / (speedForMaxStick - minSpeedForStick)));
	
		float angle = Mathf.Atan2(currentVelocity.y, currentVelocity.x) * Mathf.Rad2Deg;
		transform.eulerAngles = new Vector3(0f, 0f, angle + 90f);

		Vector2 targetTipPos = _contactPos + _intoWallDir * stickDepth;
		Vector2 tipOffset = transform.TransformVector(new Vector2(0f, tipLocalY));

		transform.position = targetTipPos - tipOffset;
		
		//transform.position += (Vector3)currentVelocity.normalized * stickDepth;
		
		//float angle = Mathf.Atan2(currentVelocity.y, currentVelocity.x) * Mathf.Rad2Deg;
		//transform.eulerAngles = new Vector3(0f, 0f, angle + 90f);

		transform.SetParent(_wall);
		
		float pinAngle = Mathf.Atan2(_intoWallDir.y, _intoWallDir.x) * Mathf.Rad2Deg;
		GameObject pinObj = Instantiate(pinPrefab, _contactPos, Quaternion.Euler(0f, 0f, pinAngle + 90f));
		stickEvent?.Invoke(pinObj.GetComponent<WrappedThreadPin>());
	}

	public void Launch(Vector2 launchDirection, float power)
	{
		transform.SetParent(null);

		hasHit = false;

		if (stuckWallCollider != null && stuckNeedleCollider != null)
			Physics2D.IgnoreCollision(stuckNeedleCollider, stuckWallCollider, true);

		transform.position += (Vector3)(stuckWallNormal * 0.1f);

		rb.bodyType = RigidbodyType2D.Dynamic;
		rb.linearVelocity = launchDirection.normalized * power;

		StartCoroutine(ReenableWallCollisionAfterDelay());
	}
	
	private IEnumerator ReenableWallCollisionAfterDelay()
	{
		yield return new WaitForSeconds(0.25f);

		if (stuckWallCollider != null && stuckNeedleCollider != null)
			Physics2D.IgnoreCollision(stuckNeedleCollider, stuckWallCollider, false);
		else
		{
			print("HUH???");
		}

		stuckWallCollider = null;
		stuckNeedleCollider = null;
	}
}
