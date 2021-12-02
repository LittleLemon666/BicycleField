using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using PathCreation;

public class RoadManager : MonoBehaviour
{
    private int current_segment = 2;

    private StreamReader reader;

    public string file_name;
    public PathCreator path_creator;

    Vector3 last_segment = new Vector3(0,0,0);

    private void Start()
    {
        reader = new StreamReader(Application.dataPath + "/StreamingAssets/" + file_name);

        //remove first default segment
        removeEarliestRoad();

        while (path_creator.bezierPath.NumSegments < Info.MAX_LOADED_SEGMENT)
        {
            getAndSetNextSegment();
        }

        //remove second default segment
        getAndSetNextSegment();
    }

    // Update is called once per frame
    void Update()
    {
        if (Info.MAX_LOADED_SEGMENT - current_segment <= Info.PRELOAD_SEGMENT)
        {
            getAndSetNextSegment();
        }

        path_creator.bezierPath = path_creator.bezierPath; //force update
    }

    private void getAndSetNextSegment()
    {
        if (getNextSegment(out string str_point))
        {
            Vector3 vec3_point = Functions.StrToVec3(str_point);

            if ((vec3_point - last_segment).magnitude < 100)
            {
                getAndSetNextSegment();
                return;
            }
            else 
            {
                last_segment = vec3_point;
            }

            spawnAnchorCheckpoint(vec3_point);

            generateRoad(vec3_point);
            if (path_creator.bezierPath.NumSegments > Info.MAX_LOADED_SEGMENT) removeEarliestRoad();
        }
    }

    private bool getNextSegment(out string point_data)
    {
        point_data = reader.ReadLine();
        return point_data != null;
    }

    private void generateRoad(Vector3 road)
    {
        path_creator.bezierPath.AddSegmentToEnd(road);
    }

    private void removeEarliestRoad()
    {
        path_creator.bezierPath.DeleteSegment(0);
        current_segment--;
    }

    private void spawnAnchorCheckpoint(Vector3 position)
    {
        GameObject prefab = new GameObject();
        prefab.transform.position = position;
        prefab.AddComponent<SphereCollider>();
        prefab.GetComponent<SphereCollider>().isTrigger = true;
        prefab.GetComponent<SphereCollider>().transform.localScale *= Info.CHECKPOINT_SIZE;
        prefab.AddComponent<AnchorCheckpoint>();

    }

    public void incrementCurrentSegment()
    {
        current_segment++;
    }
}
