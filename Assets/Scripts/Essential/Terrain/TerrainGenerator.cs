using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

static public class TerrainGenerator
{
    static public string file_path = "ShilinDalunweiMountain/features.f";            // _150_32 _100_16_fix
    static public bool is_initial = false;                              // Whether terrain has been calculated
    static public int x_patch_num;                                      // The number of patches in x
    static public int z_patch_num;                                      // The number of patches in z
    static public float min_x;                                          // The minimum of whole terrains x
    static public float min_y;
    static public float min_z;                                          // The minimum of whole terrains z
    static public float max_x;                                          // The maximum of whole terrains x
    static public float max_y;
    static public float max_z;                                          // The maximum of whole terrains z
    static float boundary_min_x;
    static float boundary_min_z;
    static float origin_x;
    static float origin_y;
    static float origin_z;
    static public WVec3[] features;
    static public Material terrain_mat;                                 // Use the standard material. The vertices are calculated by CPU
    static public Material terrain_idw_mat;                             // Use the material with IDW shader
    static public Material terrain_nni_mat;                             // Use the material with NNI shader
    static public bool generate;
    static public int vision_patch_num = 20;                            // The number of patches the viewer can see
    static public float feature_include_length = 320.0f;
    static public GameObject[] terrains;                                // Store all patches of terrains
    static public bool need_update = false;                             // Call TerrainManager to generate new patches
    static public int patch_x_index;                                    // Used to calculate the index for removing checking
    static public int patch_z_index;                                    // Used to calculate the index for removing checking
    static public Queue<Vector3> loading_vec3s = new Queue<Vector3>();  // Loading Queue
    static public Queue<int> queue_patch_x_index = new Queue<int>();    // Coordinate info Queue
    static public Queue<int> queue_patch_z_index = new Queue<int>();    // Coordinate info Queue
    static public bool[] is_loaded;                                     // Check the terrain is called to generate or not
    static public bool[] is_generated;                                  // Check the terrain is generated or not
    static public List<int> generated_x_list;                           // Used to check and remove terrain whether in view or not
    static public List<int> generated_z_list;                           // Used to check and remove terrain whether in view or not
    static public KDTree kdtree;                                        // For searching and recording feature points
    static public int terrain_mode = 0;                                 // 0 is DEM, 1 is IDW, 2 is NNI, controled by TerrainManager
    static public bool show_feature_ball = true;
    static public GameObject feature_ball_prefab;
    static public bool is_queue_generate_patch_empty = false;
    static public ComputeBuffer[] progress_buffer;
    //static public ComputeBuffer[] heights_buffer;
    //static public float[][] heights;
    static public WVec3[] constraints;

    static public Material heightmap_mat;
    static public ComputeShader compute_shader;
    static public Texture2D main_tex;
    static public Texture2D[] heightmaps;
    static public GameObject terrain_manager;
    /// <summary>
    /// Load feature points file with file_path.
    /// Everything that needs height information must wait until is_initial is True.
    /// </summary>
    static public void loadTerrain()
    {
        readFeatureFile(Application.streamingAssetsPath + "//" + file_path);
        is_loaded = new bool[x_patch_num * z_patch_num];
        is_generated = new bool[x_patch_num * z_patch_num];
        heightmaps = new Texture2D[x_patch_num * z_patch_num];
        terrains = new GameObject[x_patch_num * z_patch_num];
        progress_buffer = new ComputeBuffer[x_patch_num * z_patch_num];
        //heights_buffer = new ComputeBuffer[x_patch_num * z_patch_num];
        //heights = new float[x_patch_num * z_patch_num][];
        generated_x_list = new List<int>();
        generated_z_list = new List<int>();
        is_initial = true;
    }

    /// <summary>
    /// Generate terrain near position.
    /// If is_initial is false, every point must store to loading_vec3s and wait TerrainGenerator to initialize.
    /// </summary>
    /// <param name="position">The area with position.</param>
    static public void generateTerrain(Vector3 position)
    {
        if (!is_initial) // generated_x_list, generated_z_list havn't been initial
        {
            loading_vec3s.Enqueue(position);
        }
        else
        {
            getAreaTerrainInfo(position.x, position.z);

            removeAreaTerrain(position.x, position.z);
        }
    }

    static public void removeTerrain(Vector3 position)
    {
        removeAreaTerrain(position.x, position.z);
    }

