using System;
using System.Collections.Generic;
using Duckov.Quests;
using Duckov.Quests.Conditions; // 需要 using RequireQuestsActive
using Duckov.Quests.Tasks;
using Duckov.Scenes;
using System.Reflection;
using Duckov.MiniMaps;
using UnityEngine;
using UnityEngine.SceneManagement; // 需要 using SceneManager
using System.Linq;
using Unity.VisualScripting;

namespace ShowQuestsAreaOnMap
{
    // 这个类负责查找和缓存任务位置信息
    public class QuestsAreaManager : MonoBehaviour
    {
        public static QuestsAreaManager Instance { get; private set; }
        private const string LogPrefix = "[ShowQuestsAreaOnMap][Manager] ";

        // --- 缓存 ---
        // 缓存预扫描的 RequireQuestsActive 触发器
        private Dictionary<int, List<RequireQuestsActive>> _cachedQuestTriggers = new Dictionary<int, List<RequireQuestsActive>>();
        private bool _triggersScannedForCurrentScene = false;
        // 缓存 Task 类型对应的 mapElement 字段反射信息
        private Dictionary<Type, FieldInfo> _mapElementFieldCache = new Dictionary<Type, FieldInfo>();

        // --- 公开结果 ---
        // 存储当前场景最终要去绘制的任务点列表
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
            // 可选：如果希望管理器在切换场景时不被销毁（但重新扫描通常更好）
            // DontDestroyOnLoad(this.gameObject);
        }

        private void OnEnable()
        {
             Debug.Log(LogPrefix + "已启用。订阅 sceneLoaded 事件。");
             SceneManager.sceneLoaded += OnSceneLoaded;
             // 如果启用时场景已加载，立即执行一次扫描
             if (SceneManager.GetActiveScene().isLoaded)
             {
                 PreScanSceneForTriggers();
             } else {
                 _triggersScannedForCurrentScene = false; // 标记场景加载时需要扫描
             }
        }

        private void OnDisable()
        {
            Debug.Log(LogPrefix + "已禁用。取消订阅 sceneLoaded 事件并清理缓存。");
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _triggersScannedForCurrentScene = false;
            _cachedQuestTriggers.Clear();
            _mapElementFieldCache.Clear(); // 清理反射缓存
            CurrentQuestLocations.Clear(); // 清理位置列表
        }

         private void OnDestroy() {
             if (Instance == this)
             {
                 Instance = null;
                  Debug.Log(LogPrefix + "实例已销毁。");
             }
        }

