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
            public bool Rises;              // smoke: drifts up and swells instead of dropping
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

        /// <summary>
        /// A grenade going off. Four things at once, because one of them on its own always reads as a
        /// puff: a white flash that is gone in two frames, a fireball that expands and dies, a slower
        /// cloud of smoke that keeps rising, and hot debris thrown out under gravity.
        ///
        /// The flash is a point light as well as geometry - a blast that does not light the room it is
        /// in looks painted on, and it is the single cheapest thing that sells it.
        /// </summary>
        public void Explosion(Vector3 centre, float radius)
        {
            // The flash.
            Item flash = _sparkPool.Count > 0 ? _sparkPool.Pop() : CreateSpark();
            flash.Transform.gameObject.SetActive(true);
            flash.Transform.position = centre;
            flash.Transform.rotation = Random.rotation;
            flash.Transform.localScale = Vector3.one * radius * 1.5f;
            flash.BaseScale = flash.Transform.localScale;
            flash.Expiry = Time.time + 0.09f;
            flash.ScaleDown = true;
            flash.Falls = false;
            if (flash.Light != null)
            {
                flash.Light.enabled = true;
                flash.Light.range = radius * 9f;
                flash.Light.intensity = 22f;
                flash.LightIntensity = 22f;
            }
            _active.Add(flash);

            // The fireball: a handful of overlapping lumps so it is not one sphere.
            for (int i = 0; i < 9; i++)
            {
                Item ball = _sparkPool.Count > 0 ? _sparkPool.Pop() : CreateSpark();
                ball.Transform.gameObject.SetActive(true);
                ball.Transform.position = centre + Random.insideUnitSphere * radius * 0.45f;
                ball.Transform.rotation = Random.rotation;
                float size = radius * Random.Range(0.55f, 1.15f);
                ball.Transform.localScale = Vector3.one * size;
                ball.BaseScale = ball.Transform.localScale;
                ball.Expiry = Time.time + Random.Range(0.18f, 0.34f);
                ball.ScaleDown = true;
                ball.Falls = false;
                if (ball.Light != null) ball.Light.enabled = false;
                _active.Add(ball);
            }

            // Smoke, rising and slow.
            for (int i = 0; i < 12; i++)
            {
                Item smoke = _smokePool.Count > 0 ? _smokePool.Pop() : CreateSmoke();
                smoke.Transform.gameObject.SetActive(true);
                smoke.Transform.position = centre + Random.insideUnitSphere * radius * 0.6f;
                smoke.Transform.rotation = Random.rotation;
                float size = radius * Random.Range(0.5f, 1.1f);
                smoke.Transform.localScale = Vector3.one * size;
                smoke.BaseScale = smoke.Transform.localScale;
                smoke.Velocity = Vector3.up * Random.Range(0.8f, 2.2f) + Random.insideUnitSphere * 1.2f;
                smoke.Rises = true;
                smoke.Falls = false;
                smoke.Expiry = Time.time + Random.Range(0.9f, 1.7f);
                smoke.ScaleDown = false;
                _active.Add(smoke);
            }

            // And what it throws.
            for (int i = 0; i < 22; i++)
            {
                Item bit = _bloodPool.Count > 0 ? _bloodPool.Pop() : CreateBlood();
                bit.Transform.gameObject.SetActive(true);
                bit.Transform.position = centre + Random.insideUnitSphere * 0.2f;
                bit.Transform.rotation = Random.rotation;
                float scale = Random.Range(0.03f, 0.09f);
                bit.Transform.localScale = new Vector3(scale, scale, scale * 2.4f);
                bit.BaseScale = bit.Transform.localScale;
                bit.Velocity = Random.onUnitSphere * Random.Range(6f, 17f) + Vector3.up * 3f;
                bit.Falls = true;
                bit.Rises = false;
                bit.Expiry = Time.time + Random.Range(0.5f, 1.1f);
                bit.ScaleDown = true;
                _active.Add(bit);
            }
        }

        readonly Stack<Item> _smokePool = new Stack<Item>();

        Item CreateSmoke()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "smoke";
            go.layer = _layer;
            Object.Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = _palette.Smoke;
            go.transform.SetParent(_root, false);
            Item item = new Item();
            item.Transform = go.transform;
            return item;
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
                    if (item.Rises) { item.Rises = false; _smokePool.Push(item); }
                    else if (item.Falls) { item.Falls = false; _bloodPool.Push(item); }
                    else if (item.Light == null) _tracerPool.Push(item);
                    else _sparkPool.Push(item);
                    continue;
                }

                if (item.Rises)
                {
                    // Smoke slows, swells and thins rather than falling.
                    item.Velocity = Vector3.Lerp(item.Velocity, Vector3.up * 0.35f, dt * 1.6f);
                    item.Transform.position += item.Velocity * dt;
                    item.Transform.localScale = item.BaseScale * (1f + (Time.time - (item.Expiry - 1.3f)) * 0.5f);
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