    // Read feature points file
    static public void readFeatureFile(string file_path)
    {
        using (StreamReader sr = new StreamReader(file_path))
        {
            string[] inputs = sr.ReadLine().Split(' ');
            boundary_min_x = float.Parse(inputs[0]);    // for DEM
            boundary_min_z = float.Parse(inputs[1]);    // for DEM
            inputs = sr.ReadLine().Split(' ');
            origin_x = float.Parse(inputs[0]);
            origin_y = float.Parse(inputs[1]);
            origin_z = float.Parse(inputs[2]);
            inputs = sr.ReadLine().Split(' ');
            min_x = float.Parse(inputs[0]);
            min_y = float.Parse(inputs[1]);
            min_z = float.Parse(inputs[2]);
            max_x = float.Parse(inputs[3]);
            max_y = float.Parse(inputs[4]);
            max_z = float.Parse(inputs[5]);
            x_patch_num = Mathf.CeilToInt((max_x - min_x) / PublicOutputInfo.patch_length);
            z_patch_num = Mathf.CeilToInt((max_z - min_z) / PublicOutputInfo.patch_length);
            int n = int.Parse(sr.ReadLine());
            kdtree = new KDTree();
            kdtree.nodes = new WVec3[n];
            kdtree.parent = new int[n];
            kdtree.left = new int[n];
            kdtree.right = new int[n];
            List<WVec3> constraints_list = new List<WVec3>();
            for (int f_i = 0; f_i < n; f_i++)
            {
                inputs = sr.ReadLine().Split(' ');
                float x = float.Parse(inputs[0]);
                float y = float.Parse(inputs[1]);
                float z = float.Parse(inputs[2]);
                float w = float.Parse(inputs[3]);
                kdtree.nodes[f_i].x = x;
                kdtree.nodes[f_i].y = y;
                kdtree.nodes[f_i].z = z;
                kdtree.nodes[f_i].w = w;
                if (w > -1)
                {
                    WVec3 constraint = new WVec3();
                    constraint.x = x;
                    constraint.y = y;
                    constraint.z = z;
                    constraint.w = w;
                    constraints_list.Add(constraint);
                }
                int p = int.Parse(inputs[4]);
                kdtree.parent[f_i] = p;
                int l = int.Parse(inputs[5]);
                kdtree.left[f_i] = l;
                int r = int.Parse(inputs[6]);
                kdtree.right[f_i] = r;
            }
            constraints_list.Sort(delegate (WVec3 a, WVec3 b)
            {
                return a.w.CompareTo(b.w);
            });
            constraints = constraints_list.ToArray();
            Debug.Log("Read Feature File " + file_path + " Successfully");

            if (show_feature_ball)
            {
                GameObject feature_manager = new GameObject("feature_manager");
                showPoint(kdtree.nodes, "feature", feature_manager.transform, feature_ball_prefab, 1.0f);
            }
        }
    }

    static public void fixHeight(string file_path, float old_base)
    {
        using (StreamWriter sw = new StreamWriter(file_path))
        {
            sw.WriteLine($"{boundary_min_x} {boundary_min_z}");
            origin_y = -min_y;
            sw.WriteLine($"{origin_x} {origin_y} {origin_z}");
            sw.WriteLine($"{min_x} {min_y} {min_z} {max_x} {max_y} {max_z}");
            sw.WriteLine($"{kdtree.nodes.Length}");
            for (int f_i = 0; f_i < kdtree.nodes.Length; f_i++)
            {
                sw.WriteLine($"{kdtree.nodes[f_i].x} {kdtree.nodes[f_i].y} {kdtree.nodes[f_i].z} {kdtree.parent[f_i]} {kdtree.left[f_i]} {kdtree.right[f_i]}");
            }
            Debug.Log("Update Feature File " + file_path + " Successfully");
        }
    }

    static public Vector4[] getAreaFeatures(float x, float z, int x_piece, int z_piece)
    {
        float expanded_length = vision_patch_num * PublicOutputInfo.piece_length;
        int[] area_features_index = kdtree.getAreaPoints(x - expanded_length, z - expanded_length, x + x_piece * PublicOutputInfo.piece_length + expanded_length, z + z_piece * PublicOutputInfo.piece_length + expanded_length);
        Vector4[] area_features = new Vector4[area_features_index.Length];
        for (int area_features_index_index = 0; area_features_index_index < area_features_index.Length; area_features_index_index++)
        {
            WVec3 feature = kdtree.nodes[area_features_index[area_features_index_index]];
            area_features[area_features_index_index] = new Vector4(feature.x, feature.y, feature.z, feature.w);
        }
        return area_features;
    }

