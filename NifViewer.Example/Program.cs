using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DdsKtxSharp;
using NifViewer.Lib;
using Silk.NET.Assimp;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using File = System.IO.File;
using Mesh = NifViewer.Lib.Mesh;

namespace NifViewer.Example
{
    public static class Program
    {
        private static IWindow _window = null!;

        private static GL _gl = null!;
        private static Assimp _assimp = null!;
        private static readonly List<Mesh> Meshes = new();

        public static void Main(string[] args)
        {
            var options = WindowOptions.Default;
            options.Size = new Vector2D<int>(800, 600);
            options.Title = "Title";

            _window = Window.Create(options);

            _window.Load += OnLoad;
            _window.Update += OnUpdate;
            _window.Render += OnRender;

            _window.Run();
        }

        private static void OnLoad()
        { 
            _gl = GL.GetApi(_window);
            _assimp = Assimp.GetApi();

            using var nifFile = new nifly.NifFile(true);
            var res = nifFile.Load("meshes\\coin\\ulfric.nif", new nifly.NifLoadOptions());

            if (res != 0)
                throw new NotImplementedException($"Res is not 0! {res}");

            var shapeList = nifFile.GetShapeNames();
            if (shapeList == null || !shapeList.Any())
                throw new NotImplementedException("ShapeList is null or empty!");

            //TODO: multiple
            
            //GLSurface::AddMeshFromNif
            //TODO: nif->FindBlockName
            var shapeName = shapeList.First();
            var shape = nifFile.GetShapes().First(x => x.name.opEq(shapeName));

            if (shape == null)
                throw new NotImplementedException();

            var nifVerts = new nifly.vectorVector3();
            if (!nifFile.GetVertsForShape(shape, nifVerts))
                throw new NotImplementedException();

            var nifTris = new nifly.vectorTriangle();
            if (!shape.GetTriangles(nifTris))
                throw new NotImplementedException();

            var nifNorms = nifFile.GetNormalsForShape(shape);
            var nifUVs = nifFile.GetUvsForShape(shape);

            if (nifNorms == null)
                throw new NotImplementedException();
            if (nifUVs == null)
                throw new NotImplementedException();

            var mesh = new Mesh(_gl);

            if (shape.IsSkinned())
            {
                Console.WriteLine("SKINNED MODEL!");
                throw new NotImplementedException();
            }
            else
            {
                var ttg = shape.GetTransformToParent();
                var parent = nifFile.GetParentNode(shape);
                while (nifFile.GetParentNode(parent) != null)
                {
                    ttg = parent.GetTransformToParent().ComposeTransforms(ttg);
                    parent = nifFile.GetParentNode(parent);
                }

                ttg.translation = VecToMeshCoords(ttg.translation);
                mesh.MatModel = TransformToMatrix4(ttg);
            }

            var shader = nifFile.GetShader(shape);
            if (shader != null)
            {
                mesh.IsDoubleSided = shader.IsDoubleSided();
                mesh.IsModelSpace = shader.IsModelSpace();
                mesh.IsEmissive = shader.IsEmissive();
                mesh.HasSpecular = shader.HasSpecular();
                mesh.HasVertexColors = shader.HasVertexColors();
                mesh.HasVertexAlpha = shader.HasVertexAlpha();
                mesh.HasGlowMap = shader.HasGlowmap();
                mesh.HasGreyscaleColor = shader.HasGreyscaleColor();
                mesh.HasCubeMap = shader.HasEnvironmentMapping();

                if (nifFile.GetHeader().GetVersion().Stream() < 130)
                {
                    mesh.HasBacklight = shader.HasBacklight();
                    mesh.HasBacklightMap = mesh.HasBacklight;
                    mesh.HasRimLight = shader.HasRimlight();
                    mesh.HasSoftLight = shader.HasSoftlight();
                    mesh.ShaderProperties.RimLightPower = shader.GetRimlightPower();
                    mesh.ShaderProperties.SoftLighting = shader.GetSoftlight();
                }
                else
                {
                    mesh.HasBacklight = shader.GetBacklightPower() > 0.0;
                    mesh.HasSoftLight = shader.GetSubsurfaceRolloff() > 0.0;
                    mesh.ShaderProperties.SubsurfaceRolloff = shader.GetSubsurfaceRolloff();
                }

                mesh.ShaderProperties.UVOffset = shader.GetUVOffset().ToVector2D();
                mesh.ShaderProperties.UVScale = shader.GetUVScale().ToVector2D();
                mesh.ShaderProperties.SpecularColor = shader.GetSpecularColor().ToVector3D();
                mesh.ShaderProperties.SpecularStrength = shader.GetSpecularStrength();
                mesh.ShaderProperties.Shininess = shader.GetGlossiness();
                mesh.ShaderProperties.EnvironmentReflection = shader.GetEnvironmentMapScale();
                mesh.ShaderProperties.BackLightPower = shader.GetBacklightPower();
                mesh.ShaderProperties.PaletteScale = shader.GetGrayscaleToPaletteScale();
                mesh.ShaderProperties.FresnelPower = shader.GetFresnelPower();

                mesh.ShaderProperties.EmissiveColor = shader.GetEmissiveColor().ToVector3D();
                mesh.ShaderProperties.EmissiveMultiple = shader.GetEmissiveMultiple();

                mesh.ShaderProperties.Alpha = shader.GetAlpha();
            }

            var material = nifFile.GetMaterialProperty(shape);
            if (material != null)
            {
                mesh.IsEmissive = material.IsEmissive();
                
                mesh.ShaderProperties.SpecularColor = material.GetSpecularColor().ToVector3D();
                mesh.ShaderProperties.Shininess = material.GetGlossiness();

                mesh.ShaderProperties.EmissiveColor = material.GetEmissiveColor().ToVector3D();
                mesh.ShaderProperties.EmissiveMultiple = material.GetEmissiveMultiple();

                mesh.ShaderProperties.Alpha = material.GetAlpha();
            }

            var stencil = nifFile.GetStencilProperty(shape);
            if (stencil != null)
            {
                var drawMode = (nifly.DrawMode) ((stencil.flags & (int)nifly.StencilMasks.DRAW_MASK) >> (int) nifly.StencilMasks.DRAW_POS);
                switch (drawMode)
                {
                    case nifly.DrawMode.DRAW_CW:
                        mesh.IsDoubleSided = false;
                        mesh.CullMode = GLEnum.Front;
                        break;
                    case nifly.DrawMode.DRAW_BOTH:
                        mesh.IsDoubleSided = true;
                        mesh.CullMode = GLEnum.Back;
                        break;
                    default:
                        mesh.IsDoubleSided = false;
                        mesh.CullMode = GLEnum.Back;
                        break;
                }
            }

            var alphaProp = nifFile.GetAlphaProperty(shape);
            if (alphaProp != null)
            {
                mesh.AlphaFlags = alphaProp.flags;
                mesh.AlphaThreshold = alphaProp.threshold;
            }

            mesh.NumVertices = nifVerts.Count;
            mesh.NumTriangles = nifTris.Count;

            if (mesh.NumVertices > 0)
            {
                Array.Resize(ref mesh.Vertices, mesh.NumVertices);
                Array.Resize(ref mesh.Normals, mesh.NumVertices);
                Array.Resize(ref mesh.Tangents, mesh.NumVertices);
                Array.Resize(ref mesh.BiTangents, mesh.NumVertices);
                Array.Resize(ref mesh.VertexColors, mesh.NumVertices);
                Array.Resize(ref mesh.VertexAlphas, mesh.NumVertices);
                Array.Resize(ref mesh.TextureCoordinates, mesh.NumVertices);
            }

            if (mesh.NumTriangles > 0)
            {
                Array.Resize(ref mesh.Triangles, mesh.NumTriangles);
            }

            mesh.ShapeName = shapeName;
            
            // Load verts. NIF verts are scaled up by approx. 10 and rotated on the x axis (Z up, Y forward).  
            // Scale down by 10 and rotate on x axis by flipping y and z components. To face the camera, this also mirrors
            // on X and Y (180 degree y axis rotation.)
            var divVector = new Vector3D<float>(-10.0f, 10.0f, 10.0f);
            for (var i = 0; i < mesh.Vertices.Length; i++)
            {
                var vec = nifVerts[i].ToVector3D();
                vec = Vector3D.Divide(vec, divVector);
                mesh.Vertices[i] = new Vector3D<float>(vec.X, vec.Z, vec.Y);
            }

            if (nifUVs.Any())
            {
                mesh.IsTextured = true;
                for (var i = 0; i < mesh.TextureCoordinates.Length; i++)
                {
                    mesh.TextureCoordinates[i] = nifUVs[i].ToVector2D();
                }
            }

            if (nifNorms.Any())
            {
                for (var i = 0; i < mesh.Triangles.Length; i++)
                {
                    mesh.Triangles[i] = nifTris[i].ToTriangle();
                }
                
                // Calculate WeldedVertices and Normals
                mesh.SmoothNormals();
            }
            else
            {
                // Already have normals, just copy the data over
                for (var i = 0; i < mesh.NumTriangles; i++)
                {
                    mesh.Triangles[i] = nifTris[i].ToTriangle();
                }
                
                // Copy normals, Note: normals are transformed the same way the vertices are.
                for (var i = 0; i < mesh.NumVertices; i++)
                {
                    var cur = nifNorms[i];
                    var vector = new Vector3D<float>(-cur.x, cur.z, cur.y);
                    mesh.Normals[i] = vector;
                }

                // Virtually weld verts across UV seams
                mesh.CalculateWeldedVertices();
            }
            
            mesh.CalcTangentSpace();
            // TODO: CreateBVH
            
            Meshes.Add(mesh);

            mesh.BuildTriAdjacency();
            mesh.SmoothNormals();
            mesh.CreateBuffers();

            var matFile = string.Empty;
            
            // PreviewWindow::AddNifShapeTextures
            if (nifFile.GetHeader().GetVersion().User() == 12 &&
                nifFile.GetHeader().GetVersion().Stream() >= 130)
            {
                if (shader != null)
                    matFile = shader.name.get();
            }

            var texFiles = new string[9];
            
            if (!string.IsNullOrEmpty(matFile))
            {
                //TODO:
                throw new NotImplementedException();
            } else if (shader != null)
            {
                //TODO: does not work due to bad signature (std::string& -> string instead of ref string)
                /*
                for (var i = 0; i < 10; i++)
                {
                    var texFile = string.Empty;
                    nifFile.GetTextureSlot(shape, texFile, (uint)i);
                    
                    if (!string.IsNullOrEmpty(texFile))
                        texFiles.Add(texFile);
                }*/
            }

            // hardcoded for now
            texFiles[0] = "textures/COIN/Ulfric.dds";
            texFiles[1] = "textures/COIN/Ulfirc_n.dds";
            texFiles[5] = "textures/Gray.dds";

            const string vShader = "shaders/default.vert";
            const string fShader = "shaders/default.frag";

            // ResourceLoader::AddMaterial
            for (var i = 0; i < texFiles.Length; i++)
            {
                var id = LoadTexture(texFiles[i]);
            }
            
            // PreviewWindow::SetShapeTextures
            
        }

