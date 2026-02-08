using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using UnityEngine.Rendering.Universal;
using Sirenix.OdinInspector;

namespace OceanQuest
{
    // 使用 partial 类扩展 Water 的功能
    public partial class Water 
    {
        [Header("反射设置")]
        public bool _enableReflection = true;
        public LayerMask _reflectionMask = -1;
        public bool _reflectSkybox = false;
        public float _clipPlaneOffset = 0.07F;
        [ReadOnly]public string _reflectionTexName = "_ReflectionTex";

        [Header("模糊设置")]
        public bool _blurOn = true;
        [Range(0.0f, 5.0f)] public float _blurSize = 1;
        [Range(0, 10)] public int _blurIterations = 2;
        [Range(1.0f, 4.0f)] public float _downsample = 1;

        [ReadOnly] public Camera _reflectionCamera;
       [ShowInInspector] [ReadOnly]private Shader _blurShader;
        private Material _blurMaterial;
        private RenderTexture _reflectionRT;
        private RenderTexture _bluredRT;
        private static bool _insideRendering;
        private Dictionary<Camera, CommandBuffer> _blurCommandBuffers = new Dictionary<Camera, CommandBuffer>();

        // 记录旧参数用于更新 CommandBuffer
        private float _oldBlurSize, _oldDownsample;
        private int _oldBlurIterations;
        private bool _oldBlurOn;
    

        #region 生命周期
         private void OnEnable()
        {
            // 订阅 URP 的渲染事件
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }
        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            if (_reflectionCamera) {
                DestroyImmediate(_reflectionCamera.gameObject);
                _reflectionCamera = null;
            }
            if (_reflectionRT) {
                _reflectionRT.Release();
                DestroyImmediate(_reflectionRT);
            }
            if (_bluredRT) {
                _bluredRT.Release();
                DestroyImmediate(_bluredRT);
            }
            ClearCommandBuffers();
        }

        #endregion
        private Material BlurMaterial {
            get {
                if (_blurMaterial == null && _blurShader != null)
                    _blurMaterial = new Material(_blurShader);
                return _blurMaterial;
            }
        }
        // 初始模糊
        private void InitBulr() {
            _blurShader = Shader.Find("Hidden/DualKawaseBlur");
            if (_blurShader == null) 
            {
                Debug.LogError("缺少 Hidden/DualKawaseBlur Shader,请检查文件名和路径");
                return;
            }
            if (_blurMaterial == null) _blurMaterial = new Material(_blurShader);
            _oldBlurSize = _blurSize;
            _oldBlurIterations = _blurIterations;
            _oldDownsample = _downsample;
            _oldBlurOn = _blurOn;
        }
        // 更新反射
        private void UpdateReflectionSettings() {
            if (!_enableReflection) return;

            if (_blurSize != _oldBlurSize || _blurIterations != _oldBlurIterations || 
                _downsample != _oldDownsample || _blurOn != _oldBlurOn) {
                ClearCommandBuffers();
                _oldBlurSize = _blurSize;
                _oldBlurIterations = _blurIterations;
                _oldDownsample = _downsample;
                _oldBlurOn = _blurOn;
            }
        }
        
        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            // 排除反射相机自身，防止无限递归
            if (camera == _reflectionCamera) return;
            