    static public (Vector4[], Vector4[]) getAreaFeaturesForPatch(float x, float z)
    {
        float expanded_length = feature_include_length;
        int[] area_features_index = kdtree.getAreaPoints(x - expanded_length, z - expanded_length, x + PublicOutputInfo.patch_length + expanded_length, z + PublicOutputInfo.patch_length + expanded_length);
        Vector4[] area_features = new Vector4[area_features_index.Length];
        List<Vector4> area_constraints = new List<Vector4>();
        for (int area_features_index_index = 0; area_features_index_index < area_features_index.Length; area_features_index_index++)
        {
            WVec3 feature = kdtree.nodes[area_features_index[area_features_index_index]];
            area_features[area_features_index_index] = new Vector4(feature.x, feature.y, feature.z, feature.w);
            if (feature.w > -1)
                area_constraints.Add(area_features[area_features_index_index]);
        }
        area_constraints.Sort(delegate (Vector4 a, Vector4 b)
        {
            return a.w.CompareTo(b.w);
        });
        //for (int area_constraints_index = 0; area_constraints_index < area_constraints.Count - 1; area_constraints_index++)
        //{
        //    if (area_constraints[])
        //}
        return (area_features, area_constraints.ToArray());
    }

    static public Vector4[] getVertexFeatures(float x, float z)
    {
        float expanded_length = vision_patch_num * PublicOutputInfo.piece_length;
        int[] area_features_index = kdtree.getAreaPoints(x - expanded_length, z - expanded_length, x + expanded_length, z + expanded_length);
        Vector4[] area_features = new Vector4[area_features_index.Length];
        for (int area_features_index_index = 0; area_features_index_index < area_features_index.Length; area_features_index_index++)
        {
            WVec3 feature = kdtree.nodes[area_features_index[area_features_index_index]];
            area_features[area_features_index_index] = new Vector4(feature.x, feature.y, feature.z, feature.w);
        }
        return area_features;
    }

    static public Vector3[] getVertexFeaturesFromArea(float x, float z, Vector4[] area_features)
    {
        float expanded_length = vision_patch_num * PublicOutputInfo.piece_length;
        List<Vector3> vertex_features = new List<Vector3>();
        for (int area_features_index_index = 0; area_features_index_index < area_features.Length; area_features_index_index++)
        {
            if (Mathf.Abs(area_features[area_features_index_index].x - x) <= expanded_length && Mathf.Abs(area_features[area_features_index_index].z - z) <= expanded_length)
                vertex_features.Add(area_features[area_features_index_index]);
        }
        return vertex_features.ToArray();
    }

