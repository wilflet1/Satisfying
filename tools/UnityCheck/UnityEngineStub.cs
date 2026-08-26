// -----------------------------------------------------------------------------------------------
// COMPILE-CHECK STUB - not shipped, never seen by Unity (it lives outside Assets/).
//
// The container this project was written in has no Unity install, so this file declares the subset
// of the real UnityEngine API the game uses, with faithful signatures. tools/UnityCheck compiles the
// actual gameplay sources against it, which catches typos, wrong member names and signature drift
// between the game's own classes before the project is ever opened in the editor.
// -----------------------------------------------------------------------------------------------
using System;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero { get { return new Vector2(0f, 0f); } }
        public static Vector2 one { get { return new Vector2(1f, 1f); } }
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) { return b; }
        public float magnitude { get { return (float)Math.Sqrt(x * x + y * y); } }
        public float sqrMagnitude { get { return x * x + y * y; } }
        public Vector2 normalized { get { float m = magnitude; return m < 1e-5f ? zero : new Vector2(x / m, y / m); } }
        public static Vector2 operator +(Vector2 a, Vector2 b) { return new Vector2(a.x + b.x, a.y + b.y); }
        public static Vector2 operator -(Vector2 a, Vector2 b) { return new Vector2(a.x - b.x, a.y - b.y); }
        public static Vector2 operator *(Vector2 a, float s) { return new Vector2(a.x * s, a.y * s); }
        public override string ToString() { return "(" + x + ", " + y + ")"; }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vector3(float x, float y) { this.x = x; this.y = y; this.z = 0f; }

        public static Vector3 zero { get { return new Vector3(0f, 0f, 0f); } }
        public static Vector3 one { get { return new Vector3(1f, 1f, 1f); } }
        public static Vector3 up { get { return new Vector3(0f, 1f, 0f); } }
        public static Vector3 down { get { return new Vector3(0f, -1f, 0f); } }
        public static Vector3 left { get { return new Vector3(-1f, 0f, 0f); } }
        public static Vector3 right { get { return new Vector3(1f, 0f, 0f); } }
        public static Vector3 forward { get { return new Vector3(0f, 0f, 1f); } }
        public static Vector3 back { get { return new Vector3(0f, 0f, -1f); } }

        public float magnitude { get { return (float)Math.Sqrt(x * x + y * y + z * z); } }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public Vector3 normalized { get { float m = magnitude; return m < 1e-5f ? zero : new Vector3(x / m, y / m, z / m); } }

        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator -(Vector3 a) { return new Vector3(-a.x, -a.y, -a.z); }
        public static Vector3 operator *(Vector3 a, float s) { return new Vector3(a.x * s, a.y * s, a.z * s); }
        public static Vector3 operator *(float s, Vector3 a) { return new Vector3(a.x * s, a.y * s, a.z * s); }
        public static Vector3 operator /(Vector3 a, float s) { return new Vector3(a.x / s, a.y / s, a.z / s); }
        public static bool operator ==(Vector3 a, Vector3 b) { return a.x == b.x && a.y == b.y && a.z == b.z; }
        public static bool operator !=(Vector3 a, Vector3 b) { return !(a == b); }
        public override bool Equals(object o) { return o is Vector3 && (Vector3)o == this; }
        public override int GetHashCode() { return x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode(); }

        public static float Dot(Vector3 a, Vector3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
        public static Vector3 Cross(Vector3 a, Vector3 b) { return new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x); }
        public static float Distance(Vector3 a, Vector3 b) { return (a - b).magnitude; }
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { return a + (b - a) * Mathf.Clamp01(t); }
        public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t) { return a + (b - a) * t; }
        public static Vector3 MoveTowards(Vector3 a, Vector3 b, float d) { return b; }
        public static Vector3 ProjectOnPlane(Vector3 v, Vector3 n) { return v - n * Dot(v, n); }
        public static Vector3 Normalize(Vector3 v) { return v.normalized; }
        public void Normalize() { }
        public static Vector3 Slerp(Vector3 a, Vector3 b, float t) { return b; }
        public static Vector3 ClampMagnitude(Vector3 v, float max) { return v; }
        public static float Angle(Vector3 a, Vector3 b) { return 0f; }
        public override string ToString() { return "(" + x + ", " + y + ", " + z + ")"; }
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity { get { return new Quaternion(0f, 0f, 0f, 1f); } }
        public static Quaternion Euler(float x, float y, float z) { return identity; }
        public static Quaternion Euler(Vector3 e) { return identity; }
        public static Quaternion LookRotation(Vector3 forward) { return identity; }
        public static Quaternion LookRotation(Vector3 forward, Vector3 up) { return identity; }
        public static Quaternion AngleAxis(float angle, Vector3 axis) { return identity; }
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) { return a; }
        public Vector3 eulerAngles { get { return Vector3.zero; } }
        public static Quaternion operator *(Quaternion a, Quaternion b) { return identity; }
        public static Vector3 operator *(Quaternion q, Vector3 v) { return v; }
    }

    public struct Matrix4x4
    {
        public float m00, m01, m02, m03;
        public float m10, m11, m12, m13;
        public float m20, m21, m22, m23;
        public float m30, m31, m32, m33;

        public static Matrix4x4 identity { get { return new Matrix4x4(); } }
        public static Matrix4x4 TRS(Vector3 pos, Quaternion q, Vector3 scale) { return identity; }
        public Vector3 MultiplyPoint3x4(Vector3 point) { return point; }
        public Vector4 GetColumn(int index) { return new Vector4(); }
        public float this[int row, int column] { get { return 0f; } set { } }
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static implicit operator Vector3(Vector4 v) { return new Vector3(v.x, v.y, v.z); }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; this.a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white { get { return new Color(1f, 1f, 1f, 1f); } }
        public static Color black { get { return new Color(0f, 0f, 0f, 1f); } }
        public static Color clear { get { return new Color(0f, 0f, 0f, 0f); } }
        public static Color red { get { return new Color(1f, 0f, 0f, 1f); } }
        public static Color Lerp(Color a, Color b, float t) { return a; }
        public static Color operator *(Color c, float s) { return new Color(c.r * s, c.g * s, c.b * s, c.a * s); }
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public float xMin { get { return x; } }
        public float yMin { get { return y; } }
        public Vector2 center { get { return new Vector2(x + width * 0.5f, y + height * 0.5f); } }
    }

    public struct Bounds
    {
        public Vector3 center, size;
        public Bounds(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
        public Vector3 extents { get { return size * 0.5f; } }
        public void Encapsulate(Bounds other) { }
        public void Encapsulate(Vector3 point) { }
    }

    public static class Mathf
    {
        public const float PI = 3.14159265f;
        public const float Deg2Rad = PI / 180f;
        public const float Rad2Deg = 180f / PI;
        public const float Epsilon = 1e-5f;
        public const float Infinity = float.PositiveInfinity;
        public static float Abs(float v) { return Math.Abs(v); }
        public static int Abs(int v) { return Math.Abs(v); }
        public static float Sqrt(float v) { return (float)Math.Sqrt(v); }
        public static float Sin(float v) { return (float)Math.Sin(v); }
        public static float Cos(float v) { return (float)Math.Cos(v); }
        public static float Tan(float v) { return (float)Math.Tan(v); }
        public static float Acos(float v) { return (float)Math.Acos(v); }
        public static float Asin(float v) { return (float)Math.Asin(v); }
        public static float Atan2(float y, float x) { return (float)Math.Atan2(y, x); }
        public static float Exp(float v) { return (float)Math.Exp(v); }
        public static float Pow(float a, float b) { return (float)Math.Pow(a, b); }
        public static float Sign(float v) { return v < 0f ? -1f : 1f; }
        public static float Min(float a, float b) { return Math.Min(a, b); }
        public static int Min(int a, int b) { return Math.Min(a, b); }
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static int Max(int a, int b) { return Math.Max(a, b); }
        public static float Clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
        public static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
        public static float Clamp01(float v) { return Clamp(v, 0f, 1f); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
        public static float LerpUnclamped(float a, float b, float t) { return a + (b - a) * t; }
        public static float InverseLerp(float a, float b, float v) { return Clamp01((v - a) / (b - a)); }
        public static float MoveTowards(float a, float b, float d) { return b; }
        public static float Repeat(float t, float length) { return t - (float)Math.Floor(t / length) * length; }
        public static float DeltaAngle(float a, float b) { return b - a; }
        public static float SmoothStep(float a, float b, float t) { return a; }
        public static int FloorToInt(float v) { return (int)Math.Floor(v); }
        public static int CeilToInt(float v) { return (int)Math.Ceiling(v); }
        public static int RoundToInt(float v) { return (int)Math.Round(v); }
        public static bool Approximately(float a, float b) { return Math.Abs(a - b) < 1e-5f; }
    }

    public static class Random
    {
        public static float Range(float min, float max) { return min; }
        public static int Range(int min, int max) { return min; }
        public static float value { get { return 0.5f; } }
        public static Quaternion rotation { get { return Quaternion.identity; } }
        public static Vector3 insideUnitSphere { get { return Vector3.zero; } }
        public static Vector3 onUnitSphere { get { return Vector3.up; } }
    }

    public enum HideFlags { None = 0, HideAndDontSave = 61 }
    public enum FindObjectsSortMode { None, InstanceID }
    public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }
    public enum CursorLockMode { None, Locked, Confined }
    public enum LightType { Spot, Directional, Point, Area }
    public enum LightShadows { None, Hard, Soft }
    public enum FogMode { Linear = 1, Exponential = 2, ExponentialSquared = 3 }
    public enum CameraClearFlags { Skybox = 1, Color = 2, SolidColor = 2, Depth = 3, Nothing = 4 }
    public enum AudioRolloffMode { Logarithmic, Linear, Custom }
    public enum QueryTriggerInteraction { UseGlobal, Ignore, Collide }
    public enum TextureFormat { RGB24 = 3, RGBA32 = 4, ARGB32 = 5 }
    public enum RenderTextureFormat { ARGB32 = 0, Depth = 1, RFloat = 14 }
    public enum RenderTextureReadWrite { Default, Linear, sRGB }
    public enum TextureWrapMode { Repeat, Clamp, Mirror }
    public enum FilterMode { Point, Bilinear, Trilinear }
    public enum ScaleMode { StretchToFill, ScaleAndCrop, ScaleToFit }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, BeforeSplashScreen, SubsystemRegistration, AfterAssembliesLoaded }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeFieldAttribute : Attribute { }

    public class Object
    {
        public string name { get; set; }
        public HideFlags hideFlags { get; set; }
        public static void Destroy(Object target) { }
        public static void Destroy(Object target, float delay) { }
        public static void DestroyImmediate(Object target) { }
        public static void DontDestroyOnLoad(Object target) { }
        public static T FindObjectOfType<T>() where T : Object { return null; }
        public static T FindFirstObjectByType<T>() where T : Object { return null; }
        public static T[] FindObjectsOfType<T>() where T : Object { return new T[0]; }
        public static T[] FindObjectsByType<T>(FindObjectsSortMode sort) where T : Object { return new T[0]; }
        public static bool operator ==(Object a, Object b) { return ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) { return !ReferenceEquals(a, b); }
        public override bool Equals(object o) { return ReferenceEquals(this, o); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static implicit operator bool(Object o) { return !ReferenceEquals(o, null); }
    }

    public class Component : Object
    {
        public Transform transform { get; set; }
        public GameObject gameObject { get; set; }
        public T GetComponent<T>() where T : Component { return null; }
        public T[] GetComponentsInChildren<T>() where T : Component { return new T[0]; }
        public T GetComponentInChildren<T>() where T : Component { return null; }
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
    }

    public class MonoBehaviour : Behaviour { }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponentAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ExecuteAlwaysAttribute : Attribute { }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; }
        public Vector3 localPosition { get; set; }
        public Quaternion localRotation { get; set; }
        public Vector3 localScale { get; set; }
        public Vector3 forward { get; set; }
        public Vector3 right { get; set; }
        public Vector3 up { get; set; }
        public Transform parent { get; set; }
        public int childCount { get { return 0; } }
        public Transform GetChild(int index) { return null; }
        public Transform Find(string name) { return null; }
        // Transform's enumerator walks its children. It is an old non-generic IEnumerator in the real
        // engine, which is why foreach over one needs a cast; the stub matches so the same code compiles.
        public System.Collections.IEnumerator GetEnumerator() { return new Transform[0].GetEnumerator(); }
        public void SetParent(Transform parent) { }
        public void SetParent(Transform parent, bool worldPositionStays) { }
        public bool IsChildOf(Transform parent) { return false; }
        public Vector3 TransformPoint(Vector3 local) { return local; }
        public Vector3 InverseTransformPoint(Vector3 world) { return world; }
        public Vector3 TransformDirection(Vector3 local) { return local; }
        public Matrix4x4 localToWorldMatrix { get { return Matrix4x4.identity; } }
    }

    public class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { this.name = name; }
        public int layer { get; set; }
        public string tag { get; set; }
        public Transform transform { get; set; }
        public bool activeSelf { get { return true; } }
        public bool activeInHierarchy { get { return true; } }
        public void SetActive(bool active) { }
        public T AddComponent<T>() where T : Component, new() { return new T(); }
        public T GetComponent<T>() where T : Component { return null; }
        public T[] GetComponentsInChildren<T>() where T : Component { return new T[0]; }
        public static GameObject CreatePrimitive(PrimitiveType type) { return new GameObject(); }
        public static GameObject Find(string name) { return null; }
    }

    public class Texture : Object { }

    public class Texture2D : Texture
    {
        public Texture2D(int width, int height) { }
        public Texture2D(int width, int height, TextureFormat format, bool mipChain) { }
        public Texture2D(int width, int height, TextureFormat format, bool mipChain, bool linear) { }
        public TextureWrapMode wrapMode { get; set; }
        public FilterMode filterMode { get; set; }
        public void SetPixel(int x, int y, Color color) { }
        public Color GetPixel(int x, int y) { return Color.black; }
        public Color GetPixelBilinear(float u, float v) { return Color.black; }
        public void ReadPixels(Rect source, int destX, int destY) { }
        public void SetPixels(Color[] colors) { }
        public Color[] GetPixels() { return new Color[0]; }
        public void Apply() { }
        public int width { get { return 0; } }
        public int height { get { return 0; } }
        public byte[] EncodeToPNG() { return new byte[0]; }
        public bool LoadImage(byte[] data, bool markNonReadable) { return true; }
    }

    /// <summary>
    /// An off-screen target. The shot sheet renders the duellist and the sight picture into one of
    /// these rather than the screen, which is the only way a headless editor run can take a photograph.
    /// </summary>
    public class RenderTexture : Texture
    {
        public RenderTexture(int width, int height, int depth) { }
        public RenderTexture(int width, int height, int depth, RenderTextureFormat format) { }
        public RenderTexture(int width, int height, int depth, RenderTextureFormat format, RenderTextureReadWrite readWrite) { }
        public int antiAliasing { get; set; }
        public FilterMode filterMode { get; set; }
        public int width { get { return 0; } }
        public int height { get { return 0; } }
        public RenderTextureFormat format { get { return RenderTextureFormat.ARGB32; } }
        public void Release() { }
        public bool Create() { return true; }
        public static RenderTexture active { get; set; }
        public static RenderTexture GetTemporary(int width, int height, int depth, RenderTextureFormat format) { return null; }
        public static void ReleaseTemporary(RenderTexture rt) { }
    }

    public static class GL
    {
        public static void PushMatrix() { }
        public static void PopMatrix() { }
        public static void LoadPixelMatrix(float left, float right, float bottom, float top) { }
    }

    public static class Graphics
    {
        public static void Blit(Texture source, RenderTexture destination) { }
        public static void Blit(Texture source, RenderTexture destination, Material material) { }
        public static void DrawTexture(Rect screenRect, Texture texture, Rect sourceRect, int leftBorder,
                                       int rightBorder, int topBorder, int bottomBorder, Color color) { }
    }

    public class Shader : Object
    {
        public static Shader Find(string name) { return new Shader(); }
    }

    public class Material : Object
    {
        public Material(Shader shader) { }
        public void SetTexture(string name, Texture value) { }
        public Texture GetTexture(string name) { return null; }
        public bool HasProperty(string name) { return true; }
        public void SetColor(string name, Color value) { }
        public void SetFloat(string name, float value) { }
        public void EnableKeyword(string keyword) { }
        public void DisableKeyword(string keyword) { }
        public int renderQueue { get; set; }
        public float GetFloat(string name) { return 0f; }
        public Color color { get; set; }
    }

    public class Mesh : Object
    {
        public Mesh() { }
        public Vector3[] vertices { get; set; }
        public Vector3[] normals { get; set; }
        public Vector2[] uv { get; set; }
        public int[] triangles { get; set; }
        public void Clear() { }
        public void RecalculateNormals() { }
        public Rendering.IndexFormat indexFormat { get; set; }
        public BoneWeight[] boneWeights { get; set; }
        public Matrix4x4[] bindposes { get; set; }
        public int vertexCount { get { return 0; } }
        public Bounds bounds { get; set; }
        public void RecalculateBounds() { }
    }
    public class MeshFilter : Component { public Mesh mesh { get; set; } public Mesh sharedMesh { get; set; } }

    public class Renderer : Component
    {
        public Material material { get; set; }
        public Material sharedMaterial { get; set; }
        public Bounds bounds { get { return new Bounds(Vector3.zero, Vector3.zero); } }
        public Rendering.ShadowCastingMode shadowCastingMode { get; set; }
        public bool receiveShadows { get; set; }
        public bool enabled { get; set; }
    }

    public struct BoneWeight
    {
        public int boneIndex0, boneIndex1, boneIndex2, boneIndex3;
        public float weight0, weight1, weight2, weight3;
    }

    public class SkinnedMeshRenderer : Renderer
    {
        public Mesh sharedMesh { get; set; }
        public Transform[] bones { get; set; }
        public Transform rootBone { get; set; }
        public bool updateWhenOffscreen { get; set; }
        public Bounds localBounds { get; set; }
    }

    public class MeshRenderer : Renderer { }

    public class Collider : Component
    {
        public bool isTrigger { get; set; }
        public bool enabled { get; set; }
    }

    public class CapsuleCollider : Collider
    {
        public float height { get; set; }
        public float radius { get; set; }
        public Vector3 center { get; set; }
        public int direction { get; set; }
    }

    public class BoxCollider : Collider { public Vector3 size { get; set; } public Vector3 center { get; set; } }

    public struct RaycastHit
    {
        public float distance;
        public Vector3 normal;
        public Vector3 point;
        public Collider collider;
        public Transform transform;
    }

    public static class Physics
    {
        public static bool autoSimulation { get; set; }
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float maxDistance, int layerMask, QueryTriggerInteraction q) { hit = new RaycastHit(); return false; }
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float maxDistance, int layerMask) { hit = new RaycastHit(); return false; }
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float maxDistance) { hit = new RaycastHit(); return false; }
        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, int layerMask, QueryTriggerInteraction q) { return false; }
        public static void SyncTransforms() { }
        public static bool CapsuleCast(Vector3 p1, Vector3 p2, float radius, Vector3 direction, out RaycastHit hit, float maxDistance, int layerMask, QueryTriggerInteraction q) { hit = new RaycastHit(); return false; }
        public static bool SphereCast(Vector3 origin, float radius, Vector3 direction, out RaycastHit hit, float maxDistance, int layerMask, QueryTriggerInteraction q) { hit = new RaycastHit(); return false; }
        public static bool CheckCapsule(Vector3 start, Vector3 end, float radius, int layerMask, QueryTriggerInteraction q) { return false; }
        public static bool CheckSphere(Vector3 position, float radius, int layerMask, QueryTriggerInteraction q) { return false; }
        public static int OverlapCapsuleNonAlloc(Vector3 p0, Vector3 p1, float radius, Collider[] results, int layerMask, QueryTriggerInteraction q) { return 0; }
        public static bool ComputePenetration(Collider a, Vector3 positionA, Quaternion rotationA, Collider b, Vector3 positionB, Quaternion rotationB, out Vector3 direction, out float distance) { direction = Vector3.up; distance = 0f; return false; }
        public static void IgnoreLayerCollision(int layer1, int layer2, bool ignore) { }
        public static bool Linecast(Vector3 start, Vector3 end, int layerMask, QueryTriggerInteraction q) { return false; }
    }

    public class Camera : Behaviour
    {
        public float fieldOfView { get; set; }
        public float nearClipPlane { get; set; }
        public float farClipPlane { get; set; }
        public int cullingMask { get; set; }
        public float depth { get; set; }
        public CameraClearFlags clearFlags { get; set; }
        public Color backgroundColor { get; set; }
        public static Camera main { get { return null; } }
        public Vector3 WorldToScreenPoint(Vector3 position) { return Vector3.zero; }
        public Vector3 ScreenToWorldPoint(Vector3 position) { return Vector3.zero; }
        public RenderTexture targetTexture { get; set; }
        public void Render() { }
    }

    public class Light : Behaviour
    {
        public LightType type { get; set; }
        public Color color { get; set; }
        public float intensity { get; set; }
        public float range { get; set; }
        public LightShadows shadows { get; set; }
        public float shadowStrength { get; set; }
    }

    public class AudioClip : Object
    {
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream) { return new AudioClip(); }
        public bool SetData(float[] data, int offsetSamples) { return true; }
    }

    public class AudioSource : Behaviour
    {
        public AudioClip clip { get; set; }
        public bool playOnAwake { get; set; }
        public bool loop { get; set; }
        public float volume { get; set; }
        public float pitch { get; set; }
        public float spatialBlend { get; set; }
        public float minDistance { get; set; }
        public float maxDistance { get; set; }
        public float dopplerLevel { get; set; }
        public AudioRolloffMode rolloffMode { get; set; }
        public void Play() { }
        public void PlayOneShot(AudioClip clip, float volumeScale) { }
    }

    public class AudioListener : Behaviour { }

    public class AudioLowPassFilter : Behaviour
    {
        public float cutoffFrequency { get; set; }
        public float lowpassResonanceQ { get; set; }
    }

    public static class Input
    {
        public static bool GetKey(KeyCode key) { return false; }
        public static bool GetKeyDown(KeyCode key) { return false; }
        public static bool GetKeyUp(KeyCode key) { return false; }
        public static bool GetMouseButton(int button) { return false; }
        public static float GetAxisRaw(string name) { return 0f; }
        public static float GetAxis(string name) { return 0f; }
        public static Vector2 mouseScrollDelta { get { return Vector2.zero; } }
        public static Vector3 mousePosition { get { return Vector3.zero; } }
        public static int touchCount { get { return 0; } }
        public static Touch GetTouch(int index) { return default(Touch); }
    }

    public static class Screen
    {
        public static int width { get { return 1920; } }
        public static int height { get { return 1080; } }
        public static float dpi { get { return 96f; } }
        public static ScreenOrientation orientation { get; set; }
        public static bool autorotateToLandscapeLeft { get; set; }
        public static bool autorotateToLandscapeRight { get; set; }
        public static bool autorotateToPortrait { get; set; }
        public static bool autorotateToPortraitUpsideDown { get; set; }
        public static void SetResolution(int w, int h, bool fullscreen) { }
        public static SleepTimeoutValue sleepTimeout { get; set; }
    }

    public enum ScreenOrientation { Portrait = 1, PortraitUpsideDown = 2, LandscapeLeft = 3, LandscapeRight = 4, AutoRotation = 5, Landscape = 3 }

    public struct SleepTimeoutValue
    {
        public static int NeverSleep { get { return -1; } }
        public static implicit operator SleepTimeoutValue(int v) { return default(SleepTimeoutValue); }
    }

    public static class SleepTimeout
    {
        public static int NeverSleep { get { return -1; } }
        public static int SystemSetting { get { return -2; } }
    }

    public static class Time
    {
        public static float deltaTime { get { return 0.016f; } }
        public static float time { get { return 0f; } }
        public static float realtimeSinceStartup { get { return 0f; } }
        public static double realtimeSinceStartupAsDouble { get { return 0.0; } }
        public static float fixedDeltaTime { get; set; }
        public static float timeScale { get; set; }
    }

    public static class Application
    {
        public static int targetFrameRate { get; set; }
        public static bool runInBackground { get; set; }
        public static string persistentDataPath { get { return "/tmp"; } }
        public static void Quit() { }
        public static void Quit(int exitCode) { }
        public static bool isFocused { get { return true; } }
        public static bool isPlaying { get { return true; } }
        public static bool isMobilePlatform { get { return false; } }
        public static RuntimePlatform platform { get { return RuntimePlatform.WindowsPlayer; } }
    }

    public enum RuntimePlatform { WindowsPlayer, WindowsEditor, OSXPlayer, OSXEditor, LinuxPlayer, LinuxEditor, Android, IPhonePlayer }

    public static class QualitySettings { public static int vSyncCount { get; set; } }

    public static class PlayerPrefs
    {
        public static void SetString(string key, string value) { }
        public static string GetString(string key, string defaultValue) { return defaultValue; }
        public static void SetFloat(string key, float value) { }
        public static float GetFloat(string key, float defaultValue) { return defaultValue; }
        public static void Save() { }
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }

    public static class Cursor
    {
        public static CursorLockMode lockState { get; set; }
        public static bool visible { get; set; }
    }

    public static class RenderSettings
    {
        public static Rendering.AmbientMode ambientMode { get; set; }
        public static Color ambientSkyColor { get; set; }
        public static Color ambientEquatorColor { get; set; }
        public static Color ambientGroundColor { get; set; }
        public static Color ambientLight { get; set; }
        public static bool fog { get; set; }
        public static FogMode fogMode { get; set; }
        public static Color fogColor { get; set; }
        public static float fogStartDistance { get; set; }
        public static float fogEndDistance { get; set; }
    }

    public class RectOffset
    {
        public RectOffset() { }
        public RectOffset(int left, int right, int top, int bottom) { }
        public int left { get; set; }
        public int right { get; set; }
        public int top { get; set; }
        public int bottom { get; set; }
    }

    public class GUIStyleState
    {
        public Texture2D background { get; set; }
        public Color textColor { get; set; }
    }

    public class GUIStyle
    {
        public Vector2 CalcSize(GUIContent content) { return Vector2.zero; }
        public GUIStyle() { }
        public GUIStyle(GUIStyle other) { }
        public GUIStyleState normal { get; set; }
        public GUIStyleState hover { get; set; }
        public GUIStyleState active { get; set; }
        public GUIStyleState focused { get; set; }
        public GUIStyleState onNormal { get; set; }
        public int fontSize { get; set; }
        public FontStyle fontStyle { get; set; }
        public TextAnchor alignment { get; set; }
        public bool wordWrap { get; set; }
        public RectOffset padding { get; set; }
        public RectOffset margin { get; set; }
        public RectOffset border { get; set; }
        public float fixedWidth { get; set; }
        public float fixedHeight { get; set; }
    }

    public class GUISkin : Object
    {
        public GUIStyle label { get; set; }
        public GUIStyle button { get; set; }
    }

    public class GUIContent
    {
        public GUIContent() { }
        public GUIContent(string text) { }
    }

    public class GUILayoutOption { }

    public enum TouchPhase { Began, Moved, Stationary, Ended, Canceled }

    public struct Touch
    {
        public int fingerId;
        public Vector2 position;
        public Vector2 deltaPosition;
        public TouchPhase phase;
    }

    public static class GUIUtility
    {
        public static string systemCopyBuffer { get; set; }
    }

    public static class GUI
    {
        public static Color color { get; set; }
        public static Matrix4x4 matrix { get; set; }
        public static GUISkin skin { get; set; }
        public static int depth { get; set; }
        public static void Label(Rect rect, string text) { }
        public static void Label(Rect rect, string text, GUIStyle style) { }
        public static void DrawTexture(Rect rect, Texture texture) { }
        public static void DrawTexture(Rect rect, Texture texture, ScaleMode mode, bool alphaBlend) { }
        public static bool Button(Rect rect, string text, GUIStyle style) { return false; }
        public static void Box(Rect rect, string text, GUIStyle style) { }
    }

    public static class GUILayout
    {
        public static GUILayoutOption Width(float value) { return new GUILayoutOption(); }
        public static GUILayoutOption Height(float value) { return new GUILayoutOption(); }
        public static GUILayoutOption ExpandWidth(bool expand) { return new GUILayoutOption(); }
        public static void BeginArea(Rect rect) { }
        public static void BeginArea(Rect rect, GUIStyle style) { }
        public static void EndArea() { }
        public static void BeginHorizontal(params GUILayoutOption[] options) { }
        public static void BeginHorizontal(GUIStyle style, params GUILayoutOption[] options) { }
        public static void EndHorizontal() { }
        public static void BeginVertical(params GUILayoutOption[] options) { }
        public static void BeginVertical(GUIStyle style, params GUILayoutOption[] options) { }
        public static void EndVertical() { }
        public static Vector2 BeginScrollView(Vector2 scroll, params GUILayoutOption[] options) { return scroll; }
        public static void EndScrollView() { }
        public static void Label(string text, params GUILayoutOption[] options) { }
        public static void Label(string text, GUIStyle style, params GUILayoutOption[] options) { }
        public static void Box(string text, GUIStyle style, params GUILayoutOption[] options) { }
        public static bool Button(string text, params GUILayoutOption[] options) { return false; }
        public static bool Button(string text, GUIStyle style, params GUILayoutOption[] options) { return false; }
        public static string TextField(string text, params GUILayoutOption[] options) { return text; }
        public static string TextField(string text, int maxLength, GUIStyle style, params GUILayoutOption[] options) { return text; }
        public static float HorizontalSlider(float value, float min, float max, params GUILayoutOption[] options) { return value; }
        public static float HorizontalSlider(float value, float min, float max, GUIStyle slider, GUIStyle thumb, params GUILayoutOption[] options) { return value; }
        public static bool Toggle(bool value, string text, params GUILayoutOption[] options) { return value; }
        public static void Space(float pixels) { }
        public static void FlexibleSpace() { }
    }

    public enum EventType { MouseDown, MouseUp, MouseMove, KeyDown, KeyUp, ScrollWheel, Repaint, Layout, Ignore, Used }

    public class Event
    {
        public static Event current { get; set; }
        public EventType type { get; set; }
        public KeyCode keyCode { get; set; }
        public int button { get; set; }
        public bool isKey { get; set; }
        public bool shift { get; set; }
        public bool alt { get; set; }
        public bool control { get; set; }
        public Vector2 mousePosition { get; set; }
        public void Use() { }
    }

    public enum KeyCode
    {
        None = 0, Backspace = 8, Tab = 9, Return = 13, Escape = 27, Space = 32,
        Delete = 127,
        Alpha0 = 48, Alpha1 = 49, Alpha2 = 50, Alpha3 = 51, Alpha4 = 52, Alpha5 = 53,
        Alpha6 = 54, Alpha7 = 55, Alpha8 = 56, Alpha9 = 57,
        A = 97, B = 98, C = 99, D = 100, E = 101, F = 102, G = 103, H = 104, I = 105,
        J = 106, K = 107, L = 108, M = 109, N = 110, O = 111, P = 112, Q = 113, R = 114,
        S = 115, T = 116, U = 117, V = 118, W = 119, X = 120, Y = 121, Z = 122,
        UpArrow = 273, DownArrow = 274, RightArrow = 275, LeftArrow = 276,
        Insert = 277, Home = 278, End = 279, PageUp = 280, PageDown = 281,
        F1 = 282, F2 = 283, F3 = 284, F4 = 285, F5 = 286, F6 = 287, F7 = 288,
        F8 = 289, F9 = 290, F10 = 291, F11 = 292, F12 = 293,
        RightShift = 303, LeftShift = 304, RightControl = 305, LeftControl = 306,
        RightAlt = 307, LeftAlt = 308,
        Mouse0 = 323, Mouse1 = 324, Mouse2 = 325, Mouse3 = 326, Mouse4 = 327, Mouse5 = 328, Mouse6 = 329,
        JoystickButton0 = 330, JoystickButton1 = 331
    }
}

namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt16, UInt32 }

    public enum BlendMode { Zero, One, SrcAlpha = 5, OneMinusSrcAlpha = 10 }
    public enum ShadowCastingMode { Off, On, TwoSided, ShadowsOnly }
    public enum AmbientMode { Skybox = 0, Trilight = 1, Flat = 3, Custom = 4 }
}
