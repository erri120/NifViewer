using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Triangle = Silk.NET.Maths.Vector3D<ushort>;
using Edge = Silk.NET.Maths.Vector2D<float>;

namespace NifViewer.Lib
{

    [PublicAPI]
    public sealed class Mesh : IDisposable
    {
        private readonly GL _api;
        
        public Mesh(GL api)
        {
            _api = api;
        }
        
        private const bool SmoothSeamNormals = true;
        private static readonly float SmoothThreshold = Scalar.DegreesToRadians(60.0f);
        
        public Matrix4X4<float> MatModel = Matrix4X4<float>.Identity;
        
        public Vector3D<float>[] Vertices = Array.Empty<Vector3D<float>>();
        public Vector3D<float>[] Normals = Array.Empty<Vector3D<float>>();
        public Vector3D<float>[] Tangents = Array.Empty<Vector3D<float>>();
        public Vector3D<float>[] BiTangents = Array.Empty<Vector3D<float>>();
        public Vector3D<float>[] VertexColors = Array.Empty<Vector3D<float>>();
        public float[] VertexAlphas = Array.Empty<float>();
        public Vector2D<float>[] TextureCoordinates = Array.Empty<Vector2D<float>>();
        public Triangle[] Triangles = Array.Empty<Triangle>();
        public Triangle[] RenderTriangles = Array.Empty<Triangle>();
        public Edge[] Edges = Array.Empty<Edge>();

        public List<int>[] VertTris = Array.Empty<List<int>>();
        public List<int>[] VertEdges = Array.Empty<List<int>>();
        
        //Vertices that are duplicated for UVs but are in the same position.
        public Dictionary<int, List<int>> WeldedVertices = new();

        //Whether WeldedVerts has been calculated yet.
        private bool _gotWeldedVerts = false;

        //Whether the Buffers have been generated yet.
        private bool _genBuffers = false;

        private bool[] _queueUpdate = new bool[8];

        private enum UpdateType : byte
        {
            Position = 0,
            Normals = 1,
            Tangents = 2,
            BiTangents = 3,
            VertexColors = 4,
            VertexAlpha = 5,
            TextureCoordinates = 6,
            Indices = 7
        }
        
        #region Properties

        public string ShapeName { get; set; } = string.Empty;

        public int NumVertices { get; set; } = 0;
        public int NumTriangles { get; set; } = 0;

        public ShaderProperties ShaderProperties { get; set; } = new();
        
        public GLEnum CullMode { get; set; } = GLEnum.Back;

        public bool IsDoubleSided { get; set; }
        public bool IsModelSpace { get; set; }
        public bool IsEmissive { get; set; }
        public bool IsTextured { get; set; }
        public bool HasSpecular { get; set; }
        public bool HasVertexColors { get; set; }
        public bool HasVertexAlpha { get; set; }
        public bool HasGlowMap { get; set; }
        public bool HasGreyscaleColor { get; set; }
        public bool HasCubeMap { get; set; }
        public bool HasBacklight { get; set; }
        public bool HasBacklightMap { get; set; }
        public bool HasRimLight { get; set; }
        public bool HasSoftLight { get; set; }

        public ushort AlphaFlags { get; set; } = 0;
        public byte AlphaThreshold { get; set; } = 0;
        
        #endregion

        #region Functions

        private uint vao;
        private uint[] vbo = new uint[7];
        private uint ibo;
        
