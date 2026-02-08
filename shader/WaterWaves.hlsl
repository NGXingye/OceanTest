#ifndef WATER_WAVES_INCLUDED
#define WATER_WAVES_INCLUDED

// 通用常量
#ifndef PI
#define PI= 3.14159265359
#endif

#ifndef GOLDEN_ANGLE
#define GOLDEN_ANGLE = 2.39996323 // 弧度制
#endif

// ---------------------------------------------------------
// 结构定义
// ---------------------------------------------------------
struct WaveOutput
{
    float3 positionOffset; // 顶点偏移量
    float3 normal;         // 叠加后的法线
};

// ---------------------------------------------------------
// 算法 1: 手动控制(Array Based)
// ---------------------------------------------------------
// 注意：数组大小需要和C#端匹配，这里最大支持20层
 uint _WaveCount;
 half4 _WaveData[20]; // x:amplitude, y:direction(degrees), z:wavelength, 

WaveOutput CalculateManualGerstnerWaves(float3 basePos, float timeMultiplier)
{
    WaveOutput o;
    o.positionOffset = float3(0,0,0);
    
    // 初始化法线累加器。注意：为了更好的混合，我们通常累加法线的偏导数(梯度)
    float3 normalX = float3(0, 0, 0);
    float3 normalZ = float3(0, 0, 0);
    
    float3 p = basePos;

    for(uint i = 0; i < _WaveCount; i++)
    {
        float amp = _WaveData[i].x;
        float deg = _WaveData[i].y;
        float len = _WaveData[i].z;

        if(len <= 0.01) continue; // 防止除以0

        // 基础参数计算
        float k = 2.0 * PI / len; // 波数
        float c = sqrt(9.8 / k);  // 相速度
        float2 dir = float2(sin(radians(deg)), cos(radians(deg))); // 方向向量
        
        // 陡度控制 (Steepness)，这里简化处理，通常 Q = steepness / (amp * k * numWaves)
        // 为了防止波浪自我交错，Q值通常需要很小
        float qi = 0.3/ (amp * k * _WaveCount); 

        // 核心计算
        float f = k * (dot(dir, p.xz) - c * _Time.y * timeMultiplier);
        float cosf = cos(f);
        float sinf = sin(f);

        //Gerstner波公式：Q * A * D * cos(f) ，偏移累加
        o.positionOffset.x += qi * amp * dir.x * cosf;
        o.positionOffset.y += amp * sinf; // 高度不需要除以waveCount，除非想归一化
        o.positionOffset.z += qi * amp * dir.y * cosf;

        // 计算法线偏导数，法线导数累加 (Gerstner Normal Reconstruction) 
        float WAcosf = k * amp * cosf;
        float WAsinf = k * amp * sinf;

        // Gerstner波的法线公式
        normalX.x += 1.0 - qi * dir.x * dir.x * WAcosf;
        normalX.y += dir.x * WAsinf;
        normalX.z += -qi * dir.x * dir.y * WAcosf;
        
        normalZ.x += -qi * dir.x * dir.y * WAcosf;
        normalZ.y += dir.y * WAsinf;
        normalZ.z += 1.0 - qi * dir.y * dir.y * WAcosf;
    }
    
    // 重构法线
    o.normal = normalize(cross(normalZ, normalX));;
    return o;
}

// ---------------------------------------------------------
// 算法 2: 程序化生成 (Procedural Golden Angle)
// ---------------------------------------------------------
// 参数：
// baseWavelength: 最大的那个波浪的长度
// iterations: 循环次数
// lacunarity: 间隙度（通常>1，控制频率增长速度）
// gain: 增益（通常<1，控制振幅衰减速度）
// steepness: 陡峭程度
WaveOutput CalculateProceduralGerstnerWaves(float3 basePos, int iterations, float baseWavelength, float lacunarity, float gain, float steepness, float speed, float timeMultiplier)
{
    WaveOutput o;
    o.positionOffset = float3(0,0,0);
    
    float3 dPos = float3(0,0,0);
    float2 totalGrad = float2(0,0); // 用于计算法线的梯度

    float curAmp = 1.0;     // 当前振幅因子 (由gain控制)
    float curLen = baseWavelength; // 当前波长
    
    for(int i = 0; i < iterations; i++)
    {
        // 1. 计算方向 (黄金角)
        float angle = i* GOLDEN_ANGLE;
        float2 dir = float2(cos(angle), sin(angle));

        // 2. 计算物理参数
        float k = 2.0 * PI / curLen; // 波数
        float c = sqrt(9.8 / k);     // 速度
        
        // 振幅计算：基于波长比例，并乘以增益衰减
        // 经验公式：波幅通常与波长成正比
        float amp = (curLen / (2.0 * PI)) * curAmp * 0.1; 
        
        // 3. 相位计算
        float f = k * (dot(dir, basePos.xz) - c * _Time.y * speed * timeMultiplier);
        float cosf = cos(f);
        float sinf = sin(f);

        // 4. 偏移 (Gerstner Wave)
        float Q = steepness* min(0.5, 0.5 / (k * amp * iterations));
        
        o.positionOffset.x += Q * amp * dir.x * cosf;
        o.positionOffset.y += amp * sinf;
        o.positionOffset.z += Q * amp * dir.y * cosf;

        // 5. 梯度累加 (用于做法线)
        // dx = -Sum(D.x * k * A * cos(f))
        // dz = -Sum(D.y * k * A * cos(f))
        float wa = k * amp; // 此时 wa 近似等于 curAmp * 0.1
        totalGrad.x -= dir.x * wa * cosf;
        totalGrad.y -= dir.y * wa * cosf;

        // 6. 为下一次循环迭代参数 (FBM)
        curLen /= lacunarity; // 波长变短
        curAmp *= gain;       // 振幅变小
    }

    // 重构法线
    // 为了让水面看起来更平滑，可以适当调整y的分量
    o.normal = normalize(float3(totalGrad.x, 1.0, totalGrad.y));

    return o;
}

#endif