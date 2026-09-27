using System;
using UnityEditor;
using UnityEngine;

namespace SortQuest.Editor
{
    /// <summary>Physics regression checks; creates and removes temporary geometry without saving a scene.</summary>
    public static class GripperContactChecks
    {
        [MenuItem("SortQuest/Checks/Gripper Contacts")]
        public static void Run()
        {
            var root = new GameObject("Temporary gripper contact checks");
            root.transform.position = new Vector3(2000f, 2000f, 2000f);
            int previousMask = GripperCollision.Mask;
            try
            {
                GripperCollision.Mask = Physics.DefaultRaycastLayers;
                var target = new GameObject("Target");
                target.transform.SetParent(root.transform, false);
                var body = target.AddComponent<Rigidbody>();
                body.isKinematic = true;
                var box = target.AddComponent<BoxCollider>();
                box.size = new Vector3(.06f, .06f, .06f);
                Physics.SyncTransforms();
                var profile = new GripperProfile();
                var grasp = new GripperGrasp { Position = target.transform.position,
                    Rotation = Quaternion.identity, Width = .06f };
                Require(profile.parallel.TryContacts(grasp, body, out float left, out float right,
                    out Vector3 leftPoint, out Vector3 rightPoint), "Small box has opposing contacts");
                Require(Mathf.Abs(left - .03f) < .0001f && Mathf.Abs(right - .03f) < .0001f,
                    "Jaw offsets match the physical box width");
                Require(Vector3.Distance(leftPoint, box.ClosestPoint(leftPoint)) < .0001f &&
                    Vector3.Distance(rightPoint, box.ClosestPoint(rightPoint)) < .0001f,
                    "Display markers lie on collider surfaces");
                Require(profile.parallel.FingerTouches(grasp, true, left, body) &&
                    profile.parallel.FingerTouches(grasp, false, right, body), "Animated pads use the same contacts");
                Require(GripperCollision.Feasible(profile, grasp, body, null, out _), "Small-box approach fits");

                var empty = grasp;
                empty.Position += Vector3.up;
                Require(!GripperCollision.Feasible(profile, empty, body, null, out string reason) && reason == "no_contact",
                    "Empty-air candidates fail during planning");
                var oneSided = grasp;
                oneSided.Position += Vector3.right * .04f;
                Require(!profile.parallel.TryGrasp(oneSided, body, out _), "One-sided contact fails");

                // Original failure: both fingers' tolerance boxes could brush one broad face at zero opening.
                box.size = new Vector3(.25f, .15f, .20f);
                Physics.SyncTransforms();
                var face = grasp;
                face.Position += Vector3.back * (.10f + profile.parallel.fingertipPastGrasp + .001f);
                Require(!profile.parallel.TryGrasp(face, body, out _), "Two tips on one face do not make a pinch");
                foreach (GripperProfile parallel in GripperCatalog.DefaultProfiles())
                {
                    if (parallel.IsSuction) continue;
                    foreach (Vector3 axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
                    {
                        var wideBox = grasp;
                        wideBox.Rotation = Quaternion.FromToRotation(Vector3.right, axis);
                        Require(!parallel.Model.TryGrasp(wideBox, body, out _), "Cardboard exceeds " + parallel.id);
                    }
                }

                var suction = new GripperProfile { kind = GripperKind.Suction };
                var top = grasp;
                top.Position += Vector3.up * .075f;
                top.Rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                Require(GripperCollision.Feasible(suction, top, body, null, out _), "Suction seals on the cardboard top");
                var offSurface = top;
                offSurface.Position += Vector3.up * .02f;
                Require(!GripperCollision.Feasible(suction, offSurface, body, null, out reason) && reason == "no_seal",
                    "Floating suction candidate fails during planning");

                box.size = Vector3.one;
                target.transform.localScale = new Vector3(.06f, .05f, .04f);
                target.transform.rotation = Quaternion.Euler(13f, 37f, 21f);
                grasp.Rotation = target.transform.rotation;
                Physics.SyncTransforms();
                Require(profile.parallel.TryContacts(grasp, body, out left, out right, out _, out _) &&
                    Mathf.Abs(left + right - .06f) < .0005f, "Rotated and scaled item preserves contact width");

                // A valid contact still cannot override a blocked approach.
                var obstacle = new GameObject("Obstacle");
                obstacle.transform.SetParent(root.transform, false);
                obstacle.transform.position = grasp.Position - grasp.Approach * .1f;
                obstacle.AddComponent<BoxCollider>().size = Vector3.one * .2f;
                Physics.SyncTransforms();
                Require(!GripperCollision.Feasible(profile, grasp, body, null, out reason) &&
                    reason == "collision_on_approach", "Scenery blocks an otherwise valid grasp");
                Debug.Log("GRIPPER_CONTACT_CHECKS_PASS: opposing pads, surface markers, empty air, one face, " +
                    "oversized cardboard, suction, transformed geometry, and approach obstruction.");
            }
            finally
            {
                GripperCollision.Mask = previousMask;
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        private static void Require(bool passed, string label)
        {
            if (!passed) throw new InvalidOperationException("Gripper contact check failed: " + label);
        }
    }
}