        public unsafe void CreateBuffers()
        {
            if (!_genBuffers)
            {
                vao = _api.GenVertexArray();
                _api.GenBuffers((uint) vbo.Length, vbo);
                _api.GenBuffers(1, out ibo);
            }
            
            _api.BindVertexArray(vao);

            if (Vertices.Any())
            {
                _api.BindBuffer(GLEnum.ArrayBuffer, vbo[0]);
                fixed (void* v = &Vertices[0])
                {
                    _api.BufferData(GLEnum.ArrayBuffer, (nuint) (NumVertices * sizeof(Vector3D<float>)), v, GLEnum.DynamicDraw);
                }
            }

            if (Normals.Any())
            {
                _api.BindBuffer(GLEnum.ArrayBuffer, vbo[1]);
                fixed (void* v = &Normals[0])
                {
                    _api.BufferData(GLEnum.ArrayBuffer, (nuint) (NumVertices * sizeof(Vector3D<float>)), v, GLEnum.DynamicDraw);
                }
            }

            if (Tangents.Any())
            {
                _api.BindBuffer(GLEnum.ArrayBuffer, vbo[2]);
                fixed (void* v = &Tangents[0])
                {
                    _api.BufferData(GLEnum.ArrayBuffer, (nuint) (NumVertices * sizeof(Vector3D<float>)), v, GLEnum.DynamicDraw);
                }
            }

            if (BiTangents.Any())
            {
                _api.BindBuffer(GLEnum.ArrayBuffer, vbo[3]);
                fixed (void* v = &BiTangents[0])
                {
                    _api.BufferData(GLEnum.ArrayBuffer, (nuint) (NumVertices * sizeof(Vector3D<float>)), v, GLEnum.DynamicDraw);
                }
            }

            if (VertexColors.Any())
            {
                _api.BindBuffer(GLEnum.ArrayBuffer, vbo[4]);
                fixed (void* v = &VertexColors[0])
                {
                    _api.BufferData(GLEnum.ArrayBuffer, (nuint) (NumVertices * sizeof(Vector3D<float>)), v, GLEnum.DynamicDraw);
                }
            }

            if (VertexAlphas.Any())
            {
                _api.BindBuffer(GLEnum.ArrayBuffer, vbo[5]);
                fixed (void* v = &VertexAlphas[0])
                {
                    _api.BufferData(GLEnum.ArrayBuffer, (nuint) (NumVertices * sizeof(float)), v, GLEnum.DynamicDraw);
                }
            }

            if (TextureCoordinates.Any())
            {
                _api.BindBuffer(GLEnum.ArrayBuffer, vbo[6]);
                fixed (void* v = &TextureCoordinates[0])
                {
                    _api.BufferData(GLEnum.ArrayBuffer, (nuint) (NumVertices * sizeof(Vector2D<float>)), v, GLEnum.DynamicDraw);
                }
            }
            
            _api.BindBuffer(GLEnum.ArrayBuffer, 0);

            if (Vertices.Any() && Triangles.Any())
            {
                Array.Resize(ref RenderTriangles, NumTriangles);

                for (var i = 0; i < NumTriangles; i++)
                    RenderTriangles[i] = Triangles[i];
                
                _api.BindBuffer(GLEnum.ElementArrayBuffer, ibo);
                fixed (void* v = &RenderTriangles[0])
                {
                    _api.BufferData(GLEnum.ElementArrayBuffer, (nuint) (NumTriangles * sizeof(Triangle)), v, GLEnum.DynamicDraw);
                }
                _api.BindBuffer(GLEnum.ElementArrayBuffer, 0);
            } else if (Vertices.Any() && Edges.Any())
            {
                _api.BindBuffer(GLEnum.ElementArrayBuffer, ibo);
                fixed (void* v = &Edges[0])
                {
                    _api.BufferData(GLEnum.ElementArrayBuffer, (nuint) (Edges.Length * sizeof(Edge)), v, GLEnum.DynamicDraw);
                }
                _api.BindBuffer(GLEnum.ElementArrayBuffer, 0);
            }
            
            _api.BindVertexArray(0);
            _genBuffers = true;
        }

