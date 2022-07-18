using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Linq;

[ExecuteInEditMode]
public class HeightmapCompress : MonoBehaviour
{
    // data given by SmallTerrainGenerator
    public string file_path = "featureAnalyze/features.f";
    public Texture2D heightmap_edge;
    public Texture2D heightmap;
    public Vector3[] vertice;
    public float[] edges;
    public int chunk_x_piece_num;
    public int chunk_z_piece_num;
    public float map_size_width = 22.5f;
    public float map_size_height = 22.5f;
    public float piece_length = 32.0f; //2048
    public float chunk_x_min;
    public float chunk_z_min;
    public float gray_height;
    public bool get_feature;
    public bool get_terrain_feature;
    public bool add_constraints;
    Vector3[] constaints_custom;
    public Terrain terrain;
    public bool get_line_feature;
    public GameObject blue_ball;
    public GameObject red_ball;
    public float threshold;
    public float interval;
    public float epsilon;
    Vector3[] point_cloud;
    public List<Vector3> point_cloud_list;
    public bool clean;
    public bool is_finished = false;
    public bool is_fetched_features = false;
    bool wait_features = false;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (get_feature)
        {
            get_feature = false;
            wait_features = true;
            Debug.Log("Get feature start");

            // W8D in heightmap
            //for (float edge_x = 0.0f; edge_x < map_size_width; edge_x += piece_length)
            //{
            //    for (float edge_z = 0.0f; edge_z < map_size_height; edge_z += piece_length)
            //    {
            //        Color gray = heightmap_edge.GetPixel(Mathf.FloorToInt(edge_x / map_size_width * heightmap_edge.width), Mathf.FloorToInt(edge_z / map_size_height * heightmap_edge.height));
            //        if (gray.r > threshold)
            //        {
            //            List<List<Vector3>> w8d = W8D(map_size_width, map_size_height, new Vector3(edge_x, 0.0f, edge_z), point_cloud_list); // need to limit boundary
            //            Vector3[] w8d_center = new Vector3[1];
            //            Color height = heightmap.GetPixel(Mathf.FloorToInt(edge_x / map_size_width * heightmap.width), Mathf.FloorToInt(edge_z / map_size_height * heightmap.height));
            //            w8d_center[0] = new Vector3(min_x + edge_x, height.r * gray_height, min_z + edge_z);
            //            showPoint(w8d_center, "Feature_Center", feature_manager.transform, red_ball, 16.0f);
            //            for (int w8d_index = 0; w8d_index < w8d.Count; w8d_index++)
            //            {
            //                point_cloud_list.AddRange(w8d[w8d_index]);
            //            }
            //        }
            //    }
            //}

            // W8D in terrain edge detection
            StartCoroutine(getPointCloud());
        }

        if (get_terrain_feature)
        {
            get_terrain_feature = false;
            wait_features = true;
            point_cloud_list = new List<Vector3>();
            StartCoroutine(getPointCloud(terrain, terrain.transform.position, terrain.transform.position + terrain.terrainData.size, true, true));
        }

        if (add_constraints)
        {
            constaints_custom = new Vector3[] { new Vector3(52, 20, 52), new Vector3(52, 20, 76), new Vector3(76, 20, 76), new Vector3(76, 20, 52), };

        }

        if (clean)
        {
            clean = false;
            is_finished = false;
            is_fetched_features = false;
            GameObject.DestroyImmediate(GameObject.Find("feature_manager"));
        }

