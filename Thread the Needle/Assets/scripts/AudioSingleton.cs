using System;
using UnityEngine;
using UnityEngine.Audio;

public class AudioSingleton : MonoBehaviour
{
	[SerializeField] AudioSource menuMusic;
	[SerializeField] AudioSource levelMusic;
	[SerializeField] AudioSource winJingle;
	[SerializeField] AudioMixer audioMixer;
	[SerializeField] float winJingleFadeTime;
	
	public static AudioSingleton Instance { get; private set; }

	private float winJingleFadeTimer;
	
	void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(gameObject);
			return;
		}

		Instance = this;
		DontDestroyOnLoad(gameObject);
	}

	private void Update()
	{
		
	}

	public void PlayMenuMusic()
	{
		menuMusic.Play();
		if (winJingle.isPlaying)
			winJingle.Stop();
		if (levelMusic.isPlaying)
			levelMusic.Stop();
	}
	
	public void

	public void PlaySoundEffect(AudioClip _soundEffect)
	{
		
	}
}
