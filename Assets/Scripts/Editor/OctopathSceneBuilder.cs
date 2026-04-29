using System.Collections.Generic;
using System.IO;
using CathayCrossing.HD2D;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CathayCrossing.HD2D.EditorTools
{
    public static class OctopathSceneBuilder
    {
        const string RootName = "OctopathOffice_Root";
        const string MaterialsFolder = "Assets/Settings/OctopathMaterials";
        const string TexturesFolder = "Assets/Settings/OctopathTextures";

        const int RoomX = 60;
        const int RoomZ = 40;
        const float RoomHeight = 3.6f;
        const float HalfX = RoomX * 0.5f;
        const float HalfZ = RoomZ * 0.5f;

        [MenuItem("Tools/Octopath/Build Office Scene")]
        public static void BuildScene()
        {
            var existing = GameObject.Find(RootName);
            if (existing != null) Object.DestroyImmediate(existing);

            EnsureFolder(MaterialsFolder);
            EnsureFolder(TexturesFolder);

            var mats = MaterialKit.Build();

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Octopath Office");

            BuildFloor(root.transform, mats);
            BuildWalls(root.transform, mats);
            BuildOfficeFurniture(root.transform, mats);
            BuildDecor(root.transform, mats);

            var player = BuildPlayer(root.transform, mats);
            BuildLighting(root.transform, mats);
            BuildCamera(root.transform, player.transform);
            BuildPostProcessVolume(root.transform);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = player;
            SceneView.FrameLastActiveSceneView();

            Debug.Log("[Octopath] Office scene built — modeled after the reference office photo. WASD to move, Shift to run, right-drag to orbit.");
        }

        // ─── Floor (patterned yellow carpet) ──────────────────────────────────
        static void BuildFloor(Transform parent, MaterialKit mats)
        {
            var floorRoot = new GameObject("Floor").transform;
            floorRoot.SetParent(parent, false);

            System.Random rng = new System.Random(11);
            for (int x = 0; x < RoomX; x++)
            for (int z = 0; z < RoomZ; z++)
            {
                var tile = GameObject.CreatePrimitive(PrimitiveType.Quad);
                tile.name = $"FloorTile_{x}_{z}";
                tile.transform.SetParent(floorRoot, false);
                tile.transform.position = new Vector3(x - HalfX + 0.5f, 0f, z - HalfZ + 0.5f);
                tile.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

                Material mat;
                int r = rng.Next(0, 100);
                if (r < 6) mat = mats.CarpetAccent;
                else mat = ((x + z) % 2 == 0) ? mats.FloorLight : mats.FloorDark;
                tile.GetComponent<MeshRenderer>().sharedMaterial = mat;
                Object.DestroyImmediate(tile.GetComponent<MeshCollider>());
            }

            var collider = new GameObject("FloorCollider");
            collider.transform.SetParent(floorRoot, false);
            collider.transform.position = new Vector3(0, -0.05f, 0);
            var box = collider.AddComponent<BoxCollider>();
            box.size = new Vector3(RoomX, 0.1f, RoomZ);
        }

        // ─── Walls + back-wall windows with roller blinds ─────────────────────
        static void BuildWalls(Transform parent, MaterialKit mats)
        {
            var wallsRoot = new GameObject("Walls").transform;
            wallsRoot.SetParent(parent, false);

            // Left, right, front
            CreateBox(wallsRoot, "LeftWall",
                new Vector3(-HalfX - 0.05f, RoomHeight * 0.5f, 0),
                new Vector3(0.1f, RoomHeight, RoomZ), mats.Wall);
            CreateBox(wallsRoot, "RightWall",
                new Vector3(HalfX + 0.05f, RoomHeight * 0.5f, 0),
                new Vector3(0.1f, RoomHeight, RoomZ), mats.Wall);
            CreateBox(wallsRoot, "FrontRim",
                new Vector3(0, 0.15f, -HalfZ - 0.05f),
                new Vector3(RoomX, 0.3f, 0.1f), mats.Wood);

            // Back wall: build it as bottom band + top band + pillars between five windows
            float winBottom = 1.4f;
            float winTop = 3.0f;
            float winHalfW = 3.5f;
            float[] winX = { -22f, -11f, 0f, 11f, 22f };

            CreateBox(wallsRoot, "BackWall_Bottom",
                new Vector3(0, winBottom * 0.5f, HalfZ + 0.05f),
                new Vector3(RoomX, winBottom, 0.1f), mats.Wall);
            float topBandH = RoomHeight - winTop;
            CreateBox(wallsRoot, "BackWall_Top",
                new Vector3(0, winTop + topBandH * 0.5f, HalfZ + 0.05f),
                new Vector3(RoomX, topBandH, 0.1f), mats.Wall);

            float prevX = -HalfX;
            for (int i = 0; i < winX.Length; i++)
            {
                float pillarLeft = prevX;
                float pillarRight = winX[i] - winHalfW;
                if (pillarRight > pillarLeft + 0.01f)
                {
                    float w = pillarRight - pillarLeft;
                    CreateBox(wallsRoot, $"BackWall_Pillar_{i}",
                        new Vector3((pillarLeft + pillarRight) * 0.5f, (winBottom + winTop) * 0.5f, HalfZ + 0.05f),
                        new Vector3(w, winTop - winBottom, 0.1f), mats.Wall);
                }
                prevX = winX[i] + winHalfW;
            }
            if (prevX < HalfX - 0.01f)
            {
                float w = HalfX - prevX;
                CreateBox(wallsRoot, "BackWall_Pillar_Right",
                    new Vector3((prevX + HalfX) * 0.5f, (winBottom + winTop) * 0.5f, HalfZ + 0.05f),
                    new Vector3(w, winTop - winBottom, 0.1f), mats.Wall);
            }

            // Window glass + frames + roller blinds
            for (int i = 0; i < winX.Length; i++)
            {
                CreateBox(wallsRoot, $"WindowGlass_{i}",
                    new Vector3(winX[i], (winBottom + winTop) * 0.5f, HalfZ - 0.02f),
                    new Vector3(winHalfW * 2 - 0.15f, winTop - winBottom - 0.1f, 0.05f), mats.WindowGlow);

                // Frame: top, bottom, left, right, center muntin
                CreateBox(wallsRoot, $"WindowFrameTop_{i}",
                    new Vector3(winX[i], winTop, HalfZ - 0.04f),
                    new Vector3(winHalfW * 2, 0.08f, 0.06f), mats.WindowFrame);
                CreateBox(wallsRoot, $"WindowFrameBot_{i}",
                    new Vector3(winX[i], winBottom, HalfZ - 0.04f),
                    new Vector3(winHalfW * 2, 0.08f, 0.06f), mats.WindowFrame);
                CreateBox(wallsRoot, $"WindowFrameL_{i}",
                    new Vector3(winX[i] - winHalfW, (winBottom + winTop) * 0.5f, HalfZ - 0.04f),
                    new Vector3(0.08f, winTop - winBottom, 0.06f), mats.WindowFrame);
                CreateBox(wallsRoot, $"WindowFrameR_{i}",
                    new Vector3(winX[i] + winHalfW, (winBottom + winTop) * 0.5f, HalfZ - 0.04f),
                    new Vector3(0.08f, winTop - winBottom, 0.06f), mats.WindowFrame);
                CreateBox(wallsRoot, $"WindowMuntin_{i}",
                    new Vector3(winX[i], (winBottom + winTop) * 0.5f, HalfZ - 0.04f),
                    new Vector3(0.06f, winTop - winBottom, 0.06f), mats.WindowFrame);

                // Roller blinds at varied drop heights (matches the photo's mixed state)
                float[] dropPattern = { 0.95f, 0.20f, 0.55f, 0.15f, 0.80f };
                float drop = dropPattern[i % dropPattern.Length];
                if (drop > 0.15f)
                {
                    float blindH = (winTop - winBottom) * drop;
                    CreateBox(wallsRoot, $"Blind_{i}",
                        new Vector3(winX[i], winTop - blindH * 0.5f, HalfZ - 0.10f),
                        new Vector3(winHalfW * 2 - 0.05f, blindH, 0.04f), mats.BlindsDark);
                }
                CreateBox(wallsRoot, $"BlindCassette_{i}",
                    new Vector3(winX[i], winTop + 0.05f, HalfZ - 0.12f),
                    new Vector3(winHalfW * 2, 0.12f, 0.18f), mats.BlindsDark);
            }

            // Door on left wall
            CreateBox(wallsRoot, "Door",
                new Vector3(-HalfX + 0.06f, 1.1f, -HalfZ + 2.5f),
                new Vector3(0.05f, 2.2f, 1.4f), mats.Door);
            CreateBox(wallsRoot, "DoorKnob",
                new Vector3(-HalfX + 0.16f, 1.1f, -HalfZ + 3.05f),
                new Vector3(0.06f, 0.06f, 0.06f), mats.Brass);
        }

        // ─── Furniture: 13 vertical desk columns running along Z axis ─────────
        // Layout: 13 columns spaced 1.5m on X (centered, so col 6 is at X=0).
        // Chairs alternate sides per column (even = east, odd = west) so chairs
        // pair up back-to-back in odd-even gaps; even-odd gaps are walking aisles.
        const int DeskColumns = 13;
        const float DeskColumnSpacingX = 4.0f;
        // Desks slide up against the back wall (HalfZ = 20). 21m length preserved.
        const float DeskZStart = -1.2f;
        const float DeskZEnd   = 19.8f;

        static float ColumnX(int i) => (i - (DeskColumns - 1) * 0.5f) * DeskColumnSpacingX;

        static void BuildOfficeFurniture(Transform parent, MaterialKit mats)
        {
            var fRoot = new GameObject("Furniture").transform;
            fRoot.SetParent(parent, false);

            for (int i = 0; i < DeskColumns; i++)
            {
                float colX = ColumnX(i);
                bool chairEast = (i % 2 == 0);
                BuildDeskColumn(fRoot, mats, colX, chairEast, columnIndex: i);
            }

            // Whiteboard on right wall, mounted high
            CreateBox(fRoot, "WhiteboardFrame",
                new Vector3(HalfX - 0.06f, 2.4f, 3f),
                new Vector3(0.04f, 1.65f, 2.9f), mats.Wood);
            CreateBox(fRoot, "Whiteboard",
                new Vector3(HalfX - 0.08f, 2.4f, 3f),
                new Vector3(0.03f, 1.5f, 2.7f), mats.Whiteboard);

            // Water cooler near door
            var cooler = CreateBox(fRoot, "WaterCooler",
                new Vector3(-HalfX + 0.7f, 0.6f, -HalfZ + 1.0f),
                new Vector3(0.9f, 1.2f, 0.9f), mats.Cabinet);
            var jug = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            jug.name = "WaterJug";
            jug.transform.SetParent(cooler.transform, true);
            jug.transform.position = new Vector3(-HalfX + 0.7f, 1.55f, -HalfZ + 1.0f);
            jug.transform.localScale = new Vector3(0.6f, 0.35f, 0.6f);
            jug.GetComponent<MeshRenderer>().sharedMaterial = mats.WindowGlow;
            Object.DestroyImmediate(jug.GetComponent<Collider>());
        }

        static void BuildDeskColumn(Transform parent, MaterialKit mats, float colX, bool chairEast, int columnIndex)
        {
            const float deskWidth = 1.2f;     // X extent (doubled — wider desks)
            float lengthZ = DeskZEnd - DeskZStart;
            float midZ = (DeskZStart + DeskZEnd) * 0.5f;

            // Desk top
            CreateBox(parent, $"Col{columnIndex}_DeskTop",
                new Vector3(colX, 0.75f, midZ),
                new Vector3(deskWidth, 0.06f, lengthZ), mats.DeskTop);

            // Legs along length
            int legPairs = 4;
            for (int li = 0; li <= legPairs; li++)
            {
                float lz = Mathf.Lerp(DeskZStart + 0.15f, DeskZEnd - 0.15f, (float)li / legPairs);
                CreateBox(parent, "Leg",
                    new Vector3(colX - deskWidth * 0.5f + 0.05f, 0.37f, lz),
                    new Vector3(0.06f, 0.74f, 0.06f), mats.MonitorBezel);
                CreateBox(parent, "Leg",
                    new Vector3(colX + deskWidth * 0.5f - 0.05f, 0.37f, lz),
                    new Vector3(0.06f, 0.74f, 0.06f), mats.MonitorBezel);
            }

            // 3 workstations per column, evenly spaced along Z
            int seats = 3;
            float spacingZ = lengthZ / seats;
            System.Random rng = new System.Random(columnIndex * 31 + 7);
            for (int i = 0; i < seats; i++)
            {
                float z = DeskZStart + spacingZ * (i + 0.5f);
                BuildColumnWorkstation(parent, mats, new Vector3(colX, 0, z), chairEast, rng);
            }
        }

        // Workstation oriented along a vertical column (desk runs Z, user faces ±X).
        static void BuildColumnWorkstation(Transform parent, MaterialKit mats, Vector3 origin, bool chairEast, System.Random rng)
        {
            float chairOffsetX  = chairEast ?  1.00f : -1.00f;
            float chairFacingY  = chairEast ?  270f  :  90f;    // chair back faces away from desk
            float monitorOffsetX = chairEast ? -0.40f :  0.40f; // monitor on far side, screen toward chair
            float kbOffsetX     = chairEast ?  0.25f : -0.25f;
            float screenSign    = chairEast ?  1f    : -1f;

            bool screenOn = rng.Next(0, 100) < 60;
            Material screenMat = screenOn ? mats.MonitorScreen : mats.MonitorOff;

            // Monitor — long axis along Z, thin along X (so screen faces ±X toward chair)
            var mon = CreateBox(parent, "Monitor",
                new Vector3(origin.x + monitorOffsetX, 1.25f, origin.z),
                new Vector3(0.06f, 0.55f, 0.85f), mats.MonitorBezel);

            CreateBox(parent, "Screen",
                new Vector3(origin.x + monitorOffsetX + screenSign * 0.04f, 1.25f, origin.z),
                new Vector3(0.04f, 0.46f, 0.78f), screenMat);

            CreateBox(parent, "MonitorStand",
                new Vector3(origin.x + monitorOffsetX, 0.92f, origin.z),
                new Vector3(0.16f, 0.20f, 0.16f), mats.MonitorBezel);
            CreateBox(parent, "MonitorBase",
                new Vector3(origin.x + monitorOffsetX, 0.81f, origin.z),
                new Vector3(0.20f, 0.02f, 0.30f), mats.MonitorBezel);

            // Keyboard runs along Z (long axis matches desk)
            CreateBox(parent, "Keyboard",
                new Vector3(origin.x + kbOffsetX, 0.81f, origin.z),
                new Vector3(0.18f, 0.03f, 0.5f), mats.MonitorBezel);
            CreateBox(parent, "Mouse",
                new Vector3(origin.x + kbOffsetX, 0.81f, origin.z + 0.32f),
                new Vector3(0.10f, 0.03f, 0.08f), mats.MonitorBezel);

            // Mug, sometimes
            if (rng.Next(0, 100) < 50)
            {
                var mug = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                mug.name = "Mug";
                mug.transform.SetParent(parent, true);
                mug.transform.position = new Vector3(origin.x + kbOffsetX, 0.88f, origin.z - 0.32f);
                mug.transform.localScale = new Vector3(0.13f, 0.09f, 0.13f);
                mug.GetComponent<MeshRenderer>().sharedMaterial = mats.Brass;
                Object.DestroyImmediate(mug.GetComponent<Collider>());
            }

            BuildChair(parent, mats,
                new Vector3(origin.x + chairOffsetX, 0, origin.z),
                facingY: chairFacingY);
        }

        static void BuildChair(Transform parent, MaterialKit mats, Vector3 pos, float facingY)
        {
            var chair = new GameObject("Chair");
            chair.transform.SetParent(parent, false);
            chair.transform.position = pos;
            chair.transform.rotation = Quaternion.Euler(0, facingY, 0);

            // Seat
            CreateBox(chair.transform, "Seat", pos + new Vector3(0, 0.5f, 0),
                new Vector3(0.6f, 0.10f, 0.6f), mats.ChairFabric);
            // Back (offset and orientation follow chair facing — thin face perpendicular to forward)
            Vector3 backOffset = chair.transform.rotation * new Vector3(0, 0.5f, -0.28f);
            var back = CreateBox(chair.transform, "Back", pos + backOffset,
                new Vector3(0.6f, 0.85f, 0.08f), mats.ChairFabric);
            back.transform.rotation = chair.transform.rotation;
            // Headrest
            Vector3 headOffset = chair.transform.rotation * new Vector3(0, 1.05f, -0.28f);
            var headrest = CreateBox(chair.transform, "Headrest", pos + headOffset,
                new Vector3(0.45f, 0.18f, 0.08f), mats.ChairFabric);
            headrest.transform.rotation = chair.transform.rotation;
            // Center column
            CreateBox(chair.transform, "Column", pos + new Vector3(0, 0.25f, 0),
                new Vector3(0.08f, 0.5f, 0.08f), mats.MonitorBezel);
            // Wheel base
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2f / 5f;
                var spoke = CreateBox(chair.transform, $"Spoke_{i}",
                    pos + new Vector3(Mathf.Cos(a) * 0.32f, 0.05f, Mathf.Sin(a) * 0.32f),
                    new Vector3(0.4f, 0.05f, 0.07f), mats.MonitorBezel);
                spoke.transform.rotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0);
            }
        }

        // ─── Decor ────────────────────────────────────────────────────────────
        static void BuildDecor(Transform parent, MaterialKit mats)
        {
            var dRoot = new GameObject("Decor").transform;
            dRoot.SetParent(parent, false);

            // KOKO red-circle logo on right wall
            BuildKokoSign(dRoot, mats, new Vector3(HalfX - 0.08f, 2.55f, -HalfZ + 2.5f));

            // Plants in dead spaces beyond the columns
            BuildPlant(dRoot, mats, new Vector3(-HalfX + 1.0f, 0, HalfZ - 1.0f));
            BuildPlant(dRoot, mats, new Vector3(HalfX - 1.0f, 0, HalfZ - 1.0f));
            BuildPlant(dRoot, mats, new Vector3(-HalfX + 1.0f, 0, -HalfZ + 6.0f));

            // Pooh plush on top of column 0 (leftmost) near the window end
            BuildPoohPlush(dRoot, mats, new Vector3(ColumnX(0), 0.81f, DeskZEnd - 0.8f));

            // Open laptop on column 7 middle workstation desk (z=9.3)
            BuildLaptop(dRoot, mats, new Vector3(ColumnX(7), 0.81f, 9.3f), 14f);

            // Backpack on the floor in a walking aisle (between cols 5 and 6) at the front
            float aisleX = (ColumnX(5) + ColumnX(6)) * 0.5f;
            CreateBox(dRoot, "Backpack",
                new Vector3(aisleX, 0.32f, -3f),
                new Vector3(0.30f, 0.65f, 0.40f), mats.BagFabric);
            CreateBox(dRoot, "BackpackStrap",
                new Vector3(aisleX, 0.55f, -3.18f),
                new Vector3(0.06f, 0.20f, 0.06f), mats.MonitorBezel);

            // Jacket draped over col 6's east chair backrest at workstation z=2.3
            var jacket = CreateBox(dRoot, "Jacket",
                new Vector3(ColumnX(6) + 1.28f, 1.05f, 2.3f),
                new Vector3(0.10f, 0.55f, 0.45f), mats.JacketWhite);
            jacket.transform.rotation = Quaternion.Euler(0, 0f, 8f);

            // Scattered desk figurines / mugs / clutter — spread across all columns
            System.Random rng = new System.Random(7);
            Material[] toy = { mats.KokoRed, mats.PoohYellow, mats.WindowGlow, mats.Leaf, mats.Brass, mats.JacketWhite };
            for (int i = 0; i < 24; i++)
            {
                int col = rng.Next(0, DeskColumns);
                float fx = ColumnX(col) + ((float)rng.NextDouble() - 0.5f) * 0.4f;
                float fz = DeskZStart + 0.6f + (float)rng.NextDouble() * (DeskZEnd - DeskZStart - 1.2f);
                CreateBox(dRoot, $"DeskItem_{i}",
                    new Vector3(fx, 0.86f, fz),
                    new Vector3(0.08f + (float)rng.NextDouble() * 0.05f,
                                0.10f + (float)rng.NextDouble() * 0.18f,
                                0.08f + (float)rng.NextDouble() * 0.05f),
                    toy[rng.Next(toy.Length)]);
            }
        }

        static void BuildKokoSign(Transform parent, MaterialKit mats, Vector3 wallPos)
        {
            // Red disc with white inner ring — visible from camera side (-X normal)
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "KokoRing";
            ring.transform.SetParent(parent, true);
            ring.transform.position = wallPos;
            ring.transform.rotation = Quaternion.Euler(0, 0, 90f);
            ring.transform.localScale = new Vector3(0.95f, 0.04f, 0.95f);
            ring.GetComponent<MeshRenderer>().sharedMaterial = mats.KokoRed;
            Object.DestroyImmediate(ring.GetComponent<Collider>());

            var dot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            dot.name = "KokoDot";
            dot.transform.SetParent(parent, true);
            dot.transform.position = wallPos + new Vector3(-0.05f, 0, 0);
            dot.transform.rotation = Quaternion.Euler(0, 0, 90f);
            dot.transform.localScale = new Vector3(0.55f, 0.04f, 0.55f);
            dot.GetComponent<MeshRenderer>().sharedMaterial = mats.Whiteboard;
            Object.DestroyImmediate(dot.GetComponent<Collider>());

            // Tiny accent dot in the center for the K-mark feel
            var inner = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            inner.name = "KokoInner";
            inner.transform.SetParent(parent, true);
            inner.transform.position = wallPos + new Vector3(-0.10f, 0, 0);
            inner.transform.rotation = Quaternion.Euler(0, 0, 90f);
            inner.transform.localScale = new Vector3(0.22f, 0.04f, 0.22f);
            inner.GetComponent<MeshRenderer>().sharedMaterial = mats.KokoRed;
            Object.DestroyImmediate(inner.GetComponent<Collider>());
        }

        static void BuildPoohPlush(Transform parent, MaterialKit mats, Vector3 pos)
        {
            var plush = new GameObject("PoohPlush");
            plush.transform.SetParent(parent, false);
            plush.transform.position = pos;

            void Sphere(string n, Vector3 lp, float s, Material m)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = n;
                go.transform.SetParent(plush.transform, false);
                go.transform.localPosition = lp;
                go.transform.localScale = Vector3.one * s;
                go.GetComponent<MeshRenderer>().sharedMaterial = m;
                Object.DestroyImmediate(go.GetComponent<Collider>());
            }

            Sphere("Body", new Vector3(0, 0.30f, 0), 0.55f, mats.PoohYellow);
            Sphere("Belly", new Vector3(0, 0.25f, -0.05f), 0.52f, mats.PoohYellow);
            Sphere("Shirt", new Vector3(0, 0.42f, 0), 0.52f, mats.KokoRed);
            Sphere("Head", new Vector3(0, 0.72f, 0), 0.45f, mats.PoohYellow);
            Sphere("EarL", new Vector3(-0.18f, 0.95f, 0), 0.18f, mats.PoohYellow);
            Sphere("EarR", new Vector3(0.18f, 0.95f, 0), 0.18f, mats.PoohYellow);
            Sphere("Snout", new Vector3(0, 0.66f, -0.20f), 0.20f, mats.PoohYellow);
            Sphere("Nose", new Vector3(0, 0.68f, -0.30f), 0.07f, mats.PoohBrown);
            Sphere("EyeL", new Vector3(-0.10f, 0.78f, -0.18f), 0.05f, mats.PoohBrown);
            Sphere("EyeR", new Vector3(0.10f, 0.78f, -0.18f), 0.05f, mats.PoohBrown);
        }

        static void BuildLaptop(Transform parent, MaterialKit mats, Vector3 pos, float tiltDeg)
        {
            // Base flat on desk
            CreateBox(parent, "LaptopBase",
                pos + new Vector3(0, 0.02f, 0),
                new Vector3(0.6f, 0.04f, 0.42f), mats.LaptopSilver);

            // Lid as a single box tilted backward (toward -Z so the screen faces the camera)
            var lid = CreateBox(parent, "LaptopLid",
                pos + new Vector3(0, 0.22f, 0.14f),
                new Vector3(0.6f, 0.36f, 0.04f), mats.LaptopSilver);
            lid.transform.rotation = Quaternion.Euler(-tiltDeg, 0, 0);

            var face = CreateBox(parent, "LaptopScreenFace",
                pos + new Vector3(0, 0.22f, 0.11f),
                new Vector3(0.55f, 0.32f, 0.01f), mats.MonitorScreen);
            face.transform.rotation = Quaternion.Euler(-tiltDeg, 0, 0);
        }

        static void BuildPlant(Transform parent, MaterialKit mats, Vector3 pos)
        {
            var plant = new GameObject("Plant");
            plant.transform.SetParent(parent, false);
            plant.transform.position = pos;

            var pot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pot.name = "Pot";
            pot.transform.SetParent(plant.transform, false);
            pot.transform.localPosition = new Vector3(0, 0.3f, 0);
            pot.transform.localScale = new Vector3(0.7f, 0.3f, 0.7f);
            pot.GetComponent<MeshRenderer>().sharedMaterial = mats.Pot;
            // Replace the default mesh collider with a cheaper capsule that still blocks the player.
            Object.DestroyImmediate(pot.GetComponent<Collider>());
            var potCol = pot.AddComponent<CapsuleCollider>();
            potCol.height = 1f;
            potCol.radius = 0.5f;

            for (int i = 0; i < 5; i++)
            {
                var leaf = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                leaf.name = $"Leaf_{i}";
                leaf.transform.SetParent(plant.transform, false);
                leaf.transform.localPosition = new Vector3(
                    Mathf.Cos(i * 1.2f) * 0.25f,
                    0.85f + Mathf.Sin(i * 0.7f) * 0.15f,
                    Mathf.Sin(i * 1.2f) * 0.25f);
                leaf.transform.localScale = Vector3.one * (0.55f + (i % 2) * 0.12f);
                leaf.GetComponent<MeshRenderer>().sharedMaterial = mats.Leaf;
                Object.DestroyImmediate(leaf.GetComponent<Collider>());
            }
        }

        // ─── Player character (4-direction HD2D billboard sprite) ─────────────
        static GameObject BuildPlayer(Transform parent, MaterialKit mats)
        {
            var player = new GameObject("Player");
            player.transform.SetParent(parent, false);
            // Spawn in the central walking aisle (between cols 5 and 6) at the front
            // of the room so the player isn't stuck inside a desk.
            float aisleX = (ColumnX(5) + ColumnX(6)) * 0.5f;
            player.transform.position = new Vector3(aisleX, 0, DeskZStart - 0.5f);
            player.tag = "Player";

            var spriteRoot = new GameObject("SpriteRoot");
            spriteRoot.transform.SetParent(player.transform, false);
            spriteRoot.transform.localPosition = Vector3.zero;
            var dirBillboard = spriteRoot.AddComponent<DirectionalBillboardSprite>();

            var sprite = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sprite.name = "CharacterSprite";
            sprite.transform.SetParent(spriteRoot.transform, false);
            sprite.transform.localPosition = new Vector3(0, 0.85f, 0);
            sprite.transform.localScale = new Vector3(1.1f, 1.7f, 1f);
            Object.DestroyImmediate(sprite.GetComponent<Collider>());
            var spriteRenderer = sprite.GetComponent<MeshRenderer>();
            spriteRenderer.sharedMaterial = mats.CharacterSprite;

            // Wire 4-direction sprite swapping
            dirBillboard.spriteRenderer = spriteRenderer;
            dirBillboard.spriteQuad = sprite.transform;
            dirBillboard.frontMaterial = mats.CharacterSprite;
            dirBillboard.backMaterial = mats.CharacterBack;
            dirBillboard.sideMaterial = mats.CharacterSide;

            var shadow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            shadow.name = "ShadowBlob";
            shadow.transform.SetParent(player.transform, false);
            shadow.transform.localPosition = new Vector3(0, 0.02f, 0);
            shadow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shadow.transform.localScale = new Vector3(1.0f, 1.0f, 1f);
            Object.DestroyImmediate(shadow.GetComponent<Collider>());
            shadow.GetComponent<MeshRenderer>().sharedMaterial = mats.Shadow;

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.6f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.8f, 0f);
            cc.skinWidth = 0.04f;
            cc.minMoveDistance = 0f;
            cc.stepOffset = 0.2f;

            var ctrl = player.AddComponent<OctopathPlayerController>();
            ctrl.spriteRoot = spriteRoot.transform;
            ctrl.spriteVisual = sprite.transform;   // quad bobs up/down while walking
            dirBillboard.controller = ctrl;

            return player;
        }

        // ─── Lighting (indoor office: floating fluorescent panels + soft window) ──
        static void BuildLighting(Transform parent, MaterialKit mats)
        {
            var lightRoot = new GameObject("Lighting").transform;
            lightRoot.SetParent(parent, false);

            // Invisible point lights in a 5x3 grid covering the larger room — gives flat,
            // even office illumination without any visible fixture.
            float[] gridX = { -24f, -12f, 0f, 12f, 24f };
            float[] gridZ = { -12f, 0f, 12f };
            int li = 0;
            foreach (float gx in gridX)
            foreach (float gz in gridZ)
            {
                var go = new GameObject($"FluorLight_{li++}");
                go.transform.SetParent(lightRoot, false);
                go.transform.position = new Vector3(gx, RoomHeight - 0.3f, gz);
                var pt = go.AddComponent<Light>();
                pt.type = LightType.Point;
                pt.color = new Color(1.0f, 0.98f, 0.94f);   // neutral office white
                pt.intensity = 4.5f;
                pt.range = 12f;
                pt.shadows = LightShadows.None;
            }

            // Soft cool window light — gives subtle directional shape
            var keyGO = new GameObject("KeyLight (Window)");
            keyGO.transform.SetParent(lightRoot, false);
            keyGO.transform.rotation = Quaternion.Euler(45f, -12f, 0f);
            var key = keyGO.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(0.88f, 0.94f, 1.0f);
            key.intensity = 0.45f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.45f;

            // Even, slightly cool indoor ambient
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.58f, 0.60f, 0.64f);
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.50f, 0.50f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.30f, 0.30f);
            RenderSettings.fog = false;
        }

        // ─── Camera ───────────────────────────────────────────────────────────
        static void BuildCamera(Transform parent, Transform follow)
        {
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (c == null) continue;
                if (c.gameObject.CompareTag("MainCamera")) Object.DestroyImmediate(c.gameObject);
            }
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (v == null) continue;
                if (v.isGlobal) Object.DestroyImmediate(v.gameObject);
            }

            var camGO = new GameObject("MainCamera");
            camGO.tag = "MainCamera";
            camGO.transform.SetParent(parent, false);
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.12f, 0.16f);
            cam.allowHDR = true;
            cam.allowMSAA = true;

            var data = camGO.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;

            camGO.AddComponent<AudioListener>();

            var oc = camGO.AddComponent<OctopathCamera>();
            oc.target = follow;
            oc.pitch = 28f;                  // Octopath-like horizontal-ish tilt
            oc.yaw = 0f;                     // head-on, no oblique angle
            oc.distance = 18f;
            oc.fov = 24f;                    // low FOV → diorama / tilt-shift feel
            oc.positionSmoothTime = 0f;      // no lag — player locked to screen center
            oc.rotationSmoothTime = 0f;
            oc.allowMouseOrbit = false;      // fixed angle, no accidental rotation
            oc.allowScrollZoom = true;       // scroll still allowed for in/out
            oc.targetOffset = new Vector3(0f, 0.9f, 0f);
        }

        // ─── Volume ──────────────────────────────────────────────────────────
        static void BuildPostProcessVolume(Transform parent)
        {
            var profile = OctopathPostProcessSetup.CreateOrUpdateProfile();
            var volGO = new GameObject("PostProcessVolume");
            volGO.transform.SetParent(parent, false);
            volGO.layer = 0;
            var vol = volGO.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1;
            vol.profile = profile;
        }

        // ─── Helpers ─────────────────────────────────────────────────────────
        static void EnsureFolder(string assetsRelativePath)
        {
            if (AssetDatabase.IsValidFolder(assetsRelativePath)) return;
            string parent = Path.GetDirectoryName(assetsRelativePath).Replace('\\', '/');
            string leaf = Path.GetFileName(assetsRelativePath);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static GameObject CreateBox(Transform parent, string name, Vector3 worldPos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.position = worldPos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        // ─── Materials ───────────────────────────────────────────────────────
        class MaterialKit
        {
            public Material FloorLight, FloorDark, CarpetAccent;
            public Material Wall, Wood, Door, Brass;
            public Material WindowFrame, WindowGlow, BlindsDark;
            public Material Whiteboard, Cabinet;
            public Material MonitorBezel, MonitorScreen, MonitorOff;
            public Material ChairFabric;
            public Material Pot, Leaf, Carpet;
            public Material CeilingTile, CeilingLight;
            public Material DeskTop, DividerFabric;
            public Material KokoRed, PoohYellow, PoohBrown;
            public Material LaptopSilver, BagFabric, JacketWhite;
            public Material Shadow, CharacterSprite, CharacterBack, CharacterSide;

            public static MaterialKit Build()
            {
                var k = new MaterialKit
                {
                    FloorLight     = MakeLit("FloorLight",     new Color(0.72f, 0.62f, 0.32f)),
                    FloorDark      = MakeLit("FloorDark",      new Color(0.50f, 0.42f, 0.20f)),
                    CarpetAccent   = MakeLit("CarpetAccent",   new Color(0.68f, 0.30f, 0.22f)),
                    Wall           = MakeLit("Wall",           new Color(0.86f, 0.84f, 0.80f)),
                    Wood           = MakeLit("Wood",           new Color(0.34f, 0.22f, 0.15f)),
                    Door           = MakeLit("Door",           new Color(0.28f, 0.18f, 0.12f)),
                    Brass          = MakeLit("Brass",          new Color(0.85f, 0.65f, 0.30f), metallic: 0.85f, smoothness: 0.7f),
                    WindowFrame    = MakeLit("WindowFrame",    new Color(0.16f, 0.16f, 0.18f)),
                    WindowGlow     = MakeEmissive("WindowGlow", new Color(1.0f, 0.96f, 0.85f), 2.0f),
                    BlindsDark     = MakeLit("BlindsDark",     new Color(0.10f, 0.11f, 0.13f), smoothness: 0.05f),
                    Whiteboard     = MakeLit("Whiteboard",     new Color(0.96f, 0.96f, 0.94f), smoothness: 0.5f),
                    Cabinet        = MakeLit("Cabinet",        new Color(0.40f, 0.45f, 0.50f), smoothness: 0.3f),
                    MonitorBezel   = MakeLit("MonitorBezel",   new Color(0.10f, 0.10f, 0.12f), smoothness: 0.4f),
                    MonitorScreen  = MakeEmissive("MonitorScreen", new Color(0.45f, 0.72f, 1.0f), 1.2f),
                    MonitorOff     = MakeLit("MonitorOff",     new Color(0.04f, 0.04f, 0.05f), smoothness: 0.6f),
                    ChairFabric    = MakeLit("ChairFabric",    new Color(0.08f, 0.08f, 0.10f), smoothness: 0.05f),
                    Pot            = MakeLit("Pot",            new Color(0.55f, 0.30f, 0.20f)),
                    Leaf           = MakeLit("Leaf",           new Color(0.25f, 0.55f, 0.30f), smoothness: 0.05f),
                    Carpet         = MakeLit("Carpet",         new Color(0.55f, 0.20f, 0.20f), smoothness: 0.0f),
                    CeilingTile    = MakeLit("CeilingTile",    new Color(0.92f, 0.92f, 0.92f), smoothness: 0.05f),
                    CeilingLight   = MakeEmissive("CeilingLight", new Color(1.0f, 0.98f, 0.92f), 2.5f),
                    DeskTop        = MakeLit("DeskTop",        new Color(0.86f, 0.80f, 0.68f), smoothness: 0.25f),
                    DividerFabric  = MakeLit("DividerFabric",  new Color(0.55f, 0.52f, 0.48f), smoothness: 0.05f),
                    KokoRed        = MakeEmissive("KokoRed",   new Color(0.92f, 0.18f, 0.20f), 0.6f),
                    PoohYellow     = MakeLit("PoohYellow",     new Color(0.95f, 0.78f, 0.30f), smoothness: 0.05f),
                    PoohBrown      = MakeLit("PoohBrown",      new Color(0.18f, 0.10f, 0.06f)),
                    LaptopSilver   = MakeLit("LaptopSilver",   new Color(0.78f, 0.78f, 0.80f), metallic: 0.5f, smoothness: 0.5f),
                    BagFabric      = MakeLit("BagFabric",      new Color(0.45f, 0.43f, 0.42f), smoothness: 0.05f),
                    JacketWhite    = MakeLit("JacketWhite",    new Color(0.92f, 0.88f, 0.80f), smoothness: 0.05f),
                    Shadow         = MakeShadow(),
                    CharacterSprite = MakeCharacterSpriteMat("CharacterSprite", "CharacterSprite", CharView.Front),
                    CharacterBack   = MakeCharacterSpriteMat("CharacterBack",   "CharacterBack",   CharView.Back),
                    CharacterSide   = MakeCharacterSpriteMat("CharacterSide",   "CharacterSide",   CharView.Side),
                };
                AssetDatabase.SaveAssets();
                return k;
            }

            static Material MakeLit(string name, Color color, float metallic = 0f, float smoothness = 0.15f)
            {
                string path = $"{MaterialsFolder}/{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }
                else
                {
                    mat.shader = shader;
                }
                mat.SetColor("_BaseColor", color);
                mat.SetColor("_Color", color);
                mat.SetFloat("_Metallic", metallic);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetFloat("_Glossiness", smoothness);
                EditorUtility.SetDirty(mat);
                return mat;
            }

            static Material MakeEmissive(string name, Color color, float intensity)
            {
                var mat = MakeLit(name, color, 0f, 0.6f);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetColor("_EmissionColor", color * intensity);
                EditorUtility.SetDirty(mat);
                return mat;
            }

            static Material MakeShadow()
            {
                string path = $"{MaterialsFolder}/Shadow.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }
                else mat.shader = shader;

                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

                string texPath = $"{TexturesFolder}/ShadowBlob.asset";
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                if (tex == null)
                {
                    tex = MakeRadialAlpha(64);
                    AssetDatabase.CreateAsset(tex, texPath);
                }
                mat.SetTexture("_BaseMap", tex);
                mat.SetTexture("_MainTex", tex);
                mat.SetColor("_BaseColor", new Color(0, 0, 0, 0.55f));
                mat.SetColor("_Color", new Color(0, 0, 0, 0.55f));
                EditorUtility.SetDirty(mat);
                return mat;
            }

            enum CharView { Front, Back, Side }

            static Material MakeCharacterSpriteMat(string matName, string texName, CharView view)
            {
                string path = $"{MaterialsFolder}/{matName}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Transparent");
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }
                else mat.shader = shader;

                mat.SetFloat("_Surface", 0f);
                mat.SetOverrideTag("RenderType", "TransparentCutout");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                mat.SetInt("_ZWrite", 1);
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.5f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                // Render both sides so localScale.x = -1 horizontal flipping (for left-facing
                // side view) is still visible to the camera.
                mat.SetFloat("_Cull", 0f);

                string texPath = $"{TexturesFolder}/{texName}.asset";
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                if (tex == null)
                {
                    tex = MakeCharacterTexture(64, 96, view);
                    AssetDatabase.CreateAsset(tex, texPath);
                }
                mat.SetTexture("_BaseMap", tex);
                mat.SetTexture("_MainTex", tex);
                mat.SetColor("_BaseColor", Color.white);
                mat.SetColor("_Color", Color.white);
                EditorUtility.SetDirty(mat);
                return mat;
            }

            static Texture2D MakeRadialAlpha(int size)
            {
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), c) / (size * 0.5f);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a;
                    tex.SetPixel(x, y, new Color(0, 0, 0, a));
                }
                tex.Apply();
                return tex;
            }

            static Texture2D MakeCharacterTexture(int w, int h, CharView view)
            {
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Point;
                tex.wrapMode = TextureWrapMode.Clamp;

                Color clear     = new Color(0, 0, 0, 0);
                Color skin      = new Color(0.96f, 0.80f, 0.65f);
                Color skinShade = new Color(0.78f, 0.60f, 0.46f);
                Color hair      = new Color(0.30f, 0.18f, 0.10f);
                Color hairShade = new Color(0.20f, 0.10f, 0.05f);
                Color tunic     = new Color(0.22f, 0.40f, 0.62f);
                Color tunicSh   = new Color(0.14f, 0.27f, 0.45f);
                Color belt      = new Color(0.45f, 0.28f, 0.15f);
                Color pants     = new Color(0.18f, 0.13f, 0.08f);
                Color boots     = new Color(0.12f, 0.08f, 0.05f);
                Color outline   = new Color(0.05f, 0.04f, 0.10f);
                Color cape      = new Color(0.55f, 0.18f, 0.22f);
                Color capeSh    = new Color(0.38f, 0.10f, 0.14f);

                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) tex.SetPixel(x, y, clear);

                if (view == CharView.Front)
                {
                    // Body
                    FillRect(tex, 22, 38, 20, 22, tunic);
                    FillRect(tex, 22, 38, 20, 8, tunicSh);
                    FillRect(tex, 22, 36, 20, 3, belt);
                    FillRect(tex, 24, 18, 16, 18, pants);
                    FillRect(tex, 22, 12, 8, 7, boots);
                    FillRect(tex, 34, 12, 8, 7, boots);
                    // Cape edges peeking out either side
                    FillRect(tex, 16, 36, 6, 30, cape);
                    FillRect(tex, 42, 36, 6, 30, cape);
                    FillRect(tex, 16, 36, 6, 12, capeSh);
                    FillRect(tex, 42, 36, 6, 12, capeSh);
                    // Arms + hands
                    FillRect(tex, 16, 44, 6, 16, tunic);
                    FillRect(tex, 42, 44, 6, 16, tunic);
                    FillRect(tex, 17, 40, 4, 5, skin);
                    FillRect(tex, 43, 40, 4, 5, skin);
                    // Head + face
                    FillRect(tex, 24, 60, 16, 18, skin);
                    FillRect(tex, 24, 60, 16, 5, skinShade);
                    FillRect(tex, 22, 72, 20, 10, hair);
                    FillRect(tex, 22, 78, 20, 4, hairShade);
                    FillRect(tex, 22, 65, 4, 12, hair);
                    FillRect(tex, 38, 65, 4, 12, hair);
                    // Eyes + mouth
                    FillRect(tex, 27, 70, 3, 3, outline);
                    FillRect(tex, 34, 70, 3, 3, outline);
                    FillRect(tex, 30, 65, 4, 1, outline);
                }
                else if (view == CharView.Back)
                {
                    // Body silhouette identical to front
                    FillRect(tex, 22, 38, 20, 22, tunic);
                    FillRect(tex, 22, 38, 20, 8, tunicSh);
                    FillRect(tex, 22, 36, 20, 3, belt);
                    FillRect(tex, 24, 18, 16, 18, pants);
                    FillRect(tex, 22, 12, 8, 7, boots);
                    FillRect(tex, 34, 12, 8, 7, boots);
                    // Cape covers most of the back
                    FillRect(tex, 16, 18, 32, 50, cape);
                    FillRect(tex, 16, 56, 32, 12, capeSh);
                    FillRect(tex, 16, 18, 32, 6, capeSh);
                    // Arms + hands (visible at sides)
                    FillRect(tex, 16, 44, 6, 16, tunic);
                    FillRect(tex, 42, 44, 6, 16, tunic);
                    FillRect(tex, 17, 40, 4, 5, skin);
                    FillRect(tex, 43, 40, 4, 5, skin);
                    // Head — back of skull is all hair, no facial features
                    FillRect(tex, 24, 60, 16, 18, hair);
                    FillRect(tex, 24, 60, 16, 4, hairShade);
                    FillRect(tex, 22, 72, 20, 10, hair);
                    FillRect(tex, 22, 78, 20, 4, hairShade);
                    FillRect(tex, 22, 65, 4, 12, hair);
                    FillRect(tex, 38, 65, 4, 12, hair);
                    // Hint of neck shading at the bottom of the head
                    FillRect(tex, 26, 60, 12, 2, hairShade);
                }
                else // CharView.Side — profile facing screen-right
                {
                    // Narrower torso
                    FillRect(tex, 24, 38, 16, 22, tunic);
                    FillRect(tex, 24, 38, 16, 8, tunicSh);
                    FillRect(tex, 24, 36, 16, 3, belt);
                    FillRect(tex, 26, 18, 12, 18, pants);
                    FillRect(tex, 25, 12, 7, 7, boots);
                    FillRect(tex, 32, 12, 7, 7, boots);
                    // Cape trailing behind (left side of texture = back)
                    FillRect(tex, 18, 36, 8, 30, cape);
                    FillRect(tex, 18, 36, 8, 14, capeSh);
                    // Forward-swung arm
                    FillRect(tex, 38, 44, 5, 16, tunic);
                    FillRect(tex, 39, 40, 4, 5, skin);
                    // Head profile
                    FillRect(tex, 26, 60, 14, 18, skin);
                    FillRect(tex, 26, 60, 14, 4, skinShade);
                    FillRect(tex, 26, 72, 14, 10, hair);
                    FillRect(tex, 26, 78, 14, 4, hairShade);
                    FillRect(tex, 24, 65, 4, 13, hair);   // back of hair
                    // Single eye + mouth on the front side of the face
                    FillRect(tex, 34, 70, 3, 3, outline);
                    FillRect(tex, 36, 65, 2, 1, outline);
                    // Subtle nose hint
                    FillRect(tex, 39, 67, 1, 2, skinShade);
                }

                AddOutline(tex, outline);

                tex.Apply();
                return tex;
            }

            static void FillRect(Texture2D t, int x0, int y0, int w, int h, Color c)
            {
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int px = x0 + x, py = y0 + y;
                    if (px < 0 || px >= t.width || py < 0 || py >= t.height) continue;
                    t.SetPixel(px, py, c);
                }
            }

            static void AddOutline(Texture2D t, Color outline)
            {
                int w = t.width, h = t.height;
                var src = t.GetPixels();
                var dst = (Color[])src.Clone();

                for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int idx = y * w + x;
                    if (src[idx].a > 0.01f) continue;
                    bool neighborOpaque =
                        src[(y) * w + (x - 1)].a > 0.01f ||
                        src[(y) * w + (x + 1)].a > 0.01f ||
                        src[(y - 1) * w + x].a > 0.01f ||
                        src[(y + 1) * w + x].a > 0.01f;
                    if (neighborOpaque) dst[idx] = outline;
                }
                t.SetPixels(dst);
            }
        }
    }
}
