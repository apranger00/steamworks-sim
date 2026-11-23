using System;
using System.Collections.Generic;
using SteamworksSim.Field;
using UnityEngine;

namespace SteamworksSim.Game
{
    /// <summary>
    /// Loose GEARS on PhysX (11 in, 10 teeth, 2 in thick, 0.52 kg): the ones a loading-station chute drops that
    /// no robot catches. Robots can push them around; a robot with a gear intake can pick them up (Catch).
    /// </summary>
    public class GearPool : MonoBehaviour
    {
        const float MassKg = 0.52f;
        public int maxGears = 60;

        /// <summary>Spec position (in) of a gear: return true if a robot takes it.</summary>
        public Func<Vector3, bool> Catch;
        public int Count => active.Count;

        readonly List<Rigidbody> active = new List<Rigidbody>(), free = new List<Rigidbody>();
        Mesh mesh; Material material; PhysicsMaterial physics; Transform root;

        public void Init(Material mat)
        {
            material = mat;
            mesh = FieldAnimator.GearMesh();
            physics = new PhysicsMaterial("Gear") { bounciness = 0.1f, dynamicFriction = 0.45f, staticFriction = 0.55f };
            root = new GameObject("Gears").transform;
            PhysicsLayers.Setup();
        }

        Rigidbody Make()
        {
            var go = new GameObject("Gear") { layer = PhysicsLayers.Gear };
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = FieldAnimator.GearColliderMesh(); mc.convex = true; mc.sharedMaterial = physics;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = MassKg;
            rb.angularDamping = 0.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            return rb;
        }

        /// <summary>A gear at a spec position (in), lying flat, moving at a spec velocity (in/s).</summary>
        public void Spawn(Vector3 specPos, Vector3 specVel, float yawDeg = 0f)
        {
            Rigidbody rb;
            if (free.Count > 0) { rb = free[free.Count - 1]; free.RemoveAt(free.Count - 1); }
            else if (active.Count < maxGears) rb = Make();
            else { rb = active[0]; active.RemoveAt(0); }
            rb.gameObject.SetActive(true);
            var pos = Units.ToUnity(specPos);
            var rot = Quaternion.Euler(0f, yawDeg, 0f);
            rb.position = pos; rb.rotation = rot;
            rb.transform.SetPositionAndRotation(pos, rot);
            rb.linearVelocity = Units.ToUnity(specVel); rb.angularVelocity = Vector3.zero;
            active.Add(rb);
        }

        public void Clear()
        {
            foreach (var rb in active) { rb.gameObject.SetActive(false); free.Add(rb); }
            active.Clear();
        }

        void FixedUpdate()
        {
            if (Catch == null) return;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var rb = active[i];
                if (Catch(Units.ToSpec(rb.position)))
                {
                    rb.gameObject.SetActive(false);
                    active.RemoveAt(i); free.Add(rb);
                }
            }
        }
    }
}
