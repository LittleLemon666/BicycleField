using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using ble_api1;

public class IndoorBike_FTMS
{
    public bool want_connect = true;
    Dictionary<string, Dictionary<string, string>> devices = new Dictionary<string, Dictionary<string, string>>();
    string selectedDeviceId = "";
    string selectedServiceId = "";
    string selectedCharacteristicId = "";

    bool isSubscribed = false;

    public string output;
    public float speed; public bool has_speed = false;
    public float average_speed; public bool has_average_speed = false;
    public float rpm; public bool has_rpm = false;
    public float average_rpm; public bool has_average_rpm = false;
    public float distance; public bool has_distance = false;
    public float resistance; public bool has_resistance = false;
    public float power; public bool has_power = false;
    public float average_power; public bool has_average_power = false;
    public float expended_energy; public bool has_expended_energy = false;

    MonoBehaviour mono;
    public IndoorBike_FTMS(MonoBehaviour _mono) {
        mono = _mono;
    }

    // Start is called before the first frame update
    public IEnumerator connect()
    {
        if (!want_connect) yield break;

        quit();

        yield return mono.StartCoroutine(connect_device());
        if (selectedDeviceId.Length == 0) yield break;


        Debug.Log("connecting device finish");

        //BleApi.StopDeviceScan();

        yield return mono.StartCoroutine(connect_service());
        if (selectedServiceId.Length == 0) yield break;

        yield return mono.StartCoroutine(connect_characteristic());
        if (selectedCharacteristicId.Length == 0) yield break;


        subscribe();
    }

    IEnumerator connect_device()
    {
        Debug.Log("connecting device...");
        
        BleApi.StartDeviceScan();
        //BleApi.StartAdvertisementScan();


        //yield break;
        BleApi.ScanStatus status = BleApi.ScanStatus.AVAILABLE;


        BleApi.DeviceUpdate device_res = new BleApi.DeviceUpdate();
        int count = 0;
        do
        {
            status = BleApi.PollDevice(ref device_res, false);
            //Debug.Log(count++);
            if (status == BleApi.ScanStatus.AVAILABLE)
            {
                if (!devices.ContainsKey(device_res.id))
                    devices[device_res.id] = new Dictionary<string, string>() {
                            { "name", "" },
                            { "isConnectable", "False" }
                        };
                if (device_res.nameUpdated)
                    devices[device_res.id]["name"] = device_res.name;
                if (device_res.isConnectableUpdated)
                    devices[device_res.id]["isConnectable"] = device_res.isConnectable.ToString();
                // consider only devices which have a name and which are connectable
                if (devices[device_res.id]["name"] == "APXPRO 46080" && devices[device_res.id]["isConnectable"] == "True")
                {
                    //BleApi.Connect(device_res.id);
                    selectedDeviceId = device_res.id;
                    break;
                }
            }
            else if (status == BleApi.ScanStatus.FINISHED)
            {

                if (selectedDeviceId.Length == 0)
                {
                    Debug.LogError("device APXPRO 46080 not found!");
                }
            }
            yield return 0;
        } while (status == BleApi.ScanStatus.AVAILABLE || status == BleApi.ScanStatus.PROCESSING);
    }

    IEnumerator connect_service()
    {
        Debug.Log("connecting service...");
        BleApi.ScanServices(selectedDeviceId);
        BleApi.ScanStatus status;
        BleApi.Service service_res = new BleApi.Service();
        do
        {
            status = BleApi.PollService(out service_res, false);
            Debug.Log(service_res.uuid);
            if (status == BleApi.ScanStatus.AVAILABLE)
            {
                Debug.Log(service_res.uuid);
                if (service_res.uuid == "{00001826-0000-1000-8000-00805f9b34fb}")
                {
                    selectedServiceId = service_res.uuid;
                    break;
                }
            }
            else if (status == BleApi.ScanStatus.FINISHED)
            {
                if (selectedServiceId.Length == 0)
                {
                    Debug.LogError("service {00001826-0000-1000-8000-00805f9b34fb} not found!");
                }
            }
            yield return 0;
        } while (status == BleApi.ScanStatus.AVAILABLE || status == BleApi.ScanStatus.PROCESSING);
    }

    IEnumerator connect_characteristic()
    {
        Debug.Log("connecting characteristic...");
        BleApi.ScanCharacteristics(selectedDeviceId, selectedServiceId);
        BleApi.ScanStatus status;
        BleApi.Characteristic characteristics_res = new BleApi.Characteristic();

        do
        {
            status = BleApi.PollCharacteristic(out characteristics_res, false);
            if (status == BleApi.ScanStatus.AVAILABLE)
            {
                if (characteristics_res.uuid == "{00002ad2-0000-1000-8000-00805f9b34fb}")
                {
                    selectedCharacteristicId = characteristics_res.uuid;
                    break;
                }
            }
            else if (status == BleApi.ScanStatus.FINISHED)
            {
                if (selectedCharacteristicId.Length == 0)
                {
                    Debug.LogError("characteristic {00002ad2-0000-1000-8000-00805f9b34fb} not found!");
                }
            }
            yield return 0;
        } while (status == BleApi.ScanStatus.AVAILABLE || status == BleApi.ScanStatus.PROCESSING);
    }