        public unsafe void UpdateBuffers()
        {
            if (!_genBuffers) return;
            
            _api.BindVertexArray(vao);

            if (Vertices.Any() && _queueUpdate[(int)UpdateType.Position])
            {
                _api.BindBuffer(GLEnum.ArrayBuffer, vbo[(int)UpdateType.Position]);
                fixed (void* v = &Vertices[0])
                {
                    _api.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint) (Vertices.Length * sizeof(Vector3D<float>)), v);
                }

                _queueUpdate[(int)UpdateType.Position] = false;
            }
            
            if (Normals.Any() && _queueUpdate[(int)UpdateType.Normals])
            {
                _api.BindBuffer(GLEnum.ArrayBuffer, vbo[(int)UpdateType.Normals]);
                fixed (void* v = &Normals[0])
                {
                    _api.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint) (Normals.Length * sizeof(Vector3D<float>)), v);
                }

                _queueUpdate[(int)UpdateType.Normals] = false;
            }
            
            if (Tangents.Any() && _queueUpdate[(int)UpdateType.Tangents])
            {
                _api.BindBuffer(GLEnum.ArrayBuffer, vbo[(int)UpdateType.Tangents]);
                fixed (void* v = &Tangents[0])
                {
                    _api.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint) (Tangents.Length * sizeof(Vector3D<float>)), v);
                }

                _queueUpdate[(int)UpdateType.Tangents] = false;
            }
            
            if (BiTangents.Any() && _queueUpdate[(int)UpdateType.BiTangents])
            {
                _api.BindBuffer(GLEnum.ArrayBuffer, vbo[(int)UpdateType.BiTangents]);
                fixed (void* v = &BiTangents[0])
                {
                    _api.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint) (BiTangents.Length * sizeof(Vector3D<float>)), v);
                }

                _queueUpdate[(int)UpdateType.BiTangents] = false;
            }
            
            if (VertexColors.Any() && _queueUpdate[(int)UpdateType.VertexColors])
            {
                _api.BindBuffer(GLEnum.ArrayBuffer, vbo[(int)UpdateType.VertexColors]);
                fixed (void* v = &VertexColors[0])
                {
                    _api.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint) (VertexColors.Length * sizeof(Vector3D<float>)), v);
                }

                _queueUpdate[(int)UpdateType.VertexColors] = false;
            }
            
            if (VertexAlphas.Any() && _queueUpdate[(int)UpdateType.VertexAlpha])
            {
                _api.BindBuffer(GLEnum.ArrayBuffer, vbo[(int)UpdateType.VertexAlpha]);
                fixed (void* v = &VertexAlphas[0])
                {
                    _api.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint) (VertexAlphas.Length * sizeof(float)), v);
                }

                _queueUpdate[(int)UpdateType.VertexAlpha] = false;
            }
            
            if (TextureCoordinates.Any() && _queueUpdate[(int)UpdateType.TextureCoordinates])
            {
                _api.BindBuffer(GLEnum.ArrayBuffer, vbo[(int)UpdateType.TextureCoordinates]);
                fixed (void* v = &TextureCoordinates[0])
                {
                    _api.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint) (TextureCoordinates.Length * sizeof(Vector2D<float>)), v);
                }

                _queueUpdate[(int)UpdateType.TextureCoordinates] = false;
            }
            
            _api.BindBuffer(GLEnum.ArrayBuffer, 0);

            if (_queueUpdate[(int)UpdateType.Indices])
            {
                if (Triangles.Any())
                {
                    _api.BindBuffer(GLEnum.ElementArrayBuffer, ibo);
                    fixed (void* v = &RenderTriangles[0])
                    {
                        _api.BufferSubData(GLEnum.ElementArrayBuffer, 0, (nuint) (NumTriangles * sizeof(Triangle)), v);
                    }
                    _api.BindBuffer(GLEnum.ElementArrayBuffer, 0);
                } else if (Edges.Any())
                {
                    _api.BindBuffer(GLEnum.ElementArrayBuffer, ibo);
                    fixed (void* v = &Edges[0])
                    {
                        _api.BufferSubData(GLEnum.ElementArrayBuffer, 0, (nuint) (Edges.Length * sizeof(Edge)), v);
                    }
                    _api.BindBuffer(GLEnum.ElementArrayBuffer, 0);
                }
                
                _queueUpdate[(int)UpdateType.Indices] = false;
            }
            
            _api.BindVertexArray(0);
        }
        
        public void Dispose()
        {
            if (!_genBuffers) return;
            
            _api.BindBuffer(GLEnum.ArrayBuffer, 0);
            _api.BindBuffer(GLEnum.ElementArrayBuffer, 0);
            _api.BindVertexArray(0);
            
            _api.DeleteBuffers((uint) vbo.Length, vbo);
            _api.DeleteBuffers(1, ibo);
            _api.DeleteVertexArray(vao);
        }

        public void BuildTriAdjacency()
        {
            if (!Triangles.Any()) return;
            
            Array.Resize(ref VertTris, NumVertices);
            Array.Fill(VertTris, new List<int>());
            
            for (var t = 0; t < NumTriangles; t++)
            {
                var tri = Triangles[t];
                var i1 = tri.X;
                var i2 = tri.Y;
                var i3 = tri.Z;

                if (i1 >= NumVertices || i2 >= NumVertices || i3 >= NumVertices)
                    continue;
                
                VertTris[i1].Add(t);
                VertTris[i2].Add(t);
                VertTris[i3].Add(t);
            }
        }
        
        public void SmoothNormals()
        {
            //mesh::SmoothNormals
            if (!Normals.Any()) return;

            // Zero old Normals
            Array.Fill(Normals, Vector3D<float>.Zero);
            
            // Face Normals
            for (var i = 0; i < NumTriangles; i++)
            {
                var tri = Triangles[i];
                var trinomial = Trinomial(tri);

                Normals[tri.X] = Vector3D.Add(Normals[tri.X], trinomial);
                Normals[tri.Y] = Vector3D.Add(Normals[tri.Y], trinomial);
                Normals[tri.Z] = Vector3D.Add(Normals[tri.Z], trinomial);
            }
            
            // Normalize the Normals...
            for (var i = 0; i < NumVertices; i++)
            {
                Normals[i] = Vector3D.Normalize(Normals[i]);
            }

            if (SmoothSeamNormals)
            {
                // Smooth welded Vertex Normals
                if (!_gotWeldedVerts)
                    CalculateWeldedVertices();

                var seamNormals = new List<Tuple<int, Vector3D<float>>>();
                foreach (var (key, value) in WeldedVertices)
                {
                    var normal = Normals[key];
                    var seamNormal = normal;
                    foreach (var i in value)
                    {
                        if (Angle(normal, Normals[i]) < SmoothThreshold)
                        {
                            seamNormal = Vector3D.Add(seamNormal, Normals[i]);
                        }
                    }

                    seamNormal = Vector3D.Normalize(seamNormal);
                    seamNormals.Add(new Tuple<int, Vector3D<float>>(key, seamNormal));
                }

                foreach (var (key, value) in seamNormals)
                {
                    Normals[key] = value;
                }
            }

            _queueUpdate[(int) UpdateType.Normals] = true;
            CalcTangentSpace();
        }

        public void CalcTangentSpace()
        {
            if (!Normals.Any() || !TextureCoordinates.Any()) return;

            for (var i = 0; i < NumTriangles; i++)
            {
                var tri = Triangles[i];
                var i1 = tri.X;
                var i2 = tri.Y;
                var i3 = tri.Z;

                var v1 = Vertices[i1];
                var v2 = Vertices[i2];
                var v3 = Vertices[i3];

                var w1 = TextureCoordinates[i1];
                var w2 = TextureCoordinates[i2];
                var w3 = TextureCoordinates[i3];

                var x1 = v2.X - v1.X;
                var x2 = v3.X - v1.X;
                var y1 = v2.Y - v1.Y;
                var y2 = v3.Y - v1.Y;
                var z1 = v2.Z - v1.Z;
                var z2 = v3.Z - v1.Z;

                var s1 = w2.X - w1.X;
                var s2 = w3.X - w1.X;
                var t1 = w2.Y - w1.Y;
                var t2 = w3.Y - w1.Y;

                var r = s1 * t2 - s2 * t1;
                r = r >= 0.0f ? 1.0f : -1.0f;

                var sDir = new Vector3D<float>(
                    (t2 * x1 - t1 * x2) * r,
                    (t2 * y1 - t1 * y2) * r,
                    (t2 * z1 - t1 * z2) * r
                );

                var tDir = new Vector3D<float>(
                    (s1 * x2 - s2 * x1) * r,
                    (s1 * y2 - s2 * y1) * r,
                    (s1 * z2 - s2 * z1) * r
                );
                
                sDir = Vector3D.Normalize(sDir);
                tDir = Vector3D.Normalize(tDir);

                Tangents[i1] = Vector3D.Add(tDir, Tangents[i1]);
                Tangents[i2] = Vector3D.Add(tDir, Tangents[i2]);
                Tangents[i3] = Vector3D.Add(tDir, Tangents[i3]);
                
                BiTangents[i1] = Vector3D.Add(sDir, BiTangents[i1]);
                BiTangents[i2] = Vector3D.Add(sDir, BiTangents[i2]);
                BiTangents[i3] = Vector3D.Add(sDir, BiTangents[i3]);
            }
            
            // TODO: maybe put this loop into the previous one?
            for (var i = 0; i < NumVertices; i++)
            {
                var curTangent = Tangents[i];
                var curBiTangent = BiTangents[i];

                var curNormal = Normals[i];
                
                if (IsZero(curTangent) || IsZero(curBiTangent))
                {
                    curTangent.X = curNormal.Y;
                    curTangent.Y = curNormal.Z;
                    curTangent.Z = curNormal.X;
                    
                    Tangents[i] = curTangent;
                    BiTangents[i] = Vector3D.Cross(curNormal, curTangent);
                }
                else
                {
                    Tangents[i] = Vector3D.Normalize(Tangents[i]);
                    Tangents[i] = Vector3D
                        .Subtract(Tangents[i], Vector3D
                            .Multiply(curNormal, Vector3D
                                .Dot(curNormal, Tangents[i])));
                    Tangents[i] = Vector3D.Normalize(curTangent);

                    BiTangents[i] = Vector3D.Normalize(BiTangents[i]);

                    BiTangents[i] = Vector3D
                        .Subtract(BiTangents[i], Vector3D
                            .Multiply(curNormal, Vector3D
                                .Dot(curNormal, BiTangents[i])));
                    BiTangents[i] = Vector3D
                        .Subtract(BiTangents[i], Vector3D
                            .Multiply(Tangents[i], Vector3D
                                .Dot(Tangents[i], BiTangents[i])));
                    
                    BiTangents[i] = Vector3D.Normalize(BiTangents[i]);
                }
            }

            _queueUpdate[(int)UpdateType.Tangents] = true;
            _queueUpdate[(int)UpdateType.BiTangents] = true;
        }
        
        public void CalculateWeldedVertices()
        {
            _gotWeldedVerts = true;

            // TODO: custom KdTree for Vector3D:
            // https://github.com/viliwonka/KDTree

            var indices = new List<ushort>();
            for (ushort i = 0; i < NumVertices; i++)
                indices.Add(i);
            
            // smallest first
            indices.Sort((i, j) => Vertices[i].X < Vertices[j].X ? -1 : 1);

            var matches = new List<List<ushort>>(NumVertices / 2);
            var used = new bool[NumVertices];
            for (var i = 0; i < NumVertices; ++i)
            {
                if (used[i]) continue;
                
                var matched = false;
                for (var j = i + 1; j < NumVertices; ++j)
                {
                    if (used[j]) continue;

                    var a = Vertices[indices[j]];
                    var b = Vertices[indices[i]];

                    if (a.X - b.X >= float.Epsilon) break;
                    if (Scalar.Abs(b.Y - a.Y) >= float.Epsilon) continue;
                    if (Scalar.Abs(b.Z - a.Z) >= float.Epsilon) continue;

                    if (!matched)
                    {
                        matches.Add(new List<ushort>
                        {
                            indices[i]
                        });
                    }

                    matched = true;
                    matches.Last().Add(indices[j]);
                    used[j] = true;
                }
            }

            foreach (var matchset in matches)
            {
                for (var j = 0; j < matchset.Count; ++j)
                {
                    if (!WeldedVertices.TryGetValue(matchset[j], out var weldVerts))
                    {
                        weldVerts = new List<int>();
                        WeldedVertices.Add(matchset[j], weldVerts);
                    }

                    for (var k = 0; k < matchset.Count; ++k)
                    {
                        if (j == k) continue;
                        weldVerts.Add(matchset[k]);
                    }
                }
            }
        }
        
        private Vector3D<float> Trinomial(Triangle cur)
        {
            var p1 = Vertices[cur.X];
            var p2 = Vertices[cur.Y];
            var p3 = Vertices[cur.Z];

            var x = (p2.Y - p1.Y) * (p3.Z - p1.Z) - (p2.Z - p1.Z) * (p3.Y - p1.Y);
            var y = (p2.Z - p1.Z) * (p3.X - p1.X) - (p2.X - p1.X) * (p3.Z - p1.Z);
            var z = (p2.X - p1.X) * (p3.Y - p1.Y) - (p2.Y - p1.Y) * (p3.X - p1.X);

            return new Vector3D<float>(x, y, z);
        }

        private static float Angle(Vector3D<float> a, Vector3D<float> b)
        {
            var normA = Vector3D.Normalize(a);
            var normB = Vector3D.Normalize(b);

            var dot = Vector3D.Dot(normA, normB);

            return dot switch
            {
                > 1.0f => 0.0f,
                < -1.0f => Scalar<float>.Pi,
                0.0f => Scalar<float>.Pi / 2.0f,
                _ => Scalar.Acos(dot)
            };
        }

        private static bool IsZero(Vector3D<float> vector, bool useEpsilon = false)
        {
            if (!useEpsilon) return 
                vector.X == 0.0f &&
                vector.Y == 0.0f &&
                vector.Z == 0.0f;
            
            var abs = Vector3D.Abs(vector);
            return
                abs.X < Scalar<float>.Epsilon &&
                abs.Y < Scalar<float>.Epsilon &&
                abs.Z < Scalar<float>.Epsilon;

        }

        #endregion
    }

    [PublicAPI]
    public class ShaderProperties
    {
        public Vector2D<float> UVOffset { get; set; } = Vector2D<float>.Zero;
        public Vector2D<float> UVScale { get; set; } = Vector2D<float>.One;
        public Vector3D<float> SpecularColor { get; set; } = Vector3D<float>.One;
        public float SpecularStrength { get; set; } = 1.0f;
        public float Shininess { get; set; } = 30.0f;
        public float EnvironmentReflection { get; set; } = 1.0f;
        public Vector3D<float> EmissiveColor { get; set; } = Vector3D<float>.One;
        public float EmissiveMultiple { get; set; } = 1.0f;
        public float Alpha { get; set; } = 1.0f;
        public float BackLightPower { get; set; } = 0.0f;
        public float RimLightPower { get; set; } = 2.0f;
        public float SoftLighting { get; set; } = 0.3f;
        public float SubsurfaceRolloff { get; set; } = 0.3f;
        public float FresnelPower { get; set; } = 5.0f;
        public float PaletteScale { get; set; } = 0.0f;
    }
}
