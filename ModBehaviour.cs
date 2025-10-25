using System;
using System.Collections.Generic;
using Duckov.MiniMaps.UI;
using Duckov.Quests;
using Duckov.Quests.Tasks;
using Duckov.Scenes;
using Duckov.UI;
using System.Reflection;
using Duckov.MiniMaps;
using UnityEngine;
using UnityEngine.SceneManagement;
using HarmonyLib;
using System.Linq;
using Unity.VisualScripting;

namespace ShowQuestsAreaOnMap
{
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private bool _mapActive = false;
        private HashSet<GameObject> _questCircleObjects = new HashSet<GameObject>();
        private Harmony _harmony;
        public static ModBehaviour Instance { get; private set; }
        
        private static Dictionary<Type, FieldInfo> _mapElementFieldCache = new Dictionary<Type, FieldInfo>();
        
        private const string LogPrefix = "[ShowQuestsAreaOnMap] ";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning(LogPrefix + "侦测到重复的 Mod 实例，已销毁。");
                Destroy(this.gameObject);
                return;
            }
            Instance = this;
            _harmony = new Harmony("com.yexiao.ShowQuestsAreaOnMap");
            
            
            Debug.Log(LogPrefix + "Mod 初始化完成!");
        }

        private void ClearCircle()
        {
            Debug.Log(LogPrefix + $"正在清理 {_questCircleObjects.Count} 个任务圆圈...");
            foreach (GameObject circle in _questCircleObjects)
            {
                if (circle != null)
                {
                    UnityEngine.Object.Destroy(circle);
                }
            }
            _questCircleObjects.Clear();
        }

        private void ScanQuestObjectives()
        {
            Debug.Log(LogPrefix + "开始扫描任务目标...");
            if (QuestManager.Instance == null)
            {
                Debug.LogError(LogPrefix + "QuestManager.Instance 为 null，扫描中止。");
                return;
            }
            
            QuestSpawnPatcher.RescanAndClear();
            
            string currentMapId = LevelManager.GetCurrentLevelInfo().sceneName;
            Debug.Log(LogPrefix+$"地图ID：{currentMapId}");
            if (string.IsNullOrEmpty(currentMapId))
            {
                Debug.LogWarning(LogPrefix + "无法获取当前地图 ID，扫描中止。");
                return;
            }
            
            List<ActiveQuestSpawn> spawnsToDraw = new List<ActiveQuestSpawn>();
            
            foreach (Quest quest in QuestManager.Instance.ActiveQuests)
            {
                if (quest.RequireSceneInfo == null || quest.RequireSceneInfo.ID != currentMapId) continue;
                
                foreach (var task in quest.Tasks)
                {
                    if (task == null || task.IsFinished()) continue;

                    MapElementForTask mapElement = null;
                    float taskRadius = 10f; 
                    Vector3? taskPosition = null; 
                    string questName = quest.DisplayName; 
                    
                    try
                    {
                        mapElement = GetMapElementFromTask(task);

                        if (mapElement != null) 
                        {
                            if (mapElement.locations != null && mapElement.locations.Count > 0)
                            {
                                foreach (MultiSceneLocation loc in mapElement.locations)
                                {
                                    if (loc.IsUnityNull() && loc.TryGetLocationPosition(out Vector3 pos))
                                    {
                                        taskPosition = pos; 
                                        taskRadius = mapElement.range;
                                        Debug.Log(LogPrefix + $"[静态 MapElement] 找到 '{questName}' @ {taskPosition.Value}");
                                        break; 
                                    }
                                }
                            }
                        }
                        
                        else if (task is QuestTask_TaskEvent eventTaskNoMapElement) 
                        {
                            
                            if (QuestSpawnPatcher.TaskEventEmitterEventKeyField != null)
                            {
                                string targetKey = eventTaskNoMapElement.EventKey;
                                
                                TaskEventEmitter[] allEmitters = UnityEngine.Object.FindObjectsOfType<TaskEventEmitter>();
                                // Debug.Log(LogPrefix + $"... 全局查找 {allEmitters.Length} 个 Emitter for key '{targetKey}'...");
                                
                                foreach(var emitter in allEmitters)
                                {
                                     if (emitter == null) continue;
                                     try
                                     {
                                         string emitterKey =
                                             (string)QuestSpawnPatcher.TaskEventEmitterEventKeyField.GetValue(emitter);
                                         if (emitterKey == targetKey)
                                         {
                                             taskPosition = emitter.transform.position;
                                             Debug.Log(LogPrefix +
                                                       $"[TaskEvent Emitter] 找到匹配 Emitter '{emitter.name}' @ {taskPosition.Value} for task '{questName}'");
                                             break;
                                         }
                                     }
                                     catch (Exception ex)
                                     {
                                         Debug.Log(LogPrefix+$"比较emitterKey和现有eventTaskNoMapElement出问题了：{ex}");
                                     }
                                }
                                if (!taskPosition.HasValue)
                                {
                                     Debug.LogWarning(LogPrefix + $"[静态 TaskEvent Emitter] 未找到 key 为 '{targetKey}' 的 Emitter for task '{questName}'");
                                }
                            }
                            else { Debug.LogError(LogPrefix + "[静态 TaskEvent 查找] TaskEventEmitter key 字段反射失败，无法查找 Emitter。"); }
                            Debug.Log(LogPrefix + $"[静态 TaskEvent Transform] 找到 '{questName}' @ {taskPosition.Value}");
                        }
                        
                    }
                    catch (Exception e) { Debug.LogError(LogPrefix + $"扫描任务 '{task.GetType().Name}' ('{questName}') 时出错: {e.Message}"); }
                    
                    if (taskPosition.HasValue) 
                    {
                        spawnsToDraw.Add(new ActiveQuestSpawn {
                            AssociatedTask = task, 
                            QuestName = questName,
                            Position = taskPosition.Value,
                            Radius = taskRadius > 10f ? taskRadius : 10f, 
                            SceneID = currentMapId 
                        });
                    }
                    
                } 
            } 
            
            foreach (var spawn in QuestSpawnPatcher.ActiveSpawns)
            {
                Debug.Log(LogPrefix + $"[新加入]spawn.SceneID:{spawn.SceneID} | currentMapId:{currentMapId}");
                if (spawn.SceneID == currentMapId)
                {
                    spawnsToDraw.Add(spawn);
                }
            }
            
            int oldCirclesDrawn = 0;
            ClearCircle(); 
            foreach (var spawn in spawnsToDraw)
            {
                oldCirclesDrawn++;
            }

            var endFinalDrawList = spawnsToDraw
                .GroupBy(s => new { s.QuestName, PosKey = $"{s.Position.x:F1}_{s.Position.z:F1}" })
                .Select(g => g.First());

            int circlesDrawn = 0;
            ClearCircle(); 
            foreach (var spawn in endFinalDrawList)
            {
                DrawQuestMarker(spawn.Position, spawn.Radius, spawn.QuestName);
                circlesDrawn++;
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
            Debug.Log(LogPrefix + "地图已打开，开始绘制...");
            ClearCircle();
            _mapActive = true;
            ScanQuestObjectives();
        }

        private void EndDraw()
        {
            if (_mapActive)
            {
                Debug.Log(LogPrefix + "地图已关闭，清理圆圈...");
                _mapActive = false;
                ClearCircle();
            }
        }
        
        private void OnEnable()
        {
            Debug.Log(LogPrefix + "Mod 已启用。");
            
            try
            {
                QuestSpawnPatcher.ApplyPatches(_harmony);
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + $"应用补丁时出错: {e.Message}");
            }
            
            View.OnActiveViewChanged += OnActiveViewChanged;
        }

        private void OnDisable()
        {
            Debug.Log(LogPrefix + "Mod 已禁用。");
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
        
        private MapElementForTask GetMapElementFromTask(Task task)
        {
            Type taskType = task.GetType();
            FieldInfo mapElementField;

            if (_mapElementFieldCache.TryGetValue(taskType, out mapElementField))
            {
                if (mapElementField == null) return null;
                try { return (MapElementForTask)mapElementField.GetValue(task); }
                catch (Exception ex) { Debug.LogError($"{LogPrefix} GetValue failed for {taskType.Name}.mapElement: {ex.Message}"); return null;}
            }
            
            mapElementField = taskType.GetField("mapElement", BindingFlags.NonPublic | BindingFlags.Instance);
            _mapElementFieldCache[taskType] = mapElementField;
            
            if (mapElementField == null) return null;

            try { return (MapElementForTask)mapElementField.GetValue(task); }
            catch (Exception ex) { Debug.LogError($"{LogPrefix} GetValue failed for {taskType.Name}.mapElement: {ex.Message}"); return null;}
        }
    }
}