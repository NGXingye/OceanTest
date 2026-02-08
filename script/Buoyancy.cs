using System.Collections.Generic;
using UnityEngine;

namespace OceanQuest 
{
    public class Buoyancy : MonoBehaviour
    {
        #region 字段
        [Header("配置预设")]
        [SerializeField] private BuoyancyPreset preset;
        
        [Header("体素采样参数")]
        [SerializeField] private int slicesPerAxis = 8; // 三轴采样精度
        [SerializeField] private int voxelsLimit = 50;
        // 增加一个开关，决定是否只生成在碰撞体内部的点（对于船体或凹模型很重要）
        [SerializeField] private bool onlyInsideCollider = true; 
        [Header("物理参数")]
        [SerializeField] private float objDensity = 500f; // 物体密度
        [SerializeField] private float dragCoefficient = 0.5f; // 水的平移阻力系数
        [SerializeField] private float angularDragCoefficient = 0.2f; // 水的旋转阻力系数
        
        [Header("可视化调试")]
         public bool drawPoints = true;
        // public bool drawBounds = true;         
        // public bool drawOriginalPoints = false; 
        // public bool drawWeldedPoints = true;   
        // public float pointSize = 0.1f;         
        // public Color originalPointColor = Color.yellow;  
        // public Color weldedPointColor = Color.cyan;      
        [Header("浮力可视化(运行时生效)")]
        public bool showDebugLines = true; // 开关
        [Tooltip("力的可视化线条长度缩放")]
        public float forceLineScale = 1f; 
        
        private const float WATER_DENSITY = 1000f; // 水的密度
        private List<Vector3> _originalVoxels;
        private List<Vector3> _weldVoxels; // 优化后的体素

        private Vector3 LocalArchimedesForce { get; set; } // 单个浮力的受力向量
        private Rigidbody Rb { get; set; }
        private Collider Col { get; set; }
        private float VoxelHalfHeight { get; set; }

        #endregion

        #region 生命周期
        void Start()
        {
            InitializeComponent();
            if (preset != null)
            {
                ApplyPresetValues();
            }
            SetRig();
            
            // 1. 先生成体素
            CreateVoxel();
            
            if(Rb.mass < 0.01f) Rb.mass = 1f;// 确保刚体质量不是0
            
            // 2. 根据生成的有效点数，计算每个点分配的浮力
            CalculateArchimedesForce();
            
            // 3. 计算用于浮力插值的半高度
            CalculateVoxelHalfHeight();
        }

        private void FixedUpdate()
        {
            if (_weldVoxels == null || _weldVoxels.Count == 0) return;
            // CalculateArchimedesForce(); //实时更新浮力大小，方便调试
            int pointsUnderWater = 0;
            foreach (var point in _weldVoxels)
            {
                bool isUnderWater = ApplyBuoyancyForce(point);
                if (isUnderWater) pointsUnderWater++;
            }
            if (pointsUnderWater == 0 && transform.position.y < 0) Debug.LogWarning("物体在水面下，但没有点检测到水！请检查水位或物体大小。");
        }
        #endregion

        #region 内部实现
        private void InitializeComponent()
        {
            Col = GetComponent<Collider>();
            Rb = GetComponent<Rigidbody>();
            
            if (Rb == null) Rb = gameObject.AddComponent<Rigidbody>();
           
        }

        #region 辅助
        public void ApplyPresetValues()
        {
            if (preset == null) return;
            objDensity = preset.density;
            dragCoefficient = preset.dragCoefficient;
            angularDragCoefficient = preset.angularDragCoefficient;
            Rb.mass=preset.mass;
        }
        //在编辑器里右键组件标题，可以直接执行这个方法
        [ContextMenu("应用预设配置")]
        private void ContextMenuApplyPreset()
        {
            ApplyPresetValues();
#if UNITY_EDITOR
            // 标记物体已修改，以便Ctrl+S能保存数值变化
            UnityEditor.EditorUtility.SetDirty(this);
#endif
            Debug.Log($"已应用预设: {preset.name}");
        }
        

        #endregion
     
        private void SetRig()
        {
            // 必须手动设置重心，防止Unity自动计算的重心偏离几何中心太远导致翻船
            // 但如果物体本身形状不规则，建议在编辑器里调整CenterOfMass，而不是代码写死
            // 这里保留原本逻辑，但要注意：bounds.center是世界坐标，需要转本地
            Rb.centerOfMass = transform.InverseTransformPoint(Col.bounds.center);
            Rb.useGravity = true; 
            Rb.drag = 0.05f; // 空气阻力
            Rb.angularDrag = 0.05f; // 空气旋转阻力
        }

