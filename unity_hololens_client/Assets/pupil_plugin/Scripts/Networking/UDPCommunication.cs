﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Text;
using MessagePack;
using HoloToolkit.Unity;

#if !UNITY_EDITOR
using System.Linq;
using System.IO;
using Windows.Networking.Sockets;
using Windows.Networking.Connectivity;
using Windows.Networking;
#endif

public class UDPCommunication : Singleton<UDPCommunication>
{
	private readonly  Queue<Action> ExecuteOnMainThread = new Queue<Action>();


	#if !UNITY_EDITOR

	//Send an UDP-Packet
	public async void SendUDPMessage(byte[] data)
	{
        UnityEngine.Debug.Log("UDP data head " + (char)data[0]);
        await _SendUDPMessage(data);
	}

    DatagramSocket socket;

	async void Start()
	{

	Debug.Log("Waiting for a connection...");

	socket = new DatagramSocket();
	socket.MessageReceived += Socket_MessageReceived;

	HostName IP = null;
	try
	{
	    var icp = NetworkInformation.GetInternetConnectionProfile();

	    IP = Windows.Networking.Connectivity.NetworkInformation.GetHostNames()
	    .SingleOrDefault(
	    hn =>
	    hn.IPInformation?.NetworkAdapter != null && hn.IPInformation.NetworkAdapter.NetworkAdapterId
	    == icp.NetworkAdapter.NetworkAdapterId);

	await socket.BindEndpointAsync(IP, PupilSettings.Instance.connection.pupilRemotePort);
	}
	catch (Exception e)
	{
	    Debug.Log(e.ToString());
	    Debug.Log(SocketError.GetStatus(e.HResult).ToString());
	    return;
	}
	}
    
	private async System.Threading.Tasks.Task _SendUDPMessage(byte[] data)
	{
	using (var stream = await socket.GetOutputStreamAsync(new Windows.Networking.HostName(PupilSettings.Instance.connection.pupilRemoteIP), PupilSettings.Instance.connection.pupilRemotePort))
	    {
	        using (var writer = new Windows.Storage.Streams.DataWriter(stream))
	        {
	            writer.WriteBytes(data);
	            await writer.StoreAsync();

	        }
	    }
	}


	#else
	// to make Unity-Editor happy :-)
	void Start()
	{

	}

	public void SendUDPMessage(byte[] data)
	{

	}

	#endif
	// Update is called once per frame
	void Update()
	{
		while (ExecuteOnMainThread.Count > 0)
		{
			ExecuteOnMainThread.Dequeue().Invoke();
		}
	}
		
