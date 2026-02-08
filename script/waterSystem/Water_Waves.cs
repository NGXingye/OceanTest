
using UnityEngine;

namespace OceanQuest
{
    public struct WaveStruct
    {
        public Vector3 position;
        public Vector3 normal;
        public WaveStruct(Vector3 pos, Vector3 n)
        {
            this.position = pos;
            this.normal = n;
        }
        public void ResetWave()
        {
            position = Vector3.zero;
            normal.Set(0, 1, 0);
        }
    }
    public partial class Water
    {
        [SerializeField]
        WaveSetting waveSetting;
        private int waveCount;
        private WaveStruct waveOut;

        public void UpdateWaves()
        {
            waveSetting.UpdateWaveData();
            waveCount = waveSetting.GetWaveCount();

            if (waveCount > 0)
            {
                Shader.SetGlobalInt(ShaderParametersID.WaveCountID, waveCount);
                Shader.SetGlobalVectorArray(ShaderParametersID.WaveDataID, waveSetting.waveData);
            }
        }
        public WaveStruct GetWaveInfoAtPosition(Vector3 worldPosition, float timeMultiplier = 1f)
        {
            // 如果没有波浪设置，返回默认值
            if (waveSetting == null || waveCount == 0)
            {
                return new WaveStruct(worldPosition, Vector3.up);
            }

            Vector3 offset = Vector3.zero;
            Vector3 normalX = Vector3.zero;
            Vector3 normalZ = Vector3.zero;

            Vector3 p = worldPosition;

            // 获取波浪数据
            Vector4[] waveDataArray = waveSetting.waveData;

            for (int i = 0; i < waveCount; i++)
            {
                float amp = waveDataArray[i].x;
                float deg = waveDataArray[i].y;
                float len = waveDataArray[i].z;

                if (len <= 0.01f) continue;

                // 基础参数计算
                float k = 2.0f * Mathf.PI / len; // 波数
                float c = Mathf.Sqrt(9.8f / k);  // 相速度

                // 方向向量（注意：Unity中通常使用弧度，但这里保持与Shader一致的角度制）
                float rad = deg * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));

                // 陡度控制
                float qi = 1.2f / (amp * k * waveCount);

                // 核心计算
                float f = k * (Vector2.Dot(dir, new Vector2(p.x, p.z)) - c * Time.time * timeMultiplier);
                float cosf = Mathf.Cos(f);
                float sinf = Mathf.Sin(f);

                // 位置偏移累加
                offset.x += qi * amp * dir.x * cosf;
                offset.y += amp * sinf;
                offset.z += qi * amp * dir.y * cosf;

                // 法线偏导数累加
                float WAcosf = k * amp * cosf;
                float WAsinf = k * amp * sinf;

                normalX.x += 1.0f - qi * dir.x * dir.x * WAcosf;
                normalX.y += dir.x * WAsinf;
                normalX.z += -qi * dir.x * dir.y * WAcosf;

                normalZ.x += -qi * dir.x * dir.y * WAcosf;
                normalZ.y += dir.y * WAsinf;
                normalZ.z += 1.0f - qi * dir.y * dir.y * WAcosf;
            }

            // 重构法线
            Vector3 normal = Vector3.Cross(normalZ, normalX).normalized;

            // 返回最终位置和法线
            return new WaveStruct(worldPosition + offset, normal);
        }

        // 批处理版本 - 高效计算多个点的波浪信息
        public WaveStruct[] GetWaveInfoAtPositions(Vector3[] positions, float timeMultiplier = 1f)
        {
            WaveStruct[] results = new WaveStruct[positions.Length];

            for (int i = 0; i < positions.Length; i++)
            {
                results[i] = GetWaveInfoAtPosition(positions[i], timeMultiplier);
            }

            return results;
        }
        public Vector3 GetWaterDisplacement(Vector3 pos)
        {
            waveOut.ResetWave();
            waveOut = GetWaveInfoAtPosition(pos, 1);
            return waveOut.position - pos;
        }

        // 精确计算高度（带迭代校正）
        public float GetWaveHeightAccurate(Vector3 worldPosition, int iterations = 2)
        {
            Vector3 displacement = Vector3.zero;
            Vector3 pos = worldPosition;

            for (int i = 0; i < iterations; i++)
            {
                displacement = GetWaterDisplacement(pos);
                pos = worldPosition - displacement;
            }

            return GetWaterDisplacement(pos).y;
        }

        // 直接计算高度（快速近似）
        public float GetWaveHeightFast(Vector3 worldPosition)
        {
            return GetWaveInfoAtPosition(worldPosition).position.y;
        }
    }
    
}