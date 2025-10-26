using System;
using System.Collections.Generic;
using Duckov.Quests;
using Duckov.Quests.Conditions; 
using Duckov.Quests.Tasks;
using Duckov.Scenes;
using System.Reflection;
using Duckov.MiniMaps;
using UnityEngine;
using UnityEngine.SceneManagement; 
using System.Linq;
using Unity.VisualScripting;

namespace ShowQuestsAreaOnMap
{
    public class QuestsAreaManager : MonoBehaviour
    {
        public static QuestsAreaManager Instance { get; private set; }
        private const string LogPrefix = "[ShowQuestsAreaOnMap][Manager] ";
        
        private Dictionary<int, List<RequireQuestsActive>> _cachedQuestTriggers = new Dictionary<int, List<RequireQuestsActive>>();
        private bool _triggersScannedForCurrentScene = false;
        
        private Dictionary<Type, FieldInfo> _mapElementFieldCache = new Dictionary<Type, FieldInfo>();
        
        public List<ActiveQuestSpawn> CurrentQuestLocations { get; private set; } = new List<ActiveQuestSpawn>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning(LogPrefix + "检测到重复的 QuestsAreaManager 实例，已销毁。");
                Destroy(this.gameObject);
                return;
            }
            Instance = this;
            Debug.Log(LogPrefix + "实例已初始化。");
        }

        private void OnEnable()
        {
             Debug.Log(LogPrefix + "已启用。订阅 sceneLoaded 事件。");
             SceneManager.sceneLoaded += OnSceneLoaded;
             
             if (SceneManager.GetActiveScene().isLoaded)
             {
                 PreScanSceneForTriggers();
             } else {
                 _triggersScannedForCurrentScene = false; 
             }
        }

        private void OnDisable()
        {
            Debug.Log(LogPrefix + "已禁用。取消订阅 sceneLoaded 事件并清理缓存。");
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _triggersScannedForCurrentScene = false;
            _cachedQuestTriggers.Clear();
            _mapElementFieldCache.Clear(); 
            CurrentQuestLocations.Clear(); 
        }

         private void OnDestroy() {
             if (Instance == this)
             {
                 Instance = null;
                  Debug.Log(LogPrefix + "实例已销毁。");
             }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Debug.Log(LogPrefix + $"场景 '{scene.name}' 已加载。重置扫描标记并执行预扫描...");
            _triggersScannedForCurrentScene = false; 
            CurrentQuestLocations.Clear(); 
            PreScanSceneForTriggers();
        }

        private void PreScanSceneForTriggers()
        {
            if (_triggersScannedForCurrentScene) return;

            Debug.Log(LogPrefix + "开始预扫描场景中的 RequireQuestsActive...");
            _cachedQuestTriggers.Clear();

            RequireQuestsActive[] allTriggers = FindObjectsOfType<RequireQuestsActive>();
            Debug.Log(LogPrefix + $"场景中找到 {allTriggers.Length} 个 RequireQuestsActive 组件。");
            foreach (var trigger in allTriggers)
            {
                if (trigger == null || trigger.RequiredQuestIDs == null) continue;
                foreach (int questId in trigger.RequiredQuestIDs)
                {
                    if (!_cachedQuestTriggers.ContainsKey(questId))
                    {
                        _cachedQuestTriggers[questId] = new List<RequireQuestsActive>();
                    }
                    if (!_cachedQuestTriggers[questId].Contains(trigger))
                    {
                         _cachedQuestTriggers[questId].Add(trigger);
                    }
                }
            }
            _triggersScannedForCurrentScene = true;
            Debug.Log(LogPrefix + $"预扫描完成，缓存了 {_cachedQuestTriggers.Count} 个不同的 Quest ID 关联的触发器。");
        }

        private MapElementForTask GetMapElementFromTask(Task task)
        {
            if (task == null) return null; 
            Type taskType = task.GetType();
            FieldInfo mapElementField;

            if (_mapElementFieldCache.TryGetValue(taskType, out mapElementField))
            {
                if (mapElementField == null) return null;
                try { return (MapElementForTask)mapElementField.GetValue(task); } catch { return null;}
            }

            mapElementField = taskType.GetField("mapElement", BindingFlags.NonPublic | BindingFlags.Instance);
            _mapElementFieldCache[taskType] = mapElementField; 

            if (mapElementField == null) return null;

            try { return (MapElementForTask)mapElementField.GetValue(task); } catch { return null;}
        }

        public void UpdateQuestLocations()
        {
            Debug.Log(LogPrefix + "UpdateQuestLocations 调用。开始扫描...");
            CurrentQuestLocations.Clear(); 

            if (QuestManager.Instance == null) { Debug.LogError(LogPrefix + "QuestManager.Instance 为 null。"); return; }

            QuestSpawnPatcher.RescanAndClear();

            string currentMapId = LevelManager.GetCurrentLevelInfo().sceneName;
            if (string.IsNullOrEmpty(currentMapId)) { Debug.LogWarning(LogPrefix + "无法获取当前地图 ID。"); return; }

            if (!_triggersScannedForCurrentScene)
            {
                 Debug.LogWarning(LogPrefix + "当前场景触发器未扫描，执行预扫描...");
                 PreScanSceneForTriggers();
            }

            List<ActiveQuestSpawn> foundSpawns = new List<ActiveQuestSpawn>(); 

            foreach (Quest quest in QuestManager.Instance.ActiveQuests)
            {
                if (quest.RequireSceneInfo == null || quest.RequireSceneInfo.ID != currentMapId) continue;

                foreach (var task in quest.Tasks)
                {
                    if (task == null || task.IsFinished()) continue;

                    float taskRadius = 10f;
                    Vector3? taskPosition = null;
                    string questName = quest.DisplayName;
                    string subSceneId = null;

                    try
                    {
                        MapElementForTask mapElement = GetMapElementFromTask(task);
                        if (mapElement != null)
                        {
                            if (mapElement.locations != null && mapElement.locations.Count > 0)
                            {
                                foreach (MultiSceneLocation loc in mapElement.locations)
                                {
                                    if (loc.IsUnityNull() && loc.TryGetLocationPosition(out Vector3 pos))
                                    {
                                        taskPosition = pos;
                                        subSceneId = loc.SceneID;
                                        taskRadius = mapElement.range;
                                        break;
                                    }
                                }
                            }
                        }

                        if (!taskPosition.HasValue)
                        {
                            if (_cachedQuestTriggers.TryGetValue(quest.ID, out var triggersForThisQuest))
                            {
                                RequireQuestsActive firstTrigger = triggersForThisQuest.FirstOrDefault(t => t != null);
                                if (firstTrigger != null)
                                {
                                    taskPosition = firstTrigger.transform.position;
                                    subSceneId = null;
                                }
                            }
                        }

                        if (!taskPosition.HasValue && task is QuestTask_TaskEvent eventTaskNoMapElement /* && mapElement == null */ )
                        {
                             #region Emitter Search Logic (不变)
                              if (QuestSpawnPatcher.TaskEventEmitterEventKeyField != null)
                              {
                                  string targetKey = eventTaskNoMapElement.EventKey;
                                  if(!string.IsNullOrEmpty(targetKey))
                                  {
                                      TaskEventEmitter[] allEmitters = UnityEngine.Object.FindObjectsOfType<TaskEventEmitter>();
                                      foreach(var emitter in allEmitters)
                                      {
                                           if (emitter == null) continue;
                                           try
                                           {
                                               string emitterKey = (string)QuestSpawnPatcher.TaskEventEmitterEventKeyField.GetValue(emitter);
                                               if (emitterKey == targetKey)
                                               {
                                                   taskPosition = emitter.transform.position;
                                                   subSceneId = null;
                                                   break;
                                               }
                                           } catch { /* ignore */ }
                                      }
                                  }
                              } 
                              #endregion
                        }

                    } catch (Exception e) { Debug.LogError(LogPrefix + $"扫描任务 '{task.GetType().Name}' ('{questName}') 时出错: {e.Message}"); }

                    if (taskPosition.HasValue)
                    {
                        foundSpawns.Add(new ActiveQuestSpawn
                        {
                            AssociatedTask = task,
                            QuestName = questName,
                            Position = taskPosition.Value,
                            Radius = taskRadius > 0.1f ? taskRadius : 10f,
                            SceneID = currentMapId,
                            TargetSubSceneID = subSceneId
                        });
                    }
                } 
            } 

            foreach (var spawn in QuestSpawnPatcher.ActiveSpawns)
            {
                if (spawn.SceneID == currentMapId)
                {
                    foundSpawns.Add(spawn);
                }
            }

            CurrentQuestLocations = foundSpawns
                .GroupBy(s => new { s.QuestName, s.TargetSubSceneID, PosKey = $"{s.Position.x:F1}_{s.Position.z:F1}" })
                .Select(g => g.First())
                .ToList();

            Debug.Log(LogPrefix + $"UpdateQuestLocations 完成。找到 {foundSpawns.Count} 个原始点，存储了 {CurrentQuestLocations.Count} 个唯一任务点。");
        }
    }
}