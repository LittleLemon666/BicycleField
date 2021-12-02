using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PathCreation;
using PathCreation.Examples;

public class OSMRoadRender : MonoBehaviour
{
    bool is_initial = false;
    public bool editor_mode = true;
    public string osm3d_file_name = "YangJin3D.osm";
    public GameObject cam;
    public GameObject view_instance;
    public OSMReader osm_reader;
    HierarchyControl hierarchy_c;
    public Material roads_polygon_mat;
    public Dictionary<string, List<GameObject>> pathes_objects;
    //GameObject roads_manager;
    public GameObject road_name_prefab;
    GameObject road_textes_manager;

    // Start is called before the first frame update
    void Start()
    {
        osm_reader = new OSMReader();
        //roads_manager = new GameObject("roads_manager");
        road_textes_manager = new GameObject("road_textes_manager");
    }

    // Update is called once per frame
    void Update()
    {
        if (!osm_reader.read_finish)
        {
            osm_reader.readOSM(Application.streamingAssetsPath + "//" + osm3d_file_name, false, Application.streamingAssetsPath + "//" + osm3d_file_name);
        }
        else if (!is_initial && osm_reader.read_finish)
        {
            is_initial = true;
            hierarchy_c = new HierarchyControl();
            // each hierarchy manage range is 200m x 200m
            hierarchy_c.setup((int)(osm_reader.boundary_max.x - osm_reader.boundary_min.x) / 200, (int)(osm_reader.boundary_max.y - osm_reader.boundary_min.y) / 200, osm_reader.boundary_max.x, osm_reader.boundary_max.y);
            
            pathes_objects = new Dictionary<string, List<GameObject>>();
            int road_index = 0;
            // process non-merged road
            for (road_index = 0; road_index < osm_reader.pathes.Count; road_index++)
            {
                if (osm_reader.pathes[road_index].is_merged)
                    break;

                //if (osm_reader.pathes[road_index].highway != Highway.Primary && osm_reader.pathes[road_index].highway != Highway.Secondary && osm_reader.pathes[road_index].highway != Highway.Trunk && osm_reader.pathes[road_index].highway != Highway.Unclassified)
                //    continue;

                // roads
                createRoadPolygons(osm_reader.pathes[road_index]);
            }

            // new way (merged)
            for (; road_index < osm_reader.pathes.Count; road_index++)
            {
                createRoadPolygons(osm_reader.pathes[road_index]);
            }

            setCam();
        }
    }