    /// <summary>
    /// generate a patch terrain with piece_num * piece_num size
    /// </summary>
    /// <param name="x_index">the index of vertex which is the beginning to generate in x axis.</param>
    /// <param name="z_index">the index of vertex which is the beginning to generate in z axis.</param>
    /// <param name="x_piece_num">the number of piece you want to generate in x axis.</param>
    /// <param name="z_piece_num">the number of piece you want to generate in z axis.</param>
    static public IEnumerator generateTerrainPatch(int x_index, int z_index, int x_piece_num, int z_piece_num)
    {
        Mesh mesh = new Mesh();
        float[,,] terrain_points = new float[x_piece_num + 1, (z_piece_num + 1), 3];
        Vector3[] vertice = new Vector3[(x_piece_num + 1) * (z_piece_num + 1)];
        Vector2[] uv = new Vector2[(x_piece_num + 1) * (z_piece_num + 1)];
        int[] indices = new int[6 * x_piece_num * z_piece_num];
        int indices_index = 0;
        float center_x = min_x + (2 * x_index + x_piece_num) * PublicOutputInfo.piece_length / 2;
        float center_z = min_z + (2 * z_index + z_piece_num) * PublicOutputInfo.piece_length / 2;
        float center_y = 0.0f;
        if (terrain_mode == 1)
            center_y = min_y + getIDWHeight(center_x, center_z);
        //float center_y = min_y + IDW.inverseDistanceWeighting(getVertexFeatures(center_x, center_z), center_x, center_z); // -15
        Vector4[] area_features = getAreaFeatures(min_x + x_index * PublicOutputInfo.piece_length, min_z + z_index * PublicOutputInfo.piece_length, x_piece_num, z_piece_num);
        Vector3 center = new Vector3(center_x, center_y, center_z);
        for (int i = 0; i <= x_piece_num; i++)
        {
            for (int j = 0; j <= z_piece_num; j++)
            {
                terrain_points[i, j, 0] = min_x + (x_index + i) * PublicOutputInfo.piece_length;
                terrain_points[i, j, 2] = min_z + (z_index + j) * PublicOutputInfo.piece_length;
                if (terrain_mode == 0)
                    terrain_points[i, j, 1] = min_y + getDEMHeight(terrain_points[i, j, 0], terrain_points[i, j, 2]); // min_y is a bias
                else
                    terrain_points[i, j, 1] = center_y;
                //terrain_points[i, j, 1] = min_y + getDEMHeight(terrain_points[i, j, 0], terrain_points[i, j, 2]); // min_y is a bias
                //Vector3[] vf = getVertexFeatures(terrain_points[i, j, 0], terrain_points[i, j, 2]);
                //terrain_points[i, j, 1] = min_y + IDW.inverseDistanceWeighting(vf, terrain_points[i, j, 0], terrain_points[i, j, 2]); // min_y is a bias  -15
                //Vector3[] vfa = getVertexFeaturesFromArea(terrain_points[i, j, 0], terrain_points[i, j, 2], area_features);
                //terrain_points[i, j, 1] = min_y + IDW.inverseDistanceWeighting(vfa, terrain_points[i, j, 0], terrain_points[i, j, 2]);
                vertice[i * (z_piece_num + 1) + j] = new Vector3(terrain_points[i, j, 0] - center.x, terrain_points[i, j, 1] - center.y, terrain_points[i, j, 2] - center.z);
                uv[i * (z_piece_num + 1) + j] = new Vector2((float)i / x_piece_num, (float)j / z_piece_num);
            }
        }

        for (int i = 0; i < x_piece_num; i++)
        {
            for (int j = 0; j < z_piece_num; j++)
            {
                // counter-clockwise
                indices[indices_index++] = i * (z_piece_num + 1) + j;
                indices[indices_index++] = (i + 1) * (z_piece_num + 1) + j + 1;
                indices[indices_index++] = (i + 1) * (z_piece_num + 1) + j;
                indices[indices_index++] = i * (z_piece_num + 1) + j;
                indices[indices_index++] = i * (z_piece_num + 1) + j + 1;
                indices[indices_index++] = (i + 1) * (z_piece_num + 1) + j + 1;
            }
        }

        mesh.vertices = vertice;
        mesh.uv = uv;
        mesh.triangles = indices;
        //Recalculations
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.Optimize();
        //Name the mesh
        mesh.name = "terrain_mesh";
        GameObject terrain = new GameObject("terrain_peice_" + x_index + "_" + z_index);
        MeshFilter mf = terrain.AddComponent<MeshFilter>();
        MeshRenderer mr = terrain.AddComponent<MeshRenderer>();
        mf.mesh = mesh;
        if (terrain_mode == 0)
        {
            mr.material = new Material(terrain_mat);
        }
        else if (terrain_mode == 1)
        {
            mr.material = new Material(terrain_idw_mat);
            mr.material.SetVectorArray("features", area_features);
            mr.material.SetInt("features_count", area_features.Length);
            mr.material.SetFloat("height_base", min_y);
            mr.material.SetFloat("dxz", PublicOutputInfo.piece_length);
        }
        else
        {
            mr.material = new Material(terrain_nni_mat);
            mr.material.SetVectorArray("features", area_features);
            mr.material.SetInt("features_count", area_features.Length);
            mr.material.SetFloat("height_base", min_y);
        }
        terrain.transform.position = center;
        terrains[x_index * z_patch_num + z_index] = terrain;
        generated_x_list.Add(x_index);
        generated_z_list.Add(z_index);
        //Debug.Log("Success: " + x_small_min + "_" + z_small_min);

        terrain.AddComponent<TerrainView>();
        yield return null;
    }

    static void getAreaTerrainInfo(float x, float z)
    {
        int patch_x_index = Mathf.FloorToInt((x - min_x) / PublicOutputInfo.patch_length);
        int patch_z_index = Mathf.FloorToInt((z - min_z) / PublicOutputInfo.patch_length);
        queue_patch_x_index.Enqueue(patch_x_index);
        queue_patch_z_index.Enqueue(patch_z_index);
        need_update = true;
    }

    /// <summary>
    /// Remove terrains not near the position
    /// </summary>
    static void removeAreaTerrain(float x, float z)
    {
        for (int generated_list_index = 0; generated_list_index < generated_x_list.Count; generated_list_index++)
        {
            int ddist = Mathf.Abs(generated_x_list[generated_list_index] - patch_x_index) / PublicOutputInfo.piece_num + Mathf.Abs(generated_z_list[generated_list_index] - patch_z_index) / PublicOutputInfo.piece_num;
            if (ddist > vision_patch_num * 2)
            {
                is_generated[generated_x_list[generated_list_index] * z_patch_num + generated_z_list[generated_list_index]] = false;
                GameObject.Destroy(terrains[generated_list_index]);
                terrains[generated_x_list[generated_list_index] * z_patch_num + generated_z_list[generated_list_index]] = null;
                generated_x_list.RemoveAt(generated_list_index);
                generated_z_list.RemoveAt(generated_list_index);
                generated_list_index--;
            }
        }
    }

