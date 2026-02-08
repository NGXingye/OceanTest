using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using UnityEngine;
using UnityEngine.Rendering;

namespace OceanQuest
{
    public partial class Water
    {
        public class Element
        {
            public Transform transform;
            public MeshRenderer meshRenderer;

            public Element(Transform trans, MeshRenderer mesh)
            {
                this.transform = trans;
                this.meshRenderer = mesh;
            }
        }

        [System.Flags]
        enum Seams
        {
            None = 0,
            Left = 1,
            Right = 2,
            Top = 4,
            Bottom = 8,
            All = Left | Right | Top | Bottom
        };

        #region 字段
        [Label("网格尺寸")][SerializeField] float lengthScale = 100f;//网格尺寸
        [SerializeField, Range(1, 40)] int vertexDensity = 30;//网格密度
        [SerializeField, Range(0, 8)] int clipLevels = 8;//层级数量
        [SerializeField, Range(0, 100)] float skirtSize = 50f;//边缘尺寸

        [SerializeField] Transform viewer;
        [SerializeField] Material waterMat;

        List<Element> rings = new List<Element>();
        List<Element> trims = new List<Element>();
        Element center;
        Element skirt;
        Quaternion[] trimRotations;
        int previousVertexDensity;
        float previousSkirtSize;
        [SerializeField] Material[] materials;

        #region 公开接口
        public Transform geoRoot { get; private set; }
        public void SetViewer(Transform t) => viewer = t;
        public Transform GetViewer() => viewer;

        #endregion

        #region debug网格显示
        [Header("网格4级Lod")]
        [Label("中心最密网格")][SerializeField] Material debugCenterMat;
        [Label("围绕中心的网格")][SerializeField] Material debugRingMat;
        [Label("L形填补网格")][SerializeField] Material debugTirmMat;
        [Label("最外围网格")][SerializeField] Material debugSkirtMat;
        #endregion

        #endregion

