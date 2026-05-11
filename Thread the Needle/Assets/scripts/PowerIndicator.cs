using UnityEngine;
using UnityEngine.UI;

public class PowerIndicator : MonoBehaviour
{
	[SerializeField] float minPower;
	[SerializeField] float maxPower;
	[SerializeField] float barSpeed;
	
	private bool inPowerMinigame = false;

	private Slider slider;
	
	void Start()
	{
		slider = GetComponent<Slider>();
		slider.enabled = false;
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
		inPowerMinigame = true;
		slider.enabled = true;
		slider.value = 0f;
	}
	
	public float EndPowerMinigame()
	{
		inPowerMinigame = false;
		slider.enabled = false;
		return Mathf.Lerp(minPower, maxPower, slider.value);
	}
}
