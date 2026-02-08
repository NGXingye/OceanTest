using UnityEngine;
using System.Collections.Generic;

namespace OceanQuest
{
    public partial class Water:MonoBehaviour
    {
        private static Water instance;
        public static Water Instance
        {
            get
            {
                if ((instance == null))
                {
                    instance = FindObjectOfType<Water>();
                }
                return instance;
            }
        }

       private void Awake()
        {
            
            instance = this;

            int reflectionLayer = LayerMask.NameToLayer("Reflection");
            if (reflectionLayer != -1) gameObject.layer = reflectionLayer;
            else Debug.LogWarning("在项目中添加名为 Reflection 的 Layer");
            
            InitGeometry();
            InitBulr();
        }

        // Update is called once per frame
       private void Update()
        {
            UpdateWaves();
            UpdateGeometry();
            UpdateReflectionSettings();
        }
      
    }
}