        #region 网格创建
        Mesh CreatePlaneMesh(int width, int height, float lengthScale, Seams seams = Seams.None, int trianglesShift = 0)
        {
            Mesh mesh = new Mesh();
            mesh.name = "Plane Mesh";
            if ((width + 1) * (height + 1) >= 256 * 256)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            Vector3[] vertices = new Vector3[(width + 1) * (height + 1)];
            int[] triangles = new int[width * height * 2 * 3];
            Vector3[] normals = new Vector3[(width + 1) * (height + 1)];

            for (int i = 0; i < height + 1; i++)
            {
                for (int j = 0; j < width + 1; j++)
                {
                    int x = j;
                    int z = i;

                    if ((i == 0 && seams.HasFlag(Seams.Bottom)) || (i == height && seams.HasFlag(Seams.Top)))
                        x = x / 2 * 2;
                    if ((j == 0 && seams.HasFlag(Seams.Left)) || (j == width && seams.HasFlag(Seams.Right)))
                        z = z / 2 * 2;

                    vertices[j + i * (width + 1)] = new Vector3(x, 0, z) * lengthScale;
                    normals[j + i * (width + 1)] = Vector3.up;
                }
            }

            int tris = 0;
            for (int i = 0; i < height; i++)
            {
                for (int j = 0; j < width; j++)
                {
                    int k = j + i * (width + 1);
                    if ((i + j + trianglesShift) % 2 == 0)
                    {
                        triangles[tris++] = k;
                        triangles[tris++] = k + width + 1;
                        triangles[tris++] = k + width + 2;

                        triangles[tris++] = k;
                        triangles[tris++] = k + width + 2;
                        triangles[tris++] = k + 1;
                    }
                    else
                    {
                        triangles[tris++] = k;
                        triangles[tris++] = k + width + 1;
                        triangles[tris++] = k + 1;

                        triangles[tris++] = k + 1;
                        triangles[tris++] = k + width + 1;
                        triangles[tris++] = k + width + 2;
                    }
                }
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.normals = normals;
            return mesh;
        }
        Mesh CreateRingMesh(int k, float lengthScale)
        {
            Mesh mesh = new Mesh();
            mesh.name = "Clip Ring Mesh";
            if ((2 * k + 1) * (2 * k + 1) >= 256 * 256)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            CombineInstance[] combine = new CombineInstance[4];

            combine[0].mesh = CreatePlaneMesh(2 * k, (k - 1) / 2, lengthScale, Seams.Bottom | Seams.Right | Seams.Left);
            combine[0].transform = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one);

            combine[1].mesh = CreatePlaneMesh(2 * k, (k - 1) / 2, lengthScale, Seams.Top | Seams.Right | Seams.Left);
            combine[1].transform = Matrix4x4.TRS(new Vector3(0, 0, k + 1 + (k - 1) / 2) * lengthScale, Quaternion.identity, Vector3.one);

            combine[2].mesh = CreatePlaneMesh((k - 1) / 2, k + 1, lengthScale, Seams.Left);
            combine[2].transform = Matrix4x4.TRS(new Vector3(0, 0, (k - 1) / 2) * lengthScale, Quaternion.identity, Vector3.one);

            combine[3].mesh = CreatePlaneMesh((k - 1) / 2, k + 1, lengthScale, Seams.Right);
            combine[3].transform = Matrix4x4.TRS(new Vector3(k + 1 + (k - 1) / 2, 0, (k - 1) / 2) * lengthScale, Quaternion.identity, Vector3.one);

            mesh.CombineMeshes(combine, true);
            return mesh;
        }
        Mesh CreateTrimMesh(int k, float lengthScale)
        {
            Mesh mesh = new Mesh();
            mesh.name = "Clip Trim Mesh";
            CombineInstance[] combine = new CombineInstance[2];

            combine[0].mesh = CreatePlaneMesh(k + 1, 1, lengthScale, Seams.None, 1);
            combine[0].transform = Matrix4x4.TRS(new Vector3(-k - 1, 0, -1) * lengthScale, Quaternion.identity, Vector3.one);

            combine[1].mesh = CreatePlaneMesh(1, k, lengthScale, Seams.None, 1);
            combine[1].transform = Matrix4x4.TRS(new Vector3(-1, 0, -k - 1) * lengthScale, Quaternion.identity, Vector3.one);

            mesh.CombineMeshes(combine, true);
            return mesh;
        }
        Mesh CreateSkirtMesh(int k, float outerBorderScale)
        {
            Mesh mesh = new Mesh();
            mesh.name = "Clip Skirt Mesh";
            CombineInstance[] combine = new CombineInstance[8];

            Mesh quad = CreatePlaneMesh(1, 1, 1);
            Mesh hStrip = CreatePlaneMesh(k, 1, 1);
            Mesh vStrip = CreatePlaneMesh(1, k, 1);


            Vector3 cornerQuadScale = new Vector3(outerBorderScale, 1, outerBorderScale);
            Vector3 midQuadScaleVert = new Vector3(1f / k, 1, outerBorderScale);
            Vector3 midQuadScaleHor = new Vector3(outerBorderScale, 1, 1f / k);

            combine[0].transform = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, cornerQuadScale);
            combine[0].mesh = quad;

            combine[1].transform = Matrix4x4.TRS(Vector3.right * outerBorderScale, Quaternion.identity, midQuadScaleVert);
            combine[1].mesh = hStrip;

            combine[2].transform = Matrix4x4.TRS(Vector3.right * (outerBorderScale + 1), Quaternion.identity, cornerQuadScale);
            combine[2].mesh = quad;

            combine[3].transform = Matrix4x4.TRS(Vector3.forward * outerBorderScale, Quaternion.identity, midQuadScaleHor);
            combine[3].mesh = vStrip;

            combine[4].transform = Matrix4x4.TRS(Vector3.right * (outerBorderScale + 1)
                + Vector3.forward * outerBorderScale, Quaternion.identity, midQuadScaleHor);
            combine[4].mesh = vStrip;

