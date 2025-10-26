using Duckov.Quests;
using Duckov.Quests.Tasks;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;


namespace ShowQuestsAreaOnMap
{
    public class ActiveQuestSpawn
    {
        public Task AssociatedTask { get; set; }
        public string QuestName { get; set; }
        public Vector3 Position { get; set; }
        public float Radius { get; set; }
        public string SceneID { get; set; }
    }

    public static class QuestSpawnPatcher
    {
        private const string LogPrefix = "[ShowQuestsAreaOnMap][QuestSpawnPatcher] ";
        
        public static readonly List<ActiveQuestSpawn> ActiveSpawns = new List<ActiveQuestSpawn>();
        
        public static readonly HashSet<MapElementForTask> ActiveMapElements = new HashSet<MapElementForTask>();
        
        private static PropertyInfo _spawnItemTaskTaskProperty;
        private static FieldInfo _taskEventEventKeyField;
        private static PropertyInfo _spawnPrefabTaskTaskProperty;
        public static FieldInfo TaskEventEmitterEventKeyField { get; private set; }
        
        private static SpawnPrefabForTask _currentlySpawningPrefabTask;
        
        public static void ApplyPatches(Harmony harmony)
        {

            Debug.Log(LogPrefix + "正在应用 Harmony 补丁...");
            
            
            _spawnItemTaskTaskProperty = typeof(SpawnItemForTask)
                .GetProperty("task", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_spawnItemTaskTaskProperty == null)
                Debug.LogError(LogPrefix + "反射失败: SpawnItemForTask.task");
            
            _taskEventEventKeyField = typeof(QuestTask_TaskEvent)
                .GetField("eventKey", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_taskEventEventKeyField == null)
                Debug.LogError(LogPrefix + "反射失败: QuestTask_TaskEvent.eventKey");
            
            _spawnPrefabTaskTaskProperty = typeof(SpawnPrefabForTask)
                .GetProperty("task", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_spawnPrefabTaskTaskProperty == null)
                Debug.LogError(LogPrefix + "反射失败: SpawnPrefabForTask.task");
            
            
            try
            {
                var typeTaskEventEmitter = AccessTools.TypeByName("TaskEventEmitter"); 
                if (typeTaskEventEmitter != null)
                {
                    TaskEventEmitterEventKeyField = typeTaskEventEmitter.GetField("eventKey", BindingFlags.NonPublic | BindingFlags.Instance);
                     
                    if (TaskEventEmitterEventKeyField == null)
                        Debug.LogError(LogPrefix + "反射失败: 无法找到 TaskEventEmitter 的 eventKey 字段!");
                    else
                        Debug.Log(LogPrefix + $"成功反射 TaskEventEmitter 的字段: {TaskEventEmitterEventKeyField.Name}");
                } else { Debug.LogError(LogPrefix + "反射失败: 找不到类型 TaskEventEmitter"); }
            } catch (System.Exception ex) { Debug.LogError(LogPrefix + $"反射 TaskEventEmitter eventKey 失败: {ex.Message}"); }
            
            try
            {
                var originalSpawnItem = AccessTools.Method(typeof(SpawnItemForTask), "SpawnItem");
                var postfixSpawnItem = new HarmonyMethod(typeof(QuestSpawnPatcher), nameof(OnItemSpawned));
                harmony.Patch(originalSpawnItem, postfix: postfixSpawnItem);
                Debug.Log(LogPrefix + "成功修补 SpawnItemForTask.SpawnItem");
            }
            catch (System.Exception e)
            {
                Debug.LogError(LogPrefix + "修补 SpawnItemForTask.SpawnItem 失败: " + e.Message);
            }

            try
            {
                var originalSpawnPrefab = AccessTools.Method(typeof(SpawnPrefabForTask), "Spawn");
                var prefixSpawnPrefab = new HarmonyMethod(typeof(QuestSpawnPatcher), nameof(OnPrefabSpawn_Prefix)); 
                harmony.Patch(originalSpawnPrefab, prefix: prefixSpawnPrefab); 
                Debug.Log(LogPrefix + "成功修补 SpawnPrefabForTask.Spawn (Prefix)");
            } catch (System.Exception e) { Debug.LogError(LogPrefix + "修补 SpawnPrefabForTask.Spawn (Prefix) 失败: " + e.Message); }
            
            try
            {
                var originalInstantiate = AccessTools.Method(typeof(Object), nameof(Object.Instantiate),
                    new [] { typeof(GameObject), typeof(Vector3), typeof(Quaternion) });
                var postfixInstantiate = new HarmonyMethod(typeof(QuestSpawnPatcher), nameof(OnInstantiate_Postfix));
                harmony.Patch(originalInstantiate, postfix: postfixInstantiate);
                Debug.Log(LogPrefix + "成功修补 Object.Instantiate (Postfix)");
            } catch (System.Exception e) { Debug.LogError(LogPrefix + "修补 Object.Instantiate 失败: " + e.Message); }
            
            try
            {
                var originalSetVisibility = AccessTools.Method(typeof(MapElementForTask), "SetVisibility");
                var postfixSetVisibility = new HarmonyMethod(typeof(QuestSpawnPatcher), nameof(OnSetVisibility));
                harmony.Patch(originalSetVisibility, postfix: postfixSetVisibility);
                Debug.Log(LogPrefix + "成功修补 MapElementForTask.SetVisibility");
            }
            catch (System.Exception e)
            {
                Debug.LogError(LogPrefix + "修补 SetVisibility 失败: " + e.Message);
            }
            
            try
            {
                var originalDespawnAll = AccessTools.Method(typeof(MapElementForTask), "DespawnAll");
                var postfixDespawnAll = new HarmonyMethod(typeof(QuestSpawnPatcher), nameof(OnDespawnAll));
                harmony.Patch(originalDespawnAll, postfix: postfixDespawnAll);
                Debug.Log(LogPrefix + "成功修补 MapElementForTask.DespawnAll");
            }
            catch (System.Exception e)
            {
                Debug.LogError(LogPrefix + "修补 DespawnAll 失败: " + e.Message);
            }
        }
        
        
        public static void RescanAndClear()
        {
            if (QuestManager.Instance == null) return;
            
            var activeTasks = new HashSet<Task>();
            foreach (var quest in QuestManager.Instance.ActiveQuests)
            {
                foreach (var task in quest.Tasks)
                {
                    if (task != null && !task.IsFinished())
                    {
                        activeTasks.Add(task);
                    }
                }
            }
            
            ActiveSpawns.RemoveAll(spawn => 
                spawn.AssociatedTask == null || 
                !activeTasks.Contains(spawn.AssociatedTask)
            );
        }
        

