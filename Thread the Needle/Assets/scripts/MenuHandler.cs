using System;
using FullscreenEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class MenuHandler : MonoBehaviour
{
	[SerializeField] string firstLevelSceneName;
	[SerializeField] Canvas settingsCanvas;
	[SerializeField] AudioMixer audioMixer;
	[SerializeField] AudioMixerGroup sfxGroup;
	[SerializeField] AudioMixerGroup musicGroup;
	[SerializeField] Material mat;
	[SerializeField] float minDB;
	[SerializeField] float maxDB;

	private bool inSettingsScreen;

	private void Start()
	{
		OnMasterVolumeChanged(0.5f);
		OnMusicVolumeChanged(0.5f);
		OnSfxVolumeChanged(0.5f);
		OnReduceMovementChanged(false);
		AudioSingleton.Instance.PlayMenuMusic();
		settingsCanvas.enabled = false;
	}

	public void StartButton()
	{
		if (inSettingsScreen)
			return;

		SceneManager.LoadScene(firstLevelSceneName, LoadSceneMode.Single);
	}

	public void SettingsButton()
	{
		if (inSettingsScreen)
			return;
		
		inSettingsScreen = true;
		settingsCanvas.enabled = true;
	}

	public void ExitSettingsButton()
	{
		if (!inSettingsScreen)
			return;

		inSettingsScreen = false;
		settingsCanvas.enabled = false;
	}

	public void OnMasterVolumeChanged(float _value)
	{
		audioMixer.SetFloat(audioMixer.name, Mathf.Lerp(minDB, maxDB, _value));
	}

	public void OnMusicVolumeChanged(float _value)
	{
		audioMixer.SetFloat(musicGroup.name, Mathf.Lerp(minDB, maxDB, _value));
	}

	public void OnSfxVolumeChanged(float _value)
	{
		audioMixer.SetFloat(sfxGroup.name, Mathf.Lerp(minDB + 5, maxDB, _value));
	}

	public void OnReduceMovementChanged(bool _value)
	{
		mat.SetInt("_reduce_background_movement", _value ? 1 : 0);
	}
}