        private static unsafe uint LoadTexture(string path)
        {
            if (!File.Exists(path))
                throw new ArgumentException("File does not exist!", nameof(path));

            // ResourceLoader::LoadTexture
            var id = 0u;

            var ext = Path.GetExtension(path);
            if (!ext.Equals(".dds", StringComparison.OrdinalIgnoreCase))
                throw new NotImplementedException();

            var data = File.ReadAllBytes(path);
            var parser = DdsKtxParser.FromMemory(data);

            var imageData = parser.GetSubData(0, 0, 0, out var subData);

            return id;
        }
        
        private static unsafe void OnRender(double obj)
        {
            _gl.Clear((uint) ClearBufferMask.ColorBufferBit | (uint) ClearBufferMask.DepthBufferBit);
            
            // TODO: UpdateProjection()
            
            // Render regular meshes
            foreach (var mesh in Meshes)
            {
                
            }
        }

        private static void RenderMesh(Mesh mesh)
        {
            mesh.UpdateBuffers();
            
            
        }

        private static void OnUpdate(double obj)
        {
        }
        
        private static nifly.Vector3 VecToMeshCoords(nifly.Vector3 vec)
        {
            //mesh::VecToMeshCoords
            var vecNew = new nifly.Vector3(vec);
            vecNew.x /= -10.0f;
            vecNew.y /= 10.0f;
            vecNew.z /= 10.0f;

            (vecNew.z, vecNew.y) = (vecNew.y, vecNew.z);
            return vecNew;
        }

        private static Matrix4X4<float> TransformToMatrix4(nifly.MatTransform xform)
        {
            //glm::mat4x4 TransformToMatrix4
            var mat = Matrix4X4<float>.Identity;

            float yaw = 0.0f, pitch = 0.0f, roll = 0.0f;
            xform.rotation.ToEulerAngles(ref yaw, ref pitch, ref roll);
            
            var translationMatrix = Matrix4X4.CreateTranslation(xform.translation.x, xform.translation.y, xform.translation.z);
            mat = Matrix4X4.Multiply(mat, translationMatrix);


            //convert to radians
            var yawRadians = Scalar.DegreesToRadians(yaw);
            var pitchRadians = Scalar.DegreesToRadians(pitch);
            var rawRadians = Scalar.DegreesToRadians(roll);

            var eulerMatrix = Matrix4X4.CreateFromYawPitchRoll(yawRadians, pitchRadians, rawRadians);
            mat = Matrix4X4.Multiply(mat, eulerMatrix);

            var scaleMatrix = Matrix4X4.CreateScale(xform.scale);
            mat = Matrix4X4.Multiply(mat, scaleMatrix);
            
            return mat;
        }
    }
}
