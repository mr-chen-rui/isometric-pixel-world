using UnityEngine;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;

[ExecuteAlways]
public class DenseGrassRenderer : MonoBehaviour
{
    [Header("References")]
    public MeshFilter terrainMeshFilter;
    public Material grassMaterial;

    [Header("Grass Settings")]
    [Min(1)] public int instanceCount = 250000;
    public Vector2 widthRange = new Vector2(0.8f, 1.2f);
    public Vector2 heightRange = new Vector2(0.7f, 1.4f);
    [Range(0f, 1f)] public float alignToNormal = 0.5f;
    [Range(0f, 1f)] public float maxSlope = 0.7f;

    [Range(0f, 1f)] public float modulateScale = 1;
    [SerializeField] private LayerMask grassObstructorLayer;
    [Tooltip("Minimum distance a grass obstructor must be from the ground for grass to spawn")]
    [Range(0.1f, 5f)] public float minDistance;

    private Mesh _quadMesh;
    private GraphicsBuffer _commandBuffer;
    private GraphicsBuffer _instanceBuffer;
    private RenderParams _renderParams;
    private Bounds _worldBounds;

    // 36 bytes (9 floats) - matches HLSL StructuredBuffer layout cleanly
    [StructLayout(LayoutKind.Sequential)]
    private struct GrassInstance
    {
        public Vector3 position;
        public float randomSeed;
        public Vector3 normal;
        public Vector2 scale;
        public float modulate;
    }

    void OnEnable()
    {
        if (terrainMeshFilter == null || terrainMeshFilter.sharedMesh == null || grassMaterial == null)
        {
            Debug.LogError("DenseGrassRenderer: Missing Terrain MeshFilter or Grass Material.");
            return;
        }

        _quadMesh = CreateBottomPivotQuad();
        int spawnedCount = GenerateGrassData();

        if (spawnedCount == 0)
        {
            Debug.LogWarning("DenseGrassRenderer: 0 instances generated. Check maxSlope or terrain mesh normals.");
            return;
        }

        SetupRenderParams();
    }

    void Update()
    {
        if (_commandBuffer == null || _instanceBuffer == null || _quadMesh == null) return;

        Graphics.RenderMeshIndirect(_renderParams, _quadMesh, _commandBuffer);
    }

    void SetupRenderParams()
    {
        // Bind buffer directly to the material so URP Render Graph always recognizes it
        grassMaterial.SetBuffer("_GrassInstances", _instanceBuffer);

        _renderParams = new RenderParams(grassMaterial)
        {
            worldBounds = _worldBounds,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = true
        };
    }

    int GenerateGrassData()
    {
        Mesh mesh = terrainMeshFilter.sharedMesh;
        Transform terrainTransform = terrainMeshFilter.transform;

        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        int[] triangles = mesh.triangles;
        int triCount = triangles.Length / 3;

        if (normals == null || normals.Length == 0)
        {
            mesh.RecalculateNormals();
            normals = mesh.normals;
        }

        float[] cumulativeAreas = new float[triCount];
        float totalArea = 0f;

        for (int i = 0; i < triCount; i++)
        {
            Vector3 v0 = terrainTransform.TransformPoint(vertices[triangles[i * 3]]);
            Vector3 v1 = terrainTransform.TransformPoint(vertices[triangles[i * 3 + 1]]);
            Vector3 v2 = terrainTransform.TransformPoint(vertices[triangles[i * 3 + 2]]);

            float area = Vector3.Cross(v1 - v0, v2 - v0).magnitude * 0.5f;
            totalArea += area;
            cumulativeAreas[i] = totalArea;
        }

        GrassInstance[] instances = new GrassInstance[instanceCount];
        int validCount = 0;
        int maxAttempts = instanceCount * 4;
        int attempts = 0;

        Physics.queriesHitBackfaces = true; // Need to be able to have rays hit backs of faces

        while (validCount < instanceCount && attempts < maxAttempts)
        {
            attempts++;

            float randomArea = Random.Range(0f, totalArea);
            int triIndex = System.Array.BinarySearch(cumulativeAreas, randomArea);
            if (triIndex < 0) triIndex = ~triIndex;
            triIndex = Mathf.Clamp(triIndex, 0, triCount - 1);

            int i0 = triangles[triIndex * 3];
            int i1 = triangles[triIndex * 3 + 1];
            int i2 = triangles[triIndex * 3 + 2];

            float r1 = Mathf.Sqrt(Random.value);
            float r2 = Random.value;
            float w0 = 1f - r1;
            float w1 = r1 * (1f - r2);
            float w2 = r1 * r2;

            Vector3 localNormal = (normals[i0] * w0 + normals[i1] * w1 + normals[i2] * w2).normalized;
            Vector3 worldNormal = terrainTransform.TransformDirection(localNormal).normalized;

            if (worldNormal.y < maxSlope) continue;

            Vector3 localPos = vertices[i0] * w0 + vertices[i1] * w1 + vertices[i2] * w2;
            Vector3 worldPos = terrainTransform.TransformPoint(localPos);

            if (Physics.Raycast(worldPos, Vector3.up, out RaycastHit hit, minDistance, grassObstructorLayer))
            {
                continue;
            }

            instances[validCount] = new GrassInstance
            {
                position = worldPos,
                randomSeed = Random.value,
                normal = Vector3.Lerp(Vector3.up, worldNormal, alignToNormal).normalized,
                scale = new Vector2(
                    Random.Range(widthRange.x, widthRange.y),
                    Random.Range(heightRange.x, heightRange.y)
                ),
                modulate = Mathf.PerlinNoise(worldPos.x * modulateScale, worldPos.z * modulateScale) // Passing modulate value to shader
            };
            validCount++;
        }

        if (validCount == 0) return 0;

        Renderer terrainRenderer = terrainMeshFilter.GetComponent<Renderer>();
        _worldBounds = terrainRenderer != null ? terrainRenderer.bounds : new Bounds(terrainTransform.position, Vector3.one * 1000f);
        _worldBounds.Expand(heightRange.y * 5f);

        int stride = Marshal.SizeOf(typeof(GrassInstance));
        _instanceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, validCount, stride);
        _instanceBuffer.SetData(instances, 0, 0, validCount);

        // Explicitly initialize ALL fields of IndirectDrawIndexedArgs
        _commandBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        var commandData = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
        commandData[0].indexCountPerInstance = _quadMesh.GetIndexCount(0);
        commandData[0].instanceCount = (uint)validCount;
        commandData[0].startIndex = _quadMesh.GetIndexStart(0);
        commandData[0].baseVertexIndex = _quadMesh.GetBaseVertex(0);
        commandData[0].startInstance = 0;
        _commandBuffer.SetData(commandData);

        return validCount;
    }

    Mesh CreateBottomPivotQuad()
    {
        Mesh quad = new Mesh { name = "BottomPivotQuad" };
        quad.vertices = new[]
        {
            new Vector3(-0.5f, 0f, 0f),
            new Vector3( 0.5f, 0f, 0f),
            new Vector3(-0.5f, 1f, 0f),
            new Vector3( 0.5f, 1f, 0f)
        };
        quad.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };
        quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        quad.RecalculateBounds();
        return quad;
    }

    void OnDisable()
    {
        _commandBuffer?.Release();
        _commandBuffer = null;

        _instanceBuffer?.Release();
        _instanceBuffer = null;

        if (_quadMesh != null)
        {
            Destroy(_quadMesh);
            _quadMesh = null;
        }
    }
}