    void subscribe()
    {
        Debug.Log("Subscribe...");
        BleApi.SubscribeCharacteristic(selectedDeviceId, selectedServiceId, selectedCharacteristicId, false);
        isSubscribed = true;
    }


    public void quit()
    {
        BleApi.Quit();
    }


    // Update is called once per frame
    public void Update()
    {


        if (isSubscribed)
        {
            BleApi.BLEData res = new BleApi.BLEData();
            while (BleApi.PollData(out res, false))
            {
                {
                    has_speed = false;
                    has_average_speed = false;
                    has_rpm = false;
                    has_average_rpm = false;
                    has_distance = false;
                    has_resistance = false;
                    has_power = false;
                    has_average_power = false;
                    has_expended_energy = false;
                }

                output = String.Empty;
                //subcribeText.text = BitConverter.ToString(res.buf, 0, res.size) + "\n";
                int index = 0;
                int flags = BitConverter.ToUInt16(res.buf, index);
                index += 2;
                if ((flags & 0) == 0)
                {
                    has_speed = true;
                    float value = (float)BitConverter.ToUInt16(res.buf, index);
                    speed = (value * 1.0f) / 100.0f;
                    output += "Speed: " + speed + "\n";
                    index += 2;
                }
                if ((flags & 2) > 0)
                {
                    //??
                    has_average_speed = true;
                    average_speed = BitConverter.ToUInt16(res.buf, index);
                    output += "Average Speed: " + average_speed + "\n";
                    index += 2;
                }
                if ((flags & 4) > 0)
                {
                    rpm = (BitConverter.ToUInt16(res.buf, index) * 1.0f) / 2.0f;
                    output += "RPM: (rev/min): " + rpm + "\n";
                    index += 2;
                }
                if ((flags & 8) > 0)
                {
                    average_rpm = (BitConverter.ToUInt16(res.buf, index) * 1.0f) / 2.0f;
                    output += "Average RPM: " + average_rpm + "\n";
                    index += 2;
                }
                if ((flags & 16) > 0)
                {
                    distance = BitConverter.ToUInt16(res.buf, index); // ?????s
                    output += "Distance (meter): " + distance + "\n";
                    index += 2;
                }
                if ((flags & 32) > 0)
                {
                    resistance = BitConverter.ToInt16(res.buf, index);
                    output += "Resistance: " + resistance + "\n";
                    index += 2;
                }
                if ((flags & 64) > 0)
                {
                    power = BitConverter.ToInt16(res.buf, index);
                    output += "Power (Watt): " + power + "\n";
                    index += 2;
                }
                if ((flags & 128) > 0)
                {
                    average_power = BitConverter.ToInt16(res.buf, index);
                    output += "AveragePower: " + average_power + "\n";
                    index += 2;
                }
                if ((flags & 256) > 0)
                {
                    expended_energy = BitConverter.ToUInt16(res.buf, index);
                    output += "ExpendedEnergy: " + expended_energy + "\n";
                    index += 2;
                }
                // subcribeText.text = Encoding.ASCII.GetString(res.buf, 0, res.size);
            }
            //writeInvoke(778.0f);

        }

        
    }



    public void writeInvoke(float val)
    {
        //ble_api2.pcBleApi.Connect(selectedDeviceId);

        //ble_api2.pcBleApi.StartAdvertisementScan();

        BleApi.SubscribeCharacteristic(selectedDeviceId, selectedServiceId, "{00002ad9-0000-1000-8000-00805f9b34fb}", false);

        byte william1 = Convert.ToByte((int)val % 256);
        byte william2 = Convert.ToByte((int)val / 256);
        byte[] payload = { 0x11, 0x00, 0x00, william1, william2, 0x00, 0x00 };
        //Array.Reverse(payload);
        BleApi.BLEData data = new BleApi.BLEData();
        data.buf = new byte[512];
        data.deviceId = selectedDeviceId;
        data.serviceUuid = selectedServiceId;
        data.characteristicUuid = "{00002ad9-0000-1000-8000-00805f9b34fb}";

        for (int i = 0; i < payload.Length; i++)
        {
            data.buf[i] = payload[i];
            //data.buf[i] = Reverse(payload[i]);
        }
        data.size = (short)payload.Length;
        BleApi.SendData(in data, false);


        //ble_api2.pcBleApi.StopAdvertisementScan();

        //ble_api2.pcBleApi.DisConnect(selectedDeviceId);
    }

    public static byte Reverse(byte inByte)
    {
        byte result = 0x00;

        for (byte mask = 0x80; Convert.ToInt32(mask) > 0; mask >>= 1)
        {
            // shift right current result
            result = (byte)(result >> 1);

            // tempbyte = 1 if there is a 1 in the current position
            var tempbyte = (byte)(inByte & mask);
            if (tempbyte != 0x00)
            {
                // Insert a 1 in the left
                result = (byte)(result | 0x80);
            }
        }

        return (result);
    }
}