    static public float getHeightWithBais(float x, float z)
    {
        return getHeightFromComputeShader(x, z);
        //if (terrain_mode == 0)
        //    return getDEMHeight(x, z, true) + min_y;
        //else if (terrain_mode == 1)
        //    return getIDWHeight(x, z) + min_y;
        //else
        //    return getNNIHeight(x, z) + min_y;
    }

    static public float getDEMHeight(float x, float z, bool interpolation = true)
    {
        x += boundary_min_x + origin_x;
        z += boundary_min_z + origin_z;
        float lon = (float)MercatorProjection.xToLon(x);
        float lat = (float)MercatorProjection.yToLat(z);
        List<EarthCoord> all_coords = new List<EarthCoord>();
        all_coords.Add(new EarthCoord(lon, lat));
        return HgtReader.getElevations(all_coords, interpolation)[0];
    }

    static public float getIDWHeight(float x, float z, float old_base = 0.0f)
    {
        getAreaTerrainInfo(x, z);
        //Vector3[] area_features = getAreaFeatures(center_piece_x, center_piece_z, 4, 4);
        Vector4[] vertex_features = getVertexFeatures(x, z);
        return IDW.inverseDistanceWeighting(vertex_features, x, z, old_base);
    }

    static public float getNNIHeight(float x, float z, float old_base = 0.0f)
    {
        getAreaTerrainInfo(x, z);
        //Vector3[] area_features = getAreaFeatures(center_piece_x, center_piece_z, 4, 4);
        Vector4[] vertex_features = getVertexFeatures(x, z);
        return NNI.naturalNeighborInterpolation(vertex_features, x, z, old_base);
    }

    static public void generateSmallHeightmapTerrain(Texture2D heightmap, int x_small_min, int z_small_min, int x_small_length, int z_small_length)
    {
        //Debug.Log("Calculating: " + x_small_min + "_" + z_small_min);
        Mesh mesh = new Mesh();
        float[,,] terrain_points = new float[x_small_length, z_small_length, 3];
        Vector3[] vertice = new Vector3[x_small_length * z_small_length];
        Vector2[] uv = new Vector2[x_small_length * z_small_length];
        int[] indices = new int[6 * (x_small_length - 1) * (z_small_length - 1)];
        int indices_index = 0;
        float center_x = min_x + (2 * x_small_min + x_small_length - 1) * PublicOutputInfo.piece_length / 2;
        float center_z = min_z + (2 * z_small_min + z_small_length - 1) * PublicOutputInfo.piece_length / 2;
        //float center_y = min_y + getDEMHeight(center_x, center_z);
        float center_y = min_y;
        Vector3 center = new Vector3(center_x, center_y, center_z);
        for (int i = 0; i < x_small_length; i++)
        {
            for (int j = 0; j < z_small_length; j++)
            {
                terrain_points[i, j, 0] = min_x + (x_small_min + i) * PublicOutputInfo.piece_length;
                terrain_points[i, j, 2] = min_z + (z_small_min + j) * PublicOutputInfo.piece_length;
                //terrain_points[i, j, 1] = min_y + getDEMHeight(terrain_points[i, j, 0], terrain_points[i, j, 2]); // min_y is a bias
                terrain_points[i, j, 1] = min_y + heightmap.GetPixel(i, j).r * 255; // min_y is a bias  -15
                vertice[i * z_small_length + j] = new Vector3(terrain_points[i, j, 0] - center.x, terrain_points[i, j, 1] - center.y, terrain_points[i, j, 2] - center.z);
                uv[i * z_small_length + j] = new Vector2((float)(x_small_min + i) / x_patch_num, (float)(z_small_min + j) / z_patch_num);
            }
        }

        for (int i = 0; i < x_small_length - 1; i++)
        {
            for (int j = 0; j < z_small_length - 1; j++)
            {
                // counter-clockwise
                indices[indices_index++] = i * z_small_length + j;
                indices[indices_index++] = (i + 1) * z_small_length + j + 1;
                indices[indices_index++] = (i + 1) * z_small_length + j;
                indices[indices_index++] = i * z_small_length + j;
                indices[indices_index++] = i * z_small_length + j + 1;
                indices[indices_index++] = (i + 1) * z_small_length + j + 1;
            }
        }

        mesh.vertices = vertice;
        mesh.uv = uv;
        mesh.triangles = indices;
        //Recalculations
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.Optimize();
        //Name the mesh
        mesh.name = "terrain_mesh";
        GameObject terrain = new GameObject("terrain_Heightmap_" + x_small_min + "_" + z_small_min);
        MeshFilter mf = terrain.AddComponent<MeshFilter>();
        MeshRenderer mr = terrain.AddComponent<MeshRenderer>();
        mf.mesh = mesh;
        mr.material = terrain_mat;
        terrain.transform.position = center;
        terrains[x_small_min * z_patch_num + z_small_min] = terrain;
        //Debug.Log("Success: " + x_small_min + "_" + z_small_min);
    }

