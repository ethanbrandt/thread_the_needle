using UnityEngine;
using UnityEngine.InputSystem;

public class Controller : MonoBehaviour
{
	[SerializeField] DirectionIndicator directionIndicator;
	[SerializeField] PowerIndicator powerIndicator;
	[SerializeField] Needle needle;

	private float directionOffset;
	
	enum GameState
	{
		STUCK,
		DIRECTION_MINIGAME,
		POWER_MINIGAME_START,
		POWER_MINIGAME_HOLD,
		MOVING
	}

	private GameState currentGameState = GameState.STUCK;

	private void Start()
	{
		needle.stickEvent += HandleStickEvent;
	}

	private void HandleStickEvent()
	{
		currentGameState = GameState.STUCK;
	}
	
	private void OnJump(InputValue _value)
	{
		if (_value.isPressed)
		{
			if (currentGameState == GameState.STUCK)
			{
				directionIndicator.StartDirectionMinigame();
				currentGameState = GameState.DIRECTION_MINIGAME;
			}
			else if (currentGameState == GameState.DIRECTION_MINIGAME)
			{
				directionOffset = directionIndicator.EndDirectionMinigame();
				currentGameState = GameState.POWER_MINIGAME_START;
			}
			else if (currentGameState == GameState.POWER_MINIGAME_START)
			{
				powerIndicator.StartPowerMinigame();
				currentGameState = GameState.POWER_MINIGAME_HOLD;
			}
		}
		else
		{
			if (currentGameState == GameState.POWER_MINIGAME_HOLD)
			{
				
				currentGameState = GameState.MOVING;
			}
		}
	}
}
