using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Linq;

public class FeatureFileGenerator : MonoBehaviour
{
    OSMEditor osm_editor;
    RoadIntegration road_integration;
    public bool generate;
    bool getFeatureAfterGenerate = true;
    public Material heightmap_mat;
    public Texture2D heightmap;
    public GameObject blue_ball;
    public GameObject red_ball;
    const int piece_num_in_chunk = 192;
    public string file_path = "features.f";
    int x_piece_num;
    int z_piece_num;
    public bool show_feature_points;
    List<GameObject> terrains = new List<GameObject>();     // Store all chunks of terrains
    List<Vector3> point_cloud_list = new List<Vector3>();
    public float threshold = 100.0f;
    public float epsilon = 16.0f;
    int wait_terrain_count = -1;

    // Start is called before the first frame update
    void Start()
    {
        osm_editor = GetComponent<OSMEditor>();
        road_integration = GetComponent<RoadIntegration>();
    }

    // Update is called once per frame
    void Update()
    {
        if (generate && osm_editor.is_initial)
        {
            generate = false;
            separateGenerateTerrain(road_integration.terrain_min_x, road_integration.terrain_min_z, road_integration.terrain_max_x, road_integration.terrain_max_z);
        }

        if (getFeatureAfterGenerate)
        {
            for (int terrain_index = 0; terrain_index < terrains.Count; terrain_index++)
            {
                if (!terrains[terrain_index].GetComponent<HeightmapCompress>().is_fetched_features && terrains[terrain_index].GetComponent<HeightmapCompress>().is_finished)
                {
                    terrains[terrain_index].GetComponent<HeightmapCompress>().is_fetched_features = true;
                    point_cloud_list.AddRange(terrains[terrain_index].GetComponent<HeightmapCompress>().point_cloud_list);
                    wait_terrain_count--;
                }
            }

            if (wait_terrain_count == 0)
            {
                wait_terrain_count = -1;
                List<Vector3> point_list = new List<Vector3>(point_cloud_list);
                List<Vector3> bicycle_points_list = road_integration.bicyclePointsListToVec3();
                point_list.AddRange(bicycle_points_list);
                int bicycle_constrain_reverse_count = bicycle_points_list.Count;
                int bicycle_constrain_reverse_index = 0;
                Vector3[] points = point_list.Distinct().ToArray();
                WVec3[] features = new WVec3[points.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    features[i].x = points[i].x;
                    features[i].y = points[i].y;
                    features[i].z = points[i].z;
                    if (i < points.Length - bicycle_constrain_reverse_count)
                        features[i].w = -1;
                    else
                        features[i].w = bicycle_constrain_reverse_index++;
                }
                writeFeatureFile(Application.streamingAssetsPath + "//" + file_path, features);
            }
        }
    }

    void separateGenerateTerrain(float min_x, float min_z, float max_x, float max_z)
    {
        x_piece_num = Mathf.CeilToInt((max_x - min_x) / PublicOutputInfo.piece_length);
        z_piece_num = Mathf.CeilToInt((max_z - min_z) / PublicOutputInfo.piece_length);

        wait_terrain_count = 0;
        for (int x_index = 0; x_index < x_piece_num; x_index += piece_num_in_chunk)
        {
            for (int z_index = 0; z_index < z_piece_num; z_index += piece_num_in_chunk)
            {
                float chunk_x_min = min_x + x_index * PublicOutputInfo.piece_length;
                float chunk_z_min = min_z + z_index * PublicOutputInfo.piece_length;
                int chunk_x_piece_num = Mathf.Min(x_piece_num - x_index, piece_num_in_chunk);
                int chunk_z_piece_num = Mathf.Min(z_piece_num - z_index, piece_num_in_chunk);
                generateChunkTerrain(chunk_x_min, chunk_z_min, chunk_x_piece_num, chunk_z_piece_num);
                wait_terrain_count++;
            }
        }
    }