    static public void showPoint(WVec3[] points, string tag, Transform parent, GameObject ball_prefab, float ball_size)
    {
        for (int point_index = 0; point_index < points.Length; point_index++)
        {
            GameObject ball = GameObject.Instantiate(ball_prefab, new Vector3(points[point_index].x, points[point_index].y, points[point_index].z), Quaternion.identity); // + min_y
            ball.transform.localScale = new Vector3(ball_size, ball_size, ball_size);
            ball.name = tag + "_" + point_index.ToString();
            ball.transform.parent = parent;

            if (points[point_index].w > 8)
            {
                ball.GetComponent<MeshRenderer>().material.color = Color.red;
            }
        }
    }

    /// <summary>
    /// generate terrain patch texture in compute shader
    /// </summary>
    /// <param name="x_index"></param>
    /// <param name="z_index"></param>
    /// <param name="x_piece_num"></param>
    /// <param name="z_piece_num"></param>
    /// <returns></returns>
    static public IEnumerator generateTerrainPatchTex(int x_index, int z_index, int x_piece_num, int z_piece_num)
    {
        // ================================== fetch feature points ===================================================
        var area_features_constraints = getAreaFeaturesForPatch(min_x + x_index * PublicOutputInfo.patch_length, min_z + z_index * PublicOutputInfo.patch_length);
        Vector4[] area_features = area_features_constraints.Item1;
        Vector4[] area_constraints = area_features_constraints.Item2;
        // ===========================================================================================================

        // ================================= setting compute shader ==================================================
        RenderTexture tex = new RenderTexture(PublicOutputInfo.tex_size, PublicOutputInfo.tex_size, 24);
        tex.enableRandomWrite = true;
        tex.Create();
        var fence = Graphics.CreateGraphicsFence(UnityEngine.Rendering.GraphicsFenceType.AsyncQueueSynchronisation, UnityEngine.Rendering.SynchronisationStageFlags.ComputeProcessing);
        Graphics.WaitOnAsyncGraphicsFence(fence);
        int kernelHandler = compute_shader.FindKernel("CSMain");
        compute_shader.SetTexture(kernelHandler, "Result", tex);
        compute_shader.SetVectorArray("features", area_features);
        compute_shader.SetInt("features_count", area_features.Length);
        compute_shader.SetVectorArray("constraints", area_constraints);
        compute_shader.SetInt("constraints_count", area_constraints.Length);
        compute_shader.SetFloat("x", min_x + x_index * PublicOutputInfo.patch_length);
        compute_shader.SetFloat("z", min_z + z_index * PublicOutputInfo.patch_length);
        compute_shader.SetFloat("resolution", PublicOutputInfo.patch_length / PublicOutputInfo.tex_size); // patch_length / tex_length
        progress_buffer[x_index * z_patch_num + z_index] = new ComputeBuffer(1, 4);
        int[] progress = new int[] { 0 };
        progress_buffer[x_index * z_patch_num + z_index].SetData(progress);
        compute_shader.SetBuffer(kernelHandler, "progress", progress_buffer[x_index * z_patch_num + z_index]);
        //heights_buffer[x_index * z_patch_num + z_index] = new ComputeBuffer(PublicOutputInfo.tex_size * PublicOutputInfo.tex_size, 4);
        //heights[x_index * z_patch_num + z_index] = new float[PublicOutputInfo.tex_size * PublicOutputInfo.tex_size];
        //heights_buffer[x_index * z_patch_num + z_index].SetData(heights[x_index * z_patch_num + z_index]);
        //compute_shader.SetBuffer(kernelHandler, "heights", heights_buffer[x_index * z_patch_num + z_index]);
        compute_shader.Dispatch(kernelHandler, Mathf.CeilToInt(PublicOutputInfo.tex_size / 8), Mathf.CeilToInt(PublicOutputInfo.tex_size / 8), 1);
        Graphics.WaitOnAsyncGraphicsFence(fence);
        //heights_buffer[x_index * z_patch_num + z_index].GetData(heights[x_index * z_patch_num + z_index]);
        // ===========================================================================================================

        // ================================= rendering texture2D =====================================================
        heightmaps[x_index * z_patch_num + z_index] = new Texture2D(PublicOutputInfo.tex_size, PublicOutputInfo.tex_size, TextureFormat.RGB24, false); // 320 + 128 + 320
        Rect rectReadPicture = new Rect(0, 0, PublicOutputInfo.tex_size, PublicOutputInfo.tex_size);
        RenderTexture.active = tex;
        // Read pixels
        heightmaps[x_index * z_patch_num + z_index].ReadPixels(rectReadPicture, 0, 0);
        heightmaps[x_index * z_patch_num + z_index].wrapMode = TextureWrapMode.Clamp;
        heightmaps[x_index * z_patch_num + z_index].Apply();
        RenderTexture.active = null; // added to avoid errors
        // ===========================================================================================================

        // ================================= setting GameObject ======================================================
        terrains[x_index * z_patch_num + z_index] = new GameObject("terrain_peice_" + x_index + "_" + z_index);
        MeshRenderer mr = terrains[x_index * z_patch_num + z_index].AddComponent<MeshRenderer>();
        mr.material = new Material(terrain_mat);
        mr.material.SetTexture("_MainTex", heightmaps[x_index * z_patch_num + z_index]);
        //mr.material.SetTexture("_MainTex", main_tex);
        terrains[x_index * z_patch_num + z_index].AddComponent<TerrainView>();
        terrains[x_index * z_patch_num + z_index].GetComponent<TerrainView>().x_index = x_index;
        terrains[x_index * z_patch_num + z_index].GetComponent<TerrainView>().z_index = z_index;
        terrains[x_index * z_patch_num + z_index].GetComponent<TerrainView>().x_piece_num = x_piece_num;
        terrains[x_index * z_patch_num + z_index].GetComponent<TerrainView>().z_piece_num = z_piece_num;
        terrains[x_index * z_patch_num + z_index].transform.parent = terrain_manager.transform;
        generated_x_list.Add(x_index);
        generated_z_list.Add(z_index);
        // ===========================================================================================================
        yield return null;
    }

