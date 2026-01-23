using UnityEngine;

namespace OceanQuest
{
    public class Water : MonoBehaviour
    {
        public static Water Instance;

        [Header("波浪设置")]
        // 波浪高度（振幅）
        public float waveHeight = 0.5f; 
        // 波浪密集程度（频率）
        public float waveFrequency = 0.5f; 
        // 波浪滚动速度
        public float waveSpeed = 1.0f;

        [Header("细节波纹 (让水面看起来更乱)")]
        public float detailWaveHeight = 0.2f;
        public float detailWaveFrequency = 1.2f;
        public float detailWaveSpeed = 2.3f;

        private void Awake()
        {
            Instance = this;
        }

        /// <summary>
        /// 输入世界坐标 (x, z)，返回该点的 Y 轴高度
        /// </summary>
        public float GetWaterHeight(float x, float z)
        {
            // 基础大波浪：利用 Sine 函数，结合 X 和 Z 轴以及时间
            float baseWave = Mathf.Sin(x * waveFrequency + Time.time * waveSpeed) 
                             + Mathf.Cos(z * waveFrequency + Time.time * waveSpeed * 0.8f);

            // 细节小波纹：频率更高，速度更快
            float detailWave = Mathf.Sin(x * detailWaveFrequency - Time.time * detailWaveSpeed) 
                               * Mathf.Cos(z * detailWaveFrequency + Time.time * detailWaveSpeed);

            // 叠加并缩放高度
            // baseWave 的范围大约是 -2 到 2，所以最后要乘上高度系数
            float y = (baseWave * waveHeight) + (detailWave * detailWaveHeight);
            
            return transform.position.y + y;
        }
    }
}