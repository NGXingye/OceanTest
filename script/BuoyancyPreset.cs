using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace OceanQuest
{
    [CreateAssetMenu(fileName = "BuoyancyPreset", menuName = "OceanSyetem/Buoyancy Preset")]
    public class BuoyancyPreset : ScriptableObject
    {
        [Header("物理属性")]
        
        [Tooltip("密度 (kg/m^3)。水是1000。小于1000会上浮。")]
        [Range(10, 2000)]
        public float density = 500f;
        
        [Tooltip("质量 (mg),同密度，质量越大，浮力越大")]
        [Range(10, 2000)]
        public float mass  = 5f;
        
        [Header("水阻力")]
        [Tooltip("平移阻力：影响物体在水中移动的难易程度")]
        [Range(0f, 10f)]
        public float dragCoefficient = 0.5f;

        [Tooltip("旋转阻力：影响物体在水中旋转的难易程度")]
        [Range(0f, 10f)]
        public float angularDragCoefficient = 1.5f;
    }
}
