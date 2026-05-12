using UnityEngine;
using UnityEngine.UI;
public class DirectionIndicator : MonoBehaviour
{
	[SerializeField] float minMaxZRot;
	[SerializeField] float rotSpeed;
	[SerializeField] float wallNormalOffset = 1.25f;
	[SerializeField] float wallTangentOffset = 0f;

	private bool movingLeft;
	private bool inDirectionMinigame = false;

	private GameObject arrow;
	private Image indicatorImage;
	private Image arrowImage;
	
	void Start()
	{
		arrow = transform.GetChild(0).gameObject;
		indicatorImage = GetComponent<Image>();
		arrowImage = arrow.GetComponent<Image>();
		
		indicatorImage.enabled = false;
		arrowImage.enabled = false;
	}

	void Update()
	{
		if (!inDirectionMinigame)
			return;
		
		float currRotSpeed = movingLeft ? rotSpeed : -rotSpeed;
		arrow.transform.Rotate(0f, 0f, currRotSpeed * Time.deltaTime);

		float offset = Mathf.DeltaAngle(0f, arrow.transform.localEulerAngles.z);
		if (offset > minMaxZRot)
			movingLeft = false;
		if (offset < -minMaxZRot)
			movingLeft = true;
	}

	public void StartDirectionMinigame()
	{
		StartDirectionMinigame(transform.position, transform.up);
	}

	public void StartDirectionMinigame(Vector2 contactPoint, Vector2 wallNormal)
	{
		inDirectionMinigame = true;
		indicatorImage.enabled = true;
		arrowImage.enabled = true;
		movingLeft = false;

		wallNormal = wallNormal.sqrMagnitude > 0.0001f ? wallNormal.normalized : Vector2.up;
		Vector2 tangent = new Vector2(-wallNormal.y, wallNormal.x);
		transform.position = contactPoint + wallNormal * wallNormalOffset + tangent * wallTangentOffset;
		transform.eulerAngles = new Vector3(0f, 0f, GetUpAlignedZRotation(wallNormal));

		arrow.transform.localEulerAngles = new Vector3(0f, 0f, minMaxZRot);
	}
	
	public float EndDirectionMinigame()
	{
		inDirectionMinigame = false;
    	indicatorImage.enabled = false;
    	arrowImage.enabled = false;
    	return Mathf.DeltaAngle(0f, arrow.transform.localEulerAngles.z);
	}

	private float GetUpAlignedZRotation(Vector2 direction)
	{
		return Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
	}
}
