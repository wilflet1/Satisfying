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
            public Vector3 Velocity;        // set for anything that has to fall
            public bool Falls;
        }

        readonly Transform _root;
        readonly Palette _palette;
        readonly List<Item> _active = new List<Item>();
        readonly Stack<Item> _tracerPool = new Stack<Item>();
        readonly Stack<Item> _sparkPool = new Stack<Item>();
        readonly Stack<Item> _bloodPool = new Stack<Item>();
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

        /// <summary>
        /// A round going into someone. Drawn on whoever is told about the hit, and they are only told
        /// once the server has resolved it - so this never appears for a shot the server disagreed
        /// with, which is the whole reason it is driven by the hit event rather than by pulling the
        /// trigger.
        ///
        /// A spray of small dark flecks thrown along the round's path, under gravity. Not a decal:
        /// there are no textures in this project and a puff of geometry reads better than a red
        /// square would.
        /// </summary>
        public void Blood(Vector3 position, Vector3 direction, float amount)
        {
            int flecks = Mathf.Clamp(Mathf.RoundToInt(5f + amount * 12f), 5, 22);
            Vector3 along = direction.sqrMagnitude > 1e-4f ? direction.normalized : Vector3.forward;

            for (int i = 0; i < flecks; i++)
            {
                Item item = _bloodPool.Count > 0 ? _bloodPool.Pop() : CreateBlood();
                item.Transform.gameObject.SetActive(true);
                item.Transform.position = position + Random.insideUnitSphere * 0.05f;
                item.Transform.rotation = Random.rotation;

                float scale = Mathf.Lerp(0.012f, 0.045f, Random.value) * Mathf.Lerp(0.7f, 1.4f, amount);
                item.Transform.localScale = new Vector3(scale, scale, scale * Mathf.Lerp(1f, 2.2f, Random.value));
                item.BaseScale = item.Transform.localScale;

                // Mostly onward through the wound, with a cone of scatter round it.
                item.Velocity = (along * Mathf.Lerp(1.6f, 4.5f, Random.value)
                                 + Random.onUnitSphere * Mathf.Lerp(0.8f, 2.4f, Random.value))
                                * Mathf.Lerp(0.8f, 1.2f, amount);
                item.Falls = true;
                item.Expiry = Time.time + Random.Range(0.45f, 0.9f);
                item.ScaleDown = true;
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

        Item CreateBlood()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "blood";
            go.layer = _layer;
            Object.Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = _palette.Blood;
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

        public void Update() { Update(Time.deltaTime); }

        /// <summary>
        /// Stepped by an explicit dt so the effects can be run forward outside play mode - the shot
        /// sheet photographs a blood spray in flight, and Time.deltaTime is zero there.
        /// </summary>
        public void Update(float dt)
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
                    if (item.Falls) { item.Falls = false; _bloodPool.Push(item); }
                    else if (item.Light == null) _tracerPool.Push(item);
                    else _sparkPool.Push(item);
                    continue;
                }

                if (item.Falls)
                {
                    item.Velocity += Vector3.down * (14f * dt);
                    item.Transform.position += item.Velocity * dt;
                    if (item.Velocity.sqrMagnitude > 0.01f)
                        item.Transform.rotation = Quaternion.LookRotation(item.Velocity.normalized);
                }

                if (!item.ScaleDown) continue;
                float k = Mathf.Clamp01((item.Expiry - now) * 12f);
                item.Transform.localScale = new Vector3(item.BaseScale.x * k, item.BaseScale.y * k, item.BaseScale.z);
                if (item.Light != null && item.Light.enabled) item.Light.intensity = item.LightIntensity * k;
            }
        }
    }
}
