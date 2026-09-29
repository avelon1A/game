using UnityEngine;
using Veil.Sim;

namespace Veil.View
{
    /// <summary>
    /// Third-person, slightly elevated camera (GDD §18): smooth follow, adjustable distance,
    /// collision against the arena, aim support, plus cinematic "shots" for menus.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        public float Yaw;
        public float Pitch = 20f;
        public float Distance = 8f;
        public float Sensitivity = 0.12f;
        public MapData Map;

        private Vector3 _pos;
        private Quaternion _rot = Quaternion.identity;
        private float _shake;
        private float _fovTarget = 60f;
        private float _collisionDist = 99f;
        private float _pivotY;
        private bool _hasPivot;

        public void Init(Camera cam)
        {
            Cam = cam;
            _pos = cam.transform.position;
            _rot = cam.transform.rotation;
        }

        public void Shake(float amount) => _shake = Mathf.Max(_shake, amount);

        public void SetFov(float fov) => _fovTarget = fov;

        /// <summary>Gameplay follow. lookDelta in pixels, scroll in notches.</summary>
        public void Follow(Vector3 target, Vector2 lookDelta, float scroll, float dt)
        {
            Yaw += lookDelta.x * Sensitivity;
            Pitch = Mathf.Clamp(Pitch - lookDelta.y * Sensitivity, -8f, 65f);
            Distance = Mathf.Clamp(Distance - scroll * 0.9f, 3.5f, 14f);

            var rot = Quaternion.Euler(Pitch, Yaw, 0);
            // gentle vertical smoothing so jumps/landings don't jerk the camera
            _pivotY = _hasPivot ? Mathf.Lerp(_pivotY, target.y, 1 - Mathf.Exp(-8f * dt)) : target.y;
            _hasPivot = true;
            Vector3 pivot = new Vector3(target.x, _pivotY, target.z) + Vector3.up * 1.9f;
            float want = Distance;
            float hit = CollisionDistance(pivot, rot * Vector3.back, want);
            // pull in fast, ease out slowly
            _collisionDist = hit < _collisionDist ? hit : Mathf.Lerp(_collisionDist, hit, 1 - Mathf.Exp(-4f * dt));
            Vector3 camPos = pivot + rot * Vector3.back * _collisionDist + rot * Vector3.right * 0.35f;
            if (camPos.y < 0.4f) camPos.y = 0.4f;

            _pos = camPos;
            _rot = rot;
            Apply(dt);
        }

        /// <summary>Cinematic framing (menus, lobby, results).</summary>
        public void Shot(Vector3 pos, Vector3 lookAt, float dt, float smooth = 3f)
        {
            var rot = Quaternion.LookRotation(lookAt - pos);
            float k = 1 - Mathf.Exp(-smooth * dt);
            _pos = Vector3.Lerp(_pos, pos, k);
            _rot = Quaternion.Slerp(_rot, rot, k);
            Apply(dt);
        }

        public void Snap(Vector3 pos, Vector3 lookAt)
        {
            _pos = pos;
            _rot = Quaternion.LookRotation(lookAt - pos);
            Apply(0);
        }

        private void Apply(float dt)
        {
            Vector3 p = _pos;
            if (_shake > 0.001f)
            {
                p += Random.insideUnitSphere * _shake * 0.25f;
                _shake = Mathf.MoveTowards(_shake, 0, dt * 3f);
            }
            Cam.transform.SetPositionAndRotation(p, _rot);
            Cam.fieldOfView = Mathf.Lerp(Cam.fieldOfView, _fovTarget, 1 - Mathf.Exp(-8f * dt));
        }

        private float CollisionDistance(Vector3 pivot, Vector3 dir, float max)
        {
            if (Map == null) return max;
            for (float d = 0.6f; d <= max; d += 0.35f)
            {
                Vector3 p = pivot + dir * d;
                var p2 = new Vec2(p.x, p.z);
                foreach (int oi in Map.Query(p2))
                {
                    var o = Map.Obstacles[oi];
                    if (o.Kind == ObstacleKind.Water || o.Kind == ObstacleKind.Tree) continue;
                    if (o.Height < p.y - 0.3f) continue;
                    if (o.SignedDistance(p2) < 0.35f) return Mathf.Max(1.2f, d - 0.45f);
                }
            }
            return max;
        }
    }
}