    /// <summary>
    /// generate a patch terrain with piece_num * piece_num size
    /// </summary>
    /// <param name="x_index">the index of vertex which is the beginning to generate in x axis.</param>
    /// <param name="z_index">the index of vertex which is the beginning to generate in z axis.</param>
    /// <param name="x_piece_num">the number of piece you want to generate in x axis.</param>
    /// <param name="z_piece_num">the number of piece you want to generate in z axis.</param>
    static public IEnumerator generateTerrainPatchWithTex(int x_index, int z_index, int x_piece_num, int z_piece_num)
    {
        is_generated[x_index * z_patch_num + z_index] = true;
        Mesh mesh = new Mesh();
        float[,,] terrain_points = new float[x_piece_num + 1, (z_piece_num + 1), 3];
        Vector3[] vertice = new Vector3[(x_piece_num + 1) * (z_piece_num + 1)];
        Vector2[] uv = new Vector2[(x_piece_num + 1) * (z_piece_num + 1)];
        int[] indices = new int[6 * x_piece_num * z_piece_num];
        int indices_index = 0;
        float center_x = min_x + (2 * x_index + 1) * PublicOutputInfo.patch_length / 2;
        float center_z = min_z + (2 * z_index + 1) * PublicOutputInfo.patch_length / 2;
        float center_y = getHeightFromComputeShader(center_x, center_z);
        //Debug.Log(center_y);
        //if (terrain_mode == 1)
        //    center_y = min_y + getIDWHeight(center_x, center_z);

        Vector3 center = new Vector3(center_x, center_y, center_z);
        for (int i = 0; i <= x_piece_num; i++)
        {
            for (int j = 0; j <= z_piece_num; j++)
            {
                uv[i * (z_piece_num + 1) + j] = new Vector2((float)i / x_piece_num, (float)j / z_piece_num);
                terrain_points[i, j, 0] = min_x + x_index * PublicOutputInfo.patch_length + i * PublicOutputInfo.piece_length;
                terrain_points[i, j, 1] = getHeightFromTex(x_index, z_index, uv[i * (z_piece_num + 1) + j].x, uv[i * (z_piece_num + 1) + j].y);
                terrain_points[i, j, 2] = min_z + z_index * PublicOutputInfo.patch_length + j * PublicOutputInfo.piece_length;
                vertice[i * (z_piece_num + 1) + j] = new Vector3(terrain_points[i, j, 0] - center.x, terrain_points[i, j, 1] - center.y, terrain_points[i, j, 2] - center.z);
            }
        }

        for (int i = 0; i < x_piece_num; i++)
        {
            for (int j = 0; j < z_piece_num; j++)
            {
                // counter-clockwise
                indices[indices_index++] = i * (z_piece_num + 1) + j;
                indices[indices_index++] = (i + 1) * (z_piece_num + 1) + j + 1;
                indices[indices_index++] = (i + 1) * (z_piece_num + 1) + j;
                indices[indices_index++] = i * (z_piece_num + 1) + j;
                indices[indices_index++] = i * (z_piece_num + 1) + j + 1;
                indices[indices_index++] = (i + 1) * (z_piece_num + 1) + j + 1;
            }
        }

        mesh.vertices = vertice;
        mesh.uv = uv;
        mesh.triangles = indices;
        //Recalculations
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.Optimize();
        //Name the mesh
        mesh.name = "terrain_mesh";
        MeshFilter mf = terrains[x_index * z_patch_num + z_index].AddComponent<MeshFilter>();
        mf.mesh = mesh;
        //mr.material = new Material(heightmap_mat);
        //mr.material.SetTexture("_MainTex", main_tex);
        //mr.material.SetTexture("_HeightmapTex", heightmaps[x_index * z_patch_num + z_index]);

        terrains[x_index * z_patch_num + z_index].transform.position = center;
        yield return null;
    }