        // 生成点云时，尽量贴合物体形状
        private List<Vector3> SliceConvex()
        {
            var pointsList = new List<Vector3>();
            Bounds bounds = Col.bounds; // 此时是世界坐标Bounds

            // 计算步长
            float stepX = bounds.size.x / slicesPerAxis;
            float stepY = bounds.size.y / slicesPerAxis;
            float stepZ = bounds.size.z / slicesPerAxis;

            for (int x = 0; x < slicesPerAxis; x++)
            {
                for (int y = 0; y < slicesPerAxis; y++)
                {
                    for (int z = 0; z < slicesPerAxis; z++)
                    {
                        // 计算世界坐标采样点
                        float worldX = bounds.min.x + stepX * (x + 0.5f);
                        float worldY = bounds.min.y + stepY * (y + 0.5f);
                        float worldZ = bounds.min.z + stepZ * (z + 0.5f);
                        Vector3 worldPoint = new Vector3(worldX, worldY, worldZ);

                        // 核心改进：检查点是否在Collider内部
                        // 注意：对于凹多边形(MeshCollider且未开启Convex)，此方法可能不准，但在凸包下有效
                        // 如果不想做复杂判断，至少保证点在 Bounds 内（目前逻辑已经保证）
                        
                        if (onlyInsideCollider)
                        {
                            // ClosestPoint 返回的是点到Collider表面最近的点（如果点在内部，则返回点本身）
                            Vector3 closest = Col.ClosestPoint(worldPoint);
                            // 由于精度问题，判断距离接近0
                            if (Vector3.Distance(closest, worldPoint) < 0.01f) 
                            {
                                pointsList.Add(transform.InverseTransformPoint(worldPoint));
                            }
                        }
                        else
                        {
                            pointsList.Add(transform.InverseTransformPoint(worldPoint));
                        }
                    }
                }
            }
            return pointsList;
        }

        private static void WeldPoints(IList<Vector3> list, int targetCount)
        {
            if (list.Count <= targetCount || targetCount < 1) return;

            while (list.Count > targetCount)
            {
                int first, second;
                FindClosestPoints(list, out first, out second);

                var mixed = (list[first] + list[second]) * 0.5f;
                
                // RemoveAt逻辑正确：先删大的索引，再删小的
                list.RemoveAt(second);
                list.RemoveAt(first);
                list.Add(mixed);
            }
        }

