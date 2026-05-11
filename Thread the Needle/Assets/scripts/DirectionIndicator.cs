using UnityEngine;
using UnityEngine.UI;
public class DirectionIndicator : MonoBehaviour
{
	[SerializeField] float minMaxZRot;
	[SerializeField] float rotSpeed;

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
		inDirectionMinigame = true;
		indicatorImage.enabled = true;
		arrowImage.enabled = true;
		movingLeft = false;
		arrow.transform.eulerAngles = new Vector3(0f, 0f, minMaxZRot);
	}
	
	public float EndDirectionMinigame()
	{
		inDirectionMinigame = false;
    	indicatorImage.enabled = false;
    	arrowImage.enabled = false;
    	return Mathf.DeltaAngle(0f, arrow.transform.localEulerAngles.z);
	}
}