	public void InterpreteUDPData(byte[] data)
	{
		switch (data[0])
		{
		// Connection established
		case (byte) '0':
			switch (data [1])
			{
			case (byte) 'I':
				UnityEngine.Debug.Log ("Connection established");
				PupilSettings.Instance.connection.isConnected = true;
				break;
			default:
				UnityEngine.Debug.Log ("Unknown response: " + (char) data[1]);
				break;
			}
			break;
		case (byte) 'E':
			switch (data [1])
			{
			case (byte) 'C':
				if (data [2] == (byte) 'S') // "notify.calibration.successful"
				{
					UnityEngine.Debug.Log ("notify.calibration.successful");
					PupilSettings.Instance.calibration.currentStatus = Calibration.Status.Succeeded;
					PupilTools.CalibrationFinished ();
				} else if (data [2] == (byte) 'F') // "notify.calibration.failed"
				{
					UnityEngine.Debug.Log("notify.calibration.failed");
					PupilSettings.Instance.calibration.currentStatus = Calibration.Status.NotSet;
					PupilTools.CalibrationFailed();
				}
				else
					UnityEngine.Debug.Log ("Unknown calibration ended event");
				break;
			case (byte) 'G':
				if (data [2] == (byte)'2')
				{
					if (data [3] == (byte)'1')
					{
						//UnityEngine.Debug.Log("Left eye position received");
						var leftEyePosition = FloatArrayFromPacket (data, 4);
						PupilData._2D.LeftEyePosUDP.x = leftEyePosition [0];
						PupilData._2D.LeftEyePosUDP.y = leftEyePosition [1];
					    UnityEngine.Debug.Log ("Left eye position: " + PupilData._2D.LeftEyePosUDP.ToString());
					} else if (data [3] == (byte)'0')
					{
						//UnityEngine.Debug.Log("Right eye position received");
						var rightEyePosition = FloatArrayFromPacket (data, 4);
						PupilData._2D.RightEyePosUDP.x = rightEyePosition [0];
						PupilData._2D.RightEyePosUDP.y = rightEyePosition [1];
					    UnityEngine.Debug.Log ("Right Eye Position: " + PupilData._2D.RightEyePosUDP.ToString());
					} else if (data [3] == (byte)'2')
					{
						var gaze2DPosition = FloatArrayFromPacket (data, 4);
						PupilData._2D.Gaze2DPosUDP.x = gaze2DPosition [0];
						PupilData._2D.Gaze2DPosUDP.y = gaze2DPosition [1];
					    UnityEngine.Debug.Log ("Gazepoint 2D: " + PupilData._2D.Gaze2DPosUDP.ToString());
					}
					else
						UnityEngine.Debug.Log ("Unknown gaze 2d data");
				} else if (data [2] == (byte)'3')
				{
					var gaze3DPosition = FloatArrayFromPacket (data, 4);
					PupilData._3D.Gaze3DPosUDP.x = gaze3DPosition [0] / PupilSettings.PupilUnitScalingFactor;
					PupilData._3D.Gaze3DPosUDP.y = gaze3DPosition [1] / PupilSettings.PupilUnitScalingFactor;
					PupilData._3D.Gaze3DPosUDP.z = gaze3DPosition [2] / PupilSettings.PupilUnitScalingFactor;
					UnityEngine.Debug.Log ("Gazepoint 3D: " + PupilData._3D.Gaze3DPosUDP.ToString());
				} else
					UnityEngine.Debug.Log ("Unknown gaze event");
				break;
			default:
				UnityEngine.Debug.Log ("Unknown event");
				break;
			}
			break;
		case 90:
			UnityEngine.Debug.Log ("Start/stop calibration command");
			if (data [1] == 1)
				PupilTools.StartCalibration ();
			else
				PupilTools.StopCalibration ();
			break;
		case 91:
			UnityEngine.Debug.Log ("Forcing 2D calibration mode (Pupil version < 1 detected)");
			PupilSettings.Instance.calibration.currentMode = Calibration.Mode._2D;
			break;
		default:
			UnityEngine.Debug.Log(StringFromPacket(data));
			break;
		}
	}

	private float[] FloatArrayFromPacket (byte[] data, int offset = 1)
	{
		float[] floats = new float[(data.Length-1)/sizeof(float)];
		for(int i = 0; i < floats.Length; i++)
		{
			floats[i] = BitConverter.ToSingle(data, offset + i*sizeof(float));
		}
		return floats;
	}

	private string StringFromPacket (byte[] data)
	{
		byte[] message = new byte[data.Length - 1];
		for (int i = 1; i < data.Length; i++)
		{
			message [i-1] = data [i];
		}
		return Encoding.ASCII.GetString (message);
	}

	#if !UNITY_EDITOR

	static MemoryStream ToMemoryStream(Stream input)
	{
	    try
	    {                                         // Read and write in
	        byte[] block = new byte[0x1000];       // blocks of 4K.
	        MemoryStream ms = new MemoryStream();
	        while (true)
	        {
	            int bytesRead = input.Read(block, 0, block.Length);
	            if (bytesRead == 0) return ms;
	            ms.Write(block, 0, bytesRead);
	        }
	    }
	    finally { }
	}

	private void Socket_MessageReceived(Windows.Networking.Sockets.DatagramSocket sender,
	Windows.Networking.Sockets.DatagramSocketMessageReceivedEventArgs args)
	{
	    //Read the message that was received from the UDP  client.
	    Stream streamIn = args.GetDataStream().AsStreamForRead();
	    MemoryStream ms = ToMemoryStream(streamIn);
	    byte[] msgData = ms.ToArray();


	    if (ExecuteOnMainThread.Count == 0)
	    {
	        ExecuteOnMainThread.Enqueue(() => { InterpreteUDPData(msgData); });
	    }
	}

	#endif
}