            // 仅对主相机或场景相机执行反射
            if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) return;

            RenderPlanarReflection(context, camera);
        }
        private void RenderPlanarReflection(ScriptableRenderContext context, Camera currentCam) {
            
           if (!_enableReflection) return;
            if (_insideRendering) return;
            _insideRendering = true;

            // 1. 准备资源 (RT 和 相机)
            EnsureResources(currentCam);

            // 2. 同步相机设置与矩阵计算
            UpdateReflectionCameraMatrices(currentCam);

            // 3. 执行渲染
            GL.invertCulling = true;
            // URP 专用的单相机渲染方法
            UniversalRenderPipeline.RenderSingleCamera(context, _reflectionCamera);
            GL.invertCulling = false;

            // 4. 后处理(模糊)与全局贴图设置
            UpdateReflectionTextures(currentCam);

            _insideRendering = false;
        }
        private void EnsureResources(Camera cam) {
            if (_reflectionCamera == null) {
                GameObject go = new GameObject("WaterReflectionCamera_" + cam.name);
                go.hideFlags = HideFlags.HideAndDontSave;
                _reflectionCamera = go.AddComponent<Camera>();
                _reflectionCamera.enabled = false;
            }

            int width = Mathf.RoundToInt(Screen.width / _downsample);
            int height = Mathf.RoundToInt(Screen.height / _downsample);

            if (_reflectionRT == null || _reflectionRT.width != width || _reflectionRT.height != height) {
                if (_reflectionRT) _reflectionRT.Release();
                _reflectionRT = new RenderTexture(width, height, 24, cam.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default);
                _reflectionRT.hideFlags = HideFlags.DontSave;
                _reflectionCamera.targetTexture = _reflectionRT;
            }

            if (_blurOn && (_bluredRT == null || _bluredRT.width != width || _bluredRT.height != height)) {
                if (_bluredRT) _bluredRT.Release();
                _bluredRT = new RenderTexture(width, height, 0, _reflectionRT.format);
                _bluredRT.hideFlags = HideFlags.DontSave;
            }
        }
        private void UpdateReflectionCameraMatrices(Camera currentCam) {
            // 同步相机设置
            _reflectionCamera.backgroundColor = Color.black;
            _reflectionCamera.clearFlags = _reflectSkybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            _reflectionCamera.cullingMask = _reflectionMask;

            // 计算反射矩阵
            Vector3 pos = transform.position;
            Vector3 normal = transform.up;
            float d = -Vector3.Dot(normal, pos) - _clipPlaneOffset;
            Vector4 reflectionPlane = new Vector4(normal.x, normal.y, normal.z, d);

            Matrix4x4 reflection = Matrix4x4.zero;
            CalculateReflectionMatrix(ref reflection, reflectionPlane);
            
            Vector3 oldPos = currentCam.transform.position;
            Vector3 newPos = reflection.MultiplyPoint(oldPos);
            _reflectionCamera.worldToCameraMatrix = currentCam.worldToCameraMatrix * reflection;

            Vector4 clipPlane = CameraSpacePlane(_reflectionCamera, pos, normal, 1.0f);
            _reflectionCamera.projectionMatrix = currentCam.CalculateObliqueMatrix(clipPlane);

            _reflectionCamera.transform.position = newPos;
            
        }
        private void UpdateReflectionTextures(Camera cam) 
        {
         if (_blurOn && BlurMaterial != null && _reflectionRT != null && _bluredRT != null)
            {
                ApplyDualKawase(_reflectionRT,_bluredRT);
                Shader.SetGlobalTexture(_reflectionTexName, _bluredRT);
            }
            else
            {
                Shader.SetGlobalTexture(_reflectionTexName, _reflectionRT);
            }
        }
        private void ApplyDualKawase(RenderTexture src, RenderTexture dest)
        {
            int tw = src.width;
            int th = src.height;

            _blurMaterial.SetFloat("_Offset", _blurSize);

            // 下采样
            var last = src;
            var tmpRenderTextures = new RenderTexture[_blurIterations];

            for (int i = 0; i < _blurIterations; i++)
            {
                tw >>= 1;
                th >>= 1;
                if (tw < 2 || th < 2) break;

                var rt = RenderTexture.GetTemporary(tw, th, 0, src.format);
                rt.filterMode = FilterMode.Bilinear;
                Graphics.Blit(last, rt, _blurMaterial, 0); // Pass 0: Down
                last = tmpRenderTextures[i] = rt;
            }

            // 上采样
            for (int i = _blurIterations - 2; i >= 0; i--)
            {
                var rt = tmpRenderTextures[i];
                var up = RenderTexture.GetTemporary(rt.width, rt.height, 0, src.format);
                Graphics.Blit(last, up, _blurMaterial, 1); // Pass 1: Up
                
                // 释放旧的
                RenderTexture.ReleaseTemporary(tmpRenderTextures[i]);
                tmpRenderTextures[i] = up; // 存入新的以便下次释放
                last = up;
            }

            Graphics.Blit(last, dest, _blurMaterial, 1);

            // 最后清理
            for (int i = 0; i < _blurIterations; i++)
            {
                if (tmpRenderTextures[i] != null)
                    RenderTexture.ReleaseTemporary(tmpRenderTextures[i]);
            }
        }
        private void ClearCommandBuffers() {
            foreach (var kvp in _blurCommandBuffers) {
                if (kvp.Key) kvp.Key.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, kvp.Value);
            }
            _blurCommandBuffers.Clear();
        }
        // --- 静态数学计算工具 ---
        private static void CalculateReflectionMatrix(ref Matrix4x4 reflectionMat, Vector4 plane) {
            reflectionMat.m00 = (1F - 2F * plane[0] * plane[0]);
            reflectionMat.m01 = (-2F * plane[0] * plane[1]);
            reflectionMat.m02 = (-2F * plane[0] * plane[2]);
            reflectionMat.m03 = (-2F * plane[3] * plane[0]);
            reflectionMat.m10 = (-2F * plane[1] * plane[0]);
            reflectionMat.m11 = (1F - 2F * plane[1] * plane[1]);
            reflectionMat.m12 = (-2F * plane[1] * plane[2]);
            reflectionMat.m13 = (-2F * plane[3] * plane[1]);
            reflectionMat.m20 = (-2F * plane[2] * plane[0]);
            reflectionMat.m21 = (-2F * plane[2] * plane[1]);
            reflectionMat.m22 = (1F - 2F * plane[2] * plane[2]);
            reflectionMat.m23 = (-2F * plane[3] * plane[2]);
            reflectionMat.m33 = 1F;
        }
        private Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign) {
            Vector3 offsetPos = pos + normal * _clipPlaneOffset;
            Matrix4x4 m = cam.worldToCameraMatrix;
            Vector3 cpos = m.MultiplyPoint(offsetPos);
            Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;
            return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
        }
    }
}