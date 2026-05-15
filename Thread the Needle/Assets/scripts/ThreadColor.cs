using System;
using UnityEngine;

public class ThreadColor : MonoBehaviour
{
	[SerializeField] ThreadColorEnum threadColor;

	public string GetColorString()
	{
		switch (threadColor)
		{
			case ThreadColorEnum.RED:
				return "red_tile";
			case ThreadColorEnum.PURPLE:
				return "purple_tile";
			case ThreadColorEnum.GREEN:
				return "green_tile";
			case ThreadColorEnum.YELLOW:
				return "yellow_tile";
			case ThreadColorEnum.BLUE:
				return "blue_tile";
		}

		return "";
	}
	
	[Serializable]
	public enum ThreadColorEnum
	{
		RED,
		PURPLE,
		GREEN,
		YELLOW,
		BLUE
	}
}
