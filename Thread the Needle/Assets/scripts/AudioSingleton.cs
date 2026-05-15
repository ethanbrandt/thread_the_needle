using System.Collections;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Serialization;

public class AudioSingleton : MonoBehaviour
{
	[Header("Music")]
	[SerializeField] AudioSource menuMusic;
	[SerializeField] AudioSource levelMusic;
	[SerializeField] AudioSource winJingle;
	[SerializeField] AudioMixer audioMixer;
	[SerializeField, FormerlySerializedAs("winJingleFadeTime")] float musicFadeTime = 1f;

	[Header("Sound Effects")]
	[SerializeField] AudioMixerGroup soundEffectMixerGroup;
	[SerializeField, Range(0f, 1f)] float soundEffectVolume = 1f;
	[SerializeField] GameObject sfxObjectPrefab;
	
	public static AudioSingleton Instance { get; private set; }

	private AudioSource currentMusic;
	private Coroutine musicFadeRoutine;
	private float menuMusicVolume = 1f;
	private float levelMusicVolume = 1f;
	private float winJingleVolume = 1f;
	private bool menuMusicStarted;
	private bool levelMusicStarted;
	private bool winJingleStarted;
	
	void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(gameObject);
			return;
		}

		Instance = this;
		DontDestroyOnLoad(gameObject);
		CacheMusicVolumes();
		CacheStartedMusic();
	}

	public void PlayMenuMusic()
	{
		FadeToMusic(menuMusic);
	}

	public void PlayLevelMusic()
	{
		FadeToMusic(levelMusic);
	}

	public void PlayWinJingle()
	{
		FadeToMusic(winJingle);
	}
	
	public void PlaySoundEffect(AudioClip _soundEffect, Vector3 _position)
	{
		if (!_soundEffect)
			return;

		GameObject soundEffectObject = Instantiate(sfxObjectPrefab, _position, Quaternion.identity);
		DontDestroyOnLoad(soundEffectObject);

		AudioSource soundEffectSource = soundEffectObject.GetComponent<AudioSource>();
		soundEffectSource.clip = _soundEffect;
		soundEffectSource.Play();

		float pitch = Mathf.Abs(soundEffectSource.pitch);
		float destroyDelay = pitch > 0.001f ? _soundEffect.length / pitch : _soundEffect.length;
		Destroy(soundEffectObject, destroyDelay + 3f);
	}

	private void FadeToMusic(AudioSource _nextMusic)
	{
		if (_nextMusic == null)
			return;

		if (musicFadeRoutine != null)
			StopCoroutine(musicFadeRoutine);

		musicFadeRoutine = StartCoroutine(FadeToMusicRoutine(_nextMusic));
	}

	private IEnumerator FadeToMusicRoutine(AudioSource _nextMusic)
	{
		float nextStartVolume = currentMusic == _nextMusic ? _nextMusic.volume : 0f;
		float nextTargetVolume = GetTargetVolume(_nextMusic);
		float fadeDuration = Mathf.Max(0f, musicFadeTime);
		float menuStartVolume = menuMusic != _nextMusic && menuMusic != null ? menuMusic.volume : 0f;
		float levelStartVolume = levelMusic != _nextMusic && levelMusic != null ? levelMusic.volume : 0f;
		float winStartVolume = winJingle != _nextMusic && winJingle != null ? winJingle.volume : 0f;

		PrepareMusicSource(_nextMusic);
		currentMusic = _nextMusic;

		if (fadeDuration <= 0f)
		{
			StopOrPauseOtherMusic(_nextMusic);
			_nextMusic.volume = nextTargetVolume;
			musicFadeRoutine = null;
			yield break;
		}

		float timer = 0f;
		while (timer < fadeDuration)
		{
			timer += Time.deltaTime;
			float fadePercent = Mathf.Clamp01(timer / fadeDuration);

			FadeMusicSourceOut(menuMusic, _nextMusic, menuStartVolume, fadePercent);
			FadeMusicSourceOut(levelMusic, _nextMusic, levelStartVolume, fadePercent);
			FadeMusicSourceOut(winJingle, _nextMusic, winStartVolume, fadePercent);
			_nextMusic.volume = Mathf.Lerp(nextStartVolume, nextTargetVolume, fadePercent);
			yield return null;
		}

		StopOrPauseOtherMusic(_nextMusic);
		_nextMusic.volume = nextTargetVolume;
		musicFadeRoutine = null;
	}

	private void PrepareMusicSource(AudioSource _musicSource)
	{
		_musicSource.volume = currentMusic == _musicSource ? _musicSource.volume : 0f;

		if (!_musicSource.loop)
		{
			_musicSource.Stop();
			_musicSource.time = 0f;
			_musicSource.Play();
			SetMusicStarted(_musicSource, true);
			return;
		}

		if (HasMusicStarted(_musicSource))
			_musicSource.UnPause();
		else
		{
			_musicSource.Play();
			SetMusicStarted(_musicSource, true);
		}
	}

	private void StopOrPauseMusic(AudioSource _musicSource)
	{
		if (!_musicSource.loop)
		{
			_musicSource.Stop();
			return;
		}

		_musicSource.Pause();
	}

	private void StopOrPauseOtherMusic(AudioSource _except)
	{
		StopOrPauseOtherMusicSource(menuMusic, _except);
		StopOrPauseOtherMusicSource(levelMusic, _except);
		StopOrPauseOtherMusicSource(winJingle, _except);
	}

	private void StopOrPauseOtherMusicSource(AudioSource _musicSource, AudioSource _except)
	{
		if (_musicSource == null || _musicSource == _except)
			return;

		_musicSource.volume = 0f;
		StopOrPauseMusic(_musicSource);
	}

	private void FadeMusicSourceOut(AudioSource _musicSource, AudioSource _except, float _startVolume, float _fadePercent)
	{
		if (!_musicSource || _musicSource == _except)
			return;

		_musicSource.volume = Mathf.Lerp(_startVolume, 0f, _fadePercent);
	}

	private float GetTargetVolume(AudioSource _musicSource)
	{
		if (_musicSource == menuMusic)
			return menuMusicVolume;

		if (_musicSource == levelMusic)
			return levelMusicVolume;

		if (_musicSource == winJingle)
			return winJingleVolume;

		return 1f;
	}

	private bool HasMusicStarted(AudioSource _musicSource)
	{
		if (_musicSource == menuMusic)
			return menuMusicStarted;

		if (_musicSource == levelMusic)
			return levelMusicStarted;

		if (_musicSource == winJingle)
			return winJingleStarted;

		return _musicSource.isPlaying;
	}

	private void SetMusicStarted(AudioSource _musicSource, bool _started)
	{
		if (_musicSource == menuMusic)
			menuMusicStarted = _started;
		else if (_musicSource == levelMusic)
			levelMusicStarted = _started;
		else if (_musicSource == winJingle)
			winJingleStarted = _started;
	}

	private void CacheMusicVolumes()
	{
		if (menuMusic != null)
			menuMusicVolume = menuMusic.volume;

		if (levelMusic != null)
			levelMusicVolume = levelMusic.volume;

		if (winJingle != null)
			winJingleVolume = winJingle.volume;
	}

	private void CacheStartedMusic()
	{
		CacheStartedMusicSource(menuMusic);
		CacheStartedMusicSource(levelMusic);
		CacheStartedMusicSource(winJingle);
	}

	private void CacheStartedMusicSource(AudioSource _musicSource)
	{
		if (_musicSource == null || !_musicSource.isPlaying)
			return;

		SetMusicStarted(_musicSource, true);
		currentMusic = _musicSource;
	}
}