            combine[5].transform = Matrix4x4.TRS(Vector3.forward * (outerBorderScale + 1), Quaternion.identity, cornerQuadScale);
            combine[5].mesh = quad;

            combine[6].transform = Matrix4x4.TRS(Vector3.right * outerBorderScale
                + Vector3.forward * (outerBorderScale + 1), Quaternion.identity, midQuadScaleVert);
            combine[6].mesh = hStrip;

            combine[7].transform = Matrix4x4.TRS(Vector3.right * (outerBorderScale + 1)
                + Vector3.forward * (outerBorderScale + 1), Quaternion.identity, cornerQuadScale);
            combine[7].mesh = quad;
            mesh.CombineMeshes(combine, true);
            return mesh;
        }
        #endregion

        #region 初始化相关
        Element InstantiateElement(string name, Mesh mesh, Material mat)
        {
            GameObject obj = new GameObject();
            obj.name = name;
            //obj.hideFlags = HideFlags.HideAndDontSave;
            obj.layer = gameObject.layer;
            obj.transform.SetParent(geoRoot);
            obj.transform.localPosition = Vector3.zero;
            MeshFilter meshFilter = obj.AddComponent<MeshFilter>();
            meshFilter.mesh = mesh;
            MeshRenderer meshRenderer = obj.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = true;
            meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            meshRenderer.material = mat;
            meshRenderer.allowOcclusionWhenDynamic = false;
            return new Element(obj.transform, meshRenderer);
        }
        //初始化网格
        private void InstantiateMeshes()
        {
            CleanMeshes();

            rings.Clear();
            trims.Clear();

            GameObject root = new GameObject("Geometry Root");//

            //root.hideFlags = HideFlags.HideAndDontSave;
            root.transform.parent = transform;
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            geoRoot = root.transform;

            int k = GridSize();
            center = InstantiateElement("Center", CreatePlaneMesh(2 * k, 2 * k, 1, Seams.All), materials[materials.Length - 1]);
            Mesh ring = CreateRingMesh(k, 1);
            Mesh trim = CreateTrimMesh(k, 1);

            for (int i = 0; i < clipLevels; i++)
            {
                rings.Add(InstantiateElement("Ring " + i, ring, materials[materials.Length - 1]));
                trims.Add(InstantiateElement("Trim " + i, trim, materials[materials.Length - 1]));
            }
            skirt = InstantiateElement("Skirt", CreateSkirtMesh(k, skirtSize), materials[materials.Length - 1]);
        }
        private void InitGeometry()
        {
            if (viewer == null)
                viewer = Camera.main.transform;

            trimRotations = new Quaternion[]
            {
            Quaternion.AngleAxis(180, Vector3.up),
            Quaternion.AngleAxis(90, Vector3.up),
            Quaternion.AngleAxis(270, Vector3.up),
            Quaternion.identity,
            };
        
            InstantiateMeshes();
        }
       
        #endregion