    void createRoadPolygons(Way path) // generate pieces of road
    {
        List<Vector3> path_points = osm_reader.toPositions(path.ref_node);

        List<GameObject> path_objects = new List<GameObject>();
        //Transform[] trans = new Transform[path_points.Count];
        //GameObject road_obj = new GameObject(path.id);

        //PathCreator pc = road_obj.AddComponent<PathCreator>();
        //pc.bezierPath = new BezierPath(trans, false, PathSpace.xyz);
        ////all_pc.Add(pc);
        //RoadMeshCreator rm = road_obj.AddComponent<RoadMeshCreator>();
        //rm.pathCreator = pc;
        //rm.roadWidth = 6.0f;
        //rm.flattenSurface = true;
        //rm.roadMaterial = roads_polygon_mat;
        //rm.undersideMaterial = roads_polygon_mat;
        //rm.TriggerUpdate();

        ////smooth road
        //GameObject instance_s = Instantiate(view_instance);
        //instance_s.GetComponent<ViewInstance>().instance = road_obj;
        //instance_s.GetComponent<ViewInstance>().setRoad(path.id, path_points, cam, GetComponent<RoadIntegration>());
        //instance_s.GetComponent<ViewInstance>().setup(false);
        //instance_s.AddComponent<MeshCollider>();
        ////instance_s.GetComponent<MeshCollider>().sharedMesh = road_obj.mesh;
        //instance_s.transform.parent = roads_manager.transform;

        GameObject road_manager = new GameObject(path.id);
        List<int> belong_to_hier_x = new List<int>();
        List<int> belong_to_hier_y = new List<int>();
        belong_to_hier_x.Clear();
        belong_to_hier_y.Clear();
        int belong_x = 0;
        int belong_y = 0;

        Vector3[][] vertex = new Vector3[(path_points.Count - 1) * 2][];
        Vector3 f = new Vector3();
        Vector3 up = new Vector3();
        Vector3 right = new Vector3();
        Vector3[] road_point = new Vector3[path_points.Count * 2];
        for (int road_point_index = 0; road_point_index < path_points.Count - 1; road_point_index++)
        {
            f = path_points[road_point_index + 1] - path_points[road_point_index];
            up = new Vector3(0, 1, 0);
            right = Vector3.Cross(f, up).normalized * path.road_width;
            road_point[road_point_index * 2] = path_points[road_point_index] - right + path.layer * up * 10;
            road_point[road_point_index * 2 + 1] = path_points[road_point_index] + right + path.layer * up * 10;
        }
        road_point[(path_points.Count - 1) * 2] = path_points[path_points.Count - 1] - right + path.layer * up * 10;
        road_point[(path_points.Count - 1) * 2 + 1] = path_points[path_points.Count - 1] + right + path.layer * up * 10;

        for (int road_point_index = 0; road_point_index < path_points.Count - 1; road_point_index++)
        {
            vertex[road_point_index * 2] = new Vector3[3];
            vertex[road_point_index * 2][0] = road_point[road_point_index * 2];
            vertex[road_point_index * 2][1] = road_point[road_point_index * 2 + 1];
            vertex[road_point_index * 2][2] = road_point[road_point_index * 2 + 3];

            vertex[road_point_index * 2 + 1] = new Vector3[3];
            vertex[road_point_index * 2 + 1][0] = road_point[road_point_index * 2];
            vertex[road_point_index * 2 + 1][1] = road_point[road_point_index * 2 + 3];
            vertex[road_point_index * 2 + 1][2] = road_point[road_point_index * 2 + 2];
        }

        for (int piece_index = 0; piece_index < vertex.Length; piece_index++)
        {
            belong_to_hier_x.Clear();
            belong_to_hier_y.Clear();

            Mesh mesh = new Mesh();

            for (int vertex_indice = 0; vertex_indice < 3; vertex_indice++)
            {
                hierarchy_c.calcLocation(vertex[piece_index][vertex_indice].x, vertex[piece_index][vertex_indice].z, ref belong_x, ref belong_y);
                belong_to_hier_x.Add(belong_x);
                belong_to_hier_y.Add(belong_y);
            }

            int[] indice = new int[3];
            indice[0] = 0;
            indice[1] = 1;
            indice[2] = 2;

            //Assign data to mesh
            mesh.vertices = vertex[piece_index];
            mesh.triangles = indice;

            //Recalculations
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.Optimize();

            //Name the mesh
            mesh.name = path.id;


            GameObject road_peice = new GameObject();
            road_peice.name = "instance_" + path.id + "_" + piece_index;
            MeshFilter mf = road_peice.AddComponent<MeshFilter>();
            MeshRenderer mr = road_peice.AddComponent<MeshRenderer>();
            mf.mesh = mesh;
            mr.material = roads_polygon_mat;
            road_peice.transform.parent = road_manager.transform;

            //road_peice.AddComponent<ViewInstance>();
            //road_peice.GetComponent<ViewInstance>().cam = cam;
            //road_peice.GetComponent<ViewInstance>().points = vertex[piece_index];
            //road_peice.GetComponent<ViewInstance>().instance = road_peice;
            //road_peice.GetComponent<ViewInstance>().setup(false, false);
            //road_peice.transform.parent = road_manager.transform;
            GameObject instance_p = Instantiate(view_instance);
            instance_p.GetComponent<ViewInstance>().instance = road_peice;
            instance_p.GetComponent<ViewInstance>().setRoad(path.id, vertex[piece_index], cam, GetComponent<RoadIntegration>());
            instance_p.GetComponent<ViewInstance>().setup(false);
            instance_p.AddComponent<MeshCollider>();

            //instance_p.GetComponent<MeshCollider>().sharedMesh = mesh;
            instance_p.transform.parent = road_manager.transform;
            instance_p.name = "road_" + path.id + "_" + piece_index;

            for (int belong_index = 0; belong_index < belong_to_hier_x.Count; belong_index++)
            {
                hierarchy_c.heirarchy_master[belong_to_hier_x[belong_index], belong_to_hier_y[belong_index]].objects.Add(instance_p);
            }

            path_objects.Add(instance_p);
        }

        pathes_objects.Add(path.id, path_objects);

        // show text on the road
        Vector3 text_center = path_points[path_points.Count / 2] + new Vector3(0,10,0);
        GameObject road_name = Instantiate(road_name_prefab);
        road_name.GetComponent<TMPro.TextMeshPro>().text = path.name;
        road_name.GetComponent<TMPro.TextMeshPro>().rectTransform.position = text_center;
        road_name.transform.parent = road_textes_manager.transform;
    }

    void setCam()
    {
        cam.transform.position = osm_reader.points_lib["45263678_226830312+0"].position + new Vector3(0, 800.0f, 0);
        cam.transform.rotation = Quaternion.Euler(90,0,0);
    }
}