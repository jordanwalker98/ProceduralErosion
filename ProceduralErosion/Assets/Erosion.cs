using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Erosion : MonoBehaviour
{
    [System.Serializable]
    public class Tile
    {
        public RenderTexture heightmap;
        public Texture2D baseHeightmap;
        public GameObject go;
        public Mesh mesh;
        public Vector2 offset;
    }

    public List<Tile> tiles = new List<Tile>();
    public int tileSize = 2;
    public GameObject tilePrefab;

    public Vector2 globalOffset;
    public int maxDroplets;
    public int erosionBrushRadius = 3;
    public const int size = 256;
    public const int border = 64;
    public const int tsize = size + border * 2;
    public int hydroIterations = 20;
    public int thermalIterations = 1;
    public float noiseFreq;
    public RenderTexture height, heightDelta;
    ComputeBuffer dropletsBuffer;
    ComputeBuffer erosionArgsBuffer;
    ComputeBuffer brushCoordsBuffer;
    ComputeBuffer brushWeightBuffer;
    ComputeShader erosion, noiseHeightmap;
    Mesh mesh;

    // Start is called before the first frame update
    void Start()
    {
        height = new RenderTexture(tsize, tsize, 0, RenderTextureFormat.RFloat);
        height.enableRandomWrite = true;
        heightDelta = new RenderTexture(tsize, tsize, 0, RenderTextureFormat.RInt);
        heightDelta.enableRandomWrite = true;

        erosion = Resources.Load<ComputeShader>("Erosion");
        noiseHeightmap = Resources.Load<ComputeShader>("NoiseHeightmap");
        dropletsBuffer = new ComputeBuffer(maxDroplets, sizeof(int) * 8, ComputeBufferType.Append);
        erosionArgsBuffer = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);

        CreateErosionBrush();
        for (int y = 0; y < tileSize; y++)
        {
            for (int x = 0; x < tileSize; x++)
            {
                Tile tile = new Tile();
                tile.go = Instantiate(tilePrefab, new Vector3(x * 129, 0, y * 129), Quaternion.identity);
                tile.heightmap = new RenderTexture(tsize, tsize, 0, RenderTextureFormat.RFloat, 0);
                tile.heightmap.enableRandomWrite = true;
                BuildMesh(ref tile.mesh);
                tile.go.GetComponent<MeshFilter>().mesh = tile.mesh;
                tile.go.GetComponent<MeshRenderer>().material = new Material(tile.go.GetComponent<MeshRenderer>().material);
                tile.go.GetComponent<MeshRenderer>().material.SetTexture("heightmap", tile.heightmap);
                tile.go.GetComponent<MeshRenderer>().material.SetFloat("size", size);
                tile.offset = new Vector2(x, y);

                tiles.Add(tile);
            }
        }
    }

    private void Update()
    {
        UpdateTiles();
    }

    static int tri(int x, int y, int num)
    {
        return (y * num + x);
    }

    void BuildMesh(ref Mesh mesh)
    {
        mesh = new Mesh();
        int vsize = 128 + 1;
        Vector3[] vertices = new Vector3[vsize * vsize];
        Vector2[] uv = new Vector2[vsize * vsize];
        List<int> indices = new List<int>((vsize - 1) * (vsize - 1) * 6);

        for (int x = 0; x < vsize; x++)
        {
            for (int y = 0; y < vsize; y++)
            {

                float dt = 1.0f / (vsize - 1);
                float xp = x * dt;
                float yp = y * dt;
                vertices[x + y * vsize] = new Vector3(xp - 0.5f, 0, yp - 0.5f) * vsize;
                uv[x + y * vsize] = new Vector2(xp, yp);

                if (x < (vsize - 1) && y < (vsize - 1))
                {
                    indices.Add(tri(x, y, vsize));
                    indices.Add(tri(x, y + 1, vsize));
                    indices.Add(tri(x + 1, y + 1, vsize));

                    indices.Add(tri(x + 1, y + 1, vsize));
                    indices.Add(tri(x + 1, y, vsize));
                    indices.Add(tri(x, y, vsize));
                }
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = indices.ToArray();
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.hideFlags = HideFlags.DontSave;
    }

    int tileIdx = 0;

    // Update is called once per frame
    void UpdateTiles()
    {
        for (int i = 0; i < tiles.Count; i++)
       // for (int i = 0; i < 1; i++)
        {
            Tile tile = tiles[tileIdx];
            GenerateTileTerrain(tile);
            tile.go.GetComponent<MeshRenderer>().material.SetTexture("heightmap", tile.heightmap);
            tile.go.GetComponent<MeshRenderer>().material.SetFloat("size", size);
            tile.go.GetComponent<MeshRenderer>().material.SetFloat("tsize", tsize);
            tile.go.GetComponent<MeshRenderer>().material.SetInt("border", border);
            tile.go.GetComponent<MeshRenderer>().material.SetVector("offset", tile.offset);
            tileIdx = ((tileIdx + 1) % tiles.Count);
        }
    }

    private void FixedUpdate()
    {
        globalOffset += Vector2.right * 1.0f / size;
    }

    int numBrushCoords;
    void CreateErosionBrush()
    {
        List<Vector2Int> brushCoords = new List<Vector2Int>();
        List<float> brushWeights = new List<float>();

        float weightSum = 0;
        for (int brushY = -erosionBrushRadius; brushY <= erosionBrushRadius; brushY++)
        {
            for (int brushX = -erosionBrushRadius; brushX <= erosionBrushRadius; brushX++)
            {
                float sqrDst = brushX * brushX + brushY * brushY;
                if (sqrDst < erosionBrushRadius * erosionBrushRadius)
                {
                    brushCoords.Add(new Vector2Int(brushX, brushY));
                    float brushWeight = 1 - Mathf.Sqrt(sqrDst) / erosionBrushRadius;
                    weightSum += brushWeight;
                    brushWeights.Add(brushWeight);
                }
            }
        }
        for (int i = 0; i < brushWeights.Count; i++)
        {
            brushWeights[i] /= weightSum;
        }
        numBrushCoords = brushCoords.Count;

        // Send brush data to compute shader
        brushCoordsBuffer = new ComputeBuffer(brushCoords.Count, sizeof(int)*2);
        brushWeightBuffer = new ComputeBuffer(brushWeights.Count, sizeof(int));
        brushCoordsBuffer.SetData(brushCoords);
        brushWeightBuffer.SetData(brushWeights);
    }

    void GenerateTileTerrain(Tile tile)
    {
        Vector2 offset = tile.offset + globalOffset;
        //generate droplets
        erosion.SetInt("border", border);
        erosion.SetInt("mapSize", size);
        erosion.SetInt("tmapSize", tsize);
        erosion.SetVector("offset", offset);

        dropletsBuffer.SetCounterValue(0);
        erosion.SetBuffer(4, "dropletsAppend", dropletsBuffer);
        erosion.Dispatch(4, tsize / 8, tsize / 8, 1);
        ComputeBuffer.CopyCount(dropletsBuffer, erosionArgsBuffer, 0);

        erosion.SetBuffer(5, "args", erosionArgsBuffer);
        erosion.Dispatch(5, 1, 1, 1);

        //generate base heightmap
        noiseHeightmap.SetInt("border", border);
        noiseHeightmap.SetInt("mapSize", size);
        noiseHeightmap.SetVector("offset", offset);
        noiseHeightmap.SetFloat("noiseFreq", noiseFreq);
        noiseHeightmap.SetTexture(0, "map", height);
        noiseHeightmap.Dispatch(0, tsize / 8, tsize / 8, 1);

        //initial height delta clear
        erosion.SetTexture(2, "heightDelta", heightDelta);
        erosion.Dispatch(2, tsize / 8, tsize / 8, 1);

        erosion.SetBuffer(0, "args", erosionArgsBuffer);
        erosion.SetInt("maxLifetime", hydroIterations);
        erosion.SetFloat("inertia", 0.05f);
        erosion.SetFloat("evaporateSpeed", 0.01f);
        erosion.SetFloat("gravity", 4f);
        erosion.SetFloat("depositSpeed", 0.3f);
        erosion.SetFloat("erodeSpeed", 0.3f);
        erosion.SetFloat("sedimentCapacityFactor", 0.1f);
        erosion.SetFloat("minSedimentCapacity", 4);

        erosion.SetInt("brushLength", numBrushCoords);
        erosion.SetBuffer(0, "brushCoords", brushCoordsBuffer);
        erosion.SetBuffer(0, "brushWeights", brushWeightBuffer);
        erosion.SetTexture(0, "map", height);
        erosion.SetTexture(0, "heightDelta", heightDelta);

        erosion.SetBuffer(0, "droplets", dropletsBuffer);
        erosion.SetTexture(1, "map", height);
        erosion.SetTexture(1, "heightDelta", heightDelta);

        erosion.SetTexture(6, "map", height);
        erosion.SetTexture(6, "heightDelta", heightDelta);

        for (int i = 0; i < hydroIterations; i++)
        {
            erosion.DispatchIndirect(0, erosionArgsBuffer, sizeof(int));
            erosion.Dispatch(1, tsize / 8, tsize / 8, 1);
        }

        for (int i = 0; i < thermalIterations; i++)
        {
            erosion.Dispatch(6, tsize / 8, tsize / 8, 1);
            erosion.Dispatch(1, tsize / 8, tsize / 8, 1);
        }

        erosion.SetTexture(3, "map", height);
        erosion.SetTexture(3, "height", tile.heightmap);
        erosion.Dispatch(3, tsize / 8, tsize / 8, 1);
    }
}