        #region 更新相关
        void UpdatePositions()
        {
            int k = GridSize();
            int activeLevels = ActiveLodlevels();

            float scale = ClipLevelScale(-1, activeLevels);
            Vector3 previousSnappedPosition = Snap(viewer.position, scale * 2);
            center.transform.position = previousSnappedPosition + OffsetFromCenter(-1, activeLevels);
            center.transform.localScale = new Vector3(scale, 1, scale);

            for (int i = 0; i < clipLevels; i++)
            {
                rings[i].transform.gameObject.SetActive(i < activeLevels);
                trims[i].transform.gameObject.SetActive(i < activeLevels);
                if (i >= activeLevels) continue;

                scale = ClipLevelScale(i, activeLevels);
                Vector3 centerOffset = OffsetFromCenter(i, activeLevels);
                Vector3 snappedPosition = Snap(viewer.position, scale * 2);

                Vector3 trimPosition = centerOffset + snappedPosition + scale * (k - 1) / 2 * new Vector3(1, 0, 1);
                int shiftX = previousSnappedPosition.x - snappedPosition.x < float.Epsilon ? 1 : 0;
                int shiftZ = previousSnappedPosition.z - snappedPosition.z < float.Epsilon ? 1 : 0;
                trimPosition += shiftX * (k + 1) * scale * Vector3.right;
                trimPosition += shiftZ * (k + 1) * scale * Vector3.forward;
                trims[i].transform.position = trimPosition;
                trims[i].transform.rotation = trimRotations[shiftX + 2 * shiftZ];
                trims[i].transform.localScale = new Vector3(scale, 1, scale);

                rings[i].transform.position = snappedPosition + centerOffset;
                rings[i].transform.localScale = new Vector3(scale, 1, scale);
                previousSnappedPosition = snappedPosition;
            }

            scale = lengthScale * 2 * Mathf.Pow(2, clipLevels);
            skirt.transform.position = new Vector3(-1, 0, -1) * scale * (skirtSize + 0.5f - 0.5f / GridSize()) + previousSnappedPosition;
            skirt.transform.localScale = new Vector3(scale, 1, scale);
        }
        void UpdateMaterials()
        {
            center.meshRenderer.material = waterMat;

            for (int i = 0; i < rings.Count; i++)
            {
                rings[i].meshRenderer.material = waterMat;
                trims[i].meshRenderer.material = waterMat;
            }

            skirt.meshRenderer.material = waterMat;
        }
        void UpdateGeometry()
        {
            if (rings.Count != clipLevels || trims.Count != clipLevels
                || previousVertexDensity != vertexDensity || !Mathf.Approximately(previousSkirtSize, skirtSize))
            {
                InstantiateMeshes();
                previousVertexDensity = vertexDensity;
                previousSkirtSize = skirtSize;
            }

            UpdatePositions();
            UpdateMaterials();
        }
        #endregion

        #region 辅助方法
        int GridSize()
        {
            return 4 * vertexDensity + 1;
        }
        int ActiveLodlevels()
        {
            return clipLevels - Mathf.Clamp((int)Mathf.Log((1.7f * Mathf.Abs(viewer.position.y) + 1) / lengthScale, 2), 0, clipLevels);
        }
        float ClipLevelScale(int level, int activeLevels)
        {
            return lengthScale / GridSize() * Mathf.Pow(2, clipLevels - activeLevels + level + 1);
        }
        Vector3 OffsetFromCenter(int level, int activeLevels)
        {
            return (Mathf.Pow(2, clipLevels) + GeometricProgressionSum(2, 2, clipLevels - activeLevels + level + 1, clipLevels - 1))
                   * lengthScale / GridSize() * (GridSize() - 1) / 2 * new Vector3(-1, 0, -1);
        }
        float GeometricProgressionSum(float b0, float q, int n1, int n2)
        {
            return b0 / (1 - q) * (Mathf.Pow(q, n2) - Mathf.Pow(q, n1));
        }
        Vector3 Snap(Vector3 coords, float scale)
        {
            if (coords.x >= 0)
                coords.x = Mathf.Floor(coords.x / scale) * scale;
            else
                coords.x = Mathf.Ceil((coords.x - scale + 1) / scale) * scale;

            if (coords.z < 0)
                coords.z = Mathf.Floor(coords.z / scale) * scale;
            else
                coords.z = Mathf.Ceil((coords.z - scale + 1) / scale) * scale;

            coords.y = 0;
            return coords;
        }
        #endregion
      
        void DestroyGO(Transform t)
            {
                t.parent = null;

    #if UNITY_EDITOR
                if (UnityEditor.EditorApplication.isPlaying)
                {
                    UnityEngine.Object.Destroy(t.gameObject);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(t.gameObject);
                }
    #else
                Object.Destroy(t.gameObject);
    #endif
            }
        void CleanMeshes()
        {
            geoRoot = transform.Find("Geometry Root");

            if (geoRoot == null)
            {
                return;
            }

            foreach (var child in geoRoot.gameObject.GetComponentsInChildren<Transform>())
            {
                if (child != geoRoot)
                {
                    DestroyGO(child);
                }
            }

            DestroyGO(geoRoot);
        }

    }

}