    void generateChunkTerrain(float chunk_x_min, float chunk_z_min, int chunk_x_piece_num, int chunk_z_piece_num)
    {
        Mesh mesh = new Mesh();
        double[,,] terrain_points = new double[(chunk_x_piece_num + 1), (chunk_z_piece_num + 1), 3];
        Vector3[] vertice = new Vector3[(chunk_x_piece_num + 1) * (chunk_z_piece_num + 1)];
        Vector2[] uv = new Vector2[(chunk_x_piece_num + 1) * (chunk_z_piece_num + 1)];
        int[] indices = new int[6 * chunk_x_piece_num * chunk_z_piece_num];
        int indices_index = 0;
        List<EarthCoord> all_coords = new List<EarthCoord>();
        for (int i = 0; i <= chunk_x_piece_num; i++)
        {
            for (int j = 0; j <= chunk_z_piece_num; j++)
            {
                float terrain_lon, terrain_lat;
                //osm_editor.osm_reader.toUnityLocation(terrain_points[i, j].x, terrain_points[i, j].z, out pos_x, out pos_z);
                terrain_points[i, j, 0] = chunk_x_min + i * PublicOutputInfo.piece_length;
                terrain_points[i, j, 2] = chunk_z_min + j * PublicOutputInfo.piece_length;
                osm_editor.osm_reader.toLonAndLat((float)terrain_points[i, j, 0], (float)terrain_points[i, j, 2], out terrain_lon, out terrain_lat);
                all_coords.Add(new EarthCoord(terrain_lon, terrain_lat));
                //terrain_points[i, j, 1] = heightmap.GetPixel(Mathf.FloorToInt((min_u + i * du) * 2048), Mathf.FloorToInt((min_v + j * dv) * 2048)).r * 100.0f;
                uv[i * (chunk_z_piece_num + 1) + j] = new Vector2((float)i / (chunk_x_piece_num + 1), (float)j / (chunk_z_piece_num + 1));
            }
        }
        //////////////////////////////get elevations/////////////////////////////////////////
        List<float> all_elevations = HgtReader.getElevations(all_coords);
        /////////////////////////////////////////////////////////////////////////////////////
        float max_height = float.MinValue;
        for (int i = 0; i <= chunk_x_piece_num; i++)
        {
            for (int j = 0; j <= chunk_z_piece_num; j++)
            {
                vertice[i * (chunk_z_piece_num + 1) + j] = new Vector3((float)terrain_points[i, j, 0], all_elevations[i * (chunk_z_piece_num + 1) + j], (float)terrain_points[i, j, 2]);
                max_height = Mathf.Max(max_height, all_elevations[i * (chunk_z_piece_num + 1) + j]);
            }
        }
        for (int i = 0; i < chunk_x_piece_num; i++)
        {
            for (int j = 0; j < chunk_z_piece_num; j++)
            {
                // counter-clockwise
                indices[indices_index++] = i * (chunk_z_piece_num + 1) + j;
                indices[indices_index++] = (i + 1) * (chunk_z_piece_num + 1) + j + 1;
                indices[indices_index++] = (i + 1) * (chunk_z_piece_num + 1) + j;
                indices[indices_index++] = i * (chunk_z_piece_num + 1) + j;
                indices[indices_index++] = i * (chunk_z_piece_num + 1) + j + 1;
                indices[indices_index++] = (i + 1) * (chunk_z_piece_num + 1) + j + 1;
            }
        }

        //Assign data to mesh
        mesh.vertices = vertice;
        mesh.uv = uv;
        mesh.triangles = indices;
        //Recalculations
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.Optimize();
        //Name the mesh
        mesh.name = "terrain_mesh";
        GameObject terrain = new GameObject("terrain");
        MeshFilter mf = terrain.AddComponent<MeshFilter>();
        MeshRenderer mr = terrain.AddComponent<MeshRenderer>();
        mf.mesh = mesh;

        Texture2D texture = exportSmallTexture((chunk_x_piece_num + 1), (chunk_z_piece_num + 1), vertice, max_height);
        heightmap_mat.SetTexture("Texture2D", texture);
        float[] edges = getTerrainEdgeDetection(vertice, (chunk_x_piece_num + 1), (chunk_z_piece_num + 1));
        mr.material = heightmap_mat;
        terrain.AddComponent<ExportPNG>();
        terrain.AddComponent<HeightmapCompress>();
        terrain.GetComponent<HeightmapCompress>().heightmap = texture;
        terrain.GetComponent<HeightmapCompress>().vertice = vertice;
        terrain.GetComponent<HeightmapCompress>().edges = edges;
        terrain.GetComponent<HeightmapCompress>().chunk_x_piece_num = chunk_x_piece_num;
        terrain.GetComponent<HeightmapCompress>().chunk_z_piece_num = chunk_z_piece_num;
        terrain.GetComponent<HeightmapCompress>().chunk_x_min = chunk_x_min;
        terrain.GetComponent<HeightmapCompress>().chunk_z_min = chunk_z_min;
        terrain.GetComponent<HeightmapCompress>().map_size_width = chunk_x_piece_num * PublicOutputInfo.piece_length;
        terrain.GetComponent<HeightmapCompress>().map_size_height = chunk_z_piece_num * PublicOutputInfo.piece_length;
        terrain.GetComponent<HeightmapCompress>().gray_height = max_height;
        terrain.GetComponent<HeightmapCompress>().blue_ball = blue_ball;
        terrain.GetComponent<HeightmapCompress>().red_ball = red_ball;
        terrain.GetComponent<HeightmapCompress>().threshold = threshold;
        terrain.GetComponent<HeightmapCompress>().epsilon = epsilon;
        StartCoroutine(terrain.GetComponent<HeightmapCompress>().getPointCloud(show_feature_points));
        terrains.Add(terrain);
    }

