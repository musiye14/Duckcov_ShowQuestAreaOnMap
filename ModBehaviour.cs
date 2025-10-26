using System;
using System.Collections.Generic;
using Duckov.MiniMaps.UI;
using Duckov.Scenes;
using Duckov.UI;
using Duckov.MiniMaps;
using UnityEngine;
using UnityEngine.SceneManagement;
using HarmonyLib;

namespace ShowQuestsAreaOnMap
{
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private bool _mapActive;
        private HashSet<GameObject> _questCircleObjects = new HashSet<GameObject>();
        private Harmony _harmony;
        public static ModBehaviour Instance { get; private set; }
        private const string LogPrefix = "[ShowQuestsAreaOnMap] ";
        
        private QuestsAreaManager _areaManager;
        

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this.gameObject); return; }
            Instance = this;
            _harmony = new Harmony("com.yexiao.ShowQuestsAreaOnMap");
            
            _areaManager = FindObjectOfType<QuestsAreaManager>();
            if (_areaManager == null)
            {
                _areaManager = gameObject.AddComponent<QuestsAreaManager>();
                Debug.Log(LogPrefix + "创建了 QuestsAreaManager 组件。");
            } else {
                Debug.Log(LogPrefix + "找到了已存在的 QuestsAreaManager 组件。");
            }

            Debug.Log(LogPrefix + "Mod 初始化完成。");
        }

        private void ClearCircle()
        {
            Debug.Log(LogPrefix + $"正在清理 {_questCircleObjects.Count} 个任务圆圈...");
            foreach (GameObject circle in _questCircleObjects)
            {
                if (circle != null)
                {
                    Destroy(circle);
                }
            }
            _questCircleObjects.Clear();
        }
        private void DrawCircles()
        {
            Debug.Log(LogPrefix + "DrawCircles 调用。");
            ClearCircle(); 

            if (_areaManager == null || _areaManager.CurrentQuestLocations == null)
            {
                Debug.LogError(LogPrefix + "区域管理器或其位置列表为 null！无法绘制。");
                return;
            }
            string playerSubSceneID = LevelManager.GetCurrentLevelInfo().activeSubSceneID;
            Debug.Log(LogPrefix + $"Player SubSceneID: '{playerSubSceneID ?? "null"}'");
            
            int circlesDrawn = 0;
            Debug.Log(LogPrefix + $"尝试绘制 {_areaManager.CurrentQuestLocations.Count} 个标记...");
            foreach (var spawn in _areaManager.CurrentQuestLocations) 
            {
                bool shouldDraw = false;
                string targetSubScene = spawn.TargetSubSceneID;
                bool targetHasSubScene = targetSubScene!= null && !string.IsNullOrEmpty(targetSubScene) && !targetSubScene.Equals("Default", StringComparison.OrdinalIgnoreCase);

                if (targetHasSubScene) 
                {
                    shouldDraw = (targetSubScene == playerSubSceneID);
                    Debug.Log(LogPrefix + $"...检查 '{spawn.QuestName}'. 目标: '{targetSubScene}'. 玩家: '{playerSubSceneID}'. 匹配特定子场景: {shouldDraw}");
                }
                else 
                {
                    shouldDraw = true; 
                    Debug.Log(LogPrefix + $"...检查 '{spawn.QuestName}'. 目标: 未指定. 玩家: '{playerSubSceneID}'. 绘制全局标记: {shouldDraw}");
                }
                
                if (shouldDraw)
                {
                    DrawQuestMarker(spawn.Position, spawn.Radius, spawn.QuestName);
                    circlesDrawn++;
                }
                else
                {
                    Debug.Log(LogPrefix + $"跳过绘制 '{spawn.QuestName}' @ {spawn.Position}. 目标子场景: '{targetSubScene ?? "N/A"}', 玩家子场景: '{playerSubSceneID ?? "N/A"}'.");
                }
            }
            Debug.Log(LogPrefix + $"绘制了 {circlesDrawn} 个圆圈。");
        }
        
        
        private void DrawQuestMarker(Vector3 position, float radius, string questName)
        {
            Debug.Log(LogPrefix + $"正在为任务 '{questName}' 在 {position} 处绘制半径为 {radius} 的标记。");
    
            GameObject markerObject = new GameObject($"QuestMarker_{questName}");
            markerObject.transform.position = position;
            
            SimplePointOfInterest poi;
            try
            {
                poi = markerObject.AddComponent<SimplePointOfInterest>();
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + $"AddComponent<SimplePointOfInterest> 失败: {e.Message}。请确保你的 Mod 引用了包含该类的 Assembly-CSharp.dll。");
                Destroy(markerObject); 
                return;
            }
            
            Sprite iconToUse = GetQuestIcon(); 
            
            try
            {
                poi.Setup(iconToUse, questName, followActiveScene: true);
            }
            catch(Exception e)
            {
                Debug.LogError(LogPrefix + $"poi.Setup 失败: {e.Message}。图标(iconToUse)是否为null?");
            }
            
            poi.Color = Color.green;      
            poi.IsArea = true;            
            poi.AreaRadius = radius;      
    
            poi.ShadowColor = Color.black;
            poi.ShadowDistance = 0f;
            
            if (MultiSceneCore.MainScene.HasValue)
            {
                SceneManager.MoveGameObjectToScene(markerObject, MultiSceneCore.MainScene.Value);
            }
            
            _questCircleObjects.Add(markerObject); 
        }
        
        private Sprite GetQuestIcon()
        {
            
            List<Sprite> allIcons = MapMarkerManager.Icons;

            if (allIcons == null)
            {
                Debug.LogError(LogPrefix + "MapMarkerManager.Icons 列表为 null! 无法获取图标。");
                return null;
            }
            
            if (allIcons.Count > 0)
            {
                Debug.LogWarning(LogPrefix + "使用列表中的第一个图标作为后备。");
                return allIcons[0]; 
            }
            
            Debug.LogError(LogPrefix + "MapMarkerManager.Icons 列表为空! 没有任何图标。");
            return null;
        }
        
        private void BeginDraw()
        {
            if (_mapActive) return;
            Debug.Log(LogPrefix + "地图已打开。更新位置并绘制...");
            _mapActive = true;
            
            _areaManager?.UpdateQuestLocations();
            DrawCircles();
        }

        private void EndDraw()
        {
            if (_mapActive)
            {
                Debug.Log(LogPrefix + "地图已关闭。清理圆圈...");
                _mapActive = false;
                ClearCircle();
            }
        }
        
        private void OnEnable()
        {
            Debug.Log(LogPrefix + "Mod 已启用。应用补丁...");
            try
            {
                if (_areaManager == null) {
                    _areaManager = gameObject.AddComponent<QuestsAreaManager>();
                    Debug.LogWarning(LogPrefix + "管理器在 OnEnable 时为 null，已创建。");
                }
                QuestSpawnPatcher.ApplyPatches(_harmony);
            } catch (Exception e) { Debug.LogError(LogPrefix + $"应用补丁时出错: {e.Message}"); }

            View.OnActiveViewChanged += OnActiveViewChanged;
            
        }

        private void OnDisable()
        {
            Debug.Log(LogPrefix + "Mod 已禁用。卸载补丁并清理...");
            View.OnActiveViewChanged -= OnActiveViewChanged;

            EndDraw(); 

            _harmony?.UnpatchAll(_harmony.Id);

            Debug.Log(LogPrefix + "Harmony 补丁已卸载。");
        }
        
        private void OnActiveViewChanged()
        {
            if (IsMapOpen())
            {
                BeginDraw();
            }
            else
            {
                EndDraw();
            }
        }
        
        private  bool IsMapOpen()
        {
            MiniMapView view = MiniMapView.Instance;

            if (view != null)
            {
                return view == View.ActiveView;
            }

            return false;
        }
        
    }
}