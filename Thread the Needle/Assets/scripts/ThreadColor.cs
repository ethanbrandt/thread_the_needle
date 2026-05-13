using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class ThreadColor : MonoBehaviour
{
	[SerializeField] Gradient defaultGradient;
	[SerializeField] List<TileGradient> tileGradients;	
	
	[Serializable]
	public struct TileGradient
	{
		public string tileName;
		public Gradient gradient;
	}
	
	void Start()
	{
		Tilemap tilemap = FindFirstObjectByType<Tilemap>();
		var lineRenderer = GetComponent<LineRenderer>();
		
		Vector3Int gridPos = new Vector3Int((int)Mathf.Round(-transform.up.x + transform.position.x), (int)Mathf.Round(-transform.up.y + transform.position.y), 0);
		Vector3Int gridPosFallback = new Vector3Int((int)Mathf.Round(transform.position.x), (int)Mathf.Round(transform.position.y), 0);
		string connectedTileName = "";
		if (tilemap.GetTile(gridPos))
			connectedTileName = tilemap.GetTile(gridPos).name;
		else if (tilemap.GetTile(gridPosFallback))
			connectedTileName = tilemap.GetTile(gridPosFallback).name;
			
		foreach (var tileGradient in tileGradients)
		{
			if (tileGradient.tileName == connectedTileName)
			{
				lineRenderer.colorGradient = tileGradient.gradient;
				return;
			}
		}

		lineRenderer.colorGradient = defaultGradient;
	}
}
