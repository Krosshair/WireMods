using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class TerrainComp : MonoBehaviour
{
	private const int terrainCompVersion = 1;

	private int m_lastHash;

	private static List<float> m_neighborHeights = new List<float>();

	private static readonly List<TerrainComp> s_instances = new List<TerrainComp>();

	private static readonly HashSet<TerrainComp> s_duplicateInstances = new HashSet<TerrainComp>();

	private bool m_initialized;

	private int m_width;

	private float m_size;

	private int m_pitch;

	private int m_operations;

	private bool[] m_modifiedHeight;

	private float[] m_levelDelta;

	private float[] m_smoothDelta;

	private bool[] m_modifiedPaint;

	private Color[] m_paintMask;

	private Heightmap m_hmap;

	private ZNetView m_nview;

	private uint m_lastDataRevision = uint.MaxValue;

	private Vector3 m_lastOpPoint;

	private float m_lastOpRadius;

	private List<Heightmap> m_neighbors;

	private TerrainComp[,] m_neighborGrid;

	private void Awake()
	{
		m_nview = GetComponent<ZNetView>();
		m_hmap = Heightmap.FindHeightmap(base.transform.position);
		if (m_hmap == null)
		{
			ZLog.LogWarning("Terrain compiler could not find hmap");
			return;
		}
		TerrainComp terrainComp = FindTerrainCompiler(base.transform.position);
		if ((bool)terrainComp)
		{
			s_duplicateInstances.Add(terrainComp);
			s_duplicateInstances.Add(this);
		}
		s_instances.Add(this);
		m_nview.Register<ZPackage>("RPC_ApplyOperation", RPC_ApplyOperation);
		Initialize();
		CheckLoad();
	}

	private void Start()
	{
		TryCleanInvalidTCs();
	}

	private void TryCleanInvalidTCs()
	{
		if (s_duplicateInstances.Count == 0)
		{
			return;
		}
		s_duplicateInstances.TryGetValue(this, out var actualValue);
		Vector3 position = base.transform.position;
		Vector3? obj = actualValue?.transform.position;
		if (position != obj)
		{
			return;
		}
		List<TerrainComp> list = s_duplicateInstances.OrderBy((TerrainComp x) => x.m_operations).ToList();
		for (int num = 0; num < list.Count - 1; num++)
		{
			TerrainComp terrainComp = list[num];
			if (terrainComp.m_nview.IsValid() && !terrainComp.m_nview.HasOwner())
			{
				terrainComp.m_nview.ClaimOwnership();
			}
			ZNetScene.instance.Destroy(terrainComp.gameObject);
			s_instances.Remove(terrainComp);
			ZLog.LogWarning($"Removed duplicate terrain compiler with {terrainComp.m_operations} operations performed on it.");
		}
		TerrainComp terrainComp2 = list[list.Count - 1];
		int count = FindAllTerrainCompilers(terrainComp2.transform.position).Count;
		s_duplicateInstances.Clear();
		ZLog.Log($"There should only be one terrainCompiler found at this area now, is that correct? [{count == 1}]. Amount of operations the terrain compiler that was kept: [{FindTerrainCompiler(terrainComp2.transform.position).m_operations}]");
	}

	private void OnDestroy()
	{
		s_instances.Remove(this);
	}

	private void Update()
	{
		if (m_nview.IsValid())
		{
			CheckLoad();
		}
	}

	private void Initialize()
	{
		m_initialized = true;
		m_width = m_hmap.m_width;
		m_size = (float)m_width * m_hmap.m_scale;
		m_pitch = m_width + 1;
		m_modifiedHeight = new bool[m_pitch * m_pitch];
		m_levelDelta = new float[m_pitch * m_pitch];
		m_smoothDelta = new float[m_pitch * m_pitch];
		m_modifiedPaint = new bool[m_pitch * m_pitch];
		m_paintMask = new Color[m_pitch * m_pitch];
		m_paintMask = m_hmap.GetPaintMask().GetPixels();
	}

	private int ComputePaintMaskHash()
	{
		double num = 0.0;
		for (int i = 0; i < m_modifiedPaint.Length; i++)
		{
			num += (double)(m_modifiedPaint[i] ? 1 : 0);
			if (m_modifiedPaint[i])
			{
				Color color = m_paintMask[i];
				num += (double)color.r;
				num += (double)color.g;
				num += (double)color.b;
				num += (double)color.a;
			}
		}
		return num.GetHashCode();
	}

	private void Save(bool paintOnly = false)
	{
		if (!m_initialized || !m_nview.IsValid() || !m_nview.IsOwner())
		{
			return;
		}
		int num = (paintOnly ? ComputePaintMaskHash() : 0);
		if (num == m_lastHash && paintOnly)
		{
			return;
		}
		m_lastHash = num;
		ZPackage zPackage = new ZPackage();
		zPackage.Write(1);
		zPackage.Write(m_operations);
		zPackage.Write(m_lastOpPoint);
		zPackage.Write(m_lastOpRadius);
		zPackage.Write(m_modifiedHeight.Length);
		for (int i = 0; i < m_modifiedHeight.Length; i++)
		{
			zPackage.Write(m_modifiedHeight[i]);
			if (m_modifiedHeight[i])
			{
				zPackage.Write(m_levelDelta[i]);
				zPackage.Write(m_smoothDelta[i]);
			}
		}
		zPackage.Write(m_modifiedPaint.Length);
		for (int j = 0; j < m_modifiedPaint.Length; j++)
		{
			zPackage.Write(m_modifiedPaint[j]);
			if (m_modifiedPaint[j])
			{
				zPackage.Write(m_paintMask[j].r);
				zPackage.Write(m_paintMask[j].g);
				zPackage.Write(m_paintMask[j].b);
				zPackage.Write(m_paintMask[j].a);
			}
		}
		byte[] bytes = Utils.Compress(zPackage.GetArray());
		m_nview.GetZDO().Set(ZDOVars.s_TCData, bytes);
		m_lastDataRevision = m_nview.GetZDO().DataRevision;
	}

	private void CheckLoad()
	{
		if (m_nview.GetZDO().DataRevision == m_lastDataRevision)
		{
			return;
		}
		int operations = m_operations;
		if (!Load())
		{
			return;
		}
		m_hmap.Poke();
		if ((bool)ClutterSystem.instance)
		{
			if (m_operations == operations + 1)
			{
				ClutterSystem.instance.ResetGrass(m_lastOpPoint, m_lastOpRadius);
			}
			else
			{
				ClutterSystem.instance.ResetGrass(m_hmap.transform.position, (float)m_hmap.m_width * m_hmap.m_scale / 2f);
			}
		}
	}

	private bool Load()
	{
		m_lastDataRevision = m_nview.GetZDO().DataRevision;
		byte[] byteArray = m_nview.GetZDO().GetByteArray(ZDOVars.s_TCData);
		if (byteArray == null)
		{
			return false;
		}
		ZPackage zPackage = new ZPackage(Utils.Decompress(byteArray));
		zPackage.ReadInt();
		m_operations = zPackage.ReadInt();
		m_lastOpPoint = zPackage.ReadVector3();
		m_lastOpRadius = zPackage.ReadSingle();
		int num = zPackage.ReadInt();
		if (num != m_modifiedHeight.Length)
		{
			ZLog.LogWarning("Terrain data load error, height array missmatch");
			return false;
		}
		for (int i = 0; i < num; i++)
		{
			m_modifiedHeight[i] = zPackage.ReadBool();
			if (m_modifiedHeight[i])
			{
				m_levelDelta[i] = zPackage.ReadSingle();
				m_smoothDelta[i] = zPackage.ReadSingle();
			}
			else
			{
				m_levelDelta[i] = 0f;
				m_smoothDelta[i] = 0f;
			}
		}
		int num2 = zPackage.ReadInt();
		for (int j = 0; j < num2; j++)
		{
			m_modifiedPaint[j] = zPackage.ReadBool();
			if (m_modifiedPaint[j])
			{
				Color color = new Color
				{
					r = zPackage.ReadSingle(),
					g = zPackage.ReadSingle(),
					b = zPackage.ReadSingle(),
					a = zPackage.ReadSingle()
				};
				m_paintMask[j] = color;
			}
		}
		if (num2 == m_width * m_width)
		{
			Color[] array = new Color[m_paintMask.Length];
			m_paintMask.CopyTo(array, 0);
			bool[] array2 = new bool[m_modifiedPaint.Length];
			m_modifiedPaint.CopyTo(array2, 0);
			int num3 = m_width + 1;
			for (int k = 0; k < m_paintMask.Length; k++)
			{
				int num4 = k / num3;
				int num5 = (k + 1) / num3;
				int num6 = k - num4;
				if (num4 == m_width)
				{
					num6 -= m_width;
				}
				if (k > 0 && (k - num4) % m_width == 0 && (k + 1 - num5) % m_width == 0)
				{
					num6--;
				}
				m_paintMask[k] = array[num6];
				m_modifiedPaint[k] = array2[num6];
			}
		}
		return true;
	}

	public static TerrainComp FindTerrainCompiler(Vector3 pos)
	{
		foreach (TerrainComp s_instance in s_instances)
		{
			float num = s_instance.m_size / 2f;
			Vector3 position = s_instance.transform.position;
			if (pos.x >= position.x - num && pos.x <= position.x + num && pos.z >= position.z - num && pos.z <= position.z + num)
			{
				return s_instance;
			}
		}
		return null;
	}

	public static List<TerrainComp> FindAllTerrainCompilers(Vector3 pos)
	{
		List<TerrainComp> list = new List<TerrainComp>();
		foreach (TerrainComp s_instance in s_instances)
		{
			float num = s_instance.m_size / 2f;
			Vector3 position = s_instance.transform.position;
			if (pos.x >= position.x - num && pos.x <= position.x + num && pos.z >= position.z - num && pos.z <= position.z + num)
			{
				list.Add(s_instance);
			}
		}
		return list;
	}

	public static bool ValidTCForAllAffectedHeightmaps(Vector3 pos, float radius)
	{
		List<Heightmap> list = new List<Heightmap>();
		Heightmap.FindHeightmap(pos, radius, list);
		int num = 0;
		foreach (Heightmap item in list)
		{
			if ((bool)FindTerrainCompiler(item.transform.position))
			{
				num++;
			}
		}
		return num == list.Count;
	}

	public void ApplyToHeightmap(Texture2D clearedMask, List<float> heights, float[] baseHeights, float[] levelOnlyHeights, Heightmap hm)
	{
		if (!m_initialized)
		{
			return;
		}
		int num = m_width + 1;
		for (int i = 0; i < num; i++)
		{
			for (int j = 0; j < num; j++)
			{
				int num2 = i * num + j;
				float num3 = m_levelDelta[num2];
				float num4 = m_smoothDelta[num2];
				if (num3 != 0f || num4 != 0f)
				{
					float num5 = heights[num2];
					float num6 = baseHeights[num2];
					float value = num5 + num3 + num4;
					value = Mathf.Clamp(value, num6 - 8f, num6 + 8f);
					heights[num2] = value;
				}
			}
		}
		for (int k = 0; k < num; k++)
		{
			for (int l = 0; l < num; l++)
			{
				int num7 = k * num + l;
				if (m_modifiedPaint[num7])
				{
					clearedMask.SetPixel(l, k, m_paintMask[num7]);
				}
			}
		}
	}

	public void ApplyOperation(TerrainOp modifier)
	{
		if (!m_nview.IsValid())
		{
			ZLog.LogError("Attempted to apply operation on invalid TerrainComp");
			return;
		}
		ZPackage zPackage = new ZPackage();
		zPackage.Write(modifier.transform.position);
		zPackage.Write(modifier.m_settings.m_rotation);
		if (modifier.m_settings.m_rotation)
		{
			zPackage.Write(modifier.transform.forward);
		}
		modifier.m_settings.Serialize(zPackage, modifier.gameObject);
		m_nview.InvokeRPC("RPC_ApplyOperation", zPackage);
	}

	private void RPC_ApplyOperation(long sender, ZPackage pkg)
	{
		if (m_nview.IsOwner())
		{
			Vector3 pos = pkg.ReadVector3();
			Vector3 rot = Vector3.zero;
			if (pkg.ReadBool())
			{
				rot = pkg.ReadVector3();
			}
			TerrainOp.Settings settings = TerrainOp.Settings.Deserialize(pkg);
			if (settings != null)
			{
				DoOperation(pos, rot, settings);
			}
		}
	}

	private void DoOperation(Vector3 pos, Vector3 rot, TerrainOp.Settings modifier)
	{
		if (m_initialized)
		{
			InternalDoOperation(pos, rot, modifier);
			bool paintOnly = modifier.m_paintCleared && !modifier.m_level && !modifier.m_raise && !modifier.m_smooth;
			Save(paintOnly);
			m_hmap.Poke(1, paintOnly);
			if ((bool)ClutterSystem.instance)
			{
				ClutterSystem.instance.ResetGrass(pos, modifier.GetRadius());
			}
		}
	}

	private void InternalDoOperation(Vector3 pos, Vector3 rot, TerrainOp.Settings modifier)
	{
		if (modifier.m_level)
		{
			LevelTerrain(pos + Vector3.up * modifier.m_levelOffset, modifier.m_levelRadius, modifier.m_square);
		}
		if (modifier.m_raise)
		{
			RaiseTerrain(pos, modifier.m_raiseRadius, modifier.m_raiseDelta, modifier.m_square, modifier.m_raisePower);
		}
		if (modifier.m_smooth)
		{
			SmoothTerrain(pos + Vector3.up * modifier.m_levelOffset, modifier.m_smoothRadius, modifier.m_square, modifier.m_smoothPower);
		}
		if (modifier.m_paintCleared)
		{
			PaintCleared(pos, rot, modifier);
		}
		m_operations++;
		m_lastOpPoint = pos;
		m_lastOpRadius = modifier.GetRadius();
	}

	private void LevelTerrain(Vector3 worldPos, float radius, bool square)
	{
		m_hmap.WorldToVertex(worldPos, out var x, out var y);
		Vector3 vector = worldPos - base.transform.position;
		float num = radius / m_hmap.m_scale;
		int num2 = Mathf.CeilToInt(num);
		int num3 = m_width + 1;
		Vector2 a = new Vector2(x, y);
		for (int i = y - num2; i <= y + num2; i++)
		{
			for (int j = x - num2; j <= x + num2; j++)
			{
				if ((square || !(Vector2.Distance(a, new Vector2(j, i)) > num)) && j >= 0 && i >= 0 && j < num3 && i < num3)
				{
					float height = m_hmap.GetHeight(j, i);
					float num4 = vector.y - height;
					int num5 = i * num3 + j;
					num4 += m_smoothDelta[num5];
					m_smoothDelta[num5] = 0f;
					m_levelDelta[num5] += num4;
					m_levelDelta[num5] = Mathf.Clamp(m_levelDelta[num5], -8f, 8f);
					m_modifiedHeight[num5] = true;
				}
			}
		}
	}

	private void RaiseTerrain(Vector3 worldPos, float radius, float delta, bool square, float power)
	{
		m_hmap.WorldToVertex(worldPos, out var x, out var y);
		Vector3 vector = worldPos - base.transform.position;
		float num = radius / m_hmap.m_scale;
		int num2 = Mathf.CeilToInt(num);
		int num3 = m_width + 1;
		Vector2 a = new Vector2(x, y);
		for (int i = y - num2; i <= y + num2; i++)
		{
			for (int j = x - num2; j <= x + num2; j++)
			{
				if (j < 0 || i < 0 || j >= num3 || i >= num3)
				{
					continue;
				}
				float num4 = 1f;
				if (!square)
				{
					float num5 = Vector2.Distance(a, new Vector2(j, i));
					if (num5 > num)
					{
						continue;
					}
					if (power > 0f)
					{
						num4 = num5 / num;
						num4 = 1f - num4;
						if (power != 1f)
						{
							num4 = Mathf.Pow(num4, power);
						}
					}
				}
				float height = m_hmap.GetHeight(j, i);
				float num6 = delta * num4;
				float num7 = vector.y + num6;
				if (delta < 0f && num7 > height)
				{
					continue;
				}
				if (delta > 0f)
				{
					if (num7 < height)
					{
						continue;
					}
					if (num7 > height + num6)
					{
						num7 = height + num6;
					}
				}
				int num8 = i * num3 + j;
				float num9 = num7 - height + m_smoothDelta[num8];
				m_smoothDelta[num8] = 0f;
				m_levelDelta[num8] += num9;
				m_levelDelta[num8] = Mathf.Clamp(m_levelDelta[num8], -8f, 8f);
				m_modifiedHeight[num8] = true;
			}
		}
	}

	private void SmoothTerrain(Vector3 worldPos, float radius, bool square, float power)
	{
		m_hmap.WorldToVertex(worldPos, out var x, out var y);
		float b = worldPos.y - base.transform.position.y;
		float num = radius / m_hmap.m_scale;
		int num2 = Mathf.CeilToInt(num);
		Vector2 a = new Vector2(x, y);
		int num3 = m_width + 1;
		for (int i = y - num2; i <= y + num2; i++)
		{
			for (int j = x - num2; j <= x + num2; j++)
			{
				float num4 = Vector2.Distance(a, new Vector2(j, i));
				if (!(num4 > num) && j >= 0 && i >= 0 && j < num3 && i < num3)
				{
					float num5 = num4 / num;
					num5 = ((power != 3f) ? Mathf.Pow(num5, power) : (num5 * num5 * num5));
					float height = m_hmap.GetHeight(j, i);
					float t = 1f - num5;
					float num6 = Mathf.Lerp(height, b, t) - height;
					int num7 = i * num3 + j;
					m_smoothDelta[num7] += num6;
					m_smoothDelta[num7] = Mathf.Clamp(m_smoothDelta[num7], -1f, 1f);
					m_modifiedHeight[num7] = true;
				}
			}
		}
	}

	private void PaintCleared(Vector3 worldPos, Vector3 rot, TerrainOp.Settings settings)
	{
		if (settings.m_halfOffset)
		{
			worldPos.x -= 0.5f;
			worldPos.z -= 0.5f;
		}
		bool isChangingSnow = WorldGenerator.IsDeepnorth(worldPos.x, worldPos.z) && settings.m_paintType == TerrainModifier.PaintType.Cultivate;
		float num = worldPos.y - base.transform.position.y;
		m_hmap.WorldToVertexMask(worldPos, out var x, out var y);
		float num2 = settings.m_paintRadius / m_hmap.m_scale;
		int num3 = Mathf.CeilToInt(num2);
		Vector2 a = new Vector2(x, y);
		Color color = Color.black;
		if (settings.m_centerMultiplicationFactor > 0f)
		{
			if (x < 0 || y < 0 || x > m_width || y > m_width)
			{
				Heightmap heightmap = Heightmap.FindHeightmap(worldPos);
				if ((bool)heightmap)
				{
					heightmap.WorldToVertexMask(worldPos, out var x2, out var y2);
					color = getMask(heightmap, FindTerrainCompiler(heightmap.transform.position), x2, y2, getIndex(x2, y2));
				}
			}
			else
			{
				color = getMask(m_hmap, this, x, y, getIndex(x, y));
			}
		}
		if (m_neighbors == null)
		{
			m_neighbors = new List<Heightmap>();
		}
		m_neighbors.Clear();
		int num4 = Mathf.Min(m_width, y + num3);
		int num5 = Mathf.Min(m_width, x + num3);
		int y3 = Mathf.Max(0, y - num3);
		while (y3 <= num4)
		{
			int x3 = Mathf.Max(0, x - num3);
			int num12;
			while (x3 <= num5)
			{
				float num6 = Vector2.Distance(a, new Vector2(x3, y3));
				Color color2;
				float clampMedian;
				if (!settings.m_paintHeightCheck || !(m_hmap.GetHeight(x3, y3) > num))
				{
					float num7 = 0f;
					float num8 = num6 / num2;
					if (settings.m_paintCurve == null || settings.m_paintCurve.length == 0)
					{
						num7 = 1f - Mathf.Clamp01(num8);
						num7 = Mathf.Pow(num7, settings.m_paintExp);
					}
					else
					{
						num7 = settings.m_paintCurve.Evaluate(num8);
					}
					num7 *= settings.m_paintStrength;
					if (settings.m_sides && settings.m_rotation)
					{
						Vector3 normalized = new Vector3((float)x3 - a.x, 0f, (float)y3 - a.y).normalized;
						float num9 = 1f - Mathf.Abs(Vector3.Dot(rot, normalized));
						num7 *= num9;
					}
					int num10 = getIndex(x3, y3);
					color2 = getMask(m_hmap, this, x3, y3, num10);
					float a2 = color2.a;
					clampMedian = 0f;
					if (num7 > 0f && color2.g > 0f && settings.m_addMedianMax > 0f)
					{
						m_neighborHeights.Clear();
						Mask(x3 - 1, y3 - 1);
						Mask(x3 - 1, y3);
						Mask(x3 - 1, y3 + 1);
						Mask(x3, y3 - 1);
						Mask(x3, y3 + 1);
						Mask(x3 + 1, y3 - 1);
						Mask(x3 + 1, y3);
						Mask(x3 + 1, y3 + 1);
						float num11 = m_neighborHeights.Median();
						clampMedian = num11 * settings.m_addMedianMax;
					}
					Color color3 = settings.m_paintType switch
					{
						TerrainModifier.PaintType.Cultivate => Heightmap.m_paintMaskCultivated, 
						TerrainModifier.PaintType.Paved => Heightmap.m_paintMaskPaved, 
						TerrainModifier.PaintType.Reset => Heightmap.m_paintMaskNothing, 
						TerrainModifier.PaintType.ClearVegetation => Heightmap.m_paintMaskClearVegetation, 
						TerrainModifier.PaintType.DeepSnow => Heightmap.m_paintMaskDeepSnow, 
						_ => Heightmap.m_paintMaskDirt, 
					};
					if (!isChangingSnow || color3.r > 0f)
					{
						color2.r = modifyTerrainValue(color2.r, color3.r, num7, color.r);
					}
					if (!isChangingSnow || color3.g > 0f)
					{
						color2.g = modifyTerrainValue(color2.g, color3.g, num7, color.g);
					}
					if (!isChangingSnow || color3.b > 0f)
					{
						color2.b = modifyTerrainValue(color2.b, color3.b, num7, color.b);
					}
					color2.a = a2;
					m_modifiedPaint[num10] = true;
					m_paintMask[num10] = color2;
					if (x3 == 0)
					{
						spread(0, 1);
						if (y3 == 0)
						{
							spread(0, 0);
							spread(1, 0);
						}
						else if (y3 == m_width)
						{
							spread(0, 2);
							spread(1, 2);
						}
					}
					else if (x3 == m_width)
					{
						spread(2, 1);
						if (y3 == 0)
						{
							spread(2, 0);
							spread(1, 0);
						}
						else if (y3 == m_width)
						{
							spread(2, 2);
							spread(1, 2);
						}
					}
					else if (y3 == 0)
					{
						spread(1, 0);
					}
					else if (y3 == m_width)
					{
						spread(1, 2);
					}
				}
				num12 = x3 + 1;
				x3 = num12;
				float modifyTerrainValue(float from, float to, float value, float centerFactor)
				{
					if (settings.m_centerMultiplicationFactor > 0f)
					{
						value = value * (1f - settings.m_centerMultiplicationFactor) + value * settings.m_centerMultiplicationFactor * centerFactor;
					}
					if (settings.m_pointMultiplicationFactor > 0f)
					{
						value = value * (1f - settings.m_pointMultiplicationFactor) + value * settings.m_pointMultiplicationFactor * from;
					}
					float num13 = (isChangingSnow ? Mathf.Clamp01(from + value) : Mathf.Lerp(from, to, value));
					if (clampMedian > 0f && num13 > from)
					{
						num13 = Mathf.Min(num13, clampMedian);
					}
					return num13;
				}
				bool spread(int ox, int oy)
				{
					if (ox < 0)
					{
						return false;
					}
					TerrainComp terrainComp = TryGetNeighbor(worldPos, ox, oy, settings.m_paintRadius);
					if (terrainComp != null)
					{
						GetNearestVertex(terrainComp, x3, y3, out var ox2, out var oy2);
						int num13 = getIndex(ox2, oy2);
						terrainComp.m_modifiedPaint[num13] = true;
						terrainComp.m_paintMask[num13] = color2;
						bool paintOnly = settings.m_paintCleared && !settings.m_level && !settings.m_raise && !settings.m_smooth;
						terrainComp.m_hmap.Poke(1, paintOnly);
					}
					return true;
				}
			}
			num12 = y3 + 1;
			y3 = num12;
		}
		void Mask(int num13, int num14)
		{
			if (num13 >= 0 && num13 <= m_width && num14 >= 0 && num14 <= m_width)
			{
				int num15 = getIndex(num13, num14);
				m_neighborHeights.Add(m_paintMask[num15].g);
			}
		}
		int getIndex(int num14, int num13)
		{
			return num13 * m_pitch + num14;
		}
		static Color getMask(Heightmap hmap, TerrainComp tc, int x4, int y4, int index)
		{
			if (hmap.m_doLateUpdate != 1 || !(tc != null))
			{
				return hmap.GetPaintMask(x4, y4);
			}
			return tc.m_paintMask[index];
		}
	}

	private TerrainComp TryGetNeighbor(Vector3 worldPos, int x, int y, float radius)
	{
		if (m_neighborGrid == null)
		{
			m_neighborGrid = new TerrainComp[3, 3];
		}
		if (m_neighborGrid[x, y] == null)
		{
			m_neighbors.Clear();
			Heightmap.FindHeightmap(worldPos, radius, m_neighbors);
			Vector3 position = base.transform.position;
			foreach (Heightmap neighbor in m_neighbors)
			{
				Vector3 position2 = neighbor.transform.position;
				int num = ((!(position2.x < position.x)) ? ((!(position2.x > position.x)) ? 1 : 2) : 0);
				int num2 = ((!(position2.z < position.z)) ? ((!(position2.z > position.z)) ? 1 : 2) : 0);
				m_neighborGrid[num, num2] = FindTerrainCompiler(neighbor.transform.position);
			}
		}
		return m_neighborGrid[x, y];
	}

	private void GetNearestVertex(TerrainComp tc, int x, int y, out int ox, out int oy)
	{
		Vector3 worldPos = m_hmap.VertexMaskToWorld(x, y);
		tc.m_hmap.WorldToVertex(worldPos, out ox, out oy);
		ox = Mathf.Clamp(ox, 0, m_width);
		oy = Mathf.Clamp(oy, 0, m_width);
	}

	private bool GetNeighborOffset(int coordX, int coordY, out int x, out int y)
	{
		x = ((coordX != 0) ? ((coordX != m_width) ? 1 : 2) : 0);
		y = ((coordY != 0) ? ((coordY != m_width) ? 1 : 2) : 0);
		if (x == 1)
		{
			return y != 1;
		}
		return true;
	}

	public bool IsOwner()
	{
		if (!m_nview.IsValid())
		{
			return false;
		}
		return m_nview.IsOwner();
	}

	public void UpdatePaintMask(Heightmap hmap)
	{
		for (int i = 0; i < m_width; i++)
		{
			for (int j = 0; j < m_width; j++)
			{
				int num = i * m_width + j;
				if (m_modifiedPaint[num])
				{
					Color color = m_paintMask[num];
					color.a = hmap.GetPaintMask(j, i).a;
					m_paintMask[num] = color;
				}
			}
		}
		Save();
		hmap.Poke();
	}

	public static void UpgradeTerrain()
	{
		if (Player.m_localPlayer == null)
		{
			return;
		}
		List<Heightmap> list = new List<Heightmap>();
		Heightmap.FindHeightmap(Player.m_localPlayer.transform.position, 150f, list);
		bool flag = false;
		foreach (Heightmap item in list)
		{
			if (UpgradeTerrain(item))
			{
				flag = true;
			}
		}
		if (!flag)
		{
			Console.instance.Print("Nothing to optimize");
		}
		else
		{
			Console.instance.Print("Optimized terrain");
		}
	}

	public static bool UpgradeTerrain(Heightmap hmap)
	{
		List<TerrainModifier> allInstances = TerrainModifier.GetAllInstances();
		int num = 0;
		List<TerrainModifier> list = new List<TerrainModifier>();
		foreach (TerrainModifier item in allInstances)
		{
			ZNetView component = item.GetComponent<ZNetView>();
			if (!(component == null) && component.IsValid() && component.IsOwner() && item.m_playerModifiction)
			{
				if (!hmap.CheckTerrainModIsContained(item))
				{
					num++;
				}
				else
				{
					list.Add(item);
				}
			}
		}
		if (list.Count == 0)
		{
			return false;
		}
		TerrainComp andCreateTerrainCompiler = hmap.GetAndCreateTerrainCompiler();
		if (!andCreateTerrainCompiler.IsOwner())
		{
			Console.instance.Print("Skipping terrain at " + hmap.transform.position.ToString() + " ( another player is currently the owner )");
			return false;
		}
		int num2 = andCreateTerrainCompiler.m_width + 1;
		float[] array = new float[andCreateTerrainCompiler.m_modifiedHeight.Length];
		for (int i = 0; i < num2; i++)
		{
			for (int j = 0; j < num2; j++)
			{
				array[i * num2 + j] = hmap.GetHeight(j, i);
			}
		}
		Color[] array2 = new Color[andCreateTerrainCompiler.m_paintMask.Length];
		for (int k = 0; k < num2; k++)
		{
			for (int l = 0; l < num2; l++)
			{
				array2[k * num2 + l] = hmap.GetPaintMask(l, k);
			}
		}
		foreach (TerrainModifier item2 in list)
		{
			item2.enabled = false;
			item2.GetComponent<ZNetView>().Destroy();
		}
		hmap.Poke();
		int num3 = 0;
		for (int m = 0; m < num2; m++)
		{
			for (int n = 0; n < num2; n++)
			{
				int num4 = m * num2 + n;
				float num5 = array[num4];
				float height = hmap.GetHeight(n, m);
				float num6 = num5 - height;
				if (!(Mathf.Abs(num6) < 0.001f))
				{
					andCreateTerrainCompiler.m_modifiedHeight[num4] = true;
					andCreateTerrainCompiler.m_levelDelta[num4] += num6;
					num3++;
				}
			}
		}
		int num7 = 0;
		for (int num8 = 0; num8 < num2; num8++)
		{
			for (int num9 = 0; num9 < num2; num9++)
			{
				int num10 = num8 * num2 + num9;
				Color color = array2[num10];
				Color paintMask = hmap.GetPaintMask(num9, num8);
				if (!(color == paintMask))
				{
					andCreateTerrainCompiler.m_modifiedPaint[num10] = true;
					andCreateTerrainCompiler.m_paintMask[num10] = color;
					num7++;
				}
			}
		}
		andCreateTerrainCompiler.Save();
		hmap.Poke();
		if ((bool)ClutterSystem.instance)
		{
			ClutterSystem.instance.ResetGrass(hmap.transform.position, (float)hmap.m_width * hmap.m_scale / 2f);
		}
		Console.instance.Print("Operations optimized:" + list.Count + "  height changes:" + num3 + "  paint changes:" + num7);
		return true;
	}

	public void SetSnowMask(float depth = -1f)
	{
		for (int i = 0; i < m_paintMask.Length; i++)
		{
			m_modifiedPaint[i] = true;
			int x = i % m_pitch;
			int y = i / m_pitch;
			Vector3 vector = m_hmap.VertexMaskToWorld(x, y);
			WorldGenerator.instance.GetBiomeHeight(Heightmap.Biome.DeepNorth, vector.x, vector.y, out var mask);
			if (depth != -1f)
			{
				mask.g = depth;
			}
			m_paintMask[i] = mask;
		}
		Save();
		m_hmap.Poke(1);
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.2.0.10050' (yours is '9.1.0.7988')