    Texture2D exportSmallTexture(int x_length, int z_length, Vector3[] vertice, float max_height)
    {
        //first Make sure you're using RGB24 as your texture format
        Texture2D texture2D = new Texture2D(x_length, z_length, TextureFormat.RGBA32, false);

        for (int i = 0; i < x_length; i++)
        {
            for (int j = 0; j < z_length; j++)
            {
                float gray = vertice[i * z_length + j].y / max_height;
                texture2D.SetPixel(i, j, new Color(gray, gray, gray));
            }
        }

        //then Save To Disk as PNG
        byte[] bytes = texture2D.EncodeToPNG();
        var dirPath = Application.dataPath + "/Resources/";
        if (!Directory.Exists(dirPath))
        {
            Directory.CreateDirectory(dirPath);
        }
        File.WriteAllBytes(dirPath + "smallImage" + ".png", bytes);

        return texture2D;
    }

    float[] getTerrainEdgeDetection(Vector3[] vertice, int x_length, int z_length)
    {
        float[] edges = new float[vertice.Length];
        float[] value = new float[8];
        int[] dx = new int[8] { 1, 1, 1, 0, -1, -1, -1, 0 };
        int[] dz = new int[8] { 1, 0, -1, -1, -1, 0, 1, 1 };
        for (int x = 0; x < x_length; x++)
        {
            for (int z = 0; z < z_length; z++)
            {
                for (int dir = 0; dir < 8; dir++)
                {
                    int get_x = x + dx[dir];
                    int get_z = z + dz[dir];
                    if (get_x < 0) get_x = 0;
                    if (get_x >= x_length) get_x = x_length - 1;
                    if (get_z < 0) get_z = 0;
                    if (get_z >= z_length) get_z = z_length - 1;
                    value[dir] = vertice[get_x * z_length + get_z].y;
                }

                float colorX =
                    value[6] * 1.0f +
                    value[7] * 2.0f +
                    value[0] * 1.0f +
                    value[2] * -1.0f +
                    value[3] * -2.0f +
                    value[4] * -1.0f;

                float colorZ =
                    value[0] * 1.0f +
                    value[1] * 2.0f +
                    value[2] * 1.0f +
                    value[4] * -1.0f +
                    value[5] * -2.0f +
                    value[6] * -1.0f;

                edges[x * z_length + z] = Mathf.Sqrt(colorX * colorX + colorZ * colorZ);
            }
        }
        return edges;
    }

    void writeFeatureFile(string file_path, WVec3[] features)
    {
        KDTree kdtree = new KDTree();
        kdtree.buildKDTree(features);

        Debug.Log("Writing " + file_path);
        using (StreamWriter sw = new StreamWriter(file_path))
        {
            sw.WriteLine(PublicOutputInfo.boundary_min.x + " " + PublicOutputInfo.boundary_min.y);
            sw.WriteLine(PublicOutputInfo.origin_pos.x + " " + PublicOutputInfo.origin_pos.y + " " + PublicOutputInfo.origin_pos.z);
            sw.WriteLine(x_piece_num + " " + z_piece_num);
            sw.WriteLine((road_integration.terrain_min_x - PublicOutputInfo.origin_pos.x).ToString() + " " + (-PublicOutputInfo.origin_pos.y).ToString() + " " + (road_integration.terrain_min_z - PublicOutputInfo.origin_pos.z).ToString());
            sw.WriteLine(features.Length);
            for (int point_index = 0; point_index < features.Length; point_index++)
            {
                Vector3 feature_out = new Vector3(kdtree.nodes[point_index].x - PublicOutputInfo.origin_pos.x, kdtree.nodes[point_index].y, kdtree.nodes[point_index].z - PublicOutputInfo.origin_pos.z);
                sw.WriteLine(feature_out.x + " " + feature_out.y + " " + feature_out.z + " " + kdtree.nodes[point_index].w + " " + kdtree.parent[point_index] + " " + kdtree.left[point_index] + " " + kdtree.right[point_index]);
            }
        }
        Debug.Log("Write " + file_path + " Successfully!");
    }
}