        private static void FindClosestPoints(IList<Vector3> list, out int first, out int second)
        {
            float minDistanceSqr = float.MaxValue;
            first = 0;
            second = 1;

            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    float distSqr = (list[i] - list[j]).sqrMagnitude; 
                    if (distSqr < minDistanceSqr)
                    {
                        minDistanceSqr = distSqr;
                        first = i;
                        second = j;
                    }
                }
            }
        }

        private void CreateVoxel()
        {
            _originalVoxels = SliceConvex();
            // 如果生成的点太少，直接用
            if (_originalVoxels.Count <= voxelsLimit)
            {
                _weldVoxels = new List<Vector3>(_originalVoxels);
            }
            else
            {
                _weldVoxels = new List<Vector3>(_originalVoxels);
                WeldPoints(_weldVoxels, voxelsLimit);
            }
            // 必须确保有点，否则无法计算
            if (_weldVoxels.Count == 0) Debug.LogError("没有生成任何体素点！请检查包围盒大小或Slices参数。");
        }

        private void CalculateVoxelHalfHeight()
        {
            //Voxel只是点，需要模拟为一个一个有体积的小方块，保证连续性（Continuity）与平滑（Smoothing）。
            // 近似计算体素的"半径"，用于处理入水时的平滑过渡
            Bounds bounds = Col.bounds;
            float minDim = Mathf.Min(bounds.size.x, bounds.size.y, bounds.size.z);
            VoxelHalfHeight = minDim / (2 * slicesPerAxis);//就是这个假想小方块的半高（从中心到顶部的距离）。
        }

        private void CalculateArchimedesForce()
        {
            if (_weldVoxels.Count == 0) return;

            // F = ρ * V * g  物体体积 V = 质量 / 密度
            float volume = Rb.mass / objDensity;
            
            // 如果物体密度小于水，它应该浮起来。如果设置得太大，volume会很小，浮力就小。
            // 这里有一个常见的误区：如果只按mass/density算体积，对于MeshCollider可能不准。
            float totalForce = WATER_DENSITY * volume * Mathf.Abs(Physics.gravity.y);
            
            // 将总浮力平均分配给每个体素点
            LocalArchimedesForce = new Vector3(0, totalForce, 0) / _weldVoxels.Count;
        }

        private float GetWaterLevel(float x, float z)
        {
            // 这里假设你有 Water 类
            if (Water.Instance != null)
            {
                return Water.Instance.GetWaveHeightAccurate(new Vector3(x,0,z));
            }
       
            return 6f; // 测试用平面水面
        }

        private bool  ApplyBuoyancyForce(Vector3 localPoint)
        {
            // 1. 转世界坐标
            var worldPoint = transform.TransformPoint(localPoint);
            float waterLevel = GetWaterLevel(worldPoint.x, worldPoint.z);
            // 2. 检查深度
            float depth = waterLevel - worldPoint.y + VoxelHalfHeight; // 加上半高修正

            // 如果在水下（depth > 0 表示有一部分在水下）
            if (depth > 0)
            {
                // 计算浸没比例 k (0 到 1)，当 worldPoint.y == waterLevel 时，k = 0.5
                float k = Mathf.Clamp01(depth / (2 * VoxelHalfHeight));

                // 计算基本浮力
                var buoyantForce = k * LocalArchimedesForce;
               
                //获取该点的线速度
                var pointVelocity = Rb.GetPointVelocity(worldPoint);
                
                // 计算水阻力     其方向与速度方向相反，阻力大小通常与速度平方成正比，这里简化为线性也可以，或者用 pointVelocity.sqrMagnitude
                var dragForce = -pointVelocity * (dragCoefficient * k); // 乘k是为了入水越深阻力越大

                float flowStrength = 0.25f; // 流动力度
                Vector3 waterFlowForce = new Vector3(
                    Mathf.Sin(Time.time) * flowStrength, // X轴来回推
                    0, 
                    Mathf.Cos(Time.time * 0.8f) * flowStrength // Z轴来回推
                ) * k;
                // 合力
                Vector3 finalForce = buoyantForce + dragForce+waterFlowForce;
                // 5. 应用力 (都在该点位置应用！)
                Rb.AddForceAtPosition(finalForce, worldPoint, ForceMode.Force);

                // 6. 额外的旋转阻尼 (可选，如果上面的dragForce已经足够产生力矩，这个可以去掉或减小)
                Rb.AddTorque(-Rb.angularVelocity * (angularDragCoefficient * k), ForceMode.Force);
                if (showDebugLines)
                {
                    // 红色线代表该点受到的浮力方向和大小
                    // 线越长，力越大
                    Debug.DrawRay(worldPoint, finalForce * forceLineScale, Color.red);
                }
                return true;
            }
            else
            {
                // 水面上，画个灰色小点辅助查看位置
                if(showDebugLines) Debug.DrawRay(worldPoint, Vector3.up * 0.1f, Color.gray);
                return false;
            }
        }
        #endregion

        #region 可视化
        private void OnDrawGizmos()
        {
            if (Col == null) return;
            if (!drawPoints || _weldVoxels == null) return;
            Gizmos.color = Color.cyan;
            foreach (var p in _weldVoxels)
            {
                Gizmos.DrawSphere(transform.TransformPoint(p), 0.05f);
            }

            // if (drawBounds)
            // {
            //     Gizmos.color = Color.white;
            //     Gizmos.DrawWireCube(Col.bounds.center, Col.bounds.size);
            // }
            //
            // if (drawOriginalPoints && _originalVoxels != null)
            // {
            //     Gizmos.color = originalPointColor;
            //     foreach (var p in _originalVoxels) Gizmos.DrawSphere(transform.TransformPoint(p), pointSize);
            // }
            //
            // if (drawWeldedPoints && _weldVoxels != null)
            // {
            //     Gizmos.color = weldedPointColor;
            //     foreach (var p in _weldVoxels) Gizmos.DrawSphere(transform.TransformPoint(p), pointSize * 1.2f);
            // }
        }
        #endregion
    }
}