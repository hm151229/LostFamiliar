using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Profiling;
#endif

namespace LostFamiliar.Battle
{
    public sealed partial class PlayerSkillExecutor
    {
        // Release retains exactly the original Instantiate call, with no counters.
        private GameObject InstantiateSkillPrefab(GameObject prefab, Vector3 position, Quaternion rotation)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (PerformanceSession) PerformanceDirectInstantiates++;
#endif
            return Instantiate(prefab, position, rotation);
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool PerformanceSession { get; private set; }
        public bool PerformanceBefore { get; private set; }
        public bool PerformanceBodyRunning { get; private set; }
        public int PerformanceRoutines { get; private set; }
        public int PerformanceDirectInstantiates { get; private set; }
        public int PerformanceHits { get; private set; }
        public int PerformanceProjectileHits { get; private set; }
        public double PerformanceDamage { get; private set; }
        public double PerformanceBodyEndedAt { get; private set; }
        public string PerformanceError { get; private set; }
        public int PerformanceCreated => _starNovaProjectilePool.CreatedCount;
        public int PerformanceReused => _starNovaProjectilePool.ReusedCount;
        public int PerformanceReturned => _starNovaProjectilePool.ReleasedCount;
        private readonly List<GameObject> _performanceEffects = new(128);
        private static readonly ProfilerMarker PerformanceStep = new("LostFamiliar.Skill.MoveNext");

        public bool PerformanceIdle
        {
            get
            {
                if (PerformanceRoutines != 0 || _starNovaProjectilePool.ActiveCount != 0) return false;
                for (int i = _performanceEffects.Count - 1; i >= 0; i--)
                {
                    GameObject effect = _performanceEffects[i];
                    // Destroy is deferred: retain inactive non-pooled instances until actually destroyed.
                    if (effect == null || (_starNovaProjectilePool.IsManaged(effect) && !effect.activeSelf))
                        _performanceEffects.RemoveAt(i);
                    else return false;
                }
                return true;
            }
        }

        public bool ConfigurePerformance(bool before, bool clearPool)
        {
            if (!PerformanceIdle) return false;
            PerformanceSession = true;
            PerformanceBefore = before;
            if (clearPool) _starNovaProjectilePool.Clear();
            return true;
        }

        public void BeginPerformanceSample()
        {
            PerformanceError = null;
            PerformanceBodyEndedAt = 0;
        }

        private IEnumerator TrackPerformanceRoutine(IEnumerator routine, bool body)
        {
            PerformanceRoutines++;
            if (body) PerformanceBodyRunning = true;
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            try
            {
                while (stack.Count > 0)
                {
                    IEnumerator current = stack.Peek();
                    bool moved = false;
                    try { using (PerformanceStep.Auto()) moved = current.MoveNext(); }
                    catch (Exception ex) { PerformanceError = ex.ToString(); }
                    if (PerformanceError != null) yield break;
                    if (!moved) { (current as IDisposable)?.Dispose(); stack.Pop(); continue; }
                    // Unity normally advances nested enumerators outside the parent MoveNext.
                    // Traverse those here so their actual execution is marked as well.
                    if (current.Current is IEnumerator nested) { stack.Push(nested); continue; }
                    yield return current.Current;
                }
            }
            finally
            {
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
                PerformanceRoutines--;
                if (body) { PerformanceBodyRunning = false; PerformanceBodyEndedAt = Time.realtimeSinceStartupAsDouble; }
            }
        }
#endif
    }
}
