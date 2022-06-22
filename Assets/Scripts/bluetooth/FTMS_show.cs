using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class FTMS_show : MonoBehaviour
{
    // Start is called before the first frame update
    public bool connect = false;
    public IndoorBike_FTMS connector;
    Text text;
    void Start()
    {
        text = GetComponent<Text>();
        connector = new IndoorBike_FTMS(this);
        if (connect) {
            StartCoroutine(connector.connect());
        }
    }

    // Update is called once per frame
    void Update()
    {
        connector.Update();
        text.text = connector.output;
    }
    private void OnApplicationQuit()
    {
        connector.quit();
    }
}
