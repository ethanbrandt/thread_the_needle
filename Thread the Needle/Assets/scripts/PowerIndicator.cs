using UnityEngine;
using UnityEngine.UI;

public class PowerIndicator : MonoBehaviour
{
	[SerializeField] float minPower;
	[SerializeField] float maxPower;
	[SerializeField] float barSpeed;
	[SerializeField] float wallNormalOffset = 1.15f;
	[SerializeField] float wallTangentOffset = 0f;

	[SerializeField] Image fillImage;
	[SerializeField] Image backgroundImage;
	
	private bool inPowerMinigame = false;

	private Slider slider;

	private void SetEnabled(bool _enabled)
	{
		slider.enabled = _enabled;
		fillImage.enabled = _enabled;
		backgroundImage.enabled = _enabled;
	}
	
	void Start()
	{
		slider = GetComponent<Slider>();
		SetEnabled(false);
	}

	void Update()
	{
		if (!inPowerMinigame)
			return;
		
		slider.value += barSpeed * Time.deltaTime;

		if (slider.value > 1f)
			slider.value = 1f;
	}

	public void StartPowerMinigame()
	{
		StartPowerMinigame(transform.position, transform.up);
	}

	public void StartPowerMinigame(Vector2 contactPoint, Vector2 wallNormal)
	{
		inPowerMinigame = true;
		SetEnabled(true);
		slider.value = 0f;

		wallNormal = wallNormal.sqrMagnitude > 0.0001f ? wallNormal.normalized : Vector2.up;
		Vector2 tangent = new Vector2(-wallNormal.y, wallNormal.x);
		transform.position = contactPoint + wallNormal * wallNormalOffset + tangent * wallTangentOffset;
		transform.eulerAngles = new Vector3(0f, 0f, Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg);
	}
	
	public float EndPowerMinigame()
	{
		inPowerMinigame = false;
		SetEnabled(false);
		return Mathf.Lerp(minPower, maxPower, slider.value);
	}
	
	
	public void ForceStop()
	{
		inPowerMinigame = false;
		SetEnabled(false);
	}
}
