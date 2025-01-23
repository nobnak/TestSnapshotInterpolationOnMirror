using Mirror;
using Mirror.BouncyCastle.Asn1.X509.SigI;
using System;
using System.Collections.Generic;
using Telepathy;
using Unity.Mathematics;
using UnityEngine;
using static Mirror.NetworkRuntimeProfiler;

public class PositionPingpong : NetworkBehaviour {

    [SerializeField] protected ServerConfig serverConfig = new();
    [SerializeField] protected ClientConfig clientConfig = new();

    protected ServerRuntime srt = new();
    protected ClientRuntime crt = new();

    protected double bufferTime => serverConfig.sendInterval * clientConfig.snapshotSettings.bufferTimeMultiplier;

    #region unity
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
            driftEma = new ExponentialMovingAverage(clientConfig.snapshotSettings.driftEmaDuration),
            deliveryTimeEma = new ExponentialMovingAverage(clientConfig.snapshotSettings.driftEmaDuration),
        };
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

    #region declarations
    public struct PositionSnapshot : Snapshot {
        public double remoteTime { get; set; }
        public double localTime { get; set; }

        public float3 position;

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
    }
    #endregion
}