        private static void OnItemSpawned(SpawnItemForTask __instance, Vector3 pos)
        {
            Debug.Log(LogPrefix + ">>> OnItemSpawned PATCH TRIGGERED <<<");
            
            var task = (Task) _spawnItemTaskTaskProperty.GetValue(__instance, null);
            if (task == null || task.IsFinished()) return;

            if (ActiveSpawns.Exists(s => s.AssociatedTask == task)) return;
            
            string sceneId = task.Master.RequireSceneInfo?.ID;
            if (string.IsNullOrEmpty(sceneId))
            {
                Debug.LogWarning(LogPrefix + $"[OnItemSpawned] 任务 '{task.Master.DisplayName}' 没有 SceneID!");
                return; 
            }
            
            Debug.Log(LogPrefix + $"[OnItemSpawned] 拦截到物品生成: {task.Master.DisplayName} @ {pos}");
            ActiveSpawns.Add(new ActiveQuestSpawn
            {
                AssociatedTask = task,
                QuestName = task.Master.DisplayName,
                Position = pos,
                Radius = 10f,
                SceneID = sceneId
            });
        }

       
       
       private static void OnPrefabSpawn_Prefix(SpawnPrefabForTask __instance)
       {
           // Debug.Log(LogPrefix+$"触发了OnPrefabSpawn_Prefix");
           _currentlySpawningPrefabTask = __instance;
       }
       
       private static void OnInstantiate_Postfix(Object __result, Vector3 position)
       {
           if (_currentlySpawningPrefabTask != null && __result != null && __result is GameObject spawnedGameObject) 
           {
               // Debug.Log(LogPrefix+$"触发了OnInstantiate_Postfix");
               var instance = _currentlySpawningPrefabTask; 
               _currentlySpawningPrefabTask = null; 

               Task task = null;
               try { task = (Task)_spawnPrefabTaskTaskProperty.GetValue(instance, null); }
               catch (System.Exception ex) { Debug.LogError(LogPrefix + $"[OnInstantiate] 反射 task 失败: {ex.Message}"); return; }

               if (task == null || task.IsFinished() || task.Master == null) return;

               Vector3 pos = spawnedGameObject.transform.position; 
               if (ActiveSpawns.Exists(s => s.AssociatedTask == task && Vector3.Distance(s.Position, pos) < 0.1f)) return;

               string sceneId = task.Master.RequireSceneInfo?.ID;
               if (string.IsNullOrEmpty(sceneId)) return;

               // Debug.Log(LogPrefix + $"[OnInstantiate] >>> 成功捕获 Prefab ({spawnedGameObject.name}) 生成: Quest='{task.Master.DisplayName}', Pos={pos}, Scene='{sceneId}' <<<");
               ActiveSpawns.Add(new ActiveQuestSpawn
               {
                   AssociatedTask = task,
                   QuestName = task.Master.DisplayName,
                   Position = pos,
                   Radius = 10f,
                   SceneID = sceneId
               });
           }
           else if (_currentlySpawningPrefabTask != null) 
           {
               // Debug.Log(LogPrefix + $"[OnInstantiate] ... 但是 Instantiate 返回的不是 GameObject (可能是 null 或其他类型: {__result?.GetType().Name ?? "null"})。清空标记。"); 
               _currentlySpawningPrefabTask = null; 
           }
       }
        
        private static void OnSetVisibility(MapElementForTask __instance, bool _visable)
        {
            // Debug.Log(LogPrefix + $">>> OnSetVisibility PATCH TRIGGERED for '{__instance?.name ?? "null"}' with visable={_visable} <<<");
            // Debug.Log(LogPrefix + $"执行OnSetVisibility");
            if (_visable)
            {
                if (ActiveMapElements.Add(__instance))
                {
                    Debug.Log(LogPrefix + $"[静态] 添加 MapElement: {__instance.name}");
                }
            }
            else
            {
                if (ActiveMapElements.Remove(__instance)) 
                {
                    Debug.Log(LogPrefix + $"[静态] 移除 MapElement: {__instance.name}");
                }
            }
        }

        private static void OnDespawnAll(MapElementForTask __instance)
        {
            // Debug.Log(LogPrefix + $">>> OnDespawnAll PATCH TRIGGERED for '{__instance?.name ?? "null"}' <<<");
            
            if (ActiveMapElements.Remove(__instance))
            {
                Debug.Log(LogPrefix + $"[静态] (DespawnAll) 移除 MapElement: {__instance.name}");
            }
        }
        
    }
}