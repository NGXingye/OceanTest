
using UnityEngine;
using Sirenix.OdinInspector;

namespace OceanQuest
{
    [System.Serializable]
    public struct Wave
    {
        [LabelText("振幅")] 
        [MinValue(0)]          
        [Tooltip("波浪的高度,建议0-5")] 
        public float amplitude;
        [LabelText("方向")]
        [Range(0,360)]
        public float direction;
        [LabelText("波长")]
        [MinValue(1)]
        [SuffixLabel("米")]
        public float waveLength;

        public Wave(float amp, float dir, float len)
        {
            this.amplitude = amp;
            this.direction = dir;
            this.waveLength = len;
        }
    }

        [CreateAssetMenu(fileName = "waveData Setting", menuName = "OceanSyetem/WaveData Setting")]
        public class WaveSetting : ScriptableObject
        {
           [Title("波浪配置", "最多支持6组波浪")]
           [ListDrawerSettings(Expanded = true,ShowPaging = true,  NumberOfItemsPerPage = 6,ShowItemCount = true)]
            public Wave[] wavesArry;

            [HideInInspector] public Vector4[] waveData = new Vector4[6];

            public int GetWaveCount()
            {
                if (wavesArry.Length < 6) return wavesArry.Length;
                return 6;
            }

        public void UpdateWaveData()
        {
            if (wavesArry == null) return;
            int count = Mathf.Min(wavesArry.Length, 6);
            for (int i = 0; i < count; i++)
            {

                if (i >= 6) break;
                waveData[i].Set(wavesArry[i].amplitude, wavesArry[i].direction, wavesArry[i].waveLength, 0);

            }
            for (int i = count; i < waveData.Length; i++)
            {
                waveData[i] = Vector4.zero;
            }
        }
            

        }

    }
