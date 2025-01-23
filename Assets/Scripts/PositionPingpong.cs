using Mirror;
using Unity.Mathematics;
using UnityEngine;

public class PositionPingpong : NetworkBehaviour {

    [SerializeField] protected ServerConfig config = new();

    protected Runtime rt = new();

    #region unity
    private void OnEnable() {
        rt = new Runtime {
            startPos = transform.position
        };
    }
    private void Update() {
        if (NetworkServer.active && isServer) {
            var x = Time.time * config.speed;
            var t = x / config.distance;
            var repeat = math.floor(t);
            t = math.frac(t) - 0.5f;
            var pingpong = (repeat % 2) == 0 ? t : -t;
            transform.position = rt.startPos + new float3(pingpong * config.distance, 0, 0);
        }
    }
    #endregion

    #region declarations
    public class Runtime {
        public float3 startPos;
        public float lastSendTime;
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

    }
    #endregion
}