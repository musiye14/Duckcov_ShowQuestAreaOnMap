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

		// 确保地图已经扫描完成
		public bool IsSceneScanComplete { get; private set; } = false;

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
				 IsSceneScanComplete = false;
                 PreScanSceneForTriggers();
				 IsSceneScanComplete = true;
             } else {
                 _triggersScannedForCurrentScene = false; 
				 IsSceneScanComplete = false;
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
			IsSceneScanComplete = false;
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
			IsSceneScanComplete = false; 
            _triggersScannedForCurrentScene = false; 
            CurrentQuestLocations.Clear(); 
            PreScanSceneForTriggers();
			IsSceneScanComplete = true;
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
				/*
					quest.RequireSceneInfo 这个为null还真不能断定不可以
					QuestTask_ReachLocation这个类型的任务有些会分开在两个地图，然后这时候主任务是没有RequireSceneInfo的，都在task里面存，比如Quest861 仓库和农场镇找伐木场
				*/

                // ===== 修改：移除 Quest 级别的场景过滤 =====
                // 原因：跨地图任务的 requireSceneID 可能只写了第一个场景，导致其他场景的 Task 无法显示
                // 应该让每个 Task 根据自己的场景信息（MultiSceneLocation、SpawnPrefabForTask 等）决定是否显示
                // if (quest.RequireSceneInfo != null && quest.RequireSceneInfo.ID != currentMapId) continue;

                foreach (var task in quest.Tasks)
                {
                    if (task == null || task.IsFinished()) continue;
					

                    float taskRadius = 10f;
                    Vector3? taskPosition = null;
                    string questName = quest.DisplayName;
                    string subSceneId = null;
					Debug.Log(LogPrefix + "检查" + questName +"的task");
                    try
                    {
                        MapElementForTask mapElement = GetMapElementFromTask(task);
                        if (mapElement != null)
                        {
                            if (mapElement.locations != null && mapElement.locations.Count > 0)
                            {
                                foreach (MultiSceneLocation loc in mapElement.locations)
                                {
                                    // ===== 修改：增加场景匹配检查 =====
                                    // 只处理属于当前场景的位置
                                    bool sceneMatches = loc.SceneID == currentMapId ||
                                                       loc.SceneID.Contains(currentMapId) ||
                                                       currentMapId.Contains(loc.SceneID);

                                    if (!loc.IsUnityNull() && sceneMatches && loc.TryGetLocationPosition(out Vector3 pos))
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

                        if (task is QuestTask_TaskEvent eventTaskNoMapElement)
                        {
                            // ===== 新增：检查 SpawnPrefabForTask 组件（跨地图任务情况）=====
                            // QuestTask_TaskEvent 在跨地图任务中会挂载 SpawnPrefabForTask 组件
                            // 从 SpawnPrefabForTask.locations 中可以获取正确的场景 ID 和位置
                            if (!taskPosition.HasValue)
                            {
                                var spawnPrefabComponent = task.GetComponent<SpawnPrefabForTask>();
                                if (spawnPrefabComponent != null)
                                {
                                    try
                                    {
                                        // 反射获取 SpawnPrefabForTask 的 locations 字段
                                        var locationsField = typeof(SpawnPrefabForTask).GetField("locations", BindingFlags.NonPublic | BindingFlags.Instance);
                                        if (locationsField != null)
                                        {
                                            var locations = locationsField.GetValue(spawnPrefabComponent) as List<MultiSceneLocation>;
                                            if (locations != null && locations.Count > 0)
                                            {
                                                foreach (var loc in locations)
                                                {
                                                    // 检查场景是否匹配
                                                    bool sceneMatches = loc.SceneID == currentMapId ||
                                                                       loc.SceneID.Contains(currentMapId) ||
                                                                       currentMapId.Contains(loc.SceneID);

                                                    if (!loc.IsUnityNull() && sceneMatches && loc.TryGetLocationPosition(out Vector3 pos))
                                                    {
                                                        taskPosition = pos;
                                                        subSceneId = loc.SceneID;
                                                        taskRadius = 10f; // SpawnPrefabForTask 使用默认半径
                                                        Debug.Log(LogPrefix + $"[QuestTask_TaskEvent] 从 SpawnPrefabForTask 获取位置: {pos}, SceneID: {loc.SceneID}");
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.LogWarning(LogPrefix + $"反射 SpawnPrefabForTask.locations 失败: {ex.Message}");
                                    }
                                }
                            }

                            // ===== 原有逻辑：通过 TaskEventEmitter 查找位置 =====
                            #region Emitter Search Logic
                            if (!taskPosition.HasValue && QuestSpawnPatcher.TaskEventEmitterEventKeyField != null)
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

                        if (task is QuestTask_ReachLocation ReachLocationTask)
                        {
                            // 通过反射获取 location 和 radius 字段
                            var locationField = typeof(QuestTask_ReachLocation).GetField("location", BindingFlags.NonPublic | BindingFlags.Instance);
                            var radiusField = typeof(QuestTask_ReachLocation).GetField("radius", BindingFlags.NonPublic | BindingFlags.Instance);

                            if (locationField != null)
                            {
                                object locationObj = locationField.GetValue(ReachLocationTask);
                                if (locationObj != null)
                                {
                                    MultiSceneLocation location = (MultiSceneLocation)locationObj;
                                    // 气笑了场景ID是Level_HiddenWarehouse_Main  任务要的ID是Level_HiddenWarehouse  同一个地方但是不同ID说是
                                    if ((location.SceneID.Contains(currentMapId) || currentMapId.Contains(location.SceneID) || location.SceneID == currentMapId) && location.TryGetLocationPosition(out Vector3 pos))
                                    {
                                        taskPosition = pos;
                                        subSceneId = location.SceneID;

                                        if (radiusField != null)
                                        {
                                            taskRadius = (float)radiusField.GetValue(ReachLocationTask);
                                        }
                                    }
                                }
                            }
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