        // 场景加载完成时调用
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Debug.Log(LogPrefix + $"场景 '{scene.name}' 已加载。重置扫描标记并执行预扫描...");
            _triggersScannedForCurrentScene = false; // 重置标记，确保执行
            CurrentQuestLocations.Clear(); // 清理旧场景的位置
            PreScanSceneForTriggers();
        }

        // 预扫描场景中的 RequireQuestsActive
        private void PreScanSceneForTriggers()
        {
            // 防止在同一场景加载事件周期内重复扫描
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

        // --- 泛型扫描 MapElement 辅助方法 (现在是私有的) ---
        private MapElementForTask GetMapElementFromTask(Task task)
        {
            if (task == null) return null; // 安全检查
            Type taskType = task.GetType();
            FieldInfo mapElementField;

            if (_mapElementFieldCache.TryGetValue(taskType, out mapElementField))
            {
                if (mapElementField == null) return null;
                try { return (MapElementForTask)mapElementField.GetValue(task); } catch { return null;}
            }

            // 未缓存，进行反射并缓存
            mapElementField = taskType.GetField("mapElement", BindingFlags.NonPublic | BindingFlags.Instance);
            _mapElementFieldCache[taskType] = mapElementField; // 缓存结果（即使是 null）

            if (mapElementField == null) return null;

            try { return (MapElementForTask)mapElementField.GetValue(task); } catch { return null;}
        }

        // --- 【!!! 核心扫描逻辑移到这里 !!!】 ---
        // 这个方法由 ModBehaviour 调用，用于更新任务位置列表
        public void UpdateQuestLocations()
        {
            Debug.Log(LogPrefix + "UpdateQuestLocations 调用。开始扫描...");
            CurrentQuestLocations.Clear(); // 清空旧结果

            if (QuestManager.Instance == null) { Debug.LogError(LogPrefix + "QuestManager.Instance 为 null。"); return; }

            // 1. 清理 Patcher 的动态列表 (仍然需要)
            QuestSpawnPatcher.RescanAndClear();

            // 2. 获取当前地图 ID
            string currentMapId = LevelManager.GetCurrentLevelInfo().sceneName;
            if (string.IsNullOrEmpty(currentMapId)) { Debug.LogWarning(LogPrefix + "无法获取当前地图 ID。"); return; }

            // 3. 确保触发器已预扫描
            if (!_triggersScannedForCurrentScene)
            {
                 Debug.LogWarning(LogPrefix + "当前场景触发器未扫描，执行预扫描...");
                 PreScanSceneForTriggers();
            }

            List<ActiveQuestSpawn> foundSpawns = new List<ActiveQuestSpawn>(); // 临时列表

            // --- 4. 扫描激活任务以查找【静态】标记点 ---
            // Debug.Log(LogPrefix + "扫描活动任务查找静态标记..."); // 这条日志可能太频繁
            foreach (Quest quest in QuestManager.Instance.ActiveQuests)
            {
                if (quest.RequireSceneInfo == null || quest.RequireSceneInfo.ID != currentMapId) continue;

                foreach (var task in quest.Tasks)
                {
                    if (task == null || task.IsFinished()) continue;

                    float taskRadius = 10f;
                    Vector3? taskPosition = null;
                    string questName = quest.DisplayName;

                    try
                    {
                        // 优先级 1: MapElement
                        MapElementForTask mapElement = GetMapElementFromTask(task);
                        if (mapElement != null)
                        {
                            if (mapElement.locations != null && mapElement.locations.Count > 0)
                            {
                                foreach (MultiSceneLocation loc in mapElement.locations)
                                {
                                    // 【!!! 修正 BUG 1 !!!】 移除错误的 IsUnityNull 判断
                                    if (loc.IsUnityNull() && loc.TryGetLocationPosition(out Vector3 pos))
                                    {
                                        taskPosition = pos;
                                        taskRadius = mapElement.range;
                                        break;
                                    }
                                }
                            }
                        }

                        // 优先级 2: RequireQuestsActive (使用缓存)
                        if (!taskPosition.HasValue)
                        {
                            if (_cachedQuestTriggers.TryGetValue(quest.ID, out var triggersForThisQuest))
                            {
                                RequireQuestsActive firstTrigger = triggersForThisQuest.FirstOrDefault(t => t != null);
                                if (firstTrigger != null)
                                {
                                    taskPosition = firstTrigger.transform.position;
                                }
                            }
                        }

                        // 优先级 3: TaskEvent Emitter (后备)
                        if (!taskPosition.HasValue && task is QuestTask_TaskEvent eventTaskNoMapElement /* && mapElement == null */ )
                        {
                            // ... (Emitter 查找逻辑 - 与之前 ModBehaviour 中的版本一致) ...
                             #region Emitter Search Logic (不变)
                              // Debug.Log(LogPrefix + $"[TaskEvent Emitter Fallback] ...");
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
                                                   // Debug.Log(LogPrefix + $"[TaskEvent Emitter Fallback] ...");
                                                   break;
                                               }
                                           } catch { /* ignore */ }
                                      }
                                      // if (!taskPosition.HasValue) { Debug.LogWarning(...); }
                                  }
                              } // else { Debug.LogError(...); }
                              #endregion
                        }
                        // 注意：我们移除了 SubmitItems Transform 作为后备

                    } catch (Exception e) { Debug.LogError(LogPrefix + $"扫描任务 '{task.GetType().Name}' ('{questName}') 时出错: {e.Message}"); }

                    // 添加找到的静态位置到临时列表
                    if (taskPosition.HasValue)
                    {
                        foundSpawns.Add(new ActiveQuestSpawn
                        {
                            AssociatedTask = task,
                            QuestName = questName,
                            Position = taskPosition.Value,
                            Radius = taskRadius > 0.1f ? taskRadius : 10f,
                            SceneID = currentMapId
                        });
                    }
                } // end foreach task
            } // end foreach quest

            // --- 5. 添加来自 Patcher 的【动态】任务 ---
            // Debug.Log(LogPrefix + $"检查 {QuestSpawnPatcher.ActiveSpawns.Count} 个动态 spawns...");
            foreach (var spawn in QuestSpawnPatcher.ActiveSpawns)
            {
                if (spawn.SceneID == currentMapId)
                {
                    foundSpawns.Add(spawn);
                    // Debug.Log(LogPrefix + $"Added dynamic point for '{spawn.QuestName}' @ {spawn.Position}");
                }
            }

            // --- 6. 去重并将最终结果存入 CurrentQuestLocations ---
            CurrentQuestLocations = foundSpawns
                .GroupBy(s => new { s.QuestName, PosKey = $"{s.Position.x:F1}_{s.Position.z:F1}" })
                .Select(g => g.First())
                .ToList(); // 将去重后的结果赋给公共属性

            Debug.Log(LogPrefix + $"UpdateQuestLocations 完成。找到 {foundSpawns.Count} 个原始点，存储了 {CurrentQuestLocations.Count} 个唯一任务点。");
        }
    }
}