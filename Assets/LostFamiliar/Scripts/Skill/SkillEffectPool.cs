using System.Collections.Generic;
using UnityEngine;

namespace LostFamiliar.Battle
{
    // Pool for StarNova projectiles and hit effects. One instance belongs to one executor.
    public sealed class SkillEffectPool
    {
        private sealed class CachedComponents
        {
            public Renderer[] Renderers;
            public TrailRenderer[] Trails;
            public ParticleSystem[] Particles;
        }

        private readonly Dictionary<GameObject, Queue<GameObject>> _available = new();
        private readonly HashSet<GameObject> _active = new();
        private readonly Dictionary<GameObject, GameObject> _sourceByInstance = new();
        private readonly Dictionary<GameObject, CachedComponents> _components = new();

        public int CreatedCount { get; private set; }
        public int ReusedCount { get; private set; }
        public int ReleasedCount { get; private set; }
        public int ActiveCount => _active.Count;

        public void GetPrefabStats(GameObject prefab, out int created, out int reused, out int released, out int active)
        {
            created = reused = released = active = 0;
            if (prefab == null) return;
            _createdByPrefab.TryGetValue(prefab, out created);
            _reusedByPrefab.TryGetValue(prefab, out reused);
            _releasedByPrefab.TryGetValue(prefab, out released);
            foreach (GameObject instance in _active)
                if (instance != null && _sourceByInstance.TryGetValue(instance, out GameObject source) && source == prefab)
                    active++;
        }

        private readonly Dictionary<GameObject, int> _createdByPrefab = new();
        private readonly Dictionary<GameObject, int> _reusedByPrefab = new();
        private readonly Dictionary<GameObject, int> _releasedByPrefab = new();

        private static void Increment(Dictionary<GameObject, int> counters, GameObject prefab)
        {
            counters.TryGetValue(prefab, out int count);
            counters[prefab] = count + 1;
        }

        public bool IsManaged(GameObject instance) =>
            instance != null && _sourceByInstance.ContainsKey(instance);

        public GameObject Get(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return null;
            if (!_available.TryGetValue(prefab, out Queue<GameObject> queue))
            {
                queue = new Queue<GameObject>();
                _available.Add(prefab, queue);
            }

            GameObject instance = null;
            while (queue.Count > 0 && instance == null)
                instance = queue.Dequeue();

            if (instance == null)
            {
                instance = Object.Instantiate(prefab, position, rotation);
                _sourceByInstance[instance] = prefab;
                _components[instance] = new CachedComponents
                {
                    Renderers = instance.GetComponentsInChildren<Renderer>(true),
                    Trails = instance.GetComponentsInChildren<TrailRenderer>(true),
                    Particles = instance.GetComponentsInChildren<ParticleSystem>(true)
                };
                CreatedCount++;
                Increment(_createdByPrefab, prefab);
            }
            else
            {
                ReusedCount++;
                Increment(_reusedByPrefab, prefab);
            }

            CachedComponents cached = _components[instance];
            instance.transform.SetPositionAndRotation(position, rotation);
            foreach (Renderer renderer in cached.Renderers)
                if (renderer != null) renderer.enabled = true;
            foreach (TrailRenderer trail in cached.Trails)
                if (trail != null) trail.Clear();
            foreach (ParticleSystem particle in cached.Particles)
                if (particle != null)
                    particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            _active.Add(instance);
            instance.SetActive(true);
            foreach (ParticleSystem particle in cached.Particles)
                if (particle != null) particle.Play(true);
            return instance;
        }

        public bool TryRelease(GameObject instance)
        {
            if (instance == null || !_sourceByInstance.TryGetValue(instance, out GameObject prefab))
                return false;
            // Managed objects must never fall through to the ordinary Destroy path.
            if (!_active.Remove(instance)) return true;

            CachedComponents cached = _components[instance];
            foreach (ParticleSystem particle in cached.Particles)
                if (particle != null)
                    particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (TrailRenderer trail in cached.Trails)
                if (trail != null) trail.Clear();
            instance.SetActive(false);
            _available[prefab].Enqueue(instance);
            ReleasedCount++;
            Increment(_releasedByPrefab, prefab);
            return true;
        }

        // Snapshot active instances because TryRelease modifies _active.
        public void ReleaseAllActive()
        {
            if (_active.Count == 0) return;
            var snapshot = new List<GameObject>(_active);
            foreach (GameObject instance in snapshot)
            {
                if (instance != null) TryRelease(instance);
            }
        }

        public void Clear()
        {
            foreach (GameObject instance in _sourceByInstance.Keys)
                if (instance != null) Object.Destroy(instance);
            _active.Clear();
            _sourceByInstance.Clear();
            _components.Clear();
            _available.Clear();
            _createdByPrefab.Clear();
            _reusedByPrefab.Clear();
            _releasedByPrefab.Clear();
        }
    }
}
