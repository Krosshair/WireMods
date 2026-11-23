using System;
using System.Collections.Generic;
using UnityEngine;

public class TerrainOp : MonoBehaviour
{
	[Serializable]
	public class Settings
	{
		public float m_levelOffset;

		[Header("Level")]
		public bool m_level;

		public float m_levelRadius = 2f;

		public bool m_square = true;

		[Header("Raise")]
		public bool m_raise;

		public float m_raiseRadius = 2f;

		public float m_raisePower;

		public float m_raiseDelta;

		[Header("Smooth")]
		public bool m_smooth;

		public float m_smoothRadius = 2f;

		public float m_smoothPower = 3f;

		[Header("Paint")]
		public bool m_paintCleared = true;

		public bool m_paintHeightCheck;

		public TerrainModifier.PaintType m_paintType;

		public float m_paintRadius = 2f;

		public float m_paintStrength = 1f;

		public float m_paintExp = 0.1f;

		public AnimationCurve m_paintCurve;

		public bool m_rotation;

		public bool m_sides;

		public float m_addMedianMax;

		public float m_centerMultiplicationFactor;

		public float m_pointMultiplicationFactor;

		public bool m_halfOffset = true;

		public void Serialize(ZPackage pkg, GameObject prefab)
		{
			string prefabName = Utils.GetPrefabName(prefab.name);
			pkg.Write(prefabName.GetStableHashCode());
		}

		public static Settings Deserialize(ZPackage pkg)
		{
			int num = pkg.ReadInt();
			if (ObjectDB.instance.TryGetTerrainOp(num, out var prefab))
			{
				return prefab.m_settings;
			}
			ZLog.LogError($"Failed to deserialize TerrainOp settings for prefab hash {num}, cancelling TerrainOp. Did you add new TerrainOp object and not refresh ZNetScene list?");
			return null;
		}

		public float GetRadius()
		{
			float num = 0f;
			if (m_level && m_levelRadius > num)
			{
				num = m_levelRadius;
			}
			if (m_raise && m_raiseRadius > num)
			{
				num = m_raiseRadius;
			}
			if (m_smooth && m_smoothRadius > num)
			{
				num = m_smoothRadius;
			}
			if (m_paintCleared && m_paintRadius > num)
			{
				num = m_paintRadius;
			}
			return num;
		}
	}

	public static bool m_forceDisableTerrainOps;

	public Settings m_settings = new Settings();

	[Header("Effects")]
	public EffectList m_onPlacedEffect = new EffectList();

	[Header("Spawn items")]
	public GameObject m_spawnOnPlaced;

	public float m_chanceToSpawn = 1f;

	public int m_maxSpawned = 1;

	public bool m_spawnAtMaxLevelDepth = true;

	private void Awake()
	{
		if (m_forceDisableTerrainOps)
		{
			return;
		}
		List<Heightmap> list = new List<Heightmap>();
		Heightmap.FindHeightmap(base.transform.position, GetRadius(), list);
		foreach (Heightmap item in list)
		{
			item.GetAndCreateTerrainCompiler().ApplyOperation(this);
		}
		if (Terminal.m_showTests)
		{
			ZLog.Log($"Painting on {list.Count} heightmaps");
		}
		OnPlaced();
		UnityEngine.Object.Destroy(base.gameObject);
	}

	public float GetRadius()
	{
		return m_settings.GetRadius();
	}

	private void OnPlaced()
	{
		m_onPlacedEffect.Create(base.transform.position, Quaternion.identity);
		if ((bool)m_spawnOnPlaced && (m_spawnAtMaxLevelDepth || !Heightmap.AtMaxLevelDepth(base.transform.position + Vector3.up * m_settings.m_levelOffset)) && UnityEngine.Random.value <= m_chanceToSpawn)
		{
			Vector3 vector = UnityEngine.Random.insideUnitCircle * 0.2f;
			GameObject obj = UnityEngine.Object.Instantiate(m_spawnOnPlaced, base.transform.position + Vector3.up * 0.5f + vector, Quaternion.identity);
			obj.GetComponent<ItemDrop>().m_itemData.m_stack = UnityEngine.Random.Range(1, m_maxSpawned + 1);
			obj.GetComponent<Rigidbody>().linearVelocity = Vector3.up * 4f;
		}
	}

	private void OnDrawGizmosSelected()
	{
		Gizmos.matrix = Matrix4x4.TRS(base.transform.position + Vector3.up * m_settings.m_levelOffset, Quaternion.identity, new Vector3(1f, 0f, 1f));
		if (m_settings.m_level)
		{
			Gizmos.color = Color.green;
			Gizmos.DrawWireSphere(Vector3.zero, m_settings.m_levelRadius);
		}
		if (m_settings.m_smooth)
		{
			Gizmos.color = Color.blue;
			Gizmos.DrawWireSphere(Vector3.zero, m_settings.m_smoothRadius);
		}
		if (m_settings.m_paintCleared)
		{
			Gizmos.color = Color.yellow;
			Gizmos.DrawWireSphere(Vector3.zero, m_settings.m_paintRadius);
		}
		Gizmos.matrix = Matrix4x4.identity;
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.2.0.10050' (yours is '9.1.0.7988')
