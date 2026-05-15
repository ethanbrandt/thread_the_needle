using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class ThreadColorPicker : MonoBehaviour
{
	[SerializeField] Gradient defaultGradient;
	[SerializeField] List<TileGradient> tileGradients;
	[SerializeField] LayerMask wallLayerMask;
	
	[SerializeField] float[] sampleDistances = { 0.05f, 0.15f, 0.35f, 0.5f };
	
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

		TileBase connectedTile = FindConnectedTile(tilemap);
		if (connectedTile != null)
		{
			foreach (var tileGradient in tileGradients)
			{
				if (tileGradient.tileName == connectedTile.name)
				{
					lineRenderer.colorGradient = tileGradient.gradient;
					return;
				}
			}
		}

		string connectedThreadColor = FindConnectedThreadColor();
		print("thread color: " + connectedThreadColor);
		if (connectedThreadColor != "")
		{
			foreach (var tileGradient in tileGradients)
			{
				if (tileGradient.tileName == connectedThreadColor)
				{
					lineRenderer.colorGradient = tileGradient.gradient;
					return;
				}
			}
		}
		
		lineRenderer.colorGradient = defaultGradient;
	}
	
	TileBase FindConnectedTile(Tilemap _tilemap)
	{
		Vector3 inwardDirection = -transform.up;

		foreach (float distance in sampleDistances)
		{
			Vector3 sampleWorldPos = transform.position + inwardDirection * distance;
			Vector3Int cell = _tilemap.WorldToCell(sampleWorldPos);

			TileBase tile = _tilemap.GetTile(cell);

			if (tile != null)
				return tile;
		}

		return null;
	}

	string FindConnectedThreadColor()
	{
		Vector3 inwardDirection = -transform.up;

		foreach (var distance in sampleDistances)
		{
			RaycastHit2D hit = Physics2D.Raycast(transform.position, inwardDirection, distance, wallLayerMask);

			if (hit && hit.transform.TryGetComponent(out ThreadColor threadColor))
				return threadColor.GetColorString();
		}

		return "";
	}
}
