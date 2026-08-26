using System;
using System.Collections.Generic;
using UnityEngine;

namespace Satisfying.Game
{
    /// <summary>
    /// A loaded avatar: the hierarchy, the skinned renderers, and the bones by name.
    /// </summary>
    public sealed class GlbModel
    {
        public GameObject Root;
        public readonly List<SkinnedMeshRenderer> Skins = new List<SkinnedMeshRenderer>();
        public readonly Dictionary<string, Transform> Bones = new Dictionary<string, Transform>();

        /// <summary>
        /// The standard humanoid bones, when the file says which is which.
        ///
        /// VRM carries an explicit map from standard names - hips, chest, head, leftUpperArm - to node
        /// indices, which is enormously better than guessing from names: it is authored by whoever
        /// made the avatar and it is right by definition. Empty for a plain glTF, where guessing is
        /// all there is.
        /// </summary>
        public readonly Dictionary<string, Transform> Humanoid = new Dictionary<string, Transform>();

        /// <summary>Where the humanoid map came from, for the panel to say so.</summary>
        public string Flavour = "glTF";

        /// <summary>Bone lookup that ignores case and any prefix an exporter has bolted on.</summary>
        public Transform Find(string name)
        {
            Transform found;
            if (Bones.TryGetValue(name, out found)) return found;

            foreach (KeyValuePair<string, Transform> kv in Bones)
            {
                string key = kv.Key;
                if (key.Equals(name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
                if (key.EndsWith(":" + name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
                if (key.EndsWith("_" + name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            }
            return null;
        }
    }

    /// <summary>
    /// A runtime loader for binary glTF - and therefore for VRM, which is glTF with a humanoid bone
    /// table in it. It exists to get a character into the game without adding an asset pipeline to a
    /// project that deliberately has none.
    ///
    /// Unity has no glTF support of its own and the usual answer is a third-party package. That was
    /// not an option here - the whole premise of the repository is clone it and press play, with no
    /// binary assets and no packages beyond Unity's own modules - so this reads the format directly.
    /// It handles the subset an avatar actually uses:
    ///
    ///   - the GLB container: header, JSON chunk, BIN chunk
    ///   - accessors and buffer views, including byte strides and the normalised integer forms
    ///   - meshes with POSITION, NORMAL, TEXCOORD_0, JOINTS_0 and WEIGHTS_0
    ///   - nodes, their transforms, and skins with inverse bind matrices
    ///   - PBR materials, reduced to a base colour and a base colour texture
    ///   - PNG and JPEG images embedded in the BIN chunk
    ///
    /// It does not do animations, morph targets, sparse accessors, draco compression or external
    /// buffers. It says so rather than failing quietly, and it only REFUSES the three that change how
    /// the geometry is decoded - the rest describe things it is entitled to ignore.
    ///
    /// COORDINATE SYSTEMS. glTF is right handed with +Y up and +Z towards the viewer; Unity is left
    /// handed with +Z forward. The conversion is to negate Z on positions and normals and to negate
    /// the x and y of quaternions, which is applied once, here, on the way in. Get it wrong and the
    /// avatar is a mirror image - which looks almost right, and is the single easiest thing to ship
    /// by accident.
    /// </summary>
    public static class GlbLoader
    {
        const uint Magic = 0x46546C67;      // 'glTF'
        const uint ChunkJson = 0x4E4F534A;  // 'JSON'
        const uint ChunkBin = 0x004E4942;   // 'BIN'

        public static GlbModel Load(byte[] bytes, string name, Shader shader, out string error)
        {
            error = null;
            if (bytes == null || bytes.Length < 20) { error = "not enough bytes for a GLB header"; return null; }

            int at = 0;
            uint magic = ReadUInt(bytes, ref at);
            if (magic != Magic) { error = "not a GLB file (bad magic)"; return null; }

            ReadUInt(bytes, ref at);                    // version
            ReadUInt(bytes, ref at);                    // total length

            string json = null;
            byte[] bin = null;

            while (at + 8 <= bytes.Length)
            {
                uint chunkLength = ReadUInt(bytes, ref at);
                uint chunkType = ReadUInt(bytes, ref at);
                if (at + chunkLength > bytes.Length) break;

                if (chunkType == ChunkJson) json = System.Text.Encoding.UTF8.GetString(bytes, at, (int)chunkLength);
                else if (chunkType == ChunkBin)
                {
                    bin = new byte[chunkLength];
                    Buffer.BlockCopy(bytes, at, bin, 0, (int)chunkLength);
                }

                at += (int)chunkLength;
                at = (at + 3) & ~3;                     // chunks are four byte aligned
            }

            if (json == null) { error = "no JSON chunk"; return null; }

            object root;
            try { root = Json.Parse(json); }
            catch (Exception e) { error = "malformed glTF JSON: " + e.Message; return null; }

            // Only refuse the extensions that change how the GEOMETRY is decoded. Everything else -
            // VRM's humanoid map, lighting extensions, material variants - describes things this
            // loader is entitled to ignore, and refusing them all would have rejected every VRM file
            // on the grounds that it also happens to contain a bone table.
            object extensions = Json.Member(root, "extensionsRequired");
            for (int i = 0; i < Json.Count(extensions); i++)
            {
                string required = Json.String(Json.At(extensions, i));
                if (required == null) continue;
                if (required != "KHR_draco_mesh_compression" &&
                    required != "KHR_mesh_quantization" &&
                    required != "EXT_meshopt_compression") continue;

                error = "the mesh is compressed with " + required + ", which this loader cannot decode";
                return null;
            }

            try { return Build(root, bin, name, shader); }
            catch (Exception e) { error = "could not build the model: " + e.Message; return null; }
        }

        // ================================================================== building

        sealed class Context
        {
            public object Root;
            public byte[] Bin;
            public Shader Shader;
            public Material[] Materials;
            public Texture2D[] Textures;
            public Transform[] Nodes;
            public GlbModel Model;
        }

        static GlbModel Build(object root, byte[] bin, string name, Shader shader)
        {
            Context c = new Context();
            c.Root = root;
            c.Bin = bin;
            c.Shader = shader != null ? shader : Shader.Find("Standard");
            c.Model = new GlbModel();

            c.Model.Root = new GameObject(name);

            LoadTextures(c);
            LoadMaterials(c);
            BuildNodes(c);
            BuildScene(c);
            BuildMeshes(c);
            LoadHumanoid(c);

            return c.Model;
        }

        /// <summary>
        /// VRM's humanoid bone table, in either of the two shapes it has shipped in.
        ///
        /// VRM 0.x puts it under extensions.VRM.humanoid.humanBones as a LIST of {bone, node}. VRM 1.0
        /// moved it to extensions.VRMC_vrm.humanoid.humanBones as an OBJECT keyed by bone name whose
        /// values are {node}. Both are read, because both are in the wild and a file that loads its
        /// mesh and then cannot be posed is not much use.
        /// </summary>
        static void LoadHumanoid(Context c)
        {
            object extensions = Json.Member(c.Root, "extensions");
            if (extensions == null) return;

            // ---- VRM 1.0
            object modern = Json.Member(Json.Member(Json.Member(extensions, "VRMC_vrm"), "humanoid"), "humanBones");
            Dictionary<string, object> modernMap = Json.Object(modern);
            if (modernMap != null)
            {
                c.Model.Flavour = "VRM 1.0";
                foreach (KeyValuePair<string, object> kv in modernMap)
                {
                    int node = Json.MemberInt(kv.Value, "node", -1);
                    if (node >= 0 && node < c.Nodes.Length) c.Model.Humanoid[kv.Key] = c.Nodes[node];
                }
                return;
            }

            // ---- VRM 0.x
            object legacy = Json.Member(Json.Member(Json.Member(extensions, "VRM"), "humanoid"), "humanBones");
            int count = Json.Count(legacy);
            if (count == 0) return;

            c.Model.Flavour = "VRM 0.x";
            for (int i = 0; i < count; i++)
            {
                object entry = Json.At(legacy, i);
                string bone = Json.MemberString(entry, "bone", null);
                int node = Json.MemberInt(entry, "node", -1);
                if (bone == null || node < 0 || node >= c.Nodes.Length) continue;
                c.Model.Humanoid[bone] = c.Nodes[node];
            }
        }

        static void LoadTextures(Context c)
        {
            object images = Json.Member(c.Root, "images");
            int count = Json.Count(images);
            c.Textures = new Texture2D[count];

            for (int i = 0; i < count; i++)
            {
                object image = Json.At(images, i);
                if (!Json.Has(image, "bufferView")) continue;      // external URIs are not supported

                int viewIndex = Json.MemberInt(image, "bufferView", -1);
                byte[] data = ReadBufferView(c, viewIndex);
                if (data == null) continue;

                // Unity decodes PNG and JPEG itself, which is the only reason this is short.
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (!texture.LoadImage(data, false)) { UnityEngine.Object.Destroy(texture); continue; }
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.name = Json.MemberString(image, "name", "image " + i);
                c.Textures[i] = texture;
            }
        }

        static void LoadMaterials(Context c)
        {
            object materials = Json.Member(c.Root, "materials");
            int count = Json.Count(materials);
            c.Materials = new Material[Mathf.Max(1, count)];

            for (int i = 0; i < count; i++)
            {
                object source = Json.At(materials, i);
                Material m = new Material(c.Shader);
                m.name = Json.MemberString(source, "name", "material " + i);

                object pbr = Json.Member(source, "pbrMetallicRoughness");
                Color baseColour = Color.white;

                object factor = Json.Member(pbr, "baseColorFactor");
                if (Json.Count(factor) >= 4)
                {
                    baseColour = new Color(Json.Float(Json.At(factor, 0), 1f), Json.Float(Json.At(factor, 1), 1f),
                                           Json.Float(Json.At(factor, 2), 1f), Json.Float(Json.At(factor, 3), 1f));
                }

                if (m.HasProperty("_Color")) m.SetColor("_Color", baseColour);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseColour);

                // Avatars are mostly rough, unlit-looking surfaces; taking the glTF numbers straight
                // keeps skin from coming out like porcelain.
                float metallic = Json.MemberFloat(pbr, "metallicFactor", 0f);
                float roughness = Json.MemberFloat(pbr, "roughnessFactor", 0.85f);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 1f - roughness);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 1f - roughness);

                Texture2D albedo = TextureOf(c, Json.Member(pbr, "baseColorTexture"));
                if (albedo != null)
                {
                    if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", albedo);
                    if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", albedo);
                }

                // An avatar's eyelashes and hair cards are cut-outs; without this they are black boxes.
                string alphaMode = Json.MemberString(source, "alphaMode", "OPAQUE");
                if (alphaMode == "MASK" || alphaMode == "BLEND")
                {
                    m.EnableKeyword("_ALPHATEST_ON");
                    if (m.HasProperty("_Mode")) m.SetFloat("_Mode", 1f);
                    if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", Json.MemberFloat(source, "alphaCutoff", 0.4f));
                    m.renderQueue = 2450;
                }

                c.Materials[i] = m;
            }

            if (count == 0) c.Materials[0] = new Material(c.Shader);
        }

        static Texture2D TextureOf(Context c, object textureRef)
        {
            if (textureRef == null) return null;
            int index = Json.MemberInt(textureRef, "index", -1);
            object textures = Json.Member(c.Root, "textures");
            object texture = Json.At(textures, index);
            if (texture == null) return null;

            int source = Json.MemberInt(texture, "source", -1);
            return source >= 0 && source < c.Textures.Length ? c.Textures[source] : null;
        }

        static void BuildNodes(Context c)
        {
            object nodes = Json.Member(c.Root, "nodes");
            int count = Json.Count(nodes);
            c.Nodes = new Transform[count];

            for (int i = 0; i < count; i++)
            {
                object node = Json.At(nodes, i);
                string name = Json.MemberString(node, "name", "node " + i);

                GameObject go = new GameObject(name);
                Transform t = go.transform;
                c.Nodes[i] = t;

                object matrix = Json.Member(node, "matrix");
                if (Json.Count(matrix) == 16)
                {
                    // A baked matrix. Decompose it, converting handedness as we go.
                    Matrix4x4 m = Matrix4x4.identity;
                    for (int e = 0; e < 16; e++) m[e % 4, e / 4] = Json.Float(Json.At(matrix, e));
                    t.localPosition = ConvertPosition(m.GetColumn(3));
                    t.localRotation = ConvertRotation(RotationOf(m));
                    t.localScale = ScaleOf(m);
                }
                else
                {
                    object translation = Json.Member(node, "translation");
                    if (Json.Count(translation) == 3)
                        t.localPosition = ConvertPosition(new Vector3(Json.Float(Json.At(translation, 0)),
                                                                      Json.Float(Json.At(translation, 1)),
                                                                      Json.Float(Json.At(translation, 2))));

                    object rotation = Json.Member(node, "rotation");
                    if (Json.Count(rotation) == 4)
                        t.localRotation = ConvertRotation(new Quaternion(Json.Float(Json.At(rotation, 0)),
                                                                         Json.Float(Json.At(rotation, 1)),
                                                                         Json.Float(Json.At(rotation, 2)),
                                                                         Json.Float(Json.At(rotation, 3))));

                    object scale = Json.Member(node, "scale");
                    if (Json.Count(scale) == 3)
                        t.localScale = new Vector3(Json.Float(Json.At(scale, 0), 1f),
                                                   Json.Float(Json.At(scale, 1), 1f),
                                                   Json.Float(Json.At(scale, 2), 1f));
                }

                if (!c.Model.Bones.ContainsKey(name)) c.Model.Bones[name] = t;
            }

            // Parent them once every node exists, so a child that appears before its parent is fine.
            for (int i = 0; i < count; i++)
            {
                object children = Json.Member(Json.At(nodes, i), "children");
                for (int k = 0; k < Json.Count(children); k++)
                {
                    int child = Json.Int(Json.At(children, k), -1);
                    if (child >= 0 && child < count) c.Nodes[child].SetParent(c.Nodes[i], false);
                }
            }
        }

        static void BuildScene(Context c)
        {
            for (int i = 0; i < c.Nodes.Length; i++)
                if (c.Nodes[i].parent == null) c.Nodes[i].SetParent(c.Model.Root.transform, false);
        }

        static void BuildMeshes(Context c)
        {
            object nodes = Json.Member(c.Root, "nodes");
            object meshes = Json.Member(c.Root, "meshes");

            for (int i = 0; i < c.Nodes.Length; i++)
            {
                object node = Json.At(nodes, i);
                if (!Json.Has(node, "mesh")) continue;

                int meshIndex = Json.MemberInt(node, "mesh", -1);
                object mesh = Json.At(meshes, meshIndex);
                if (mesh == null) continue;

                int skinIndex = Json.Has(node, "skin") ? Json.MemberInt(node, "skin", -1) : -1;
                BuildPrimitives(c, c.Nodes[i], mesh, skinIndex);
            }
        }

        static void BuildPrimitives(Context c, Transform owner, object mesh, int skinIndex)
        {
            object primitives = Json.Member(mesh, "primitives");
            int count = Json.Count(primitives);

            for (int p = 0; p < count; p++)
            {
                object primitive = Json.At(primitives, p);
                object attributes = Json.Member(primitive, "attributes");

                Vector3[] positions = ReadVector3(c, Json.MemberInt(attributes, "POSITION", -1), true);
                if (positions == null || positions.Length == 0) continue;

                Mesh unityMesh = new Mesh();
                unityMesh.name = "primitive " + p;
                if (positions.Length > 65000) unityMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                unityMesh.vertices = positions;

                Vector3[] normals = ReadVector3(c, Json.MemberInt(attributes, "NORMAL", -1), true);
                if (normals != null && normals.Length == positions.Length) unityMesh.normals = normals;

                Vector2[] uv = ReadVector2(c, Json.MemberInt(attributes, "TEXCOORD_0", -1));
                if (uv != null && uv.Length == positions.Length) unityMesh.uv = uv;

                int[] indices = ReadIndices(c, Json.MemberInt(primitive, "indices", -1), positions.Length);
                // glTF is counter-clockwise and Unity is clockwise, and negating Z above flipped the
                // winding - so the triangles are reversed to put the faces back outwards.
                for (int k = 0; k + 2 < indices.Length; k += 3)
                {
                    int swap = indices[k + 1];
                    indices[k + 1] = indices[k + 2];
                    indices[k + 2] = swap;
                }
                unityMesh.triangles = indices;

                if (normals == null || normals.Length != positions.Length) unityMesh.RecalculateNormals();
                unityMesh.RecalculateBounds();

                Material material = c.Materials[Mathf.Clamp(Json.MemberInt(primitive, "material", 0),
                                                            0, c.Materials.Length - 1)];

                Transform holder = count == 1 ? owner : new GameObject("primitive " + p).transform;
                if (count > 1) holder.SetParent(owner, false);

                if (skinIndex >= 0 && ApplySkin(c, unityMesh, attributes, skinIndex, holder, material))
                    continue;

                MeshFilter filter = holder.gameObject.AddComponent<MeshFilter>();
                filter.sharedMesh = unityMesh;
                MeshRenderer renderer = holder.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
            }
        }

        static bool ApplySkin(Context c, Mesh mesh, object attributes, int skinIndex, Transform holder,
                              Material material)
        {
            object skins = Json.Member(c.Root, "skins");
            object skin = Json.At(skins, skinIndex);
            if (skin == null) return false;

            object joints = Json.Member(skin, "joints");
            int jointCount = Json.Count(joints);
            if (jointCount == 0) return false;

            Transform[] bones = new Transform[jointCount];
            for (int i = 0; i < jointCount; i++)
            {
                int node = Json.Int(Json.At(joints, i), -1);
                bones[i] = node >= 0 && node < c.Nodes.Length ? c.Nodes[node] : null;
            }

            Matrix4x4[] bindPoses = ReadMatrices(c, Json.MemberInt(skin, "inverseBindMatrices", -1), jointCount);

            BoneWeight[] weights = ReadBoneWeights(c, attributes, mesh.vertexCount);
            if (weights == null) return false;

            mesh.boneWeights = weights;
            mesh.bindposes = bindPoses;

            SkinnedMeshRenderer renderer = holder.gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.sharedMaterial = material;
            renderer.rootBone = bones[0];
            renderer.updateWhenOffscreen = true;
            renderer.localBounds = mesh.bounds;

            c.Model.Skins.Add(renderer);
            return true;
        }

        // ================================================================== accessors

        static byte[] ReadBufferView(Context c, int index)
        {
            if (index < 0 || c.Bin == null) return null;

            object view = Json.At(Json.Member(c.Root, "bufferViews"), index);
            if (view == null) return null;

            int offset = Json.MemberInt(view, "byteOffset", 0);
            int length = Json.MemberInt(view, "byteLength", 0);
            if (offset < 0 || length <= 0 || offset + length > c.Bin.Length) return null;

            byte[] data = new byte[length];
            Buffer.BlockCopy(c.Bin, offset, data, 0, length);
            return data;
        }

        /// <summary>
        /// Everything an accessor needs to be read: where its bytes are, how many, of what, and how far
        /// apart. The stride matters - an exporter is entitled to interleave attributes, and reading a
        /// tightly packed stream out of an interleaved buffer produces a cloud of confetti.
        /// </summary>
        struct Accessor
        {
            public byte[] Bytes;
            public int Offset;
            public int Stride;
            public int Count;
            public int ComponentType;
            public int Components;
            public bool Normalised;
        }

        static bool OpenAccessor(Context c, int index, out Accessor accessor)
        {
            accessor = new Accessor();
            if (index < 0 || c.Bin == null) return false;

            object source = Json.At(Json.Member(c.Root, "accessors"), index);
            if (source == null) return false;
            if (Json.Has(source, "sparse")) return false;      // not used by avatars

            int viewIndex = Json.MemberInt(source, "bufferView", -1);
            object view = Json.At(Json.Member(c.Root, "bufferViews"), viewIndex);
            if (view == null) return false;

            accessor.Bytes = c.Bin;
            accessor.Offset = Json.MemberInt(view, "byteOffset", 0) + Json.MemberInt(source, "byteOffset", 0);
            accessor.Count = Json.MemberInt(source, "count", 0);
            accessor.ComponentType = Json.MemberInt(source, "componentType", 5126);
            accessor.Normalised = Json.Member(source, "normalized") as bool? ?? false;
            accessor.Components = ComponentsOf(Json.MemberString(source, "type", "SCALAR"));

            int packed = accessor.Components * SizeOf(accessor.ComponentType);
            int stride = Json.MemberInt(view, "byteStride", 0);
            accessor.Stride = stride > 0 ? stride : packed;
            return accessor.Count > 0;
        }

        static int ComponentsOf(string type)
        {
            switch (type)
            {
                case "VEC2": return 2;
                case "VEC3": return 3;
                case "VEC4": return 4;
                case "MAT4": return 16;
                default: return 1;
            }
        }

        static int SizeOf(int componentType)
        {
            switch (componentType)
            {
                case 5120: return 1;    // byte
                case 5121: return 1;    // unsigned byte
                case 5122: return 2;    // short
                case 5123: return 2;    // unsigned short
                case 5125: return 4;    // unsigned int
                default: return 4;      // float
            }
        }

        static float Component(in Accessor a, int element, int component)
        {
            int at = a.Offset + element * a.Stride + component * SizeOf(a.ComponentType);
            switch (a.ComponentType)
            {
                case 5120:
                {
                    sbyte v = (sbyte)a.Bytes[at];
                    return a.Normalised ? Mathf.Max(v / 127f, -1f) : v;
                }
                case 5121:
                {
                    byte v = a.Bytes[at];
                    return a.Normalised ? v / 255f : v;
                }
                case 5122:
                {
                    short v = BitConverter.ToInt16(a.Bytes, at);
                    return a.Normalised ? Mathf.Max(v / 32767f, -1f) : v;
                }
                case 5123:
                {
                    ushort v = BitConverter.ToUInt16(a.Bytes, at);
                    return a.Normalised ? v / 65535f : v;
                }
                case 5125: return BitConverter.ToUInt32(a.Bytes, at);
                default: return BitConverter.ToSingle(a.Bytes, at);
            }
        }

        static Vector3[] ReadVector3(Context c, int index, bool convert)
        {
            Accessor a;
            if (!OpenAccessor(c, index, out a) || a.Components < 3) return null;

            Vector3[] result = new Vector3[a.Count];
            for (int i = 0; i < a.Count; i++)
            {
                Vector3 v = new Vector3(Component(a, i, 0), Component(a, i, 1), Component(a, i, 2));
                result[i] = convert ? ConvertPosition(v) : v;
            }
            return result;
        }

        static Vector2[] ReadVector2(Context c, int index)
        {
            Accessor a;
            if (!OpenAccessor(c, index, out a) || a.Components < 2) return null;

            Vector2[] result = new Vector2[a.Count];
            for (int i = 0; i < a.Count; i++)
            {
                // glTF texture space runs down from the top left; Unity runs up from the bottom left.
                result[i] = new Vector2(Component(a, i, 0), 1f - Component(a, i, 1));
            }
            return result;
        }

        static int[] ReadIndices(Context c, int index, int vertexCount)
        {
            Accessor a;
            if (!OpenAccessor(c, index, out a))
            {
                int[] sequential = new int[vertexCount];
                for (int i = 0; i < vertexCount; i++) sequential[i] = i;
                return sequential;
            }

            int[] result = new int[a.Count];
            for (int i = 0; i < a.Count; i++) result[i] = (int)Component(a, i, 0);
            return result;
        }

        static Matrix4x4[] ReadMatrices(Context c, int index, int expected)
        {
            Matrix4x4[] result = new Matrix4x4[expected];
            Accessor a;
            if (!OpenAccessor(c, index, out a) || a.Components != 16)
            {
                for (int i = 0; i < expected; i++) result[i] = Matrix4x4.identity;
                return result;
            }

            int count = Mathf.Min(expected, a.Count);
            for (int i = 0; i < count; i++)
            {
                Matrix4x4 m = new Matrix4x4();
                for (int e = 0; e < 16; e++) m[e % 4, e / 4] = Component(a, i, e);
                result[i] = ConvertMatrix(m);
            }
            for (int i = count; i < expected; i++) result[i] = Matrix4x4.identity;
            return result;
        }

        static BoneWeight[] ReadBoneWeights(Context c, object attributes, int vertexCount)
        {
            Accessor joints, weights;
            if (!OpenAccessor(c, Json.MemberInt(attributes, "JOINTS_0", -1), out joints)) return null;
            if (!OpenAccessor(c, Json.MemberInt(attributes, "WEIGHTS_0", -1), out weights)) return null;

            int count = Mathf.Min(vertexCount, Mathf.Min(joints.Count, weights.Count));
            BoneWeight[] result = new BoneWeight[vertexCount];

            for (int i = 0; i < count; i++)
            {
                BoneWeight w = new BoneWeight();
                w.boneIndex0 = (int)Component(joints, i, 0);
                w.boneIndex1 = (int)Component(joints, i, 1);
                w.boneIndex2 = (int)Component(joints, i, 2);
                w.boneIndex3 = (int)Component(joints, i, 3);
                w.weight0 = Component(weights, i, 0);
                w.weight1 = Component(weights, i, 1);
                w.weight2 = Component(weights, i, 2);
                w.weight3 = Component(weights, i, 3);

                // A vertex whose weights do not add up renders as a collapsed spike.
                float total = w.weight0 + w.weight1 + w.weight2 + w.weight3;
                if (total > 0.0001f)
                {
                    float inverse = 1f / total;
                    w.weight0 *= inverse; w.weight1 *= inverse; w.weight2 *= inverse; w.weight3 *= inverse;
                }
                else
                {
                    w.weight0 = 1f;
                }
                result[i] = w;
            }
            return result;
        }

        // ================================================================== handedness

        static Vector3 ConvertPosition(Vector3 v) { return new Vector3(v.x, v.y, -v.z); }

        static Quaternion ConvertRotation(Quaternion q) { return new Quaternion(-q.x, -q.y, q.z, q.w); }

        static Matrix4x4 ConvertMatrix(Matrix4x4 m)
        {
            // Flip the Z basis and the Z translation: the same change of handedness the positions get.
            Vector3 position = ConvertPosition(m.GetColumn(3));
            Quaternion rotation = ConvertRotation(RotationOf(m));
            return Matrix4x4.TRS(position, rotation, ScaleOf(m));
        }

        static Quaternion RotationOf(Matrix4x4 m)
        {
            Vector3 forward = new Vector3(m.m02, m.m12, m.m22);
            Vector3 up = new Vector3(m.m01, m.m11, m.m21);
            if (forward.sqrMagnitude < 1e-9f || up.sqrMagnitude < 1e-9f) return Quaternion.identity;
            return Quaternion.LookRotation(forward, up);
        }

        static Vector3 ScaleOf(Matrix4x4 m)
        {
            return new Vector3(new Vector3(m.m00, m.m10, m.m20).magnitude,
                               new Vector3(m.m01, m.m11, m.m21).magnitude,
                               new Vector3(m.m02, m.m12, m.m22).magnitude);
        }

        static uint ReadUInt(byte[] bytes, ref int at)
        {
            uint value = BitConverter.ToUInt32(bytes, at);
            at += 4;
            return value;
        }
    }
}
