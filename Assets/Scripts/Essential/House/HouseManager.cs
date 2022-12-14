using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HouseManager : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        HouseGenerator.building_manager = new GameObject("BuildingManager");
        HouseGenerator.init();
    }

    // Update is called once per frame
    void Update()
    {
        if (TerrainGenerator.is_initial && HouseGenerator.queue_segment_id.Count > 0)
        {
            StartCoroutine(HouseGenerator.generateHouse(HouseGenerator.queue_segment_id.Dequeue(), HouseGenerator.queue_house_id.Dequeue(), HouseGenerator.queue_info.Dequeue()));
        }
    }
}