    static float getHeightFromTex(int x_index, int z_index, float u, float v)
    {
        Color raw = heightmaps[x_index * z_patch_num + z_index].GetPixelBilinear(u, v);
        return raw.g * 64 * 64 + raw.b * 64 + raw.a;
        //int x = Mathf.FloorToInt(u * (PublicOutputInfo.tex_size - 1));
        //int z = Mathf.FloorToInt(v * (PublicOutputInfo.tex_size - 1));
        //Debug.Log(x.ToString() + ", " + z.ToString());
        //return heights[x_index * z_patch_num + z_index][x * PublicOutputInfo.tex_size + z];
    }

    static float getHeightFromComputeShader(float x, float z)
    {
        int x_index = Mathf.FloorToInt((x - min_x) / PublicOutputInfo.patch_length);
        int z_index = Mathf.FloorToInt((z - min_z) / PublicOutputInfo.patch_length);
        if (!is_generated[x_index * z_patch_num + z_index])
        {
            //Debug.LogError(patch_x_index.ToString() + ", " + patch_z_index.ToString() + " not be loaded");
            return 0.0f;
        }
        float u = (x - (min_x + x_index * PublicOutputInfo.patch_length)) / heightmaps[x_index * z_patch_num + z_index].width;
        float v = (z - (min_z + z_index * PublicOutputInfo.patch_length)) / heightmaps[x_index * z_patch_num + z_index].height;
        return heightmaps[x_index * z_patch_num + z_index].GetPixelBilinear(u, v).r * 900;
    }

    static public bool checkTerrainLoaded()
    {
        return is_queue_generate_patch_empty;
    }

    //static void getHightFromShader(float x, float z)
    //{
    //    //first Make sure you're using RGB24 as your texture format
    //    Texture mainTexture = terrains[0].GetComponent<Renderer>().sharedMaterial.GetTexture("Texture2D_a7d369ab60fc42b2b7cc47413405165f");
    //    Texture2D texture2D = new Texture2D(mainTexture.width, mainTexture.height, TextureFormat.RGBA32, false);
    //    Debug.Log(mainTexture.width);
    //    RenderTexture currentRT = RenderTexture.active;

    //    RenderTexture renderTexture = new RenderTexture(mainTexture.width, mainTexture.height, 32);
    //    Graphics.Blit(mainTexture, renderTexture, GetComponent<Renderer>().sharedMaterial);

    //    RenderTexture.active = renderTexture;
    //    texture2D.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
    //    texture2D.Apply();

    //    Color[] pixels = texture2D.GetPixels();

    //    RenderTexture.active = currentRT;

    //    //then Save To Disk as PNG
    //    byte[] bytes = texture2D.EncodeToPNG();
    //    var dirPath = Application.dataPath + "/Resources/";
    //    if (!Directory.Exists(dirPath))
    //    {
    //        Directory.CreateDirectory(dirPath);
    //    }
    //    File.WriteAllBytes(dirPath + "smallEdge" + ".png", bytes);
    //}
}