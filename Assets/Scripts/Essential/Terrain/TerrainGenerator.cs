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
    static public float boundary_min_x;
    static public float boundary_min_z;
    static public float origin_x;
    static public float origin_y;
    static public float origin_z;
    static public WVec3[] features;
    static public Material terrain_mat;                                 // Use the standard material. The vertices are calculated by CPU
    static public Material terrain_idw_mat;                             // Use the material with IDW shader
    static public Material terrain_nni_mat;                             // Use the material with NNI shader
    static public bool generate;
    static public int vision_patch_num = 5;                             // The number of patches the viewer can see
    static public float feature_include_length = 320.0f;                // + 16 for gaussian M if want to pass gaussian filter
    static public GameObject[] terrains;                                // Store all patches of terrains
    static public bool need_update = false;                             // Call TerrainManager to generate new patches
    static public int patch_x_index;                                    // Used to calculate the index for removing checking
    static public int patch_z_index;                                    // Used to calculate the index for removing checking
    static public Queue<Vector3> loading_vec3s = new Queue<Vector3>();  // Loading Queue
    static public Queue<int> queue_patch_x_index = new Queue<int>();    // Coordinate info Queue
    static public Queue<int> queue_patch_z_index = new Queue<int>();    // Coordinate info Queue
    static public bool[] is_loaded;                                     // Check the terrain is called to generate or not
    static public bool[] is_generated;                                  // Check the terrain is generated or not
    static public int[] trigger_num_in_view;                            // Number of triggers of road, destroy when 0
    static public KDTree kdtree;                                        // For searching and recording feature points
    static public int terrain_mode = 0;                                 // 0 is IDW, 1 is DEM, 2 is NNI, controled by TerrainManager
    static public bool show_feature_ball = true;
    static public GameObject feature_ball_prefab;
    static public bool is_queue_generate_patch_empty = false;
    static public ComputeBuffer[] progress_buffer;
    static public ComputeBuffer[] height_buffer;
    static public ComputeBuffer[] height_gaussian_buffer;
    static public float[][] heights;
    static public float[][] heights_pregaussian;
    static public WVec3[] road_constraints;
    static public WVec3[] building_constraints;
    static public int[] building_constraints_points_count;
    static public int[] building_constraints_accumulate_index;

    //static public Material heightmap_mat;
    static public ComputeShader compute_shader;
    static public Texture2D main_tex;
    static public Texture2D[] heightmaps;
    static public Texture2D[] constraintsmap;
    static public GameObject terrain_manager;
    static public GameObject constraints_camera_manager;
    static public RenderTexture[] constraints_texs;
    static public GameObject building_polygons_manager;
    static public Material building_polygon_mat;
    static public GameObject[] building_polygons;
    static public bool[] constraintsmap_generated;
    static float polygon_dilation = 1.25f;
    static public float power = 3.0f;
    static public bool need_mse = false;
    static public Terrain origin_terrain;
    static bool[] gameobject_generated;
    static bool use_gaussian = true;
    /// <summary>
    /// Load feature points file with file_path.
    /// Everything that needs height information must wait until is_initial is True.
    /// </summary>
    static public void loadTerrain()
    {
        readFeatureFile(Application.streamingAssetsPath + "//" + file_path);
        is_loaded = new bool[x_patch_num * z_patch_num];
        is_generated = new bool[x_patch_num * z_patch_num];
        trigger_num_in_view = new int[x_patch_num * z_patch_num];
        heightmaps = new Texture2D[x_patch_num * z_patch_num];
        constraintsmap = new Texture2D[x_patch_num * z_patch_num];
        terrains = new GameObject[x_patch_num * z_patch_num];
        progress_buffer = new ComputeBuffer[x_patch_num * z_patch_num];
        height_buffer = new ComputeBuffer[x_patch_num * z_patch_num];
        height_gaussian_buffer = new ComputeBuffer[x_patch_num * z_patch_num];
        gameobject_generated = new bool[x_patch_num * z_patch_num];
        heights = new float[x_patch_num * z_patch_num][];
        heights_pregaussian = new float[x_patch_num * z_patch_num][];
        constraints_texs = new RenderTexture[x_patch_num * z_patch_num];
        constraints_camera_manager = new GameObject("ConstraintsCameraManager");
        constraintsmap_generated = new bool[x_patch_num * z_patch_num];
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
        }
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
            List<WVec3> road_constraints_list = new List<WVec3>();
            List<WVec3> building_constraints_list = new List<WVec3>();
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
                    if (w < 100000)
                        road_constraints_list.Add(constraint);
                    else
                        building_constraints_list.Add(constraint);
                }
                int p = int.Parse(inputs[4]);
                kdtree.parent[f_i] = p;
                int l = int.Parse(inputs[5]);
                kdtree.left[f_i] = l;
                int r = int.Parse(inputs[6]);
                kdtree.right[f_i] = r;
            }
            road_constraints_list.Sort(delegate (WVec3 a, WVec3 b)
            {
                return a.w.CompareTo(b.w);
            });
            road_constraints = road_constraints_list.ToArray();
            building_constraints_list.Sort(delegate (WVec3 a, WVec3 b)
            {
                return a.w.CompareTo(b.w);
            });
            building_constraints = building_constraints_list.ToArray();
            int building_n = int.Parse(sr.ReadLine());
            building_constraints_points_count = new int[building_n];
            building_constraints_accumulate_index = new int[building_n];
            building_polygons = new GameObject[building_n];
            int current_building_constraints_accumulate_index = 0;
            for (int f_i = 0; f_i < building_n; f_i++)
            {
                building_constraints_points_count[f_i] = int.Parse(sr.ReadLine());
                building_constraints_accumulate_index[f_i] = current_building_constraints_accumulate_index;
                current_building_constraints_accumulate_index += building_constraints_points_count[f_i];
                createBuildingPolygon(f_i);
            }
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

    static public (Vector4[], Vector4[], Vector4[], int[]) getAreaFeaturesForPatch(float x, float z)
    {
        float expanded_length = feature_include_length;
        int[] area_features_index = kdtree.getAreaPoints(x - expanded_length, z - expanded_length, x + PublicOutputInfo.patch_length + expanded_length, z + PublicOutputInfo.patch_length + expanded_length);
        Vector4[] area_features = new Vector4[area_features_index.Length];
        int[] area_constraints_index = kdtree.getAreaPoints(x - 16, z - 16, x + PublicOutputInfo.patch_length + 16, z + PublicOutputInfo.patch_length + 16);
        List<Vector4> area_road_constraints_list = new List<Vector4>();
        List<Vector4> area_building_constraints_contact_points_list = new List<Vector4>();
        for (int area_features_index_index = 0; area_features_index_index < area_features_index.Length; area_features_index_index++)
        {
            WVec3 feature = kdtree.nodes[area_features_index[area_features_index_index]];
            if (feature.w < 0)
            area_features[area_features_index_index] = new Vector4(feature.x, feature.y, feature.z, feature.w);
        }

        for (int area_constraints_index_index = 0; area_constraints_index_index < area_constraints_index.Length; area_constraints_index_index++)
        {
            WVec3 feature = kdtree.nodes[area_constraints_index[area_constraints_index_index]];
            if (feature.w > -1)
            {
                if (feature.w < 100000)
                    area_road_constraints_list.Add(new Vector4(feature.x, feature.y, feature.z, feature.w));
                else
                    area_building_constraints_contact_points_list.Add(new Vector4(feature.x, feature.y, feature.z, feature.w));
            }
        }

        var area_building_constraints = buildingConstraintsCompile(area_building_constraints_contact_points_list);

        return (area_features, roadConstraintsCompile(area_road_constraints_list), area_building_constraints.Item1, area_building_constraints.Item2);
    }

    static void addAreaRoadConstraintsHead(ref List<Vector4> area_road_constraints)
    {
        if (Mathf.Abs(area_road_constraints[0].w) < 1e-6) // first_w == 0
        {
            if (area_road_constraints.Count >= 2)
            {
                Vector4 head_v = (area_road_constraints[0] - area_road_constraints[1]).normalized;
                area_road_constraints.Insert(0, area_road_constraints[0] + head_v);
            }
        }
        else // first_w >= 1
        {
            int first_w = Mathf.FloorToInt(area_road_constraints[0].w + 0.000001f);
            Vector4 sup_constraint = new Vector4(road_constraints[first_w - 1].x, road_constraints[first_w - 1].y, road_constraints[first_w - 1].z, road_constraints[first_w - 1].w);
            area_road_constraints.Insert(0, sup_constraint);
        }
    }

    static void addAreaRoadConstraintsTail(ref List<Vector4> area_road_constraints)
    {
        int last_w = Mathf.FloorToInt(area_road_constraints[area_road_constraints.Count - 1].w + 0.000001f);
        if (last_w + 1 < road_constraints.Length)
        {
            WVec3 constraint = road_constraints[last_w + 1];
            Vector4 sup_constraint = new Vector4(constraint.x, constraint.y, constraint.z, constraint.w);
            area_road_constraints.Add(sup_constraint);
        }
        else
        {
            WVec3 constraint_0 = road_constraints[last_w];
            WVec3 constraint_1 = road_constraints[last_w - 1];
            Vector4 sup_constraint_0 = new Vector4(constraint_0.x, constraint_0.y, constraint_0.z, constraint_0.w);
            Vector4 sup_constraint_1 = new Vector4(constraint_1.x, constraint_1.y, constraint_1.z, constraint_1.w);
            Vector4 tail_v = (sup_constraint_0 - sup_constraint_1).normalized;
            area_road_constraints.Add(sup_constraint_0 + tail_v);
        }
    }

    static Vector4[] roadConstraintsCompile(List<Vector4> area_road_constraints)
    {
        area_road_constraints.Sort(delegate (Vector4 a, Vector4 b)
        {
            return a.w.CompareTo(b.w);
        });
        //string origin = "origin: ";
        //for (int area_constraints_index = 0; area_constraints_index < area_constraints.Count; area_constraints_index++)
        //{
        //    origin += area_constraints[area_constraints_index].w + " ";
        //}
        //Debug.Log(origin);
        if (area_road_constraints.Count > 0)
        {
            addAreaRoadConstraintsHead(ref area_road_constraints);
            addAreaRoadConstraintsHead(ref area_road_constraints);
            for (int area_constraints_index = 0; area_constraints_index < area_road_constraints.Count - 1; area_constraints_index++)
            {
                int diff_w = Mathf.FloorToInt(Mathf.Abs(area_road_constraints[area_constraints_index].w - area_road_constraints[area_constraints_index + 1].w) + 0.000003f);
                if (diff_w > 1)
                {
                    WVec3 constraint = road_constraints[Mathf.FloorToInt(area_road_constraints[area_constraints_index].w + 1.000001f)];
                    Vector4 sup_constraint = new Vector4(constraint.x, constraint.y, constraint.z, constraint.w);
                    area_road_constraints.Insert(area_constraints_index + 1, sup_constraint);
                }
                if (diff_w > 2)
                {
                    area_constraints_index++;
                    WVec3 constraint = road_constraints[Mathf.FloorToInt(area_road_constraints[area_constraints_index + 1].w - 1.000001f)];
                    Vector4 sup_constraint = new Vector4(constraint.x, constraint.y, constraint.z, constraint.w);
                    area_road_constraints.Insert(area_constraints_index + 1, sup_constraint);
                }
            }
            addAreaRoadConstraintsTail(ref area_road_constraints);
            addAreaRoadConstraintsTail(ref area_road_constraints);
        }
        //string aftersup = "after sup: ";
        //for (int area_constraints_index = 0; area_constraints_index < area_constraints.Count; area_constraints_index++)
        //{
        //    aftersup += area_constraints[area_constraints_index].w + " ";
        //}
        //Debug.Log(aftersup);
        return area_road_constraints.ToArray();
    }

    static (Vector4[], int[]) buildingConstraintsCompile(List<Vector4> area_building_constraints_contact_points_list)
    {
        area_building_constraints_contact_points_list.Sort(delegate (Vector4 a, Vector4 b)
        {
            return a.w.CompareTo(b.w);
        });
        List<Vector4> area_building_constraints_points_list = new List<Vector4>();
        List<int> building_constraints_points_count_list = new List<int>();
        int last_id = -1;
        //string dd = "========================================\n";
        for (int area_constraints_index = 0; area_constraints_index < area_building_constraints_contact_points_list.Count; area_constraints_index++)
        {
            int building_tag = Mathf.FloorToInt(area_building_constraints_contact_points_list[area_constraints_index].w + 0.5f);
            int building_id = building_tag / 100 - 1000;
            if (building_id != last_id)
            {
                for (int building_constraints_points_count_index = 0; building_constraints_points_count_index < building_constraints_points_count[building_id]; building_constraints_points_count_index++)
                {
                    WVec3 constraint = building_constraints[building_constraints_accumulate_index[building_id] + building_constraints_points_count_index];
                    area_building_constraints_points_list.Add(new Vector4(constraint.x, constraint.y, constraint.z, constraint.w));
                    //dd += constraint.x + " " + constraint.y + " " + constraint.z + " " + constraint.w + "\n";
                }
                WVec3 constraint_repeat = building_constraints[building_constraints_accumulate_index[building_id]];
                area_building_constraints_points_list.Add(new Vector4(constraint_repeat.x, constraint_repeat.y, constraint_repeat.z, constraint_repeat.w));
                building_constraints_points_count_list.Add(building_constraints_points_count[building_id] + 1);
                last_id = building_id;
            }
        }
        //Debug.Log(dd);
        return (area_building_constraints_points_list.ToArray(), building_constraints_points_count_list.ToArray());
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
        if (terrain_mode == 0)
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
                if (terrain_mode == 1)
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
        if (terrain_mode == 1)
        {
            mr.material = new Material(terrain_mat);
        }
        else if (terrain_mode == 0)
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
    static public IEnumerator removeAreaTerrain(float x, float z)
    {
        int center_x_index = Mathf.FloorToInt((x - min_x) / PublicOutputInfo.patch_length);
        int center_z_index = Mathf.FloorToInt((z - min_z) / PublicOutputInfo.patch_length);
        for (int i = -vision_patch_num; i <= vision_patch_num; i++)
        {
            for (int j = -vision_patch_num; j <= vision_patch_num; j++)
            {
                if (Mathf.Abs(i) + Mathf.Abs(j) > TerrainGenerator.vision_patch_num)
                    continue;
                int x_index = center_x_index + i;
                int z_index = center_z_index + j;
                if (x_index < 0 || x_index >= x_patch_num || z_index < 0 || z_index >= z_patch_num)
                    continue;
                trigger_num_in_view[x_index * z_patch_num + z_index]--;
                if (trigger_num_in_view[x_index * z_patch_num + z_index] == 0)
                {
                    is_generated[x_index * z_patch_num + z_index] = false;
                    GameObject.Destroy(terrains[x_index * z_patch_num + z_index]);
                    terrains[x_index * z_patch_num + z_index] = null;
                }
            }
        }
        yield return null;
    }

    static public float getHeightWithBais(float x, float z)
    {
        // in constraint-tex
        // out constraint-tex
        if (terrain_mode == 1)
            return getDEMHeight(x, z, true) + min_y;
        else
            return getHeightFromComputeShader(x, z) + min_y;
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

    static public float[] getDEMHeights(float[] xs, float[] zs, bool interpolation = true)
    {
        List<EarthCoord> all_coords = new List<EarthCoord>();
        for (int i = 0; i < xs.Length; i++)
        {
            float x = xs[i] + boundary_min_x + origin_x;
            float z = zs[i] + boundary_min_z + origin_z;
            float lon = (float)MercatorProjection.xToLon(x);
            float lat = (float)MercatorProjection.yToLat(z);
            all_coords.Add(new EarthCoord(lon, lat));
        }
        return HgtReader.getElevations(all_coords, interpolation).ToArray();
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
            ball.name = tag + "_" + point_index.ToString() + "w" + points[point_index].w.ToString();
            ball.transform.parent = parent;

            if (points[point_index].w > -1)
            {
                ball.GetComponent<MeshRenderer>().material.color = Color.red;
            }
        }
    }

    static public void showPoint(Vector3[] points, string tag, Transform parent, GameObject ball_prefab, float ball_size)
    {
        for (int point_index = 0; point_index < points.Length; point_index++)
        {
            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.transform.position = points[point_index];
            ball.transform.localScale = new Vector3(ball_size, ball_size, ball_size);
            ball.GetComponent<MeshRenderer>().material.color= Color.red;
            ball.name = tag;
            ball.transform.parent = parent;
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
        if (area_features.Length > 2048)
            Debug.LogError("Warning! Compute shader features size is not enough");
        Vector4[] area_road_constraints = area_features_constraints.Item2;
        if (area_road_constraints.Length > 512)
            Debug.LogError("Warning! Compute shader road_constraints size is not enough");
        Vector4[] area_building_constraints = area_features_constraints.Item3;
        if (area_building_constraints.Length > 1024)
            Debug.LogError("Warning! Compute shader building_constraints size is not enough");

        //if (x_index == 16 && z_index == 8)
        //{
        //    WVec3[] meow = new WVec3[area_building_constraints.Length];
        //    for (int i = 0; i < area_building_constraints.Length; i++)
        //    {
        //        meow[i].x = area_building_constraints[i].x;
        //        meow[i].y = area_building_constraints[i].y;
        //        meow[i].z = area_building_constraints[i].z;
        //        meow[i].w = area_building_constraints[i].w;
        //    }
        //    showPoint(meow, "feature_leak", (new GameObject()).transform, feature_ball_prefab, 1.0f);
        //}

        int[] area_building_constraints_points_count = area_features_constraints.Item4;
        if (area_building_constraints_points_count.Length > 128)
            Debug.LogError("Warning! Compute shader building_constraints_points_count size is not enough");
        // ===========================================================================================================

        // ================================= setting compute shader ==================================================
        RenderTexture result_tex = new RenderTexture(PublicOutputInfo.tex_size, PublicOutputInfo.tex_size, 24);
        result_tex.enableRandomWrite = true;
        result_tex.Create();
        RenderTexture pregaussian_tex = new RenderTexture(PublicOutputInfo.pregaussian_tex_size, PublicOutputInfo.pregaussian_tex_size, 24);
        pregaussian_tex.enableRandomWrite = true;
        pregaussian_tex.Create();
        var fence = Graphics.CreateGraphicsFence(UnityEngine.Rendering.GraphicsFenceType.AsyncQueueSynchronisation, UnityEngine.Rendering.SynchronisationStageFlags.ComputeProcessing);
        int IDW_kernel_handler = compute_shader.FindKernel("IDWTerrain");
        if (!use_gaussian)
        {
            compute_shader.SetTexture(IDW_kernel_handler, "Result", result_tex);
            compute_shader.SetVectorArray("features", area_features);
            compute_shader.SetInt("features_count", area_features.Length);
            compute_shader.SetVectorArray("road_constraints", area_road_constraints);
            compute_shader.SetInt("road_constraints_count", area_road_constraints.Length);
            compute_shader.SetVectorArray("building_constraints", area_building_constraints);
            compute_shader.SetInts("building_constraints_points_count", area_building_constraints_points_count);
            compute_shader.SetInt("building_constraints_count", area_building_constraints_points_count.Length);
            compute_shader.SetFloat("x", min_x + x_index * PublicOutputInfo.patch_length);
            compute_shader.SetFloat("z", min_z + z_index * PublicOutputInfo.patch_length);
            compute_shader.SetFloat("power", power);
            compute_shader.SetFloat("resolution", PublicOutputInfo.patch_length / (PublicOutputInfo.tex_size - 1)); // patch_length / tex_length
            compute_shader.SetInt("height_buffer_row_size", PublicOutputInfo.height_buffer_row_size);
            height_buffer[x_index * z_patch_num + z_index] = new ComputeBuffer(PublicOutputInfo.height_buffer_row_size * PublicOutputInfo.height_buffer_row_size, 4);
            heights[x_index * z_patch_num + z_index] = new float[PublicOutputInfo.height_buffer_row_size * PublicOutputInfo.height_buffer_row_size];
            height_buffer[x_index * z_patch_num + z_index].SetData(heights[x_index * z_patch_num + z_index]);
            compute_shader.SetBuffer(IDW_kernel_handler, "heights", height_buffer[x_index * z_patch_num + z_index]);
            progress_buffer[x_index * z_patch_num + z_index] = new ComputeBuffer(1, 4);
            int[] progress = new int[] { 0 };
            progress_buffer[x_index * z_patch_num + z_index].SetData(progress);
            compute_shader.SetBuffer(IDW_kernel_handler, "progress", progress_buffer[x_index * z_patch_num + z_index]);
            compute_shader.Dispatch(IDW_kernel_handler, Mathf.CeilToInt((float)PublicOutputInfo.tex_size / 8), Mathf.CeilToInt((float)PublicOutputInfo.tex_size / 8), 1);
        }
        else
        {
            compute_shader.SetTexture(IDW_kernel_handler, "Result", pregaussian_tex);
            compute_shader.SetVectorArray("features", area_features);
            compute_shader.SetInt("features_count", area_features.Length);
            compute_shader.SetVectorArray("road_constraints", area_road_constraints);
            compute_shader.SetInt("road_constraints_count", area_road_constraints.Length);
            compute_shader.SetVectorArray("building_constraints", area_building_constraints);
            compute_shader.SetInts("building_constraints_points_count", area_building_constraints_points_count);
            compute_shader.SetInt("building_constraints_count", area_building_constraints_points_count.Length);
            compute_shader.SetFloat("x", min_x + x_index * PublicOutputInfo.patch_length - PublicOutputInfo.gaussian_m);
            compute_shader.SetFloat("z", min_z + z_index * PublicOutputInfo.patch_length - PublicOutputInfo.gaussian_m);
            compute_shader.SetFloat("power", power);
            compute_shader.SetFloat("resolution", PublicOutputInfo.patch_length / (PublicOutputInfo.tex_size - 1)); // patch_length / tex_length
            compute_shader.SetInt("height_buffer_row_size", PublicOutputInfo.height_with_gaussian_buffer_row_size);
            //height_gaussian_buffer[x_index * z_patch_num + z_index] = new ComputeBuffer(PublicOutputInfo.height_with_gaussian_buffer_row_size * PublicOutputInfo.height_with_gaussian_buffer_row_size, 4);
            //heights_pregaussian[x_index * z_patch_num + z_index] = new float[PublicOutputInfo.height_with_gaussian_buffer_row_size * PublicOutputInfo.height_with_gaussian_buffer_row_size];
            //height_gaussian_buffer[x_index * z_patch_num + z_index].SetData(heights_pregaussian[x_index * z_patch_num + z_index]);
            //compute_shader.SetBuffer(IDW_kernel_handler, "heights", height_gaussian_buffer[x_index * z_patch_num + z_index]);
            height_buffer[x_index * z_patch_num + z_index] = new ComputeBuffer(PublicOutputInfo.height_with_gaussian_buffer_row_size * PublicOutputInfo.height_with_gaussian_buffer_row_size, 4);
            heights[x_index * z_patch_num + z_index] = new float[PublicOutputInfo.height_with_gaussian_buffer_row_size * PublicOutputInfo.height_with_gaussian_buffer_row_size];
            height_buffer[x_index * z_patch_num + z_index].SetData(heights[x_index * z_patch_num + z_index]);
            compute_shader.SetBuffer(IDW_kernel_handler, "heights", height_buffer[x_index * z_patch_num + z_index]);
            progress_buffer[x_index * z_patch_num + z_index] = new ComputeBuffer(1, 4);
            int[] progress = new int[] { 0 };
            progress_buffer[x_index * z_patch_num + z_index].SetData(progress);
            compute_shader.SetBuffer(IDW_kernel_handler, "progress", progress_buffer[x_index * z_patch_num + z_index]);
            compute_shader.Dispatch(IDW_kernel_handler, Mathf.CeilToInt((float)PublicOutputInfo.pregaussian_tex_size / 8), Mathf.CeilToInt((float)PublicOutputInfo.pregaussian_tex_size / 8), 1);
        }
        Graphics.WaitOnAsyncGraphicsFence(fence);
        //Debug.Log(x_index + ", " + z_index + " fence " + 1);
        // ===========================================================================================================

        // ================================= rendering texture2D =====================================================
        Rect rect_result;
        if (!use_gaussian)
        {
            rect_result = new Rect(0, 0, PublicOutputInfo.tex_size, PublicOutputInfo.tex_size);
            heightmaps[x_index * z_patch_num + z_index] = new Texture2D(PublicOutputInfo.tex_size, PublicOutputInfo.tex_size, TextureFormat.RGB24, false); // 320 + 128 + 320
            RenderTexture.active = result_tex;
        }
        else
        {
            rect_result = new Rect(0, 0, PublicOutputInfo.pregaussian_tex_size, PublicOutputInfo.pregaussian_tex_size);
            heightmaps[x_index * z_patch_num + z_index] = new Texture2D(PublicOutputInfo.pregaussian_tex_size, PublicOutputInfo.pregaussian_tex_size, TextureFormat.RGB24, false); // 320 + 128 + 320
            RenderTexture.active = pregaussian_tex;
        }
        heightmaps[x_index * z_patch_num + z_index].ReadPixels(rect_result, 0, 0);
        heightmaps[x_index * z_patch_num + z_index].wrapMode = TextureWrapMode.Clamp;
        heightmaps[x_index * z_patch_num + z_index].Apply();
        RenderTexture.active = null; // added to avoid errors
        // ===========================================================================================================

        // =================================== Gaussian Filter =======================================================
        //RenderTexture result_tex = new RenderTexture(PublicOutputInfo.tex_size, PublicOutputInfo.tex_size, 24);
        //result_tex.enableRandomWrite = true;
        //result_tex.Create();
        if (use_gaussian)
        {
            //int gaussianfilter_kernel_handler = compute_shader.FindKernel("GaussianFilter");
            //compute_shader.SetTexture(gaussianfilter_kernel_handler, "input", heightmaps[x_index * z_patch_num + z_index]);
            //compute_shader.SetBuffer(gaussianfilter_kernel_handler, "heights_input", height_gaussian_buffer[x_index * z_patch_num + z_index]);
            //compute_shader.SetTexture(gaussianfilter_kernel_handler, "Result", result_tex);
            //compute_shader.SetFloat("resolution", 1.0f); // patch_length / tex_length
            //compute_shader.SetInt("gaussian_m", PublicOutputInfo.gaussian_m); // gaussian filter M
            //compute_shader.SetInt("height_buffer_row_size", PublicOutputInfo.height_buffer_row_size);
            //compute_shader.SetInt("height_with_gaussian_buffer_row_size", PublicOutputInfo.height_with_gaussian_buffer_row_size);
            //height_buffer[x_index * z_patch_num + z_index] = new ComputeBuffer(PublicOutputInfo.height_buffer_row_size * PublicOutputInfo.height_buffer_row_size, 4);
            //heights[x_index * z_patch_num + z_index] = new float[PublicOutputInfo.height_buffer_row_size * PublicOutputInfo.height_buffer_row_size];
            //height_buffer[x_index * z_patch_num + z_index].SetData(heights[x_index * z_patch_num + z_index]);
            //compute_shader.SetBuffer(gaussianfilter_kernel_handler, "heights", height_buffer[x_index * z_patch_num + z_index]);
            //compute_shader.SetBuffer(gaussianfilter_kernel_handler, "progress", progress_buffer[x_index * z_patch_num + z_index]);
            //compute_shader.Dispatch(gaussianfilter_kernel_handler, Mathf.CeilToInt((float)PublicOutputInfo.tex_size / 8), Mathf.CeilToInt((float)PublicOutputInfo.tex_size / 8), 1);
            //Graphics.WaitOnAsyncGraphicsFence(fence);
        }
        //Debug.Log(x_index + ", " + z_index + " fence " + 2);
        // ===========================================================================================================

        // ================================= rendering texture2D =====================================================
        if (use_gaussian)
        {
            //rect_result = new Rect(0, 0, PublicOutputInfo.tex_size, PublicOutputInfo.tex_size);
            //heightmaps[x_index * z_patch_num + z_index] = new Texture2D(PublicOutputInfo.tex_size, PublicOutputInfo.tex_size, TextureFormat.RGB24, false); // 320 + 128 + 320
            //RenderTexture.active = result_tex;
            //heightmaps[x_index * z_patch_num + z_index].ReadPixels(rect_result, 0, 0);
            //heightmaps[x_index * z_patch_num + z_index].wrapMode = TextureWrapMode.Clamp;
            //heightmaps[x_index * z_patch_num + z_index].Apply();
            ////Texture2D heightmap_gaussian = new Texture2D(PublicOutputInfo.tex_size, PublicOutputInfo.tex_size, TextureFormat.RGB24, false); // 320 + 128 + 320
            ////RenderTexture.active = result_tex;
            ////heightmap_gaussian.ReadPixels(rect_result, 0, 0);
            ////heightmap_gaussian.wrapMode = TextureWrapMode.Clamp;
            ////heightmap_gaussian.Apply();
            //RenderTexture.active = null; // added to avoid errors
        }
        // ===========================================================================================================

        // ===================================== Constraints =========================================================
        //RenderTexture constraints_tex = new RenderTexture(PublicOutputInfo.tex_size, PublicOutputInfo.tex_size, 24);
        //constraints_tex.enableRandomWrite = true;
        //constraints_tex.Create();
        //int constraints_kernel_handler = compute_shader.FindKernel("Constraints");
        //compute_shader.SetTexture(constraints_kernel_handler, "input", heightmap_gaussian);
        //compute_shader.SetTexture(constraints_kernel_handler, "Result", result_tex);
        //compute_shader.SetTexture(constraints_kernel_handler, "Constraintsmap", constraints_tex);
        //compute_shader.SetFloat("x", min_x + x_index * PublicOutputInfo.patch_length);
        //compute_shader.SetFloat("z", min_z + z_index * PublicOutputInfo.patch_length);
        //compute_shader.SetFloat("resolution", PublicOutputInfo.patch_length / (PublicOutputInfo.tex_size - 1)); // patch_length / tex_length
        //compute_shader.SetVectorArray("road_constraints", area_road_constraints);
        //compute_shader.SetInt("road_constraints_count", area_road_constraints.Length);
        //compute_shader.SetVectorArray("building_constraints", area_building_constraints);
        //compute_shader.SetInts("building_constraints_points_count", area_building_constraints_points_count);
        //compute_shader.SetInt("building_constraints_count", area_building_constraints_points_count.Length);
        //progress_buffer[x_index * z_patch_num + z_index] = new ComputeBuffer(1, 4);
        //int[] progress = new int[] { 0 };
        //progress_buffer[x_index * z_patch_num + z_index].SetData(progress);
        //compute_shader.SetBuffer(constraints_kernel_handler, "progress", progress_buffer[x_index * z_patch_num + z_index]);
        //compute_shader.Dispatch(constraints_kernel_handler, Mathf.CeilToInt((float)PublicOutputInfo.tex_size / 8), Mathf.CeilToInt((float)PublicOutputInfo.tex_size / 8), 1);
        //Graphics.WaitOnAsyncGraphicsFence(fence);
        //Debug.Log(x_index + ", " + z_index + " fence " + 3);
        // ===========================================================================================================

        // ================================= rendering texture2D =====================================================
        //heightmaps[x_index * z_patch_num + z_index] = new Texture2D(PublicOutputInfo.tex_size, PublicOutputInfo.tex_size, TextureFormat.RGB24, false); // 320 + 128 + 320
        //RenderTexture.active = result_tex;
        //// Read pixels
        //heightmaps[x_index * z_patch_num + z_index].ReadPixels(rect_result, 0, 0);
        //heightmaps[x_index * z_patch_num + z_index].wrapMode = TextureWrapMode.Clamp;
        //heightmaps[x_index * z_patch_num + z_index].Apply();
        //RenderTexture.active = null; // added to avoid errors
        //constraintsmap[x_index * z_patch_num + z_index] = new Texture2D(PublicOutputInfo.tex_size, PublicOutputInfo.tex_size, TextureFormat.RGB24, false); // 320 + 128 + 320
        //RenderTexture.active = constraints_tex;
        //// Read pixels
        //constraintsmap[x_index * z_patch_num + z_index].ReadPixels(rect_result, 0, 0);
        //constraintsmap[x_index * z_patch_num + z_index].wrapMode = TextureWrapMode.Clamp;
        //constraintsmap[x_index * z_patch_num + z_index].Apply();
        //RenderTexture.active = null; // added to avoid errors
        // ===========================================================================================================

        // ================================= setting GameObject ======================================================
        if (!gameobject_generated[x_index * z_patch_num + z_index])
        {
            terrains[x_index * z_patch_num + z_index] = new GameObject("terrain_peice_" + x_index + "_" + z_index);
            MeshRenderer mr = terrains[x_index * z_patch_num + z_index].AddComponent<MeshRenderer>();
            terrains[x_index * z_patch_num + z_index].AddComponent<MeshFilter>();
            //mr.material = new Material(terrain_mat);
            mr.material.SetTexture("_MainTex", heightmaps[x_index * z_patch_num + z_index]);
            //mr.material.SetTexture("_MainTex", heightmap_pregaussian);
            //mr.material.SetTexture("_MainTex", heightmap_gaussian);
            //mr.material.SetTexture("_MainTex", constraintsmap[x_index * z_patch_num + z_index]);
            //mr.material.SetTexture("_MainTex", main_tex);
            terrains[x_index * z_patch_num + z_index].AddComponent<TerrainView>();
            terrains[x_index * z_patch_num + z_index].GetComponent<TerrainView>().x_index = x_index;
            terrains[x_index * z_patch_num + z_index].GetComponent<TerrainView>().z_index = z_index;
            terrains[x_index * z_patch_num + z_index].GetComponent<TerrainView>().x_piece_num = x_piece_num;
            terrains[x_index * z_patch_num + z_index].GetComponent<TerrainView>().z_piece_num = z_piece_num;
            terrains[x_index * z_patch_num + z_index].GetComponent<TerrainView>().need_mse = need_mse;
            //terrains[x_index * z_patch_num + z_index].GetComponent<TerrainView>().use_gaussian = use_gaussian;
            terrains[x_index * z_patch_num + z_index].GetComponent<TerrainView>().origin_terrain = origin_terrain;
            terrains[x_index * z_patch_num + z_index].transform.parent = terrain_manager.transform;
            gameobject_generated[x_index * z_patch_num + z_index] = true;
        }
        // ===========================================================================================================

        // =================================== setting Camera ========================================================
        if (!constraintsmap_generated[x_index * z_patch_num + z_index])
        {
            GameObject constraints_camera = new GameObject("ConstraintsCamera_" + x_index + "_" + z_index);
            Camera constraints_cam = constraints_camera.AddComponent<Camera>();
            constraints_cam.name = x_index + "_" + z_index;
            float center_x = min_x + (2 * x_index + 1) * PublicOutputInfo.patch_length / 2;
            float center_z = min_z + (2 * z_index + 1) * PublicOutputInfo.patch_length / 2;
            float center_y = min_y + getDEMHeight(center_x, center_z, true);
            if (terrain_mode == 0)
                center_y = min_y + heights[x_index * z_patch_num + z_index][(x_patch_num / 2) * PublicOutputInfo.height_buffer_row_size + (z_piece_num / 2)];
            constraints_camera.transform.position = new Vector3(center_x, center_y, center_z) + Vector3.up * 100;
            constraints_cam.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            constraints_camera.transform.parent = constraints_camera_manager.transform;
            constraints_cam.orthographic = true;
            constraints_cam.orthographicSize = PublicOutputInfo.tex_size / 2.0f;
            constraints_cam.cullingMask = LayerMask.GetMask("Constraints");
            constraints_texs[x_index * z_patch_num + z_index] = new RenderTexture(PublicOutputInfo.tex_size, PublicOutputInfo.tex_size, 24);
            constraints_cam.targetTexture = constraints_texs[x_index * z_patch_num + z_index];
            constraints_cam.clearFlags = CameraClearFlags.SolidColor;
            constraints_cam.backgroundColor = Color.white;
            constraints_cam.Render();
        }
        // ===========================================================================================================

        // ================================= rendering texture2D =====================================================
        if (!constraintsmap_generated[x_index * z_patch_num + z_index])
        {
            constraintsmap[x_index * z_patch_num + z_index] = new Texture2D(PublicOutputInfo.tex_size, PublicOutputInfo.tex_size, TextureFormat.RGB24, false);
            RenderTexture.active = constraints_texs[x_index * z_patch_num + z_index];
            // Read pixels
            constraintsmap[x_index * z_patch_num + z_index].ReadPixels(rect_result, 0, 0);
            constraintsmap[x_index * z_patch_num + z_index].wrapMode = TextureWrapMode.Clamp;
            constraintsmap[x_index * z_patch_num + z_index].Apply();
            RenderTexture.active = null; // added to avoid errors
                                         // Upload texture data to the GPU, so the GPU renders the updated texture
            constraintsmap_generated[x_index * z_patch_num + z_index] = true;
        }
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
        height_buffer[x_index * z_patch_num + z_index].GetData(heights[x_index * z_patch_num + z_index]);
        height_buffer[x_index * z_patch_num + z_index].Release();

        Mesh mesh = new Mesh();
        float[,,] terrain_points = new float[x_piece_num + 1, (z_piece_num + 1), 3];
        Vector3[] vertice = new Vector3[(x_piece_num + 1) * (z_piece_num + 1)];
        Vector2[] uv = new Vector2[(x_piece_num + 1) * (z_piece_num + 1)];
        int[] indices = new int[6 * x_piece_num * z_piece_num];
        int indices_index = 0;
        float center_x = min_x + (2 * x_index + 1) * PublicOutputInfo.patch_length / 2;
        float center_z = min_z + (2 * z_index + 1) * PublicOutputInfo.patch_length / 2;
        float center_y = min_y + getDEMHeight(center_x, center_z, true);
        if (terrain_mode == 0)
            //center_y = min_y + getHeightFromComputeShader(center_x, center_z);
            //center_y = min_y + heights[x_index * z_patch_num + z_index][(x_patch_num / 2) * PublicOutputInfo.height_buffer_row_size + (z_piece_num / 2)];
            center_y = min_y + getHeightFromBufferAndConstraintsmap(x_index, z_index, x_piece_num / 2, z_piece_num / 2);

        Vector3 center = new Vector3(center_x, center_y, center_z);
        for (int i = 0; i <= x_piece_num; i++)
        {
            for (int j = 0; j <= z_piece_num; j++)
            {
                uv[i * (z_piece_num + 1) + j] = new Vector2((float)i / x_piece_num, (float)j / z_piece_num);
                terrain_points[i, j, 0] = min_x + x_index * PublicOutputInfo.patch_length + i * PublicOutputInfo.piece_length;
                terrain_points[i, j, 2] = min_z + z_index * PublicOutputInfo.patch_length + j * PublicOutputInfo.piece_length;
                if (terrain_mode == 1)
                    terrain_points[i, j, 1] = min_y + getDEMHeight(terrain_points[i, j, 0], terrain_points[i, j, 2], true);
                else
                    //terrain_points[i, j, 1] = min_y + getHeightFromTexBilinear(x_index, z_index, uv[i * (z_piece_num + 1) + j].x, uv[i * (z_piece_num + 1) + j].y);
                    terrain_points[i, j, 1] = min_y + getHeightFromTex(x_index, z_index, i, j);
                    //terrain_points[i, j, 1] = min_y + getHeightFromBufferAndConstraintsmap(x_index, z_index, i, j);
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
        MeshFilter mf = terrains[x_index * z_patch_num + z_index].GetComponent<MeshFilter>();
        mf.mesh = mesh;
        //mr.material = new Material(heightmap_mat);
        //mr.material.SetTexture("_MainTex", main_tex);
        //mr.material.SetTexture("_HeightmapTex", heightmaps[x_index * z_patch_num + z_index]);

        terrains[x_index * z_patch_num + z_index].transform.position = center;
        yield return null;
    }

    static public float calcMSE(Terrain terrain, int x_index, int z_index, int x_piece_num, int z_piece_num)
    {
        float mse = 0.0f;
        Vector3[] vertices = terrains[x_index * z_patch_num + z_index].GetComponent<MeshFilter>().mesh.vertices;
        TerrainData terrainData = terrain.terrainData;
        for (int i = 0; i <= x_piece_num; i++)
        {
            for (int j = 0; j <= z_piece_num; j++)
            {
                mse += Mathf.Pow(vertices[i * (z_piece_num + 1) + j].y - terrainData.GetHeight(i * Mathf.FloorToInt(PublicOutputInfo.piece_length), j * Mathf.FloorToInt(PublicOutputInfo.piece_length)), 2);
            }
        }
        mse /= (x_piece_num + 1) * (z_piece_num + 1);
        return mse;
    }

    static float getHeightFromTex(int x_index, int z_index, int peice_x_index, int peice_z_index)
    {
        Color raw = heightmaps[x_index * z_patch_num + z_index].GetPixel(peice_x_index * Mathf.FloorToInt(PublicOutputInfo.piece_length), peice_z_index * Mathf.FloorToInt(PublicOutputInfo.piece_length));
        return raw.g * 64 * 64 + raw.b * 64 + raw.a;
        //return raw.g;
        //int x = Mathf.FloorToInt(u * (PublicOutputInfo.tex_size - 1));
        //int z = Mathf.FloorToInt(v * (PublicOutputInfo.tex_size - 1));
        //Debug.Log(x.ToString() + ", " + z.ToString());
        //return heights[x_index * z_patch_num + z_index][x * PublicOutputInfo.tex_size + z];
    }

    static float getHeightFromTexBilinear(int x_index, int z_index, float u, float v)
    {
        Color raw = heightmaps[x_index * z_patch_num + z_index].GetPixelBilinear(u, v);
        return raw.g * 64 * 64 + raw.b * 64 + raw.a;
        //return raw.g;
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
        float u = (x - (min_x + x_index * PublicOutputInfo.patch_length)) / PublicOutputInfo.patch_length;
        float v = (z - (min_z + z_index * PublicOutputInfo.patch_length)) / PublicOutputInfo.patch_length;
        return getHeightFromTexBilinear(x_index, z_index, u, v);
    }

    static float getHeightFromBufferAndConstraintsmap(int x_index, int z_index, int peice_x_index, int peice_z_index)
    {
        Color raw = constraintsmap[x_index * z_patch_num + z_index].GetPixel(peice_x_index * (int)PublicOutputInfo.piece_length, peice_z_index * (int)PublicOutputInfo.piece_length);
        int building_id = Mathf.FloorToInt(raw.r * 64 * 64 * 64 + raw.g * 64 * 64 + raw.b * 64 + 0.5f);
        if (building_id >= 0 && building_id < building_constraints_points_count.Length)
            return building_constraints[building_constraints_accumulate_index[building_id]].y;
        else // 20887
            return heights[x_index * z_patch_num + z_index][(peice_x_index * (int)PublicOutputInfo.piece_length) * PublicOutputInfo.height_buffer_row_size + (peice_z_index * (int)PublicOutputInfo.piece_length)];
    }

    static public bool checkTerrainLoaded()
    {
        return is_queue_generate_patch_empty;
    }

    static void createBuildingPolygon(int building_id) // generate a house polygon
    {
        Vector3[] vertice = new Vector3[building_constraints_points_count[building_id]];
        Vector2[] vertex2D = new Vector2[vertice.Length];
        Vector3 total = new Vector3();
        // generate polygon vertex
        for (int index = 0; index < vertice.Length; index++)
        {
            Vector3 vertex = new Vector3(building_constraints[building_constraints_accumulate_index[building_id] + index].x, building_constraints[building_constraints_accumulate_index[building_id] + index].y, building_constraints[building_constraints_accumulate_index[building_id] + index].z);
            vertice[index] = vertex;
            vertex2D[index] = new Vector2(vertex.x, vertex.z);
            total += vertex;
        }
        Vector3 center = total / vertice.Length;
        for (int index = 0; index < vertice.Length; index++)
        {
            vertice[index] -= center;
            vertice[index] *= polygon_dilation;
        }

        // Use the triangulator to get indices for creating triangles
        Triangulator tr = new Triangulator(vertex2D);
        int[] indices = tr.Triangulate();

        //Assign data to mesh
        Mesh mesh = new Mesh();
        mesh.vertices = vertice;
        mesh.triangles = indices;

        //Recalculations
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.Optimize();

        //Name the mesh
        mesh.name = building_id.ToString();

        // create a gameobject to scene
        building_polygons[building_id] = new GameObject("BuildingPolygon_" + building_id.ToString());
        MeshFilter mf = building_polygons[building_id].AddComponent<MeshFilter>();
        MeshRenderer mr = building_polygons[building_id].AddComponent<MeshRenderer>();
        mf.mesh = mesh;
        mr.material = building_polygon_mat;
        mr.material.SetInt("polygon_ID", building_id);
        building_polygons[building_id].transform.position = center;
        building_polygons[building_id].transform.parent = building_polygons_manager.transform;
        building_polygons[building_id].layer = LayerMask.NameToLayer("Constraints");
    }

    static void endFrameRendering(Camera cam)
    {
        string[] index = cam.name.Split('_');
        int x_index, z_index;
        if (index.Length == 2 && int.TryParse(index[0], out x_index) && int.TryParse(index[1], out z_index) && !constraintsmap_generated[x_index * z_patch_num + z_index])
        {
            Debug.Log(x_index + ", " + z_index);
            Rect rect_result = new Rect(0, 0, PublicOutputInfo.tex_size, PublicOutputInfo.tex_size);
            constraintsmap[x_index * z_patch_num + z_index] = new Texture2D(PublicOutputInfo.tex_size, PublicOutputInfo.tex_size, TextureFormat.RGB24, false);
            RenderTexture.active = constraints_texs[x_index * z_patch_num + z_index];
            // Read pixels
            constraintsmap[x_index * z_patch_num + z_index].ReadPixels(rect_result, 0, 0);
            constraintsmap[x_index * z_patch_num + z_index].wrapMode = TextureWrapMode.Clamp;
            constraintsmap[x_index * z_patch_num + z_index].Apply();
            RenderTexture.active = null; // added to avoid errors
            terrains[x_index * z_patch_num + z_index].GetComponent<Renderer>().material.mainTexture = constraintsmap[x_index * z_patch_num + z_index];

            // Upload texture data to the GPU, so the GPU renders the updated texture
            constraintsmap_generated[x_index * z_patch_num + z_index] = true;
        }
    }

    static public IEnumerator generateClearPlane(int x_index, int z_index, int x_piece_num, int z_piece_num)
    {
        Mesh mesh = new Mesh();
        double[,,] terrain_points = new double[x_piece_num + 1, (z_piece_num + 1), 3];
        Vector3[] vertice = new Vector3[(x_piece_num + 1) * (z_piece_num + 1)];
        Vector2[] uv = new Vector2[(x_piece_num + 1) * (z_piece_num + 1)];
        int[] indices = new int[6 * x_piece_num * z_piece_num];
        int indices_index = 0;
        double center_x = (2 * x_index + 1) * QuadTreePatch.x_dem_interval / 2;
        double center_z = (2 * z_index + 1) * QuadTreePatch.z_dem_interval / 2;
        float center_y = 0;

        Vector3 center = new Vector3((float)center_x, center_y, (float)center_z);
        for (int i = 0; i <= x_piece_num; i++)
        {
            for (int j = 0; j <= z_piece_num; j++)
            {
                uv[i * (z_piece_num + 1) + j] = new Vector2((float)i / x_piece_num, (float)j / z_piece_num);
                terrain_points[i, j, 0] = x_index * QuadTreePatch.x_dem_interval + i * QuadTreePatch.x_dem_step;
                terrain_points[i, j, 1] = 0;
                terrain_points[i, j, 2] = z_index * QuadTreePatch.z_dem_interval + j * QuadTreePatch.z_dem_step;
                vertice[i * (z_piece_num + 1) + j] = new Vector3((float)terrain_points[i, j, 0], (float)terrain_points[i, j, 1], (float)terrain_points[i, j, 2]);
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
        MeshRenderer mr = terrain.AddComponent<MeshRenderer>();
        mr.material = terrain_mat;
        MeshFilter mf = terrain.AddComponent<MeshFilter>();
        mf.mesh = mesh;

        terrain.transform.position = new Vector3();
        yield return null;
    }

    static public (int x, int z) getIndex(float x, float z)
    {
        x += boundary_min_x + origin_x;
        z += boundary_min_z + origin_z;
        float longitude = (float)MercatorProjection.xToLon(x);
        float latitude = (float)MercatorProjection.yToLat(z);
        int lon_int = (int)Mathf.Floor(longitude);
        int lat_int = (int)Mathf.Floor(latitude);
        int resolution = 3601;
        int lon_row_low = (int)Mathf.Floor((longitude - lon_int) * (resolution - 1));
        int lat_row_low = (int)Mathf.Floor((latitude - lat_int) * (resolution - 1));
        return (lon_row_low, lat_row_low);
        //return (Mathf.FloorToInt(x / x_dem_interval) * x_dem_interval, Mathf.FloorToInt(z / z_dem_interval) * z_dem_interval);
    }

    static public (float x, float z) getN25E121Location() // default N25E121
    {
        return ((float)MercatorProjection.lonToX(121) - boundary_min_x - origin_x, (float)MercatorProjection.latToY(25) - boundary_min_z - origin_z);
    }

    static public void displayDEMPoints()
    {
        //double x_min = MercatorProjection.lonToX(121);
        //double z_min = MercatorProjection.latToY(25);
        //double x_max = MercatorProjection.lonToX(122);
        //double z_max = MercatorProjection.latToY(26);
        //Debug.Log($"{(x_max - x_min) / 3600} {(z_max - z_min) / 3600}");
        float x = 0 + boundary_min_x + origin_x;
        float z = 1 + boundary_min_z + origin_z;
        float longitude = (float)MercatorProjection.xToLon(x);
        float latitude = (float)MercatorProjection.yToLat(z);
        int lon_int = (int)Mathf.Floor(longitude);
        int lat_int = (int)Mathf.Floor(latitude);
        int resolution = 3601;
        int lon_row_low = (int)Mathf.Floor((longitude - lon_int) * (resolution - 1));
        int lat_row_low = (int)Mathf.Floor((latitude - lat_int) * (resolution - 1));
        float lon_step = 1.0f / (resolution - 1);
        float lat_step = 1.0f / (resolution - 1);
        int dem_extend = 40;
        Vector3[] dem_points = new Vector3[(dem_extend + 1) * (dem_extend + 1)];
        List<EarthCoord> all_coords = new List<EarthCoord>();
        for (int i = 0; i <= dem_extend; i++)
        {
            for (int j = 0; j <= dem_extend; j++)
            {
                all_coords.Add(new EarthCoord(lon_int + (i + lon_row_low) * lon_step, lat_int + (j + lat_row_low) * lat_step));
            }
        }
        float[] ys = HgtReader.getElevations(all_coords).ToArray();
        for (int i = 0; i <= dem_extend; i++)
        {
            for (int j = 0; j <= dem_extend; j++)
            {
                dem_points[i * (dem_extend + 1) + j] = new Vector3((float)(MercatorProjection.lonToX(all_coords[i * (dem_extend + 1) + j].longitude) - boundary_min_x - origin_x), ys[i * (dem_extend + 1) + j], (float)(MercatorProjection.latToY(all_coords[i * (dem_extend + 1) + j].latitude) - boundary_min_z - origin_z));
            }
        }
        GameObject dem_points_manager = new GameObject("DEM Points");
        showPoint(dem_points, "DEM Points", dem_points_manager.transform, feature_ball_prefab, 4.0f);
    }

    static public (double lon, double lat) toLonAndLat (float x, float z)
    {
        x += boundary_min_x + origin_x;
        z += boundary_min_z + origin_z;
        return (MercatorProjection.xToLon(x), MercatorProjection.yToLat(z));
    }

    static public (double x, double z) toXAndZ(double lon, double lat)
    {
        return (MercatorProjection.lonToX(lon) - boundary_min_x - origin_x, MercatorProjection.latToY(lat) - boundary_min_z - origin_z);
    }

    static public (double lon, double lat) mapToDEM(int x_index, int z_index) // default
    {
        int resolution = 3601;
        double step = 1.0 / (resolution - 1);
        return (121 + x_index * step, 25 + z_index * step);
    }

    static public (double lon, double lat) mapToDEMF(float u, float v)
    {
        int resolution = 3601;
        double step = 1.0 / (resolution - 1);
        return (121 + u * step, 25 + v * step);
    }

    static public (double x, double z) demToXAndZ(int x_index, int z_index)
    {
        var dem_coord = mapToDEM(x_index, z_index);
        return toXAndZ(dem_coord.lon, dem_coord.lat);
    }

    static public (double x, double z) demFToXAndZ(float u, float v)
    {
        var dem_coord = mapToDEMF(u, v);
        return toXAndZ(dem_coord.lon, dem_coord.lat);
    }

    static public (float x, float z) getXAndZInDEM(float x, float z)
    {
        var lon_lat = toLonAndLat(x, z);
        int resolution = 3601;
        //Debug.Log($"{lon_lat.lon} {lon_lat.lat}");
        return ((float)((lon_lat.lon - 121) * (resolution - 1)), (float)((lon_lat.lat - 25) * (resolution - 1)));
    }

    static public void meow(string s) //-8.526772 34.41344
    {
        Debug.LogWarning(s);
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