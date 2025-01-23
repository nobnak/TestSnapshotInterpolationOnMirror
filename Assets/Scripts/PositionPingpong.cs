using Mirror;
using Mirror.BouncyCastle.Asn1.X509.SigI;
using System;
using System.Collections.Generic;
using System.Text;
using Telepathy;
using Unity.Mathematics;
using UnityEngine;
using static Mirror.NetworkRuntimeProfiler;

public class PositionPingpong : NetworkBehaviour {

    [SerializeField] protected ServerConfig serverConfig = new();
    [SerializeField] protected ClientConfig clientConfig = new();

    protected ServerRuntime srt = new();
    protected ClientRuntime crt = new();

    protected StringBuilder str = new();

    protected double bufferTime => serverConfig.sendInterval 
        * clientConfig.snapshotSettings.bufferTimeMultiplier;

    #region unity
    private void OnEnable() {
    }
    private void Update() {
        if (NetworkServer.active && isServer) {
            Move();

            if (Time.time >= srt.lastSendTime + serverConfig.sendInterval) {
                Send(srt.currPos);
                srt.lastSendTime = Time.time;
            }
        }

        if (NetworkClient.ready && isClient) {
            var dt = Time.unscaledDeltaTime;

            if (crt.snapshots.Count > 0) {
                SnapshotInterpolation.Step(
                    crt.snapshots,
                    dt,
                    ref crt.localTimeline,
                    crt.localTimescale,
                    out var fromSnapshot,
                    out var toSnapshot,
                    out double t);

                // interpolate & apply
                PositionSnapshot computed = PositionSnapshot.Interpolate(fromSnapshot, toSnapshot, t);
                transform.position = computed.position;
            }

            if (crt.mat != null) {
                if (crt.localTimescale > 1) {
                    crt.mat.color = clientConfig.catchupColor;
                } else if (crt.localTimescale < 1) {
                    crt.mat.color = clientConfig.slowdownColor;
                } else {
                    crt.mat.color = crt.defaultColor;
                }
            }
        }
    }
    #endregion

    #region server
    public override void OnStartServer() {
        Debug.Log($"{nameof(OnStartServer)}:");
        base.OnStartServer();
        srt = new ServerRuntime {
            startPos = transform.position,
        };
    }
    public override void OnStartClient() {
        Debug.Log($"{nameof(OnStartClient)}:");
        base.OnStartClient();
        crt = new ClientRuntime {
            driftEma = new ExponentialMovingAverage(
            serverConfig.sendRate * clientConfig.snapshotSettings.driftEmaDuration),
            deliveryTimeEma = new ExponentialMovingAverage(
            serverConfig.sendRate * clientConfig.snapshotSettings.deliveryTimeEmaDuration),
            mat = GetComponent<Renderer>().material,
        };
        crt.defaultColor = crt.mat.color;
        crt.localTimeline = 0;
        crt.localTimescale = 1f;
    }
    public override void OnStopClient() {
        base.OnStopClient();
        Destroy(crt.mat);
    }
    #endregion

    #region client
    [ClientRpc]
    public void RpcMessage(PositionSnapshot snap) {
        snap.localTime = NetworkTime.localTime;

        SnapshotInterpolation.InsertAndAdjust(
            crt.snapshots,
            clientConfig.snapshotSettings.bufferLimit,
            snap,
            ref crt.localTimeline,
            ref crt.localTimescale,
            serverConfig.sendInterval,
            bufferTime,
            clientConfig.snapshotSettings.catchupSpeed,
            clientConfig.snapshotSettings.slowdownSpeed,
            ref crt.driftEma,
            clientConfig.snapshotSettings.catchupNegativeThreshold,
            clientConfig.snapshotSettings.catchupPositiveThreshold,
            ref crt.deliveryTimeEma);

#if false
        str.Clear();
        str.AppendLine($"{nameof(RpcMessage)}: {snap}");
        str.AppendLine($"Time {crt.localTimeline:f2}, scale {crt.localTimescale:f2}, drift {crt.driftEma.Value:f2}, buffer {bufferTime:f2}");
        Debug.Log(str);
#endif
    }
#endregion

        #region methods
    private void Move() {
        var x = Time.time * serverConfig.speed;
        var t = x / serverConfig.distance;
        var repeat = math.floor(t);
        t = math.frac(t) - 0.5f;
        var pingpong = (repeat % 2) == 0 ? t : -t;
        srt.currPos = srt.startPos + new float3(pingpong * serverConfig.distance, 0, 0);
    }
    private void Send(float3 position) {
        var snap = new PositionSnapshot {
            remoteTime = NetworkTime.localTime,
            position = position,
        };
        RpcMessage(snap);
    }
    #endregion

    #region ui
    private void OnGUI() {
        const int width = 30; // fit 3 digits
        const int height = 20;
        Vector2 screen = Camera.main.WorldToScreenPoint(transform.position);
        string str = $"{crt.snapshots.Count}";
        GUI.Label(new Rect(screen.x - width / 2, screen.y - height / 2, width, height), str);

        float areaHeight = 100;
        using (new GUILayout.AreaScope(new Rect(10, 10, Screen.width, areaHeight))) {
            using (new GUILayout.VerticalScope()) {
                GUILayout.Label($"Local time: {crt.localTimeline:f2}, scale {crt.localTimescale:f2}");
            }
        }
    }
    #endregion

    #region declarations
    public struct PositionSnapshot : Snapshot {
        public double remoteTime { get => _remoteTime; set => _remoteTime = value; }
        public double localTime { get => _localTime; set => _localTime = value; }

        public float3 position;
        public double _remoteTime;
        public double _localTime;

        public override string ToString() {
            return $"{nameof(PositionSnapshot)}: {remoteTime:f2}, {localTime:f2}, {position}";
        }

        public static PositionSnapshot Interpolate(PositionSnapshot fromSnapshot, PositionSnapshot toSnapshot, double t) {
            return new() {
                remoteTime = 0,
                localTime = 0,
                position = math.lerp(fromSnapshot.position, toSnapshot.position, (float)t),
            };
        }
    }
    public class ServerRuntime {
        public float3 startPos;
        public float3 currPos;

        public float lastSendTime;
    }
    public class ClientRuntime {
        public SortedList<double, PositionSnapshot> snapshots = new();

        public double localTimeline;
        public double localTimescale = 1;

        public ExponentialMovingAverage driftEma;
        public ExponentialMovingAverage deliveryTimeEma;

        public Material mat;
        public Color defaultColor = Color.gray;
    }

    [System.Serializable]
    public class ServerConfig {
        [Header("Network")]
        public int sendRate = 30;

        [Header("Movement")]
        public float distance = 10;
        public float speed = 3;

        public float sendInterval => 1f / sendRate;
    }
    [System.Serializable]
    public class ClientConfig {
        public SnapshotInterpolationSettings snapshotSettings = new();

        [Header("Debug")]
        public Color catchupColor = Color.green; // green traffic light = go fast
        public Color slowdownColor = Color.red;  // red traffic light = go slow
    }
    #endregion
}