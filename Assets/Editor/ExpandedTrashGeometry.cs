using System.Collections.Generic;
using UnityEngine;

namespace SortQuest.Editor
{
    /// <summary>Physical dimensions in meters. Each piece is convex; cavities stay empty between pieces.</summary>
    internal static class ExpandedTrashGeometry
    {
        internal sealed class Model
        {
            public readonly List<Mesh> Pieces = new List<Mesh>();
            public readonly List<Mesh> Details = new List<Mesh>();
            public float Mass = .05f;
            private int tile;

            public Model(ItemType type) { tile = (int)type - 6; }
            public void Box(Vector3 p, Vector3 size, int surface = -1, float bevel = .001f, bool detail = false)
            {
                var g = new TrashArtMeshes.Geometry();
                g.Box(p, size, Mathf.Min(bevel, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * .2f),
                    surface < 0 ? tile : surface, surface < 0 ? tile : surface, 0);
                (detail ? Details : Pieces).Add(g.Mesh("Box", Vector3.one));
            }
            public void Solid(float bottom, float top, float lowerRadius, float upperRadius, int surface = -1,
                Vector3 offset = default)
            {
                int t = surface < 0 ? tile : surface;
                var g = new TrashArtMeshes.Geometry();
                g.Lathe(new[] { new Vector2(bottom, lowerRadius), new Vector2(top, upperRadius) }, 24, t, 0, 0, false);
                g.Disc(new Vector3(0, bottom, 0), lowerRadius, Vector3.down, t, 24);
                g.Disc(new Vector3(0, top, 0), upperRadius, Vector3.up, t, 24);
                Mesh mesh = g.Mesh("Round section", Vector3.one);
                Vector3[] vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i] += offset;
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                Pieces.Add(mesh);
            }
            public void Tube(float bottom, float top, float lowerRadius, float upperRadius, float wall, bool floor)
            {
                const int sectors = 16;
                for (int i = 0; i < sectors; i++)
                {
                    float a = i * Mathf.PI * 2 / sectors, b = (i + 1) * Mathf.PI * 2 / sectors;
                    Vector3 u = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    Vector3 v = new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b));
                    Vector3 lo = Vector3.up * bottom, hi = Vector3.up * top;
                    Vector3[] p = { lo + u * lowerRadius, lo + v * lowerRadius,
                        hi + v * upperRadius, hi + u * upperRadius,
                        lo + u * (lowerRadius - wall), lo + v * (lowerRadius - wall),
                        hi + v * (upperRadius - wall), hi + u * (upperRadius - wall) };
                    var g = new TrashArtMeshes.Geometry();
                    g.Polygon(new[] { p[0], p[1], p[2], p[3] }, u + v, tile, new[] {
                        new Vector2((float)i / sectors, 0), new Vector2((float)(i + 1) / sectors, 0),
                        new Vector2((float)(i + 1) / sectors, 1), new Vector2((float)i / sectors, 1) });
                    g.Polygon(new[] { p[4], p[7], p[6], p[5] }, -u - v, 52);
                    g.Polygon(new[] { p[3], p[2], p[6], p[7] }, Vector3.up, tile);
                    g.Polygon(new[] { p[0], p[4], p[5], p[1] }, Vector3.down, tile);
                    g.Polygon(new[] { p[0], p[3], p[7], p[4] }, new Vector3(u.z, 0, -u.x), tile);
                    g.Polygon(new[] { p[1], p[5], p[6], p[2] }, new Vector3(-v.z, 0, v.x), tile);
                    Pieces.Add(g.Mesh("Hollow wall " + i, Vector3.one));
                }
                if (floor) Solid(bottom, bottom + wall, lowerRadius, lowerRadius);
            }
            public Mesh Visual(string name)
            {
                var combine = new List<CombineInstance>();
                foreach (Mesh m in Pieces) combine.Add(new CombineInstance { mesh = m, transform = Matrix4x4.identity });
                foreach (Mesh m in Details) combine.Add(new CombineInstance { mesh = m, transform = Matrix4x4.identity });
                var mesh = new Mesh { name = name };
                mesh.CombineMeshes(combine.ToArray(), true, false);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        internal static Model Build(ItemType type)
        {
            var m = new Model(type);
            switch (type)
            {
                case ItemType.FoodTin:
                    m.Tube(-.045f, .045f, .034f, .034f, .003f, true); m.Mass = .045f; break;
                case ItemType.TunaCan:
                    m.Tube(-.018f, .018f, .041f, .041f, .003f, true); m.Mass = .025f; break;
                case ItemType.MetalLid:
                    m.Solid(-.002f, .002f, .039f, .039f, 48);
                    m.Solid(.002f, .004f, .035f, .035f); m.Mass = .015f; break;
                case ItemType.FoilTray:
                    Tray(m, new Vector3(.13f, .035f, .09f), .003f, 48); m.Mass = .02f; break;
                case ItemType.SteelBottle:
                    m.Solid(-.1f, .055f, .031f, .031f);
                    m.Solid(.055f, .08f, .031f, .014f, 48);
                    m.Solid(.08f, .10f, .014f, .014f, 48);
                    m.Solid(.10f, .11f, .016f, .016f, 49); m.Mass = .16f; break;
                case ItemType.YogurtCup:
                    m.Tube(-.04f, .04f, .027f, .04f, .003f, true); m.Mass = .015f; break;
                case ItemType.DetergentBottle:
                    m.Box(new Vector3(-.012f, -.01f, 0), new Vector3(.075f, .12f, .045f), -1, .006f);
                    m.Box(new Vector3(-.016f, .058f, 0), new Vector3(.052f, .02f, .039f), -1, .004f);
                    m.Solid(.068f, .082f, .014f, .014f, 52, new Vector3(-.016f, 0, 0));
                    // A genuine open handle, three bars joined to the body. No enclosing box collider.
                    m.Box(new Vector3(.04f, .037f, 0), new Vector3(.035f, .009f, .014f), 53);
                    m.Box(new Vector3(.053f, .004f, 0), new Vector3(.01f, .062f, .014f), 53);
                    m.Box(new Vector3(.04f, -.026f, 0), new Vector3(.035f, .009f, .014f), 53);
                    m.Mass = .045f; break;
                case ItemType.ShampooBottle:
                    m.Box(new Vector3(0, -.012f, 0), new Vector3(.052f, .125f, .032f), -1, .005f);
                    m.Box(new Vector3(0, .057f, 0), new Vector3(.043f, .013f, .028f), 52, .002f);
                    m.Box(new Vector3(0, .07f, 0), new Vector3(.036f, .013f, .026f), 49, .002f);
                    m.Mass = .03f; break;
                case ItemType.PlasticTub:
                    Tray(m, new Vector3(.11f, .052f, .085f), .003f, -1); m.Mass = .025f; break;
                case ItemType.PlasticCap:
                    m.Tube(-.009f, .009f, .016f, .016f, .003f, true); m.Mass = .005f; break;
                case ItemType.CerealCarton:
                    m.Box(Vector3.zero, new Vector3(.09f, .15f, .04f), -1, .001f);
                    m.Box(new Vector3(0, .0752f, 0), new Vector3(.086f, .0005f, .002f), 51, .0001f, true);
                    m.Mass = .035f; break;
                case ItemType.EggCarton:
                    // Closed six-egg carton: low lid above six individually shaped moulded feet.
                    m.Box(new Vector3(0, .012f, 0), new Vector3(.15f, .018f, .10f), -1, .004f);
                    for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z += 2)
                        m.Solid(-.035f, .003f, .016f, .023f, 51, new Vector3(x * .049f, 0, z * .025f));
                    m.Mass = .028f; break;
                case ItemType.PaperTube:
                    m.Tube(-.055f, .055f, .022f, .022f, .003f, false); m.Mass = .012f; break;
                case ItemType.FoldedNewspaper:
                    m.Box(Vector3.zero, new Vector3(.14f, .014f, .095f), -1, .001f);
                    for (int i = 0; i < 4; i++)
                        m.Box(new Vector3(0, -.0045f + i * .003f, -.0476f), new Vector3(.13f, .0005f, .0004f), 49, .0001f, true);
                    m.Mass = .045f; break;
                case ItemType.Battery9V:
                    m.Box(new Vector3(0, -.003f, 0), new Vector3(.027f, .045f, .018f), -1, .0015f);
                    m.Solid(.0195f, .0235f, .003f, .003f, 48, new Vector3(-.006f, 0, 0));
                    m.Solid(.0195f, .0235f, .0042f, .0042f, 48, new Vector3(.006f, 0, 0));
                    m.Mass = .045f; break;
                case ItemType.BatteryCoin:
                    m.Solid(-.0016f, .0016f, .01f, .01f, 48);
                    m.Box(new Vector3(0, .0017f, 0), new Vector3(.006f, .0002f, .001f), 49, .00002f, true);
                    m.Box(new Vector3(0, .0017f, 0), new Vector3(.001f, .0002f, .006f), 49, .00002f, true);
                    m.Mass = .003f; break;
                case ItemType.Smartphone:
                    m.Box(Vector3.zero, new Vector3(.07f, .009f, .14f), 54, .002f);
                    m.Box(new Vector3(0, .0046f, 0), new Vector3(.064f, .0006f, .126f), 49, .0001f, true);
                    m.Box(new Vector3(-.021f, -.0058f, .052f), new Vector3(.018f, .003f, .018f), 49, .001f);
                    for (int i = 0; i < 4; i++)
                        m.Box(new Vector3(-.011f + i * .008f, .005f, .023f - i * .015f), new Vector3(.027f, .0003f, .0007f), 52, .0001f, true);
                    m.Mass = .15f; break;
                case ItemType.CircuitBoard:
                    m.Box(Vector3.zero, new Vector3(.08f, .0025f, .10f), 53, .0003f);
                    m.Box(new Vector3(0, .005f, 0), new Vector3(.028f, .008f, .032f), 49);
                    m.Box(new Vector3(-.027f, .006f, -.026f), new Vector3(.012f, .011f, .018f), 49);
                    m.Box(new Vector3(.027f, .005f, .028f), new Vector3(.015f, .008f, .022f), 48);
                    for (int i = 0; i < 7; i++)
                        m.Box(new Vector3(-.027f + i * .009f, .0015f, -.042f), new Vector3(.004f, .0005f, .011f), 55, .0001f, true);
                    m.Mass = .035f; break;
                default: throw new System.ArgumentOutOfRangeException(nameof(type));
            }
            return m;
        }

        private static void Tray(Model m, Vector3 size, float wall, int tile)
        {
            m.Box(new Vector3(0, -size.y * .5f + wall * .5f, 0), new Vector3(size.x, wall, size.z), tile);
            for (int sign = -1; sign <= 1; sign += 2)
            {
                m.Box(new Vector3(sign * (size.x - wall) * .5f, 0, 0), new Vector3(wall, size.y, size.z), tile);
                m.Box(new Vector3(0, 0, sign * (size.z - wall) * .5f), new Vector3(size.x - 2 * wall, size.y, wall), tile);
            }
        }
    }
}