        if (wait_features && !is_fetched_features && is_finished)
        {
            is_fetched_features = true;
            wait_features = false;
            int constraint_begin_index = point_cloud_list.Count;
            point_cloud_list.AddRange(constaints_custom);
            int[] building_point_count = new int[0];
            if (add_constraints)
                building_point_count = new int[] { constaints_custom.Length };
            Vector3[] point_cloud_array = point_cloud_list.ToArray();
            WVec3[] features = new WVec3[point_cloud_array.Length];
            for (int i = 0; i < point_cloud_array.Length; i++)
            {
                features[i].x = point_cloud_array[i].x;
                features[i].y = point_cloud_array[i].y;
                features[i].z = point_cloud_array[i].z;
                if (i < constraint_begin_index)
                    features[i].w = -1;
                else
                    features[i].w = 100000 + (i - constraint_begin_index);
            }
            Vector3 boundary_min = new Vector3();
            Vector3 terrain_min = new Vector3();
            Vector3 terrain_max = new Vector3(terrain.terrainData.size.x, 0, terrain.terrainData.size.z);
            PublicOutputInfo.writeFeatureFile(Application.streamingAssetsPath + "//" + file_path, features, building_point_count, boundary_min, terrain_min, terrain_max);
        }
    }

    public Vector2 getTerrainCoord(Terrain terrain, Vector3 coord)
    {
        Vector3 related_coord = coord - terrain.transform.position;
        return new Vector2(related_coord.x / terrain.terrainData.size.x, related_coord.z / terrain.terrainData.size.z);
    }

    public IEnumerator getPointCloud(Terrain terrain, Vector3 min_vec3, Vector3 max_vec3, bool show_points = false, bool custom = false)
    {
        is_finished = false;
        is_fetched_features = false;
        GameObject feature_manager = new GameObject("feature_manager");
        if (!custom)
        {
            for (float x = min_vec3.x; x < max_vec3.x; x += interval)
            {
                for (float z = min_vec3.z; z < max_vec3.z; z += interval)
                {
                    float height = terrain.SampleHeight(new Vector3(x, 0, z));
                    Vector3 current_vec3 = new Vector3(x, height, z);
                    Vector2 coord = getTerrainCoord(terrain, current_vec3);
                    float gradient = terrain.terrainData.GetSteepness(coord.x, coord.y);
                    if (gradient > threshold)
                    {
                        List<List<Vector3>> w8d = W8DfromTerrain(terrain, min_vec3, max_vec3, current_vec3);
                        Vector3[] w8d_center = new Vector3[1];
                        w8d_center[0] = current_vec3;
                        if (show_points)
                            showPoint(w8d_center, "Feature_Center", feature_manager.transform, red_ball, 16.0f);
                        for (int w8d_index = 0; w8d_index < w8d.Count; w8d_index++)
                        {
                            //for (int w8d_point_index = 0; w8d_point_index < w8d[w8d_index].Count; w8d_point_index++)
                            //{
                            //    if (point_cloud_list.Contains(w8d[w8d_index][w8d_point_index]))
                            //    {
                            //        w8d[w8d_index].RemoveAt(w8d_point_index);
                            //        w8d_point_index--;
                            //    }
                            //}
                            point_cloud_list.AddRange(w8d[w8d_index]);
                        }
                    }
                }
            }
        }
        else
        {
            Vector3[] custom_center = new Vector3[] { new Vector3(64, 0, 64) };
            for (int custom_center_index = 0; custom_center_index < custom_center.Length; custom_center_index++)
            {
                custom_center[custom_center_index].y = terrain.SampleHeight(custom_center[custom_center_index]);
                List<List<Vector3>> w8d = W8DfromTerrain(terrain, min_vec3, max_vec3, custom_center[custom_center_index]);
                Vector3[] w8d_center = new Vector3[1];
                w8d_center[0] = custom_center[custom_center_index];
                if (show_points)
                    showPoint(w8d_center, "Feature_Center", feature_manager.transform, red_ball, 16.0f);
                for (int w8d_index = 0; w8d_index < w8d.Count; w8d_index++)
                {
                    //for (int w8d_point_index = 0; w8d_point_index < w8d[w8d_index].Count; w8d_point_index++)
                    //{
                    //    if (point_cloud_list.Contains(w8d[w8d_index][w8d_point_index]))
                    //    {
                    //        w8d[w8d_index].RemoveAt(w8d_point_index);
                    //        w8d_point_index--;
                    //    }
                    //}
                    point_cloud_list.AddRange(w8d[w8d_index]);
                }
            }
            //point_cloud_list.AddRange(custom_center);
        }

        if (show_points)
            showPoint(point_cloud_list.ToArray(), "Feature", feature_manager.transform, blue_ball, 8.0f);

        is_finished = true;
        yield return null;
    }

    public IEnumerator getPointCloud(bool show_points = false)
    {
        is_finished = false;
        is_fetched_features = false;
        GameObject feature_manager = new GameObject("feature_manager");
        bool[] flag = new bool[(chunk_x_piece_num + 1) * (chunk_z_piece_num + 1)];
        for (int x = 0; x <= chunk_x_piece_num; x++)
        {
            for (int z = 0; z <= chunk_z_piece_num; z++)
            {
                if (edges[x * (chunk_z_piece_num + 1) + z] > threshold)
                {
                    List<List<Vector3>> w8d = W8DGrid(chunk_x_piece_num + 1, chunk_z_piece_num + 1, x, z, point_cloud_list, flag);
                    Vector3[] w8d_center = new Vector3[1];
                    w8d_center[0] = vertice[x * (chunk_z_piece_num + 1) + z];
                    if (show_points)
                        showPoint(w8d_center, "Feature_Center", feature_manager.transform, red_ball, 16.0f);
                    for (int w8d_index = 0; w8d_index < w8d.Count; w8d_index++)
                    {
                        for (int w8d_point_index = 0; w8d_point_index < w8d[w8d_index].Count; w8d_point_index++)
                        {
                            if (point_cloud_list.Contains(w8d[w8d_index][w8d_point_index]))
                            {
                                w8d[w8d_index].RemoveAt(w8d_point_index);
                                w8d_point_index--;
                            }
                        }
                        point_cloud_list.AddRange(w8d[w8d_index]);
                    }
                    break;
                }
            }
        }

        if (show_points)
            showPoint(point_cloud_list.ToArray(), "Feature", feature_manager.transform, blue_ball, 8.0f);

        is_finished = true;
        yield return null;
    }

    List<List<Vector3>> W8DGrid(int x_length, int z_length, int center_x, int center_z, List<Vector3> point_cloud_list, bool[] flag)
    {
        int[] dx = new int[8] { 1, 1, 1, 0, -1, -1, -1, 0 };
        int[] dz = new int[8] { 1, 0, -1, -1, -1, 0, 1, 1 };
        List<List<Vector3>> terrain_feature_points = new List<List<Vector3>>();

        for (int dir = 0; dir < 8; dir++)
        {
            terrain_feature_points.Add(new List<Vector3>());
            int d = 0;
            while (true)
            {
                int x = center_x + d * dx[dir];
                int z = center_z + d * dz[dir];
                if (x < 0 || x >= x_length || z < 0 || z >= z_length)
                    break;
                flag[x * z_length + z] = true;
                terrain_feature_points[dir].Add(vertice[x * z_length + z]);
                d++;
            }

            if (terrain_feature_points[dir].Count > 1)
                terrain_feature_points[dir] = DouglasPeuckerAlgorithm.DouglasPeucker(terrain_feature_points[dir], epsilon);
        }

        return terrain_feature_points;
    }

    List<List<Vector3>> W8DfromTerrain(Terrain terrain, Vector3 min_vec3, Vector3 max_vec3, Vector3 center)
    {
        Vector3[] directions = new Vector3[8] { new Vector3(1.0f, 0.0f, 0.0f), new Vector3(0.707f, 0.0f, 0.707f), new Vector3(0.0f, 0.0f, 1.0f), new Vector3(-0.707f, 0.0f, 0.707f), new Vector3(-1.0f, 0.0f, 0.0f), new Vector3(-0.707f, 0.0f, -0.707f), new Vector3(0.0f, 0.0f, -1.0f), new Vector3(0.707f, 0.0f, -0.707f) };
        List<List<Vector3>> terrain_feature_points = new List<List<Vector3>>();

        for (int dir = 0; dir < 8; dir++)
        {
            List<Vector2> terrain_feature_points2d = new List<Vector2>();
            float d = 0.0f;
            while (true)
            {
                Vector3 current_vec3 = center + d * directions[dir];
                if (current_vec3.x < min_vec3.x || current_vec3.z < min_vec3.z || current_vec3.x > max_vec3.x || current_vec3.z > max_vec3.z)
                    break;
                float height = terrain.SampleHeight(current_vec3);
                //terrain_feature_lonlats.Add(terrain_feature_lonlat);
                //current_vec3.y = height;
                // no near detection
                terrain_feature_points2d.Add(new Vector2(d ,height));
                //terrain_feature_points[dir].Add(current_vec3);
                d += interval;
            }

            if (terrain_feature_points2d.Count > 1)
            {
                Vector2[] terrain_feature_points2d_array = DouglasPeuckerAlgorithm.DouglasPeucker2D(terrain_feature_points2d, epsilon).ToArray();
                List<Vector3> terrain_feature_points3d = new List<Vector3>();
                for (int terrain_feature_points2d_index = 0; terrain_feature_points2d_index < terrain_feature_points2d_array.Length; terrain_feature_points2d_index++)
                {
                    Vector3 terrain_feature_point3d = center + terrain_feature_points2d_array[terrain_feature_points2d_index].x * directions[dir];
                    terrain_feature_point3d.y = terrain_feature_points2d_array[terrain_feature_points2d_index].y;
                    terrain_feature_points3d.Add(terrain_feature_point3d);
                }
                terrain_feature_points.Add(terrain_feature_points3d);
            }
        }

        return terrain_feature_points;
    }

    List<List<Vector3>> W8D(float width_boundary, float height_boundary, Vector3 center, List<Vector3> point_cloud_list)
    {
        Vector3[] directions = new Vector3[8] { new Vector3(1.0f, 0.0f, 0.0f), new Vector3(0.707f, 0.0f, 0.707f), new Vector3(0.0f, 0.0f, 1.0f), new Vector3(-0.707f, 0.0f, 0.707f), new Vector3(-1.0f, 0.0f, 0.0f), new Vector3(-0.707f, 0.0f, -0.707f), new Vector3(0.0f, 0.0f, -1.0f), new Vector3(0.707f, 0.0f, -0.707f) };
        List<List<Vector3>> terrain_feature_points = new List<List<Vector3>>();

        for (int dir = 0; dir < 8; dir++)
        {
            List<Vector3> terrain_feature_readys = new List<Vector3>();
            terrain_feature_points.Add(new List<Vector3>());
            for (float d = 0.0f; d < width_boundary * height_boundary; d += piece_length)
            {
                Vector3 terrain_feature_ready = center + d * directions[dir];
                if (terrain_feature_ready.x < 0.0f || terrain_feature_ready.z < 0.0f || terrain_feature_ready.x > width_boundary || terrain_feature_ready.z > height_boundary)
                    break;
                Color gray = heightmap.GetPixel(Mathf.FloorToInt(terrain_feature_ready.x / map_size_width * heightmap.width), Mathf.FloorToInt(terrain_feature_ready.z / map_size_height * heightmap.height));
                //terrain_feature_lonlats.Add(terrain_feature_lonlat);
                terrain_feature_ready.y = gray.r * gray_height;
                terrain_feature_ready += new Vector3(chunk_x_min, 0.0f, chunk_z_min);
                terrain_feature_readys.Add(terrain_feature_ready);
                // no near detection
                terrain_feature_points[dir].Add(terrain_feature_ready);
            }

            if (terrain_feature_points[dir].Count > 1)
                terrain_feature_points[dir] = DouglasPeuckerAlgorithm.DouglasPeucker(terrain_feature_points[dir], epsilon);
        }

        return terrain_feature_points;
    }

    float distance2D(float x1, float z1, float x2, float z2)
    {
        return Mathf.Sqrt(Mathf.Pow(x1 - x2, 2) + Mathf.Pow(z1 - z2, 2));
    }

    void showPoint(List<Vector3> path_points_dp, string tag, Transform parent, GameObject ball_prefab, float ball_size)
    {
        showPoint(path_points_dp.ToArray(), tag, parent, ball_prefab, ball_size);
    }

    void showPoint(Vector3[] path_points_dp, string tag, Transform parent, GameObject ball_prefab, float ball_size)
    {
        for (int point_index = 0; point_index < path_points_dp.Length; point_index++)
        {
            GameObject ball = Instantiate(ball_prefab, path_points_dp[point_index], Quaternion.identity);
            ball.transform.localScale = new Vector3(ball_size, ball_size, ball_size);
            ball.name = tag + "_" + point_index.ToString();
            ball.transform.parent = parent;
        }
    }

    /// <summary>
    /// featurefile for a small terrain
    /// </summary>
    /// <param name="file_path"></param>
    /// <param name="features"></param>
    void writeFeatureFile(string file_path, WVec3[] features)
    {
        KDTree kdtree = new KDTree();
        kdtree.buildKDTree(features);

        Debug.Log("Writing " + file_path);
        using (StreamWriter sw = new StreamWriter(file_path))
        {
            sw.WriteLine(PublicOutputInfo.boundary_min.x + " " + PublicOutputInfo.boundary_min.y);
            sw.WriteLine(PublicOutputInfo.origin_pos.x + " " + PublicOutputInfo.origin_pos.y + " " + PublicOutputInfo.origin_pos.z);
            sw.WriteLine(chunk_x_piece_num + " " + chunk_z_piece_num);
            sw.WriteLine((chunk_x_min - PublicOutputInfo.origin_pos.x).ToString() + " " + (-PublicOutputInfo.origin_pos.y).ToString() + " " + (chunk_z_min - PublicOutputInfo.origin_pos.z).ToString());
            sw.WriteLine(features.Length);
            for (int point_index = 0; point_index < features.Length; point_index++)
            {
                Vector3 feature_out = new Vector3(kdtree.nodes[point_index].x - PublicOutputInfo.origin_pos.x, kdtree.nodes[point_index].y, kdtree.nodes[point_index].z - PublicOutputInfo.origin_pos.z);
                sw.WriteLine(feature_out.x + " " + feature_out.y + " " + feature_out.z + " " + kdtree.parent[point_index] + " " + kdtree.left[point_index] + " " + kdtree.right[point_index]);
            }
        }
        Debug.Log("Write " + file_path + " Successfully!");
    }
}