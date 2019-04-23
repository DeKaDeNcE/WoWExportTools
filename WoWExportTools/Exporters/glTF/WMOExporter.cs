﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Linq;
using WoWFormatLib.FileReaders;
using System.IO;
using Newtonsoft.Json;
using CASCLib;

namespace OBJExporterUI.Exporters.glTF
{
    public class WMOExporter
    {
        public static void ExportWMO(string file, BackgroundWorker exportworker = null, string destinationOverride = null)
        {
            if (exportworker == null)
            {
                exportworker = new BackgroundWorker();
                exportworker.WorkerReportsProgress = true;
            }

            Logger.WriteLine("WMO glTF Exporter: Loading file {0}...", file);

            exportworker.ReportProgress(5, "Reading WMO..");

            var outdir = ConfigurationManager.AppSettings["outdir"];
            var wmo = new WMOReader().LoadWMO(file);

            var customCulture = (System.Globalization.CultureInfo)System.Threading.Thread.CurrentThread.CurrentCulture.Clone();
            customCulture.NumberFormat.NumberDecimalSeparator = ".";
            System.Threading.Thread.CurrentThread.CurrentCulture = customCulture;

            if (destinationOverride == null)
            {
                if (!Directory.Exists(Path.Combine(outdir, Path.GetDirectoryName(file))))
                {
                    Directory.CreateDirectory(Path.Combine(outdir, Path.GetDirectoryName(file)));
                }
            }

            exportworker.ReportProgress(25, "Generating glTF..");

            var glTF = new glTF()
            {
                asset = new Asset()
                {
                    version = "2.0",
                    generator = "Marlamin's WoW Export Tools " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                    copyright = "Contents are owned by Blizzard Entertainment",
                    minVersion = "2.0"
                }
            };

            var groups = new Structs.WMOGroup[wmo.group.Count()];
            FileStream stream;

            if (destinationOverride == null)
            {
                stream = new FileStream(Path.Combine(outdir, file.Replace(".wmo", ".bin")), FileMode.OpenOrCreate);
            }
            else
            {
                stream = new FileStream(Path.Combine(destinationOverride, Path.GetFileNameWithoutExtension(file) + ".bin"), FileMode.OpenOrCreate);
            }

            var writer = new BinaryWriter(stream);

            var bufferViews = new List<BufferView>();
            var accessorInfo = new List<Accessor>();
            var meshes = new List<Mesh>();

            for (var g = 0; g < wmo.group.Count(); g++)
            {
                if (wmo.group[g].mogp.vertices == null)
                { Console.WriteLine("Group has no vertices!"); continue; }
                for (var i = 0; i < wmo.groupNames.Count(); i++)
                {
                    if (wmo.group[g].mogp.nameOffset == wmo.groupNames[i].offset)
                    {
                        groups[g].name = wmo.groupNames[i].name.Replace(" ", "_");
                    }
                }

                if (groups[g].name == "antiportal")
                { Console.WriteLine("Group is antiportal"); continue; }

                // Position bufferview
                var vPosBuffer = new BufferView()
                {
                    buffer = 0,
                    byteOffset = (uint)writer.BaseStream.Position,
                    target = 34962
                };

                var minPosX = float.MaxValue;
                var minPosY = float.MaxValue;
                var minPosZ = float.MaxValue;

                var maxPosX = float.MinValue;
                var maxPosY = float.MinValue;
                var maxPosZ = float.MinValue;

                for (var i = 0; i < wmo.group[g].mogp.vertices.Count(); i++)
                {
                    writer.Write(wmo.group[g].mogp.vertices[i].vector.X * -1);
                    writer.Write(wmo.group[g].mogp.vertices[i].vector.Z);
                    writer.Write(wmo.group[g].mogp.vertices[i].vector.Y);

                    if (wmo.group[g].mogp.vertices[i].vector.X * -1 < minPosX)
                        minPosX = wmo.group[g].mogp.vertices[i].vector.X * -1;
                    if (wmo.group[g].mogp.vertices[i].vector.Z < minPosY)
                        minPosY = wmo.group[g].mogp.vertices[i].vector.Z;
                    if (wmo.group[g].mogp.vertices[i].vector.Y < minPosZ)
                        minPosZ = wmo.group[g].mogp.vertices[i].vector.Y;

                    if (wmo.group[g].mogp.vertices[i].vector.X * -1 > maxPosX)
                        maxPosX = wmo.group[g].mogp.vertices[i].vector.X * -1;
                    if (wmo.group[g].mogp.vertices[i].vector.Z > maxPosY)
                        maxPosY = wmo.group[g].mogp.vertices[i].vector.Z;
                    if (wmo.group[g].mogp.vertices[i].vector.Y > maxPosZ)
                        maxPosZ = wmo.group[g].mogp.vertices[i].vector.Y;
                }

                vPosBuffer.byteLength = (uint)writer.BaseStream.Position - vPosBuffer.byteOffset;

                var posLoc = accessorInfo.Count();

                accessorInfo.Add(new Accessor()
                {
                    name = "vPos",
                    bufferView = bufferViews.Count(),
                    byteOffset = 0,
                    componentType = 5126,
                    count = (uint)wmo.group[g].mogp.vertices.Count(),
                    type = "VEC3",
                    min = new float[] { minPosX, minPosY, minPosZ },
                    max = new float[] { maxPosX, maxPosY, maxPosZ }
                });

                bufferViews.Add(vPosBuffer);

                // Normal bufferview
                var normalBuffer = new BufferView()
                {
                    buffer = 0,
                    byteOffset = (uint)writer.BaseStream.Position,
                    target = 34962
                };

                for (var i = 0; i < wmo.group[g].mogp.vertices.Count(); i++)
                {
                    writer.Write(wmo.group[g].mogp.normals[i].normal.X);
                    writer.Write(wmo.group[g].mogp.normals[i].normal.Z);
                    writer.Write(wmo.group[g].mogp.normals[i].normal.Y);
                }

                normalBuffer.byteLength = (uint)writer.BaseStream.Position - normalBuffer.byteOffset;

                var normalLoc = accessorInfo.Count();

                accessorInfo.Add(new Accessor()
                {
                    name = "vNormal",
                    bufferView = bufferViews.Count(),
                    byteOffset = 0,
                    componentType = 5126,
                    count = (uint)wmo.group[g].mogp.vertices.Count(),
                    type = "VEC3"
                });

                bufferViews.Add(normalBuffer);

                // TexCoord bufferview
                var texCoordBuffer = new BufferView()
                {
                    buffer = 0,
                    byteOffset = (uint)writer.BaseStream.Position,
                    target = 34962
                };

                for (var i = 0; i < wmo.group[g].mogp.vertices.Count(); i++)
                {
                    writer.Write(wmo.group[g].mogp.textureCoords[0][i].X);
                    writer.Write(wmo.group[g].mogp.textureCoords[0][i].Y);
                }

                texCoordBuffer.byteLength = (uint)writer.BaseStream.Position - texCoordBuffer.byteOffset;

                var texLoc = accessorInfo.Count();

                accessorInfo.Add(new Accessor()
                {
                    name = "vTex",
                    bufferView = bufferViews.Count(),
                    byteOffset = 0,
                    componentType = 5126,
                    count = (uint)wmo.group[g].mogp.vertices.Count(),
                    type = "VEC2"
                });

                bufferViews.Add(texCoordBuffer);

                var indexBufferPos = bufferViews.Count();

                for (var i = 0; i < wmo.group[g].mogp.renderBatches.Count(); i++)
                {
                    var batch = wmo.group[g].mogp.renderBatches[i];

                    accessorInfo.Add(new Accessor()
                    {
                        name = "indices",
                        bufferView = indexBufferPos,
                        byteOffset = batch.firstFace * 2,
                        componentType = 5123,
                        count = batch.numFaces,
                        type = "SCALAR"
                    });

                    var mesh = new Mesh();
                    mesh.name = groups[g].name + "_" + i;
                    mesh.primitives = new Primitive[1];
                    mesh.primitives[0].attributes = new Dictionary<string, int>
                    {
                        { "POSITION", posLoc },
                        { "NORMAL", normalLoc },
                        { "TEXCOORD_0", texLoc }
                    };

                    mesh.primitives[0].indices = (uint)accessorInfo.Count() - 1;

                    if (batch.flags == 2)
                    {
                        mesh.primitives[0].material = (uint)batch.possibleBox2_3;
                    }
                    else
                    {
                        mesh.primitives[0].material = batch.materialID;
                    }

                    mesh.primitives[0].mode = 4;

                    meshes.Add(mesh);
                }

                var indiceBuffer = new BufferView()
                {
                    buffer = 0,
                    byteOffset = (uint)writer.BaseStream.Position,
                    target = 34963
                };

                for (var i = 0; i < wmo.group[g].mogp.indices.Count(); i++)
                {
                    writer.Write(wmo.group[g].mogp.indices[i].indice);
                }

                indiceBuffer.byteLength = (uint)writer.BaseStream.Position - indiceBuffer.byteOffset;

                bufferViews.Add(indiceBuffer);

                // Buffer alignment!
                if ((indiceBuffer.byteOffset + indiceBuffer.byteLength) % 4 != 0)
                {
                    writer.Write((short)0);
                }
            }

            glTF.bufferViews = bufferViews.ToArray();
            glTF.accessors = accessorInfo.ToArray();

            glTF.buffers = new Buffer[1];
            glTF.buffers[0].byteLength = (uint)writer.BaseStream.Length;
            glTF.buffers[0].uri = Path.GetFileNameWithoutExtension(file) + ".bin";

            writer.Close();
            writer.Dispose();

            exportworker.ReportProgress(65, "Exporting textures..");

            if (wmo.materials == null)
            {
                Logger.WriteLine("WMO glTF exporter: Materials empty");
                return;
            }

            var materialCount = wmo.materials.Count();

            glTF.images = new Image[materialCount];
            glTF.textures = new Texture[materialCount];
            glTF.materials = new Material[materialCount];

            for (var i = 0; i < materialCount; i++)
            {
                for (var ti = 0; ti < wmo.textures.Count(); ti++)
                {
                    if (wmo.textures[ti].startOffset == wmo.materials[i].texture1)
                    {
                        var textureFilename = Path.GetFileNameWithoutExtension(wmo.textures[ti].filename).ToLower();

                        glTF.images[i].uri = textureFilename + ".png";

                        glTF.textures[i].sampler = 0;
                        glTF.textures[i].source = i;

                        glTF.materials[i].name = textureFilename;
                        glTF.materials[i].pbrMetallicRoughness = new PBRMetallicRoughness();
                        glTF.materials[i].pbrMetallicRoughness.baseColorTexture = new TextureIndex();
                        glTF.materials[i].pbrMetallicRoughness.baseColorTexture.index = i;
                        glTF.materials[i].pbrMetallicRoughness.metallicFactor = 0.0f;

                        switch (wmo.materials[i].blendMode)
                        {
                            case 0:
                                glTF.materials[i].alphaMode = "OPAQUE";
                                glTF.materials[i].alphaCutoff = 0.0f;
                                break;
                            case 1:
                                glTF.materials[i].alphaMode = "MASK";
                                glTF.materials[i].alphaCutoff = 0.90393700787f;
                                break;
                            case 2:
                                glTF.materials[i].alphaMode = "MASK";
                                glTF.materials[i].alphaCutoff = 0.5f;
                                break;
                            default:
                                glTF.materials[i].alphaMode = "OPAQUE";
                                glTF.materials[i].alphaCutoff = 0.0f;
                                break;
                        }

                        var saveLocation = "";

                        if (destinationOverride == null)
                        {
                            saveLocation = Path.Combine(outdir, Path.GetDirectoryName(file), textureFilename + ".png");
                        }
                        else
                        {
                            saveLocation = Path.Combine(outdir, destinationOverride, textureFilename + ".png");
                        }

                        if (!File.Exists(saveLocation))
                        {
                            var blpreader = new BLPReader();

                            blpreader.LoadBLP(wmo.textures[ti].filename);

                            try
                            {
                                blpreader.bmp.Save(saveLocation);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Error exporting texture " + wmo.textures[ti].filename + ": " + e.Message);
                            }
                        }
                    }
                }
            }

            glTF.samplers = new Sampler[1];
            glTF.samplers[0].name = "Default Sampler";
            glTF.samplers[0].minFilter = 9986;
            glTF.samplers[0].magFilter = 9729;
            glTF.samplers[0].wrapS = 10497;
            glTF.samplers[0].wrapT = 10497;

            glTF.scenes = new Scene[1];
            glTF.scenes[0].name = Path.GetFileNameWithoutExtension(file);

            glTF.nodes = new Node[meshes.Count()];
            var meshIDs = new List<int>();
            for (var i = 0; i < meshes.Count(); i++)
            {
                glTF.nodes[i].name = meshes[i].name;
                glTF.nodes[i].mesh = i;
                meshIDs.Add(i);
            }

            glTF.scenes[0].nodes = meshIDs.ToArray();

            glTF.meshes = meshes.ToArray();

            glTF.scene = 0;

            var currentDoodadSetName = "";
            for (var i = 0; i < wmo.doodadDefinitions.Count(); i++)
            {
                var doodadDefinition = wmo.doodadDefinitions[i];

                foreach (var doodadSet in wmo.doodadSets)
                {
                    if (doodadSet.firstInstanceIndex == i)
                    {
                        Console.WriteLine("At set: " + doodadSet.setName);
                        currentDoodadSetName = doodadSet.setName.Replace("Set_", "").Replace("SET_", "").Replace("$DefaultGlobal", "Default");
                    }
                }

                foreach (var doodadNameEntry in wmo.doodadNames)
                {
                    if (doodadNameEntry.startOffset == doodadDefinition.offset)
                    {
                        if (!File.Exists(Path.GetFileNameWithoutExtension(doodadNameEntry.filename).ToLower() + ".gltf"))
                        {
                            if (destinationOverride == null)
                            {
                                M2Exporter.ExportM2(doodadNameEntry.filename.Replace(".MDX", ".M2").Replace(".MDL", ".M2").ToLower(), null, Path.Combine(outdir, Path.GetDirectoryName(file)));
                            }
                            else
                            {
                                M2Exporter.ExportM2(doodadNameEntry.filename.Replace(".MDX", ".M2").Replace(".MDL", ".M2").ToLower(), null, destinationOverride);
                            }
                        }
                    }
                }
            }

            exportworker.ReportProgress(95, "Writing to file..");

            if (destinationOverride == null)
            {
                File.WriteAllText(Path.Combine(outdir, file.Replace(".wmo", ".gltf")), JsonConvert.SerializeObject(glTF, Formatting.Indented, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                }));
            }
            else
            {
                File.WriteAllText(Path.Combine(destinationOverride, Path.GetFileName(file.ToLower()).Replace(".wmo", ".gltf")), JsonConvert.SerializeObject(glTF, Formatting.Indented, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                }));
            }

            Logger.WriteLine("Done exporting WMO file!");
        }
    }
}
