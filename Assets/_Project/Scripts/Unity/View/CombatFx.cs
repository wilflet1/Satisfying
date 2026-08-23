using System.Collections.Generic;
using UnityEngine;

namespace Satisfying.Game
{
    /// <summary>
    /// Pooled shooting effects: tracers, muzzle flashes and impact sparks. Every shot needs to produce
    /// something you can see within the same frame you pressed the trigger.
    /// </summary>
    public sealed class CombatFx
    {
        sealed class Item
        {
            public Transform Transform;
            public float Expiry;
            public Vector3 BaseScale;
            public bool ScaleDown;
            public Light Light;
            public float LightIntensity;
        }

        readonly Transform _root;
        readonly Palette _palette;
        readonly List<Item> _active = new List<Item>();
        readonly Stack<Item> _tracerPool = new Stack<Item>();
        readonly Stack<Item> _sparkPool = new Stack<Item>();
        readonly int _layer;

        public CombatFx(Transform parent, Palette palette, int layer)
        {
            GameObject root = new GameObject("Combat FX");
            root.transform.SetParent(parent, false);
            _root = root.transform;
            _palette = palette;
            _layer = layer;
        }

        public void Tracer(Vector3 from, Vector3 to, float life = 0.055f)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.05f) return;

            Item item = _tracerPool.Count > 0 ? _tracerPool.Pop() : CreateTracer();
            item.Transform.gameObject.SetActive(true);
            item.Transform.position = from + delta * 0.5f;
            item.Transform.rotation = Quaternion.LookRotation(delta / length);
            item.Transform.localScale = new Vector3(0.022f, 0.022f, length);
            item.BaseScale = item.Transform.localScale;
            item.Expiry = Time.time + life;
            item.ScaleDown = true;
            _active.Add(item);
        }

        public void MuzzleFlash(Vector3 position, Vector3 direction, float life = 0.045f)
        {
            Item item = _sparkPool.Count > 0 ? _sparkPool.Pop() : CreateSpark();
            item.Transform.gameObject.SetActive(true);
            item.Transform.position = position + direction * 0.08f;
            item.Transform.rotation = Quaternion.LookRotation(direction);
            item.Transform.localScale = new Vector3(0.16f, 0.16f, 0.28f);
            item.BaseScale = item.Transform.localScale;
            item.Expiry = Time.time + life;
            item.ScaleDown = true;
            if (item.Light != null)
            {
                item.Light.enabled = true;
                item.Light.intensity = 3.2f;
                item.LightIntensity = 3.2f;
            }
            _active.Add(item);
        }

        /// <summary>A pane coming apart: a handful of shards that fall and fade.</summary>
        public void Shatter(Vector3 centre, Vector3 size, int shards = 14)
        {
            for (int i = 0; i < shards; i++)
            {
                Item item = _sparkPool.Count > 0 ? _sparkPool.Pop() : CreateSpark();
                item.Transform.gameObject.SetActive(true);

                Vector3 offset = new Vector3(
                    (Random.value - 0.5f) * size.x,
                    (Random.value - 0.5f) * size.y,
                    (Random.value - 0.5f) * size.z);
                item.Transform.position = centre + offset;
                item.Transform.rotation = Random.rotation;

                float scale = Mathf.Lerp(0.06f, 0.16f, Random.value);
                item.Transform.localScale = new Vector3(scale, scale * 1.4f, scale * 0.15f);
                item.BaseScale = item.Transform.localScale;
                item.Expiry = Time.time + Random.Range(0.35f, 0.8f);
                item.ScaleDown = true;
                if (item.Light != null) item.Light.enabled = false;
                _active.Add(item);
            }
        }

        public void Impact(Vector3 position, Vector3 normal, float life = 0.16f)
        {
            Item item = _sparkPool.Count > 0 ? _sparkPool.Pop() : CreateSpark();
            item.Transform.gameObject.SetActive(true);
            item.Transform.position = position + normal * 0.02f;
            item.Transform.rotation = Quaternion.LookRotation(normal);
            item.Transform.localScale = new Vector3(0.1f, 0.1f, 0.05f);
            item.BaseScale = item.Transform.localScale;
            item.Expiry = Time.time + life;
            item.ScaleDown = true;
            if (item.Light != null) item.Light.enabled = false;
            _active.Add(item);
        }

        Item CreateTracer()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "tracer";
            go.layer = _layer;
            Object.Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = _palette.Glow;
            go.transform.SetParent(_root, false);
            Item item = new Item();
            item.Transform = go.transform;
            return item;
        }

        Item CreateSpark()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "spark";
            go.layer = _layer;
            Object.Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = _palette.Glow;
            go.transform.SetParent(_root, false);

            GameObject lightGo = new GameObject("flash light");
            lightGo.transform.SetParent(go.transform, false);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 7f;
            light.color = new Color(1f, 0.85f, 0.55f);
            light.enabled = false;

            Item item = new Item();
            item.Transform = go.transform;
            item.Light = light;
            return item;
        }

        public void Update()
        {
            float now = Time.time;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Item item = _active[i];
                if (now >= item.Expiry)
                {
                    item.Transform.gameObject.SetActive(false);
                    if (item.Light != null) item.Light.enabled = false;
                    _active.RemoveAt(i);
                    if (item.Light == null) _tracerPool.Push(item);
                    else _sparkPool.Push(item);
                    continue;
                }

                if (!item.ScaleDown) continue;
                float k = Mathf.Clamp01((item.Expiry - now) * 12f);
                item.Transform.localScale = new Vector3(item.BaseScale.x * k, item.BaseScale.y * k, item.BaseScale.z);
                if (item.Light != null && item.Light.enabled) item.Light.intensity = item.LightIntensity * k;
            }
        }
    }
}
