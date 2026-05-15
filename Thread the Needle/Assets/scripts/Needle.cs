using UnityEngine;
using System;

public class Needle : MonoBehaviour
{
	[SerializeField] float minStickDepth;
	[SerializeField] float maxStickDepth;
	[SerializeField] float speedForMaxStick;
	[SerializeField] float minSpeedForStick;
	[SerializeField] float tipLocalY = -0.92f;
	[SerializeField] float tipHitTolerance = 0.15f;
	[SerializeField] LayerMask wallLayer;
	[SerializeField] LayerMask cushionLayer;
	
	[SerializeField] float disableTime = 0.05f;

	[SerializeField] GameObject pinPrefab;
	
	public Action<WrappedThreadPin> stickEvent;
	public Action failStateEvent;
	public Action winStateEvent;
	
	private Vector2 currentVelocity;

	private Rigidbody2D rb;
	
	private Collider2D stuckWallCollider;
	private Collider2D stuckNeedleCollider;
	private Vector2 stuckContactPoint;
	private Vector2 stuckWallNormal;
	private bool hasHit;
	private float disableTimer;
	
	public Vector2 StuckContactPoint => stuckContactPoint;
	public Vector2 StuckWallNormal => stuckWallNormal;

	private void Start()
	{
		rb = GetComponent<Rigidbody2D>();
		WrappedThreadPin.CutEvent += HandleCut;
	}

	private void OnDestroy()
	{
		WrappedThreadPin.CutEvent -= HandleCut;
	}

	private void HandleCut(bool _attached)
	{
		if (!_attached)
			return;
		
		hasHit = true;
		failStateEvent?.Invoke();
	}

	private void FixedUpdate()
	{
		if (disableTimer >= 0f)
			disableTimer += Time.fixedDeltaTime;
		
		if (disableTimer >= disableTime && stuckWallCollider != null && stuckNeedleCollider != null)
		{
			disableTimer = -1f;
        	Physics2D.IgnoreCollision(stuckNeedleCollider, stuckWallCollider, false);
		}
		
		currentVelocity = rb.linearVelocity;

		if (hasHit || currentVelocity.sqrMagnitude < 0.001f)
			return;

		float angle = Mathf.Atan2(currentVelocity.y, currentVelocity.x) * Mathf.Rad2Deg;
		rb.MoveRotation(angle + 90f);
	}

	private void OnCollisionEnter2D(Collision2D col)
	{
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
			float tipIntoWallAmount = Vector2.Dot(tipDir.normalized, -contact.normal.normalized);
			Debug.DrawRay(contact.point, -contact.normal, Color.cyan, 5f);
			Debug.DrawRay(contact.point, tipDir, Color.red, 5f);
			print(tipIntoWallAmount + " : " + contact.point);
			if (currentVelocity.magnitude >= minSpeedForStick && (Vector2.Distance(contact.point, tipPos) <= tipHitTolerance || tipIntoWallAmount > 0.2f))
			{
				Vector2 wallNormal = contact.normal;
				Vector2 intoWallDir = -wallNormal;
				
				Debug.DrawRay(contact.point, wallNormal, Color.green, 1f);

				stuckWallCollider = col.collider;
				stuckNeedleCollider = col.otherCollider;
				stuckContactPoint = contact.point;
				stuckWallNormal = contact.normal;
				
				StickIntoSurface(intoWallDir, contact.collider.ClosestPoint(contact.point), col.transform, wallLayer);
				hasHit = false;
				return;
			}
		}
		
		if (hasHit)
			failStateEvent?.Invoke();
	}

	private void StickIntoSurface(Vector2 _intoWallDir, Vector2 _contactPos, Transform _surface, LayerMask _surfaceLayer)
	{
		rb.linearVelocity = Vector2.zero;
		rb.angularVelocity = 0f;
		rb.bodyType = RigidbodyType2D.Kinematic;
		rb.simulated = false;
		
		float stickDepth = Mathf.Lerp(minStickDepth, maxStickDepth, Mathf.Clamp01((currentVelocity.magnitude - minSpeedForStick) / (speedForMaxStick - minSpeedForStick)));
	
		float angle = Mathf.Atan2(currentVelocity.y, currentVelocity.x) * Mathf.Rad2Deg;
		transform.eulerAngles = new Vector3(0f, 0f, angle + 90f);

		Vector2 targetTipPos = _contactPos - (Vector2)transform.up * stickDepth;
		Vector2 tipOffset = transform.TransformVector(new Vector2(0f, tipLocalY));

		transform.position = targetTipPos - tipOffset;
		
		transform.SetParent(_surface);
		RaycastHit2D hit = Physics2D.Raycast(transform.position + transform.up, -transform.up, 3f, _surfaceLayer, 0, Mathf.Infinity);

		float pinAngle;
		Vector2 pinPos;
		Vector2 pinNormal;
		if (hit && hit.transform == _surface)
		{
			pinNormal = -hit.normal.normalized;
			pinAngle = Mathf.Atan2(-hit.normal.y, -hit.normal.x) * Mathf.Rad2Deg;
			pinPos = hit.collider.ClosestPoint(hit.point);
		}
		else
		{
			Debug.LogWarning("NOT HIT");
			pinNormal = _intoWallDir.normalized;
			pinAngle = Mathf.Atan2(_intoWallDir.y, _intoWallDir.x) * Mathf.Rad2Deg;
			pinPos = _contactPos;
		}
		
		GameObject pinObj = Instantiate(pinPrefab, pinPos + pinNormal.normalized * -0.025f, Quaternion.Euler(0f, 0f, pinAngle + 90f));
		pinObj.transform.SetParent(_surface);
		stickEvent?.Invoke(pinObj.GetComponent<WrappedThreadPin>());
	}

	public void Launch(Vector2 launchDirection, float power)
	{
		transform.SetParent(null);

		hasHit = false;
		rb.simulated = true;

		if (stuckWallCollider != null && stuckNeedleCollider != null)
			Physics2D.IgnoreCollision(stuckNeedleCollider, stuckWallCollider, true);

		transform.position += (Vector3)(stuckWallNormal.normalized * 0.2f);
		Physics2D.SyncTransforms();

		rb.bodyType = RigidbodyType2D.Dynamic;
		rb.linearVelocity = launchDirection.normalized * power;

		disableTimer = 0f;
	}

	private void OnTriggerEnter2D(Collider2D other)
	{
		if (hasHit)
			return;
		
		if ((1 << other.gameObject.layer & cushionLayer) == 0)
			return;

		Vector2 impactVelocity = rb.linearVelocity.sqrMagnitude > 0.001f ? rb.linearVelocity : currentVelocity;
		if (impactVelocity.magnitude < minSpeedForStick)
			return;

		currentVelocity = impactVelocity;
		Vector2 intoCushionDir = impactVelocity.normalized;
		Vector2 tipPos = transform.TransformPoint(new Vector2(0f, tipLocalY));
		RaycastHit2D hit = Physics2D.Raycast(tipPos - intoCushionDir * tipHitTolerance, intoCushionDir, maxStickDepth + tipHitTolerance, cushionLayer, 0, Mathf.Infinity);
		Vector2 contactPos = hit && hit.collider == other ? hit.point : other.ClosestPoint(tipPos);

		stuckWallCollider = other;
		stuckNeedleCollider = GetComponent<Collider2D>();
		stuckContactPoint = contactPos;
		stuckWallNormal = -intoCushionDir;

		StickIntoSurface(intoCushionDir, contactPos, other.transform, cushionLayer);
		winStateEvent?.Invoke();
	}
}
