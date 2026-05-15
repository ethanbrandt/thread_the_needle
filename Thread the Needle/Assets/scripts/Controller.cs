using System;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class Controller : MonoBehaviour
{
	[Header("Fail State")]
	[SerializeField] float timeBeforeRestart = 3f;
	
	[Header("Win State")]
	[SerializeField] string nextSceneName;
	[SerializeField] float timeBeforeNextSceneTransition = 3f;
	
	[Header("Assignment")]
	[SerializeField] DirectionIndicator directionIndicator;
	[SerializeField] PowerIndicator powerIndicator;
	[SerializeField] Needle needle;
	[SerializeField] CinemachineCamera cam;
	
	private float directionOffset;
	private WrappedThreadPin currentPin;
	private Vector2 currentWallNormal = Vector2.up;
	private Vector2 currentContactPoint;
	private float restartTimer = -1f;
	private float winTransitionTimer = -1f;
	
	enum GameState
	{
		STUCK,
		DIRECTION_MINIGAME,
		POWER_MINIGAME_START,
		POWER_MINIGAME_HOLD,
		MOVING,
		SCENE_TRANSITION
	}

	private GameState currentGameState = GameState.STUCK;

	private void Start()
	{
		needle.stickEvent += HandleStickEvent;
		needle.failStateEvent += HandleFailStateEvent;
		needle.winStateEvent += HandleWinStateEvent;
	}

	private void Update()
	{
		if (restartTimer >= 0f)
			restartTimer += Time.deltaTime;

		if (restartTimer >= timeBeforeRestart)
		{
			restartTimer = -1f;
			SceneManager.LoadScene(SceneManager.GetActiveScene().name, LoadSceneMode.Single);
		}

		if (winTransitionTimer >= 0f)
			winTransitionTimer += Time.deltaTime;

		if (winTransitionTimer >= timeBeforeNextSceneTransition)
		{
			winTransitionTimer = -1f;
			SceneManager.LoadScene(nextSceneName, LoadSceneMode.Single);
		}
	}

	private void HandleStickEvent(WrappedThreadPin _pin)
	{
		if (currentPin)
			currentPin.Pin(_pin.transform);
		
		_pin.Needle(needle.transform);
		currentPin = _pin;

		currentWallNormal = needle.StuckWallNormal.sqrMagnitude > 0.0001f ? needle.StuckWallNormal.normalized : Vector2.up;
		currentContactPoint = needle.StuckContactPoint;
		
		directionIndicator.StartDirectionMinigame(currentContactPoint, currentWallNormal);
		currentGameState = GameState.DIRECTION_MINIGAME;
	}

	private void HandleFailStateEvent()
	{
		currentGameState = GameState.SCENE_TRANSITION;
		restartTimer = 0f;
		cam.Follow = null;
		powerIndicator.ForceStop();
		directionIndicator.ForceStop();
	}

	private void HandleWinStateEvent()
	{
		currentGameState = GameState.SCENE_TRANSITION;
		winTransitionTimer = 0f;
		powerIndicator.ForceStop();
		directionIndicator.ForceStop();
	}
	
	private void OnJump(InputValue _value)
	{
		if (currentGameState == GameState.SCENE_TRANSITION)
			return;
		
		if (_value.isPressed)
		{
			switch (currentGameState)
			{
				case GameState.DIRECTION_MINIGAME:
					directionOffset = directionIndicator.EndDirectionMinigame();
					currentGameState = GameState.POWER_MINIGAME_START;
					break;
				case GameState.POWER_MINIGAME_START:
					powerIndicator.StartPowerMinigame(currentContactPoint, currentWallNormal);
					currentGameState = GameState.POWER_MINIGAME_HOLD;
					break;
			}
		}
		else
		{
			switch (currentGameState)
			{
				case GameState.POWER_MINIGAME_HOLD:
				{
					float power = powerIndicator.EndPowerMinigame();
					
					Vector2 launchDirection = Quaternion.Euler(0f, 0f, directionOffset) * currentWallNormal;
					needle.Launch(launchDirection, power);
					
					currentGameState = GameState.MOVING;
					break;
				}
			}
		}
	}
}
